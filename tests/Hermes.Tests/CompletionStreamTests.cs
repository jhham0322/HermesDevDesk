using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hermes.Core;
internal static class CompletionStreamTests
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        Directory.CreateDirectory(root);AppLog.Configure(root);var scope=ExecutionMemory.Scope(root);var mem=new ExecutionMemory(Path.Combine(root,"memory"));
        var proof=mem.Record(scope,"task","once","verify long result","command","owned fixture",true,new string('x',12000)+"END_OK");
        check(proof.Outcome.Length==4000,"completion preview remains bounded at 4000 characters");
        check(mem.OutcomeContains(scope,"task",proof.Id,"END_OK"),"completion marker after preview limit is matched against full result");
        check(!mem.OutcomeContains(scope,"task",proof.Id,"MISSING"),"missing completion marker is not invented");
        check(!mem.OutcomeContains(scope,"other",proof.Id,"END_OK"),"another task cannot supply completion evidence");
        check(!mem.OutcomeContains(scope,"task",proof.Id,"end_ok"),"completion evidence preserves exact ordinal matching");
        mem.Record(scope,"task","once","verify long result","command","owned fixture",true,"FORGED");
        check(!mem.OutcomeContains(scope,"task",proof.Id,"FORGED"),"duplicate event cannot replace original completion evidence");
        var bad=mem.Record(scope,"task","failed","verify failed result","command","owned fixture",false,"END_OK");
        check(!mem.OutcomeContains(scope,"task",bad.Id,"END_OK"),"failed command cannot prove completion even with marker");
        var restart=new ExecutionMemory(Path.Combine(root,"memory"));check(restart.OutcomeContains(scope,"task",proof.Id,"END_OK"),"full completion proof survives restart");
        await using(var desk=new DeskController(Path.Combine(root,"desk")))
        {
            var project=desk.Register(root);var task=desk.NewTask(project,"owned completion");task.ThreadId="owned-thread";task.TurnId="owned-turn";task.Status="실행 중";var process=desk.BeginProcess(task,"Verify owned result");
            string Decide(object args){using var d=JsonDocument.Parse(JsonSerializer.Serialize(args));return desk.DecideProcess(task,d.RootElement,"owned-turn");}
            Decide(new{action="plan",items=new[]{new{id="verify",title="Verify result",acceptance="Actual execution contains END_OK",kind="evidence",expectedOutput="END_OK"}}});
            Decide(new{action="start_item",itemId="verify"});var evidence=desk.Memory.Record(scope,task.Id,"long-once","verify result","command","fixture",true,new string('a',9000)+"END_OK");
            Decide(new{action="verify_item",itemId="verify",evidenceId=evidence.Id,verification="Verified actual full result after the preview boundary."});
            check(process.Items.Single().State=="verified","real work queue verifies long successful command without preview false failure");
        }
        string Event(string type,string status)=>"event: "+type+"\ndata: "+JsonSerializer.Serialize(new{type,response=new{status,model="fixture",output=Array.Empty<object>()}})+"\n\n";
        var terminal=Event("response.completed","completed");var state=new ResponseStreamCompletion();
        check(state.Accept("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n")==null&&!state.Terminated,"partial text is not a terminal event");
        bool rejected=false;try{state.RequireTerminal();}catch(IOException){rejected=true;}check(rejected,"server EOF without terminal is rejected");
        check(state.Accept(terminal)?.Text("status")=="completed"&&state.Terminated,"normal completed event is authoritative");
        var multi=new ResponseStreamCompletion();check(multi.Accept("event: response.completed\ndata: {\"type\":\"response.completed\",\ndata: \"response\":{\"status\":\"completed\"}}\n\n")?.Text("status")=="completed","valid multiline SSE payload is parsed as one event");
        rejected=false;try{new ResponseStreamCompletion().Accept(Event("response.completed","in_progress"));}catch(IOException){rejected=true;}check(rejected,"terminal name with nonterminal status cannot falsely complete");
        foreach(var status in new[]{"failed","incomplete"})check(new ResponseStreamCompletion().Accept(Event("response."+status,status))?.Text("status")==status,"explicit "+status+" is not upgraded to completed");
        foreach(var mode in new[]{"complete-open","early-eof","timeout","user-cancel","client-timeout"})
        {
            int requests=0;UsageEntry? usage=null;var initial="data: {\"type\":\"response.output_text.delta\",\"delta\":\"PARTIAL_KEPT\"}\n\n";
            using var handler=new Mock(()=>{requests++;var content=new StreamContent(new FixtureStream(initial+(mode=="complete-open"?terminal:""),mode));content.Headers.ContentType=new MediaTypeHeaderValue("text/event-stream");return new(HttpStatusCode.OK){Content=content};});
            await using var bridge=new CompanyBridge(new ProviderSettings{Endpoint="http://127.0.0.1:17998/v1",Deployment="fixture",Protocol=ProviderProtocol.GatewayResponses,Authentication=ProviderAuth.None},new ProtectedKeyStore(Path.Combine(root,"empty-keys")),true,handler);bridge.Usage+=e=>usage=e;
            using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(4)};using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(bridge.BaseUri,"v1/responses")){Content=new StringContent("{\"model\":\"fixture\",\"stream\":true,\"input\":[]}",Encoding.UTF8,"application/json")};request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bridge.Capability);
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);if(mode=="client-timeout")
            {
                using var body=await response.Content.ReadAsStreamAsync();using var received=new MemoryStream();var bytes=new byte[4096];
                using var firstDeadline=new CancellationTokenSource(TimeSpan.FromSeconds(2));
                while(!Encoding.UTF8.GetString(received.ToArray()).Contains("PARTIAL_KEPT")){var n=await body.ReadAsync(bytes,firstDeadline.Token);if(n==0)throw new IOException("Unexpected early close");received.Write(bytes,0,n);}
                bool timedOut=false;using(var deadline=new CancellationTokenSource(TimeSpan.FromMilliseconds(100))){try{while(await body.ReadAsync(bytes,deadline.Token)>0){}}catch(OperationCanceledException){timedOut=true;}}
                check(timedOut,"client body-read deadline is distinct from server terminal completion");
                check(Encoding.UTF8.GetString(received.ToArray()).Contains("PARTIAL_KEPT")&&!Encoding.UTF8.GetString(received.ToArray()).Contains("response.completed"),"client timeout retains partial bytes without false completion");
                check(requests==1,"client timeout does not replay inference");bridge.CancelCurrent();continue;
            }
            var pending=response.Content.ReadAsStringAsync();
            if(mode=="user-cancel"){await Task.Delay(50);bridge.CancelCurrent();}
            var wire=await pending;
            check(wire.Contains("PARTIAL_KEPT"),mode+" preserves delivered partial output");check(requests==1,mode+" never automatically retries inference");
            if(mode=="complete-open")check(wire.Contains("response.completed")&&usage?.Status=="completed","terminal event ends relay without waiting for server connection EOF");
            else check(!wire.Contains("response.completed")&&usage?.Status==(mode=="user-cancel"?"cancelled":mode=="early-eof"?"unknown":"failed"),mode+" is distinguished from successful completion");
        }
    }
    private sealed class Mock(Func<HttpResponseMessage> run):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>Task.FromResult(run());}
    private sealed class FixtureStream(string data,string mode):Stream
    {
        private readonly byte[] bytes=Encoding.UTF8.GetBytes(data);private int offset;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){if(offset<bytes.Length){var n=Math.Min(buffer.Length,bytes.Length-offset);bytes.AsMemory(offset,n).CopyTo(buffer);offset+=n;return n;}if(mode=="early-eof")return 0;if(mode=="timeout")throw new TaskCanceledException("Synthetic upstream timeout");await Task.Delay(Timeout.Infinite,ct);return 0;}
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long l)=>throw new NotSupportedException();public override void Write(byte[]b,int o,int c)=>throw new NotSupportedException();
    }
}
