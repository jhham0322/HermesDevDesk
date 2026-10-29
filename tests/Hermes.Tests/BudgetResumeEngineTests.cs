using System.Collections.Concurrent;
using System.Text.Json;
using Hermes.Core;
internal static class BudgetResumeEngineTests
{
    internal static async Task Run(string root,string exe)
    {
        Directory.CreateDirectory(root);var queue=new ConcurrentQueue<Action>();var calls=0;
        await using var server=new LoopbackHttp(async (_,stream,ct)=> { Interlocked.Increment(ref calls); await LoopbackHttp.Reply(stream,200,"application/json",JsonSerializer.Serialize(new{id="owned-budget",model="actual-local-budget",choices=new[]{new{message=new{role="assistant",content="Partial result preserved. Verification remains."},finish_reason="stop"}}}),ct); });
        var data=Path.Combine(root,"data");string processId,taskId,threadId;
        void Pump(){while(queue.TryDequeue(out var action))action();}
        async Task Wait(Func<bool> ready){var clock=System.Diagnostics.Stopwatch.StartNew();while(!ready()&&clock.Elapsed.TotalSeconds<45){Pump();await Task.Delay(20);}Pump();if(!ready())throw new IOException("Owned budget engine timed out");}
        await using(var desk=new DeskController(data,a=>queue.Enqueue(a)))
        {
            desk.Configure(new(){Endpoint=server.BaseUri.AbsoluteUri+"v1/chat/completions",Deployment="budget-alias",Protocol=ProviderProtocol.GatewayChatCompletions,UseAdvancedSettings=true},"OWNED_SYNTHETIC_BUDGET_KEY");
            var project=desk.Register(root);project.BrowserEnabled=project.ComputerEnabled=project.LocalMcpEnabled=false;
            desk.State.Desktop.ExecutionBudget.MaxRounds=1;await desk.ConnectAsync(exe,root);Pump();
            var task=desk.NewTask(project,"Budget restart fixture");await desk.StartAsync(task,"Deliver a partial result; host will pause this stage.","budget-alias",true,continueUntilComplete:true);
            await Wait(()=>desk.Processes.Single().PausedForBudget);var p=desk.Processes.Single();processId=p.Id;taskId=task.Id;threadId=task.ThreadId!;
            if(p.Active||task.Status=="완료"||p.Round!=1)throw new IOException("Stage budget did not pause at turn boundary");desk.Save();
        }
        Pump();
        await using(var desk=new DeskController(data,a=>queue.Enqueue(a)))
        {
            await desk.ConnectAsync(exe,root);Pump();await desk.ResumeBudgetAsync(processId);
            await Wait(()=>desk.Processes.Count==2&&desk.Processes.All(p=>!p.Active));
            var task=desk.State.Tasks.Single(t=>t.Id==taskId);var resumed=desk.Processes.Single(p=>p.Id!=processId);
            if(!resumed.PausedForBudget||resumed.Round!=1||task.ThreadId!=threadId||task.Transcript.Count(t=>t.Origin=="model"&&t.ResponseModel=="actual-local-budget")<2)throw new IOException("Explicit resume did not preserve thread, stage count and model provenance");
            File.WriteAllText(Path.Combine(root,"budget-resume-engine.json"),JsonSerializer.Serialize(new{passed=true,phases=2,calls,sameThread=true,modelSourcePreserved=true,realCompanyRequests=0}));
        }
        Console.WriteLine("PASS: explicit budget resume after a new engine process preserves original thread, partial results, limits and model source. Local synthetic API only. Report: "+Path.Combine(root,"budget-resume-engine.json"));
    }
}
