using Hermes.Core;
internal static class CommandDispatchTests
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        Directory.CreateDirectory(root);await using var terminal=new LocalCommandSession(Path.Combine(root,"home"));int dispatched=0;
        try{await terminal.RunAsync("missing-owned-fixture.exe",root,"FIXTURE",true,()=>dispatched++);throw new Exception("Expected setup failure");}catch(Exception){check(dispatched==0&&!terminal.Running,"setup failure never accepts or clears a draft");}
        var sent=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run=terminal.RunAsync(Environment.ProcessPath!,root,"FIXTURE_WAIT",true,()=>{dispatched++;sent.SetResult();});
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        check(dispatched==1&&terminal.Running&&!run.IsCompleted,"dispatch callback fires once before command completion");
        try{await terminal.RunAsync(Environment.ProcessPath!,root,"FIXTURE_DUPLICATE",true,()=>dispatched++);throw new Exception("Duplicate command accepted");}catch(HermesException){check(dispatched==1,"repeated submission cannot dispatch another active command");}
        await terminal.StopAsync();try{await run;}catch(IOException){}
        check(!terminal.Running&&dispatched==1,"Stop ends the owned command without redispatch");
        var nonzero=await terminal.RunAsync(Environment.ProcessPath!,root,"FIXTURE_NONZERO",true,()=>dispatched++);
        check(nonzero.StartsWith("exit=7")&&dispatched==2,"nonzero command result remains a dispatched execution");
    }
}
