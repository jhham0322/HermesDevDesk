using System.Reflection;
using System.Text.Json;
using Hermes.Core;
internal static class QueueCommandValidationTests
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        Directory.CreateDirectory(root);
        await using var desk = new DeskController(Path.Combine(root,"data"));
        var project=desk.Register(root);var task=desk.NewTask(project,"Synthetic queued commands");task.ThreadId="synthetic-thread";
        var process=desk.BeginProcess(task,"Preserve queued command identities across resume");
        process.Items.Add(new(){Id="item-a",Title="A",State="running"});
        var begin=typeof(DeskController).GetMethod("BeginOperation",BindingFlags.Instance|BindingFlags.NonPublic)!;
        OperationReceipt Start(DeskController owner,WorkItem t,string json,string eventId)
        {using var doc=JsonDocument.Parse(json);return (OperationReceipt)begin.Invoke(owner,[t,doc.RootElement,eventId])!;}
        const string omitted="""{"command":"Write-Output SYNTHETIC_OK","purpose":"verify owned fixture"}""";
        var first=Start(desk,task,omitted,"first-entry");first.State="completed";first.Result="SYNTHETIC_OK";
        Safety.RegisterSecret("SYNTHETIC_OPERATION_SECRET");bool secretRejected=false;
        try{Start(desk,task,"""{"command":"owned","purpose":"owned fixture","operationId":"SYNTHETIC_OPERATION_SECRET"}""","secret-op");}catch(TargetInvocationException ex) when(ex.InnerException is HermesException){secretRejected=true;}
        check(secretRejected&&process.Operations.Count==1,"credential-bearing operation identity is rejected rather than persisted");
        check(first.Key.Length>0&&first.ItemId=="item-a","missing queued operation ID receives stable item-scoped identity");
        var again=Start(desk,task,omitted,"new-turn-event");
        check(ReferenceEquals(first,again)&&process.Operations.Count==1,"resume event change returns completed receipt without another execution");
        desk.Save();
        await using var restored=new DeskController(desk.DataDirectory);
        var savedTask=restored.State.Tasks.Single(t=>t.Id==task.Id);
        // Simulate admission after the real resume guards; no engine or work is run.
        restored.State.Processes.Single(p=>p.Id==process.Id).State=process.State;
        var recovered=Start(restored,savedTask,omitted,"restart-event");
        check(recovered.Key==first.Key&&recovered.Result==first.Result,"restart preserves generated identity and completed result");
        process.Items[0].State="verified";process.Items.Add(new(){Id="item-b",Title="B",State="running"});
        var second=Start(desk,task,omitted,"second-item");
        check(second.Key!=first.Key&&second.ItemId=="item-b","identical command in another item has separate identity");
        second.State="unknown";bool blocked=false;
        try{Start(desk,task,omitted,"unknown-retry");}catch(TargetInvocationException ex) when(ex.InnerException is HermesException){blocked=true;}
        check(blocked&&process.Operations.Count==2,"unknown effects block execution and preserve receipt");
        second.State="completed";process.Items[1].State="verified";blocked=false;
        try{Start(desk,task,omitted,"completed-item");}catch(TargetInvocationException ex) when(ex.InnerException is HermesException){blocked=true;}
        check(blocked,"completed items cannot be restarted by a command request");
        foreach(var json in new[]{"{\"timeoutMs\":999}","{\"timeoutMs\":120001}","{\"timeoutMs\":1.5}","{\"timeoutMs\":\"30000\"}","{\"timeoutMs\":null}","{\"timeoutMs\":2147483648}","{\"timeoutMs\":true}"})
        {
            using var doc=JsonDocument.Parse(json);bool rejected=false;
            try{DeskController.ValidateCommandTimeout(doc.RootElement);}catch(HermesException ex){rejected=ex.Message.Contains("No command was executed");}
            check(rejected,"invalid timeout is input rejection before dispatch: "+json);
        }
        foreach(var value in new[]{1000,30000,120000})
        {using var doc=JsonDocument.Parse("{\"timeoutMs\":"+value+"}");check(DeskController.ValidateCommandTimeout(doc.RootElement)==value,"valid timeout boundary preserved: "+value);}
        using(var doc=JsonDocument.Parse("{}"))check(DeskController.ValidateCommandTimeout(doc.RootElement)==30000,"omitted timeout keeps existing 30 second default");
        process.Items[1].State="running";
        var timed=Start(desk,task,"""{"command":"synthetic-timeout","purpose":"classify received timeout","operationId":"timeout-once"}""","timeout-event");
        using(var doc=JsonDocument.Parse("""{"exitCode":124,"stdout":"","stderr":"Synthetic command timeout"}"""))desk.RecordCommandResponse(task,timed,doc.RootElement);
        check(timed.State=="failed"&&timed.ResultReceived&&timed.ExitCode==124,"received execution timeout remains failed receipt, not input validation or unknown effect");
        check(ReferenceEquals(timed,Start(desk,task,"""{"command":"synthetic-timeout","purpose":"classify received timeout","operationId":"timeout-once"}""","timeout-retry")),"received timeout is not automatically re-executed");
    }
}
