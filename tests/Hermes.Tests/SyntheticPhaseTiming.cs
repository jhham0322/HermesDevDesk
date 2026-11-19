using System.Diagnostics;
using System.Text.Json;
using Hermes.Core;
internal static class SyntheticPhaseTiming
{
    internal static async Task Run(string root,string javac)
    {
        Directory.CreateDirectory(root);var all=Stopwatch.StartNew();var phase=Stopwatch.StartNew();
        var files=Path.Combine(root,"files");Directory.CreateDirectory(files);
        for(int i=0;i<200;i++)File.WriteAllText(Path.Combine(files,"Fixture"+i+".java"),"// synthetic search fixture");
        var setupMs=phase.Elapsed.TotalMilliseconds;phase.Restart();
        var found=Directory.EnumerateFiles(files,"*.java",SearchOption.AllDirectories).Count();var searchMs=phase.Elapsed.TotalMilliseconds;
        await using var desk=new DeskController(Path.Combine(root,"state"));var project=desk.Register(root);var task=desk.NewTask(project,"Synthetic find/build/verify");task.ThreadId="timing-thread";task.TurnId="timing-turn";var process=desk.BeginProcess(task,"Synthetic search, compile and corrected retry");
        var relay=await ResponseRecoveryTests.Relay(Path.Combine(root,"model"),"output_limit",task.Id,task.TurnId);desk.ObserveCompanyUsage(relay.usage);
        var source=Path.Combine(root,"Main.java");File.WriteAllText(source,"class Main { void test() { System.out.println(MissingName); } }");
        async Task<(double ms,int exit,string output)> Compile()
        {
            var info=new ProcessStartInfo(javac){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add("Main.java");
            var timer=Stopwatch.StartNew();using var child=Process.Start(info)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
            try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));}catch{if(!child.HasExited)child.Kill(true);throw;}
            return(timer.Elapsed.TotalMilliseconds,child.ExitCode,await stdout+"\n"+await stderr);
        }
        var failed=await Compile();if(failed.exit==0)throw new IOException("Expected synthetic compiler failure");
        phase.Restart();desk.Memory.Record(ExecutionMemory.Scope(root),task.Id,"build-first","synthetic build","command","javac Main.java",false,failed.output);desk.Save();var failedRecordMs=phase.Elapsed.TotalMilliseconds;
        phase.Restart();await Task.Delay(100);var retryWaitMs=phase.Elapsed.TotalMilliseconds; // Explicit fixture delay; not a product retry policy.
        File.WriteAllText(source,"class Main { void test() { System.out.println(\"DONE\"); } }");var corrected=await Compile();if(corrected.exit!=0)throw new IOException("Corrected synthetic source did not compile");
        phase.Restart();desk.Memory.Record(ExecutionMemory.Scope(root),task.Id,"build-corrected","synthetic build","command","javac Main.java",true,corrected.output);desk.Save();var correctedRecordMs=phase.Elapsed.TotalMilliseconds;
        var report=new{syntheticOnly=true,realProviderCalls=0,representativeRuns=1,foundFiles=found,fixtureSetupMs=setupMs,fileSearchMs=searchMs,modelHeadersMs=relay.usage.HeadersMs,modelFirstUsefulOutputMs=relay.usage.FirstUsefulOutputMs,modelTotalMs=relay.usage.TotalMs,firstBuildMs=failed.ms,firstExit=failed.exit,correctedBuildMs=corrected.ms,correctedExit=corrected.exit,retryBackoffMs=retryWaitMs,retryBackoffSource="intentional 100ms fixture delay, not measured product backoff",failedRecordAndSaveMs=failedRecordMs,correctedRecordAndSaveMs=correctedRecordMs,elapsedMs=all.Elapsed.TotalMilliseconds,unmeasured=new[]{"actual model queue vs inference split","host tool queue contention","native WPF input/scroll/render latency","real Gradle build","real company/server latency"},existingCounters=new{process.ToolQueueMs,process.ToolExecutionMs,process.AutomaticWaitMs},counterScope="direct compiler fixture bypasses managed-tool dispatcher; zero counters are not zero-cost measurements",performancePatch="none; one synthetic run with intentional waits does not establish a product bottleneck"};
        File.WriteAllText(Path.Combine(root,"phase-timing.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));
    }
}
