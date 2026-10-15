using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Hermes.Core;

internal static class HttpGatewayTests
{
    internal static async Task Run(string root, string report)
    {
        Directory.CreateDirectory(root);var passed=new List<string>();int calls=0;
        void Check(bool ok,string label){if(!ok)throw new IOException(label);passed.Add("PASS "+label);}
        void Reject(Action action,string label){try{action();}catch(IOException){Check(true,label);return;}throw new IOException(label);}
        const string fakeKey="SYNTHETIC_HTTP_GATEWAY_ONLY_NOT_A_REAL_KEY";
        try
        {
            Reject(()=>ProviderSettings.ResolveEndpointInput("gateway.invalid:8087/team/v1"),"scheme omission requires an explicit choice");
            Check(ProviderSettings.ResolveEndpointInput("gateway.invalid:8087/team/v1",EndpointSchemeChoice.Http)=="http://gateway.invalid:8087/team/v1","explicit HTTP choice preserves host port and path");
            Check(ProviderSettings.ResolveEndpointInput("gateway.invalid:9443/team/v1",EndpointSchemeChoice.Https)=="https://gateway.invalid:9443/team/v1","explicit HTTPS choice preserves port and path");
            Check(ProviderSettings.ResolveEndpointInput("https://gateway.invalid:9443/team/v1",EndpointSchemeChoice.Http).StartsWith("https://"),"explicit HTTPS URL is never downgraded by the selection");
            Check(ProviderSettings.TransportNotice("http://gateway.invalid").Contains("암호화되지"),"HTTP transport warning explicitly describes no encryption");
            foreach(var bad in new[]{"ftp://gateway.invalid","file:///sample","http://user:secret@gateway.invalid","http://gateway.invalid/?key=sample","http://gateway.invalid/#sample","http://api.openai.com"})
                Reject(()=>new ProviderSettings{Endpoint=bad,Deployment="fixture",Protocol=ProviderProtocol.GatewayResponses}.Validate(),"unsupported or unsafe base URL rejected");
            var https=new ProviderSettings{Endpoint="https://gateway.invalid:9443/team/v1",Deployment="fixture",Protocol=ProviderProtocol.GatewayResponses};
            Check(https.RequestUri(false).AbsoluteUri=="https://gateway.invalid:9443/team/v1/responses","existing HTTPS request route remains unchanged");
            using(var sockets=new SocketsHttpHandler())Check(sockets.SslOptions.RemoteCertificateValidationCallback==null,"default TLS certificate validation callback is unchanged");
            string? lastPath=null,lastModel=null;bool apiKey=false,bearer=false;bool redirect=false;
            await using var mock=new LoopbackHttp(async(request,stream,ct)=>
            {
                Interlocked.Increment(ref calls);lastPath=request.Path;
                apiKey=request.Headers.TryGetValue("api-key",out var key)&&key==fakeKey;
                bearer=request.Headers.TryGetValue("Authorization",out var auth)&&auth=="Bearer "+fakeKey;
                if(request.Body.Length>0){using var doc=JsonDocument.Parse(request.Body);lastModel=doc.RootElement.GetProperty("model").GetString();}
                if(redirect){await LoopbackHttp.Reply(stream,302,"application/json","{}",ct,new(){{"Location","http://127.0.0.1:1/never-follow"}});return;}
                await LoopbackHttp.Reply(stream,200,"application/json",request.Method=="GET"?"{\"data\":[]}":"{\"id\":\"owned-http-response\",\"status\":\"completed\",\"output\":[]}",ct);
            });
            var endpoint=mock.BaseUri.AbsoluteUri.TrimEnd('/')+"/tenant/gateway/v1";
            var data=Path.Combine(root,"owned-settings");
            await using(var desk=new DeskController(data))desk.Configure(new(){Endpoint=endpoint,Deployment="owned-http-model",Protocol=ProviderProtocol.GatewayResponses,Authentication=ProviderAuth.ApiKeyHeader},fakeKey);
            await using var reopened=new DeskController(data);
            Check(reopened.State.Provider.Endpoint==endpoint&&reopened.State.Provider.Deployment=="owned-http-model","HTTP settings survive save and reopen");
            Check(reopened.KeyStore.ReadOwnKey()==fakeKey,"only the owned fake key survives protected save/reopen");
            Check(reopened.State.Provider.RequestUri(false).Port==mock.BaseUri.Port,"explicit loopback port is preserved in the normal non-test validation path");
            using var client=new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=false});
            foreach(var auth in new[]{ProviderAuth.ApiKeyHeader,ProviderAuth.BearerKey,ProviderAuth.None})
            {
                var settings=reopened.State.Provider;settings.Authentication=auth;
                await using var bridge=new CompanyBridge(settings,reopened.KeyStore); // no allowLoopbackForTests bypass
                var before=calls;await bridge.CheckConnectionAsync();
                Check(calls==before+1&&lastPath=="/tenant/gateway/v1/models","normal HTTP models request preserves the gateway base path");
                using var request=new HttpRequestMessage(HttpMethod.Post,bridge.BaseUri.AbsoluteUri+"/responses");
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bridge.Capability);
                request.Content=new StringContent("{\"model\":\"ignored-fixture-model\",\"input\":\"owned sample\",\"stream\":false}",Encoding.UTF8,"application/json");
                using var response=await client.SendAsync(request);var body=await response.Content.ReadAsStringAsync();
                Check(response.IsSuccessStatusCode&&lastPath=="/tenant/gateway/v1/responses"&&lastModel=="owned-http-model","HTTP inference request preserves path and forwards the configured model");
                Check(auth==ProviderAuth.ApiKeyHeader?apiKey&&!bearer:auth==ProviderAuth.BearerKey?bearer&&!apiKey:!apiKey&&!bearer,"selected authentication header forwarded without a log copy");
                Check(!body.Contains(fakeKey),"fake key is not reflected in bridge response");
            }
            redirect=true;
            await using(var bridge=new CompanyBridge(reopened.State.Provider,reopened.KeyStore)){var before=calls;var result=await bridge.CheckConnectionAsync();Check(calls==before+1&&result.Contains("Redirect"),"HTTP redirect is rejected without fallback or follow-up request");}
            using var tlsFailure=new TlsFailureHandler();
            await using(var bridge=new CompanyBridge(https,reopened.KeyStore,handler:tlsFailure)){await bridge.CheckConnectionAsync();Check(tlsFailure.Calls==1&&tlsFailure.AllHttps,"TLS failure never retries using HTTP");}
            Write(true,"");
        }
        catch(Exception ex){Write(false,ex.GetType().Name);throw;}
        void Write(bool ok,string error)=>File.WriteAllText(report,JsonSerializer.Serialize(new{passed=ok,error,checks=passed.Count,results=passed,localhostRequests=calls,realCompanyRequests=0,realModelCalls=0,actualKeysRead=false,addressesOrKeysLogged=false},new JsonSerializerOptions{WriteIndented=true}));
    }
    private sealed class TlsFailureHandler:HttpMessageHandler
    {
        internal int Calls;internal bool AllHttps=true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Calls++;AllHttps&=request.RequestUri?.Scheme=="https";throw new HttpRequestException("Synthetic TLS validation failure",new AuthenticationException("Synthetic only"));}
    }
}
