using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Hermes.Core;
internal static class ResponseRecoveryTests
{
    internal static async Task<(string wire,UsageEntry usage)> Relay(string root,string mode,string? taskId=null,string? turn=null)
    {
        Directory.CreateDirectory(root);UsageEntry? last=null;int requests=0;
        var initial="data: {\"type\":\"response.output_text.delta\",\"delta\":\"PARTIAL_KEPT\"}\n\n";
        var final=mode=="output_limit"?"data: {\"type\":\"response.incomplete\",\"response\":{\"status\":\"incomplete\",\"model\":\"fixture\",\"incomplete_details\":{\"reason\":\"max_output_tokens\"},\"output\":[]}}\n\n":"";
        using var handler=new Handler(()=>{requests++;var content=new StreamContent(new Input(initial+final,mode));content.Headers.ContentType=new MediaTypeHeaderValue("text/event-stream");return new(HttpStatusCode.OK){Content=content};});
        await using var bridge=new CompanyBridge(new(){Endpoint="http://127.0.0.1:17998/v1",Deployment="fixture",Protocol=ProviderProtocol.GatewayResponses,Authentication=ProviderAuth.None},new ProtectedKeyStore(Path.Combine(root,"empty-keys")),true,handler);
        bridge.Usage+=e=>last=e;
        if(taskId!=null){bridge.BeginTask(taskId,"fixture");bridge.SetTurn(taskId,turn??"");}
        using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(5)};using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(bridge.BaseUri,"v1/responses")){Content=new StringContent("{\"model\":\"fixture\",\"stream\":true,\"input\":[]}",Encoding.UTF8,"application/json")};request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bridge.Capability);
        using var response=await client.SendAsync(request);var wire=await response.Content.ReadAsStringAsync();
        if(requests!=1||last==null)throw new IOException("Fixture unexpectedly retried or lost usage");
        return(wire,last);
    }
    internal static async Task Probe(string root)
    {
        Directory.CreateDirectory(root);var rows=new List<object>();
        foreach(var mode in new[]{"output_limit","early_eof","server_timeout"}){var result=await Relay(Path.Combine(root,mode),mode);rows.Add(new{mode,result.usage.Status,result.usage.Note,partial=result.wire.Contains("PARTIAL_KEPT"),falseComplete=result.wire.Contains("response.completed"),result.usage.HeadersMs,result.usage.FirstUsefulOutputMs,result.usage.TotalMs});}
        File.WriteAllText(Path.Combine(root,"baseline.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(rows));
    }
    internal static async Task Run(string root,Action<bool,string> check)
    {
        Directory.CreateDirectory(root);var measurements=new List<object>();
        var untrustedReason=new UsageEntry();using(var doc=JsonDocument.Parse("""{"status":"incomplete","incomplete_details":{"reason":"password=SYNTHETIC_REASON_SECRET"}}"""))ResponseInterruption.Terminal(untrustedReason,doc.RootElement);
        check(!untrustedReason.IncompleteReason.Contains("SYNTHETIC_REASON_SECRET")&&untrustedReason.FailureKind=="response_incomplete","stored unknown provider reason is bounded and credential-redacted without inventing an output-limit cause");
        foreach(var mode in new[]{"output_limit","early_eof","server_timeout"})
        {
            var data=Path.Combine(root,mode);Directory.CreateDirectory(data);
            await using var desk=new DeskController(Path.Combine(data,"state"));var project=desk.Register(data);var task=desk.NewTask(project,"STAGE3_RESUME_FIXTURE remaining work");
            task.ThreadId="STAGE3_RESUME_FIXTURE";task.TurnId="turn-"+mode;task.Model="fixture";task.ThreadLearningVersion=2;
            var process=desk.BeginProcess(task,"STAGE3_RESUME_FIXTURE preserve completed file and continue verification");
            File.WriteAllText(Path.Combine(data,"already-written.txt"),"WRITE_ONCE");
            process.Items.Add(new(){Id="written",Title="Completed file",State="verified",Kind="artifact",Artifacts=[new(){Path="already-written.txt",Contains="WRITE_ONCE"}]});
            process.Items.Add(new(){Id="remaining",Title="Verify remaining",State="running",Kind="evidence",ExpectedOutput="DONE"});
            var commandArgs=JsonSerializer.SerializeToElement(new{command="Write-Output WRITE_ONCE",purpose="already written",operationId="write-once"});
            var begin=typeof(DeskController).GetMethod("BeginOperation",BindingFlags.NonPublic|BindingFlags.Instance)!;
            // The receipt belongs to the completed item and has concrete returned output.
            process.Operations.Add(new(){Key="write-once",ItemId="written",State="completed",Result="WRITE_ONCE",ResultReceived=true,ExitCode=0,MethodDigest=ExecutionBudget.Digest(["Write-Output WRITE_ONCE","already written"])});
            ChatMessages.Delta(task,"partial","PARTIAL_KEPT");
            var response=await Relay(Path.Combine(data,"relay"),mode,task.Id,task.TurnId);
            var expected=mode=="server_timeout"?"response_timeout":"response_"+mode;check(response.usage.FailureKind==expected&&response.wire.Contains("PARTIAL_KEPT")&&!response.wire.Contains("response.completed"),mode+" keeps partial bytes and exact noncompletion category");
            if(mode=="output_limit")check(response.usage.IncompleteReason=="max_output_tokens","output-token limit is preserved as provider reason, not daily usage quota");
            desk.ObserveCompanyUsage(response.usage);desk.ObserveCompanyUsage(response.usage);
            using(var empty=JsonDocument.Parse(JsonSerializer.Serialize(new{threadId=task.ThreadId,item=new{id="partial",type="agentMessage",text=""}})))desk.Handle(new("item/completed",empty.RootElement));
            check(task.Transcript.Any(e=>e.ItemId=="partial"&&e.Text=="PARTIAL_KEPT"),mode+" empty final message cannot erase received partial text");
            check(task.Transcript.Count(e=>e.ItemId=="response-interruption-"+response.usage.Id)==1&&task.Transcript.Any(e=>e.Origin=="Hermes"&&e.Text.Contains(response.usage.Note)),mode+" exposes one host UI message with source and reason");
            using(var terminal=JsonDocument.Parse(JsonSerializer.Serialize(new{threadId=task.ThreadId,turn=new{id=task.TurnId,status="completed"}})))desk.Handle(new("turn/completed",terminal.RootElement));
            check(!process.Active&&process.BlockKind==expected&&process.Decision!="complete"&&process.Items[0].State=="verified",mode+" pauses completion loop without losing verified item");
            check(task.Transcript.Any(e=>e.Text=="PARTIAL_KEPT")&&process.Operations.Single().Result=="WRITE_ONCE",mode+" keeps received text and completed execution evidence");
            desk.Save();var snapshot=new StateStore(desk.DataDirectory).Load();
            check(snapshot.Usage.Single().FailureKind==expected&&snapshot.Processes.Single().BlockKind==expected&&snapshot.Tasks.Single().Transcript.Any(e=>e.Text=="PARTIAL_KEPT"),mode+" reason and partial work survive restart");
            bool blocked=false;try{await desk.ResumeSavedProcessAsync(process.Id);}catch(HermesException){blocked=true;}
            check(blocked&&!process.Active,mode+" requires explicit resume rather than automatic generation replay");
            var stale=response.usage;stale.Id="stale";stale.TurnId="older-turn";desk.ObserveCompanyUsage(stale);check(!task.Transcript.Any(e=>e.ItemId=="response-interruption-stale"),"late older-turn response cannot alter current UI/checkpoint");
            measurements.Add(new{mode,response.usage.HeadersMs,response.usage.FirstUsefulOutputMs,response.usage.TotalMs});
            if(mode!="output_limit")continue;
            await using var rpc=new AppServerClient();await rpc.ConnectAsync(Environment.ProcessPath!,data,Path.Combine(data,"empty-fixture-home"));
            typeof(DeskController).GetProperty("Engine")!.SetValue(desk,rpc);typeof(DeskController).GetProperty("SubscriptionReady")!.SetValue(desk,true);
            typeof(DeskController).GetField("connectedFingerprint",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(desk,desk.State.Provider.Fingerprint);task.ProviderFingerprint=desk.State.Provider.Fingerprint;
            ((HashSet<string>)typeof(DeskController).GetField("loadedThreads",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(desk)!).Add(task.ThreadId!);
            await desk.ResumeSavedProcessAsync(process.Id,true);
            var stats=await rpc.RequestAsync("fixture/stats",new{});
            check(process.Active&&WorkQueue.Next(process)?.Id=="remaining"&&stats.Text("input").Contains("remaining"),"actual explicit resume RPC uses original checkpoint and selects remaining item");
            var run=typeof(DeskController).GetMethod("RunLearnedCommand",BindingFlags.NonPublic|BindingFlags.Instance)!;
            var reportsBeforeCache=task.Transcript.Count;
            var cached=await (Task<string>)run.Invoke(desk,[task,rpc,commandArgs,"duplicate-after-resume",task.TurnId])!;
            stats=await rpc.RequestAsync("fixture/stats",new{});
            check(stats.GetProperty("commands").GetInt32()==0&&cached.Contains("WRITE_ONCE")&&File.ReadAllText(Path.Combine(data,"already-written.txt"))=="WRITE_ONCE","completed operation returns receipt after resume without another command or file write");
            check(task.Transcript.Count==reportsBeforeCache,"cached result does not invent a new command execution progress report");
            var timeout=JsonSerializer.SerializeToElement(new{command="FIXTURE_ACTUAL_TIMEOUT",purpose="owned timeout fixture",operationId="actual-timeout",timeoutMs=1000});var clock=Stopwatch.StartNew();
            var result=await (Task<string>)run.Invoke(desk,[task,rpc,timeout,"timeout-once",task.TurnId])!;
            var timed=process.Operations.Single(o=>o.Key=="actual-timeout");
            check(timed.ExitCode==124&&timed.ResultReceived&&timed.State=="failed"&&clock.ElapsedMilliseconds>=1000,"owned real PowerShell child deadline is a received execution timeout, not validation/unknown state");
            stats=await rpc.RequestAsync("fixture/stats",new{});var count=stats.GetProperty("commands").GetInt32();
            await (Task<string>)run.Invoke(desk,[task,rpc,timeout,"timeout-repeat",task.TurnId])!;stats=await rpc.RequestAsync("fixture/stats",new{});
            check(count==stats.GetProperty("commands").GetInt32(),"received command timeout is not silently executed again");
            process.Operations.Add(new(){Key="uncertain",State="unknown"});process.State="paused";task.Status="paused";task.TurnId=null;
            blocked=false;try{await desk.ResumeSavedProcessAsync(process.Id,true);}catch(OperationReviewRequiredException){blocked=true;}
            check(blocked,"explicit resume still rejects unknown side effects even after response interruption");
        }
        File.WriteAllText(Path.Combine(root,"response-measurements.json"),JsonSerializer.Serialize(measurements,new JsonSerializerOptions{WriteIndented=true}));
    }
    private sealed class Handler(Func<HttpResponseMessage> send):HttpMessageHandler
    {protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){await Task.Delay(20,ct);return send();}}
    private sealed class Input(string data,string mode):Stream
    {
        readonly byte[] bytes=Encoding.UTF8.GetBytes(data);int offset;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {if(offset<bytes.Length){await Task.Delay(15,ct);var n=Math.Min(bytes.Length-offset,buffer.Length);bytes.AsMemory(offset,n).CopyTo(buffer);offset+=n;return n;}if(mode!="server_timeout")return 0;using var deadline=new CancellationTokenSource(80);using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,deadline.Token);await Task.Delay(Timeout.Infinite,linked.Token);return 0;}
        public override int Read(byte[]b,int o,int c)=>throw new NotSupportedException();public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[]b,int o,int c)=>throw new NotSupportedException();
    }
}
