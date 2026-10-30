using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Headers;
using Hermes.Core;
internal static class ExecutionPolicyTests
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        Directory.CreateDirectory(root);
        await using var desk = new DeskController(Path.Combine(root,"data")); var project = desk.Register(root);
        var task = desk.NewTask(project,"source"); task.Model="selected-local";task.ThreadId="source-thread";task.TurnId="turn-one";
        var at=DateTimeOffset.UtcNow;
        desk.ObserveCompanyUsage(new UsageEntry { Id="request-one", TaskId=task.Id, At=at, Deployment="selected-local", Status="started" });
        ProgressMessages.Delta(task,"summary","0","[완료] model text is untrusted"); ChatMessages.Delta(task,"message","first ");
        check(TranscriptPresentation.Label(task.Transcript[0]).StartsWith("[미확인][분석]"),"missing response model stays unknown instead of impersonating selected model");
        desk.ObserveCompanyUsage(new UsageEntry { Id="request-one", TaskId=task.Id, At=at, Deployment="selected-local", Model="actual-local-v2", Status="completed" });
        check(task.Transcript.All(e=>e.ResponseModel=="actual-local-v2") && TranscriptPresentation.Label(task.Transcript[0]).Contains("요청 selected-local"),"actual response model overrides alias with requested model retained");
        task.Model="selected-next";task.TurnId="turn-two";
        desk.ObserveCompanyUsage(new UsageEntry {Id="request-two",TaskId=task.Id,At=at.AddSeconds(1),Deployment=task.Model,Status="started"});
        ChatMessages.Delta(task,"next-message","second");
        desk.ObserveCompanyUsage(new UsageEntry {Id="request-one",TaskId=task.Id,At=at,Deployment="selected-local",Model="actual-local-v2",Status="completed"});
        check(task.ActiveRequestId=="request-two" && task.Transcript.Last().ResponseModel=="", "late prior response never relabels a new model turn");
        ChatMessages.Complete(task,"message","first complete");
        check(task.Transcript.Single(e=>e.ItemId=="message").RequestedModel=="selected-local", "message completion retains original request identity after model switch");
        check(TranscriptPresentation.Label(task.Transcript[0]).Contains("[분석]") && !TranscriptPresentation.Label(task.Transcript[0]).Contains("[완료]"),"model text tags cannot forge application stage");
        check(TranscriptPresentation.Label(TranscriptPresentation.Host("중간보고","facts")).StartsWith("[Hermes]"),"host report never appears as model output");
        using(var tool=JsonDocument.Parse("""{"id":"cmd","tool":"hermes_command","success":true,"status":"completed"}""")) ToolProgress.Complete(task,tool.RootElement);
        check(TranscriptPresentation.Label(task.Transcript.Last()).StartsWith("[도구: hermes_command][완료]"),"observed completed tool has its own source and operation completion stage");
        using(var plan=JsonDocument.Parse("""{"threadId":"source-thread","plan":[]}"""))desk.Handle(new("turn/plan/updated",plan.RootElement));
        check(task.Transcript.Last().Origin=="model"&&task.Transcript.Last().Stage=="분석","engine plan retains model origin instead of impersonating a host report");
        using(var mcp=JsonDocument.Parse("""{"threadId":"source-thread","item":{"id":"owned-mcp","type":"mcpToolCall","server":"project","tool":"read_file"}}"""))desk.Handle(new("item/completed",mcp.RootElement));
        check(task.Transcript.Last().Origin=="도구: MCP/project/read_file","native MCP result preserves its actual server and tool origin");
        var before=task.Transcript.Count; desk.State.Desktop.ShowProgressSummaries=false;
        check(task.Transcript.Count(e=>!ProgressMessages.IsSummary(e)) < before && task.Transcript.Any(e=>e.Text=="first complete"),"hiding reports preserves final answer and original records");
        desk.Save();var restored=new StateStore(desk.DataDirectory).Load();
        check(restored.Tasks.Single(t=>t.Id==task.Id).Transcript.First().ResponseModel=="actual-local-v2", "restart preserves immutable source provenance");
        check(JsonSerializer.Deserialize<AppState>("{}")!.Desktop.ExecutionBudget.MaxRounds==3,"old settings migrate to bounded defaults");
        var budget=new ExecutionBudget();var process=new TaskProcess{Budget=budget,Started=DateTime.UtcNow};
        check(budget.Reason(process,true,DateTime.UtcNow)=="","fresh stage has remaining budget");
        process.Round=3;check(budget.Reason(process,true,DateTime.UtcNow).Contains("라운드") && budget.Reason(process,false,DateTime.UtcNow)=="","round budget blocks next turn without killing active work");
        process.Round=1;process.ToolCalls=20;check(budget.Reason(process,false,DateTime.UtcNow)=="","legacy tool count is telemetry and never rejects managed work");
        process.ToolCalls=0;check(budget.Reason(process,false,DateTime.UtcNow.AddMinutes(11)).Contains("시간"),"elapsed budget is evaluated at safe boundary");
        check(ExecutionBudget.Digest(new[]{"same","same"})==ExecutionBudget.Digest(new[]{"same"}),"identical successful tool outcomes are not new progress");
        var btask=desk.NewTask(project,"budget pause");btask.ThreadId="budget-thread";btask.TurnId="budget-turn";btask.Status="실행 중";
        desk.State.Desktop.ExecutionBudget.MaxRounds=1;var bp=desk.BeginProcess(btask,"bounded objective");
        using(var done=JsonDocument.Parse("""{"threadId":"budget-thread","turn":{"id":"budget-turn","status":"completed"}}""")) desk.Handle(new("turn/completed",done.RootElement));
        check(bp.PausedForBudget && !bp.Active && btask.Status!="완료" && bp.Checkpoint.Contains("다음 행동"),"budget stop stores partial checkpoint and never claims completion");
        check(btask.Transcript.Last().Stage=="중간보고", "budget boundary is an intermediate report");
        var rejected=false;try{await desk.ResumeBudgetAsync(bp.Id);}catch(HermesException){rejected=true;}check(rejected && !desk.Processes.Any(p=>p.TaskId==btask.Id&&p.Active),"resume requires explicit valid model connection and cannot resurrect offline work");
        desk.State.Desktop.ExecutionBudget.MaxRounds=10;desk.State.Desktop.ExecutionBudget.MaxStagnantRounds=1;
        var ntask=desk.NewTask(project,"no progress");ntask.ThreadId="no-progress-thread";ntask.TurnId="nt";ntask.Status="실행 중";var np=desk.BeginProcess(ntask,"avoid spinning");
        ProgressMessages.Delta(ntask,"reasoning","0","Still working");
        using(var done=JsonDocument.Parse("""{"threadId":"no-progress-thread","turn":{"id":"nt","status":"completed"}}"""))desk.Handle(new("turn/completed",done.RootElement));
        check(np.PausedForBudget && np.StagnantRounds==1,"new prose or heartbeat alone does not reset stagnation");
        desk.Save();check(new StateStore(desk.DataDirectory).Load().Processes.Single(p=>p.Id==bp.Id).Checkpoint==bp.Checkpoint,"budget checkpoint survives restart");
        var observed=new List<(string,bool)>(); var adaptation=ChatAdapter.Request(new JsonObject{["model"]="alias"});
        var wire="data: {\"model\":\"actual-stream\",\"choices\":[{\"delta\":{\"content\":\"hello\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        using var output=new MemoryStream();using var input=new MemoryStream(Encoding.UTF8.GetBytes(wire));await ChatStream.Translate(input,output,adaptation,CancellationToken.None,(model,useful)=>observed.Add((model,useful)));
        check(observed.Any(x=>x.Item1=="actual-stream"&&x.Item2),"stream observes actual model and first useful output before completion");
        observed.Clear();using var noModel=new MemoryStream(Encoding.UTF8.GetBytes(wire.Replace("\"model\":\"actual-stream\",","")));using var noModelOut=new MemoryStream();await ChatStream.Translate(noModel,noModelOut,adaptation,CancellationToken.None,(model,useful)=>observed.Add((model,useful)));
        check(observed.Where(x=>x.Item2).All(x=>x.Item1==""),"stream without model metadata does not fabricate actual model from request");
        // HTTP loopback only; no real company endpoint, no credentials.
        await using var server=new LoopbackHttp((_,stream,ct)=>LoopbackHttp.Reply(stream,200,"application/json","""{"id":"owned","model":"observed-local","choices":[{"message":{"role":"assistant","content":"local"},"finish_reason":"stop"}]}""",ct));
        var settings=new ProviderSettings{Endpoint=server.BaseUri.AbsoluteUri,Deployment="alias",Protocol=ProviderProtocol.GatewayChatCompletions,Authentication=ProviderAuth.None,UseAdvancedSettings=true};
        await using var bridge=new CompanyBridge(settings,new ProtectedKeyStore(Path.Combine(root,"owned-keys")),true);var usages=new List<UsageEntry>();bridge.Usage+=e=>usages.Add(e);
        using var client=new HttpClient();client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",bridge.Capability);
        using var response=await client.PostAsync(bridge.BaseUri.AbsoluteUri+"/responses",new StringContent("{\"input\":\"owned\",\"stream\":false}"));await response.Content.ReadAsStringAsync();
        await Task.Delay(50);var final=usages.Last();
        check(final.Model=="observed-local" && final.Deployment=="alias", "bridge records actual and requested model separately");
        check(final.HeadersMs.HasValue&&final.FirstUsefulOutputMs.HasValue&&final.TotalMs.HasValue&&final.Buffered,"privacy-preserving timing segments recorded for buffered local response");
        check(final.TotalMs>=final.HeadersMs&&final.FirstUsefulOutputMs>=final.HeadersMs,"timing segments use one monotonic request clock");
    }
}
