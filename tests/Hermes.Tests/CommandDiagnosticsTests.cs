using System.Diagnostics;
using System.Text.Json;
using Hermes.Core;
internal static class CommandDiagnosticsTests
{
    internal static async Task Run(string root,Action<bool,string> check,string? javac=null)
    {
        Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"relative"));
        File.WriteAllText(Path.Combine(root,"relative","Main.java"),"class Main { void test() { System.out.println(MissingName); } }");
        File.WriteAllText(Path.Combine(root,"gradlew.bat"),"@echo synthetic-wrapper");
        string Pre(object args){using var json=JsonDocument.Parse(JsonSerializer.Serialize(args));return CommandDiagnostics.Preflight(root,json.RootElement);}
        bool Reject(object args){try{Pre(args);return false;}catch(HermesException ex){return ex.Message.Contains("[COMMAND_PREFLIGHT]")&&ex.Message.Contains("No command was executed");}}
        check(Pre(new{command="New-Item new-output; Set-Content new-output/result.txt OK"})==root,"ordinary shell creation is not guessed to be a missing input");
        check(Pre(new{cwd="relative",inputFiles=new[]{"Main.java"},buildTool="../gradlew.bat"})==Path.Combine(root,"relative"),"relative cwd, explicit input and wrapper resolve without executing them");
        check(Reject(new{cwd="missing"}),"missing cwd is rejected before dispatch");
        check(Reject(new{inputFiles=new[]{"missing.java"}}),"missing explicit input is rejected before dispatch");
        check(Reject(new{buildTool="./missing-gradlew.bat"}),"missing explicit build tool is rejected before dispatch");
        check(Reject(new{inputFiles=new[]{"relative"}}),"directory declared as input file is rejected");
        for(int i=0;i<3;i++)check(Reject(new{inputFiles=new[]{"missing.java"}}),"unchanged missing dependency remains a no-dispatch validation rejection "+i);
        File.WriteAllText(Path.Combine(root,"missing.java"),"class Repaired {}");
        check(Pre(new{inputFiles=new[]{"missing.java"}})==root,"corrected dependency is rechecked rather than permanently cached as missing");
        var compiler="Main.java:1: error: cannot find symbol\n class Main { MissingName x; }\n              ^\n  symbol:   class MissingName\n  location: class Main\n1 error\n";
        if(javac!=null)
        {
            check(Pre(new{buildTool=javac})==root,"declared installed compiler is readable");
            var info=new ProcessStartInfo(javac){WorkingDirectory=Path.Combine(root,"relative"),RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
            info.ArgumentList.Add("Main.java");
            using var process=Process.Start(info)!;
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));}catch{if(!process.HasExited)process.Kill(true);throw;}
            compiler=await stderr+await stdout;
            check(process.ExitCode!=0&&compiler.Contains("symbol:")&&compiler.Contains("location:"),"actual javac on owned synthetic source produces first error and symbol/location");
            File.WriteAllText(Path.Combine(root,"synthetic-javac-output.txt"),compiler);
        }
        var output="> Task :compileJava\n"+new string('n',26000)+"\n"+compiler+new string('t',35000)+"\nBUILD FAILED\npassword=SYNTHETIC_PASSWORD";
        check(!ExecutionMemory.Clean(output,20000).Contains("symbol:"),"baseline prefix-only truncation reproduces missing compiler diagnostic");
        var display=CommandDiagnostics.Display(output,20000);
        check(display.Length<=20000&&display.Contains(".java:1:")&&display.Contains("error:")&&display.Contains("symbol:")&&display.Contains("location:"),"bounded Gradle-shaped preview retains first javac error, file/line, symbol/location");
        check(!display.Contains("SYNTHETIC_PASSWORD"),"diagnostic preview does not expose synthetic password value");
        var longSource="Main.java:9: error: cannot find symbol\n"+new string('s',8000)+"\n symbol: class MissingType token=SYNTHETIC_DIAGNOSTIC_SECRET\n location: class Main\n";
        var longDisplay=CommandDiagnostics.Display(longSource,4000);
        check(longDisplay.Contains("symbol:")&&longDisplay.Contains("location:")&&!longDisplay.Contains("SYNTHETIC_DIAGNOSTIC_SECRET"),"long source line cannot displace symbol/location or leak synthetic credential assignment");
        await using var desk=new DeskController(Path.Combine(root,"data"));var project=desk.Register(root);var task=desk.NewTask(project,"Synthetic compile");var p=desk.BeginProcess(task,"Preserve observed compiler error");
        var identity=CommandDiagnostics.RetryIdentity("same-command",root,root);var other=CommandDiagnostics.RetryIdentity("same-command",Path.Combine(root,"relative"),root);
        desk.Memory.Record(ExecutionMemory.Scope(root),"path-scope","bad-path-one","path context","command","same-command",false,"file not found",identity);desk.Memory.Record(ExecutionMemory.Scope(root),"path-scope","bad-path-two","path context","command","same-command",false,"file not found",identity);
        check(desk.Memory.BlockReason(ExecutionMemory.Scope(root),"path-scope","command","same-command",identity)!=null&&desk.Memory.BlockReason(ExecutionMemory.Scope(root),"path-scope","command","same-command",other)==null,"actual cwd change permits a corrected context while identical context remains repeat-blocked");
        check(CommandDiagnostics.RetryIdentity("same-command",Path.Combine(root,"relative",".."),root)==identity,"equivalent relative cwd cannot bypass repeat failure protection");
        var run=typeof(DeskController).GetMethod("RunLearnedCommand",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        for(int retry=0;retry<3;retry++)
        {
            using var args=JsonDocument.Parse("""{"command":"javac missing-for-dispatch.java","purpose":"verify declared input","inputFiles":["missing-for-dispatch.java"]}""");
            bool noDispatch=false;
            try{await (Task<string>)run.Invoke(desk,[task,null,args.RootElement,"invalid-"+retry,null])!;}
            catch(HermesException ex){noDispatch=ex.Message.Contains("[COMMAND_PREFLIGHT]");}
            check(noDispatch&&p.Operations.Count==0&&desk.Memory.Recent(ExecutionMemory.Scope(root),task.Id).Count==0,"actual command entry rejects missing input before engine/receipt/attempt "+retry);
        }
        var receipt=new OperationReceipt{Key="compile-once"};p.Operations.Add(receipt);
        using(var result=JsonDocument.Parse(JsonSerializer.Serialize(new{exitCode=1,stdout=output,stderr=""})))desk.RecordCommandResponse(task,receipt,result.RootElement);
        check(receipt.State=="failed"&&receipt.Result.Contains("symbol:")&&receipt.Result.Contains("location:"),"durable command response retains first compiler error without claiming success");
        var evidence=desk.Memory.Record(ExecutionMemory.Scope(root),task.Id,"compile-once","synthetic compilation","command","javac Main.java",false,output);
        check(evidence.Outcome.Contains("symbol:")&&evidence.Outcome.Contains("location:")&&evidence.Outcome.Length<=4000,"RAG preview preserves compiler diagnostic at existing output bound");
        var different=desk.Memory.Record(ExecutionMemory.Scope(root),task.Id,"compile-different","synthetic compilation","command","javac Main.java",false,output.Replace("cannot find symbol","incompatible types"));
        check(evidence.ErrorKey!=different.ErrorKey&&desk.Memory.BlockReason(ExecutionMemory.Scope(root),task.Id,"command","javac Main.java")==null,"distinct compiler failures retain distinct retry identities rather than a generic preview header");
        desk.Save();var loaded=new StateStore(desk.DataDirectory).Load();check(loaded.Processes.Single(x=>x.Id==p.Id).Operations.Single().Result.Contains("symbol:"),"compiler diagnostic survives state restart");
        File.WriteAllText(Path.Combine(root,"display.txt"),display);
        ExecutionMemoryTests.Run(Path.Combine(root,"memory-regression"),check);
    }
}
