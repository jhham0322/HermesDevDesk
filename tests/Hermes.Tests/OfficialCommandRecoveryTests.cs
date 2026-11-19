using System.Reflection;
using System.Text.Json;
using Hermes.Core;
internal static class OfficialCommandRecoveryTests
{
    internal static async Task Run(string root,string codex,Action<bool,string> check)
    {
        Directory.CreateDirectory(root);await using var rpc=new AppServerClient();await rpc.ConnectAsync(codex,root,Path.Combine(root,"empty-official-home"));
        await using var desk=new DeskController(Path.Combine(root,"state"));var project=desk.Register(root);var task=desk.NewTask(project,"Owned official command fixture");task.ThreadId="offline-command-thread";task.TurnId="offline-command-turn";task.Status="실행 중";var p=desk.BeginProcess(task,"Verify local command receipts without model inference");p.Items.Add(new(){Id="command",Title="Owned command",State="running"});
        var run=typeof(DeskController).GetMethod("RunLearnedCommand",BindingFlags.Instance|BindingFlags.NonPublic)!;var counter=Path.Combine(root,"once.txt").Replace("'","''");
        var args=JsonSerializer.SerializeToElement(new{command="[IO.File]::AppendAllText('"+counter+"',\"ONE`n\"); Write-Output OFFICIAL_ONCE",purpose="verify owned file once",operationId="official-once",timeoutMs=30000});
        var clock=System.Diagnostics.Stopwatch.StartNew();var first=await (Task<string>)run.Invoke(desk,[task,rpc,args,"official-first",task.TurnId])!;var firstMs=clock.ElapsedMilliseconds;
        check(p.Operations.Single().State=="completed"&&p.Operations.Single().ExitCode==0&&first.Contains("OFFICIAL_ONCE"),"official local engine returns concrete successful command evidence without a model turn");
        await (Task<string>)run.Invoke(desk,[task,rpc,args,"official-retry",task.TurnId])!;
        check(File.ReadAllLines(counter).Length==1&&p.Operations.Count==1,"official-engine completed command is not re-executed when requested again");
        var timed=JsonSerializer.SerializeToElement(new{command="Write-Output BEFORE_TIMEOUT; Start-Sleep -Seconds 5",purpose="verify local command timeout",operationId="official-timeout",timeoutMs=1000});clock.Restart();
        await (Task<string>)run.Invoke(desk,[task,rpc,timed,"official-timeout",task.TurnId])!;var timeoutMs=clock.ElapsedMilliseconds;
        var receipt=p.Operations.Single(o=>o.Key=="official-timeout");
        check(receipt.State=="failed"&&receipt.ResultReceived&&receipt.ExitCode==124,"official engine command timeout is a received failed execution rather than an input or unknown-result error");
        desk.Save();check(new StateStore(desk.DataDirectory).Load().Processes.Single().Operations.Count==2,"official local success and timeout receipts survive persisted reload");
        File.WriteAllText(Path.Combine(root,"official-command-report.json"),JsonSerializer.Serialize(new{firstMs,timeoutMs,modelRequests=0,realCompanyRequests=0,emptyIsolatedProfile=true,receiptStates=p.Operations.Select(o=>new{o.State,o.ExitCode,o.ResultReceived})},new JsonSerializerOptions{WriteIndented=true}));
    }
}
