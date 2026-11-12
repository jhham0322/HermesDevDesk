using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hermes.Core;

internal static class ChatGptPlanTests
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        Directory.CreateDirectory(root); using var rsa = RSA.Create(2048); var key = rsa.ExportParameters(false);
        var jwks = JsonSerializer.SerializeToElement(new { keys = new[] { new { kid = "fixture", kty = "RSA", use = "sig", alg = "RS256", n = ChatGptPlanAuth.B64(key.Modulus!), e = ChatGptPlanAuth.B64(key.Exponent!) } } });
        var now = DateTimeOffset.UtcNow; var nonce = "fixture-nonce"; var client = "oaiapp_fixture";
        string Jwt(string n, string audience = "oaiapp_fixture", long? expiry = null, string subject = "fixture-user")
        {
            var header = ChatGptPlanAuth.B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid = "fixture" }));
            var body = ChatGptPlanAuth.B64(JsonSerializer.SerializeToUtf8Bytes(new { iss = ChatGptPlanAuth.Issuer, aud = audience, sub = subject, nonce = n, exp = expiry ?? now.AddHours(1).ToUnixTimeSeconds(), email = "fixture@example.invalid" }));
            return header + "." + body + "." + ChatGptPlanAuth.B64(rsa.SignData(Encoding.ASCII.GetBytes(header + "." + body), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        void Reject(Action action, string label) { bool failed = false; try { action(); } catch { failed = true; } check(failed, label); }
        check(ChatGptPlanAuth.ValidateIdentity(Jwt(nonce), client, nonce, jwks, now).Subject == "fixture-user", "OAuth signed ID token is validated");
        Reject(() => ChatGptPlanAuth.ValidateIdentity(Jwt("wrong"), client, nonce, jwks, now), "OAuth nonce mismatch rejected");
        Reject(() => ChatGptPlanAuth.ValidateIdentity(Jwt(nonce,"other"), client, nonce, jwks, now), "OAuth audience mismatch rejected");
        Reject(() => ChatGptPlanAuth.ValidateIdentity(Jwt(nonce,expiry:now.AddSeconds(-1).ToUnixTimeSeconds()),client,nonce,jwks,now), "OAuth expired identity rejected");
        var signed = Jwt(nonce); var parts = signed.Split('.'); parts[1] = ChatGptPlanAuth.B64(JsonSerializer.SerializeToUtf8Bytes(new { sub = "forged" }));
        Reject(() => ChatGptPlanAuth.ValidateIdentity(string.Join('.',parts),client,nonce,jwks,now), "OAuth forged payload rejected");
        Reject(() => ChatGptPlanAuth.Callback("/auth/callback?state=a&state=b"), "OAuth duplicate callback parameters rejected");
        Reject(() => ChatGptPlanAuth.ValidateCallback(new(){{"state","wrong"},{"code","code"},{"client_id",client}},nonce,null), "OAuth state mismatch rejected");
        Reject(() => ChatGptPlanAuth.ValidateCallback(new(){{"state",nonce},{"error","access_denied"}},nonce,null), "OAuth denial does not exchange code");
        Reject(() => ChatGptPlanAuth.ValidateCallback(new(){{"state",nonce},{"code","code"},{"client_id","oaiapp_other"}},nonce,client), "OAuth returning client cannot change");
        using(var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); int browserCalls=0; bool cancelledAtBoundary=false;
            try { ChatGptPlanAuth.LaunchBrowser(new Uri("https://auth.openai.com/api/accounts/authorize"),_=>browserCalls++,cancelled.Token); }
            catch(OperationCanceledException) { cancelledAtBoundary=true; }
            check(cancelledAtBoundary&&browserCalls==0,"OAuth cancellation at browser-launch boundary prevents browser opening");
        }
        var q = new Dictionary<string,string>(); string subject = "fixture-user"; bool allowPlan = true; int refreshes = 0; string? host = null;
        var handler = new Fake(async request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            check(request.RequestUri.Host is "auth.openai.com" or "api.openai.com", "OAuth only official hosts requested");
            if (url.EndsWith("openid-configuration")) return Ok(new { issuer = ChatGptPlanAuth.Issuer, jwks_uri = ChatGptPlanAuth.Issuer + "/jwks", revocation_endpoint = ChatGptPlanAuth.Issuer + "/revoke" });
            if (url.EndsWith("/jwks")) return Ok(jwks);
            if (url.EndsWith("/revoke")) return new(HttpStatusCode.OK) { Content = new StringContent("") };
            if (url.EndsWith("/oauth/token"))
            {
                var form = ChatGptPlanAuth.Callback("/auth/callback?" + await request.Content!.ReadAsStringAsync());
                check(form["client_id"] == client && form["resource"] == ChatGptPlanAuth.Resource && !form.ContainsKey("client_secret"), "OAuth token uses issued public client and resource");
                bool refresh = form["grant_type"] == "refresh_token";
                if (refresh) { refreshes++; check(!form.ContainsKey("scope"), "OAuth refresh retains granted scope and rotates once"); }
                else check(ChatGptPlanAuth.B64(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]))) == q["code_challenge"] && form["redirect_uri"] == q["redirect_uri"], "OAuth PKCE and exact redirect are bound to token exchange");
                return Ok(new { access_token = "fixture-access-not-real", refresh_token = "fixture-refresh-"+refreshes, id_token = Jwt(q["nonce"],subject:subject), token_type = "Bearer", expires_in = refresh ? 3600 : 1, scope = allowPlan ? ChatGptPlanAuth.Scope : "openid email" });
            }
            if (url.EndsWith("/models")) { check(request.Headers.Authorization?.Parameter == "fixture-access-not-real", "OAuth model list uses selected token"); return Ok(new { models = new[] { new { slug="visible",display_name="Visible model",visibility="list" },new {slug="hidden",display_name="Hidden",visibility="hidden"} } }); }
            throw new IOException("Unexpected mock route");
        });
        using var auth = new ChatGptPlanAuth(root, handler);
        Task? callback = null;
        void Open(Uri uri)
        {
            q = ChatGptPlanAuth.Callback("/auth/callback?" + uri.Query.TrimStart('?'));
            check(uri.Host == "auth.openai.com" && q["code_challenge_method"] == "S256", "OAuth opens official authorization with PKCE S256");
            check(q["client_id"] == (auth.Accounts().Count == 0 ? "dynamic_agent_client" : client), "OAuth reuses account registration");
            check(!q.ContainsKey("agent_name_hint") || q["agent_name_hint"] == "HermesDevDesk", "OAuth consistent application identity");
            if (host != null) check(host == q["ext_agent_host_id"], "OAuth stable host identity across sign-ins"); host = q["ext_agent_host_id"];
            callback = Task.Run(async () => { using var browser = new HttpClient(); using var response = await browser.GetAsync(q["redirect_uri"] + "?code=fixture-code&state=" + q["state"] + "&client_id="+client); response.EnsureSuccessStatusCode(); });
        }
        await auth.SignInAsync(null,Open,false,CancellationToken.None); await callback!;
        var account = auth.Accounts().Single(); check(account.PlanAllowed, "OAuth granted plan scope retained");
        var catalog = await auth.ModelsAsync(account.Id,CancellationToken.None); check(catalog.Count == 1 && catalog[0].Slug == "visible", "OAuth server model visibility and slug preserved");
        check(refreshes == 1, "OAuth near-expiry refresh is performed once");
        await Task.WhenAll(Enumerable.Range(0,4).Select(_ => auth.LeaseAsync(account.Id,CancellationToken.None)));
        check(refreshes==1,"OAuth concurrent lease requests reuse the rotated token");
        await using(var engine = new AppServerClient())
        {
            var lease=await auth.LeaseAsync(account.Id,CancellationToken.None);
            await engine.ConnectAsync(Environment.ProcessPath!,root,Path.Combine(root,"owned-engine-home"),chatGptPlan:lease);
            var profile=await engine.RequestAsync("execution-profile",new{});
            var argv=profile.GetProperty("arguments").EnumerateArray().Select(x=>x.GetString()??"").ToArray();
            check(profile.GetProperty("hasPlanToken").GetBoolean(),"OAuth access token reaches only app-server child environment");
            check(!argv.Any(a=>a.Contains("fixture-access-not-real")),"OAuth token is never passed on command line");
            check(argv.Contains("model_providers.openai_chatgpt_plan.base_url=\"https://api.openai.com/v1\"") && argv.Contains("model_providers.openai_chatgpt_plan.supports_websockets=false"),"OAuth engine uses official Responses HTTP route");
            check(argv.Contains("shell_environment_policy.exclude=[\"ACCESS_TOKEN\",\"*API_KEY*\",\"*TOKEN*\"]"),"OAuth shell children exclude provider credential environment");
            check(!lease.ToString().Contains("fixture-access"),"OAuth lease diagnostic string hides token");
        }
        var legacySettings = new ProviderSettings { Endpoint="https://company.invalid/v1",Deployment="company-fixture",KeyRevision=7 };
        var legacyExpected=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { legacySettings.Mode, legacySettings.Endpoint, legacySettings.Deployment, legacySettings.Protocol, legacySettings.Authentication, legacySettings.KeyRevision, legacySettings.MaxOutputTokens }))))[..16];
        check(legacySettings.Fingerprint==legacyExpected,"Existing company provider fingerprint stays byte-for-byte compatible");
        check(!JsonSerializer.Serialize(legacySettings).Contains("OAuthAccountId"),"Existing company exports do not gain an OAuth account identifier");
        check(new ChatGptRegistration{ClientId="oaiapp_a",Subject="same-user"}.Id!=new ChatGptRegistration{ClientId="oaiapp_b",Subject="same-user"}.Id,"OAuth workspace registrations remain distinct for the same user");
        var deskRoot=Path.Combine(root,"owned-company-coexistence");
        await using(var desk=new DeskController(deskRoot))
        {
            desk.SaveModelConnection(new ProviderSettings{Endpoint="https://company.invalid/v1",Deployment="company-fixture",Protocol=ProviderProtocol.GatewayResponses,Authentication=ProviderAuth.None,UseAdvancedSettings=true},null);
            var saved=desk.ExportModelConnections();
            desk.Configure(new ProviderSettings{Mode=ConnectionMode.ExistingChatGptCli,OAuthAccountId=account.Id,Endpoint="",Deployment="oauth-model"});
            desk.Models.Add("oauth-model");
            check(desk.DisplayModels.SequenceEqual(new[]{"oauth-model"}),"OAuth picker uses only active account server models");
            check(desk.ExportModelConnections()==saved,"OAuth connection leaves existing company model export unchanged");
            check(desk.SelectModelConnection("company-fixture")&&!desk.State.Provider.IsChatGptPlanOAuth&&desk.ExportModelConnections()==saved,"Existing company model selection works after OAuth without conversion");
        }
        var emptyRoot=Path.Combine(root,"owned-oauth-restart");
        await using(var desk=new DeskController(emptyRoot)) {desk.Configure(new ProviderSettings{Mode=ConnectionMode.ExistingChatGptCli,OAuthAccountId=account.Id,Endpoint="",Deployment="oauth-model"});}
        await using(var desk=new DeskController(emptyRoot)) check(desk.State.Provider.IsChatGptPlanOAuth&&desk.State.ModelConnections.Count==0,"OAuth restart does not migrate into company model registrations");
        var aSettings=new ProviderSettings{Mode=ConnectionMode.ExistingChatGptCli,OAuthAccountId="account-a",Deployment="visible"};
        var bSettings=new ProviderSettings{Mode=ConnectionMode.ExistingChatGptCli,OAuthAccountId="account-b",Deployment="visible"};
        check(aSettings.Fingerprint!=bSettings.Fingerprint,"OAuth account switch changes thread provider fingerprint");
        var encrypted = File.ReadAllBytes(Path.Combine(root,"chatgpt-plan","accounts.dpapi")); check(!Encoding.UTF8.GetString(encrypted).Contains("fixture-access"), "OAuth credential file protected by user DPAPI");
        using (var restart = new ChatGptPlanAuth(root,new Fake(_ => throw new IOException("No network expected")))) check(restart.Accounts().Single().Id == account.Id, "OAuth registration survives app restart");
        subject = "different-account"; bool rejected = false; try { await auth.SignInAsync(account.Id,Open,false,CancellationToken.None); } catch(IOException) { rejected=true; } await callback!;
        check(rejected && auth.Accounts().Single().Id == account.Id, "OAuth re-login cannot replace another account's credentials"); subject = "fixture-user";
        using(var cancel=new CancellationTokenSource()) { var pending=auth.SignInAsync(account.Id,_=>cancel.Cancel(),false,cancel.Token); try{await pending;}catch(OperationCanceledException){} }
        check(auth.Accounts().Single().SignedIn, "OAuth cancellation preserves previous registration and credentials");
        check(await auth.SignOutAsync(account.Id,CancellationToken.None), "OAuth revocation confirmed before local logout");
        check(!auth.Accounts().Single().SignedIn && auth.Accounts().Single().Id==account.Id, "OAuth logout clears tokens and preserves account mapping");
        allowPlan=false;await auth.SignInAsync(account.Id,Open,false,CancellationToken.None);await callback!;
        bool denied=false;try{await auth.LeaseAsync(account.Id,CancellationToken.None);}catch(IOException){denied=true;}check(denied,"OAuth identity without plan permission cannot infer");
    }
    private static HttpResponseMessage Ok(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json") };
    private sealed class Fake(Func<HttpRequestMessage,Task<HttpResponseMessage>> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) => run(request); }
}
