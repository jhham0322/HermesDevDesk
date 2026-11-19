using System.Diagnostics;
using System.Text.Json;
using Hermes.Core;
internal static class CommandStartupDiagnostics
{
    internal static async Task Run(string root,string engine)
    {
        Directory.CreateDirectory(root);var results=new List<object>();
        foreach(var variant in new[]{"cmdlet","dotnet","instrumented-cmdlet"})
        {
            var data=Path.Combine(root,variant);Directory.CreateDirectory(data);var file=Path.Combine(data,"effect.txt").Replace("'","''");
            await using var rpc=new AppServerClient();await rpc.ConnectAsync(engine,data,Path.Combine(data,"empty-home"));
            var command=variant=="dotnet"?"[IO.File]::AppendAllText('"+file+"', 'ONE'+[Environment]::NewLine); Write-Output END_MARKER":"Add-Content -LiteralPath '"+file+"' -Value 'ONE'; Write-Output END_MARKER";
            if(variant=="instrumented-cmdlet")command="[Console]::WriteLine('BODY_ENTERED'); Add-Content -LiteralPath '"+file+"' -Value 'ONE'; [Console]::WriteLine('ADD_DONE'); Write-Output END_MARKER; [Console]::WriteLine('OUTPUT_DONE')";
            var timer=Stopwatch.StartNew();int? exit=null;string output="";string errorType="";
            try{var result=await rpc.RequestAsync("command/exec",new{command=new[]{"powershell.exe","-NoLogo","-NoProfile","-NonInteractive","-Command","$ErrorActionPreference='Stop';\n"+command+"\nif ($null -ne $LASTEXITCODE) { exit $LASTEXITCODE }"},cwd=data,processId="startup-fixture",streamStdoutStderr=false,streamStdin=false,tty=false,timeoutMs=30000,sandboxPolicy=new{type="dangerFullAccess"}},45);exit=result.Property("exitCode")?.GetInt32();output=result.Text("stdout")+result.Text("stderr");}
            catch(Exception ex){errorType=ex.GetType().Name;}
            var row=new{variant,elapsedMs=timer.ElapsedMilliseconds,exit,errorType,effects=File.Exists(file)?File.ReadAllLines(file).Length:0,markers=new[]{"BODY_ENTERED","ADD_DONE","END_MARKER","OUTPUT_DONE"}.Where(output.Contains).ToArray(),stdoutCharacters=output.Length,commandTimeoutMs=30000,rpcTimeoutSeconds=45,modelRequests=0};results.Add(row);Console.WriteLine(JsonSerializer.Serialize(row));
        }
        File.WriteAllText(Path.Combine(root,"startup-comparison.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
