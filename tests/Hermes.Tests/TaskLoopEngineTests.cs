using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.Core;

internal static class TaskLoopEngineTests
{
    internal static async Task Run(string root, string exe)
    {
        Directory.CreateDirectory(root); var queue = new ConcurrentQueue<Action>(); var calls = 0; var recovered = false;
        var counter = Path.Combine(root, "counter.txt").Replace("'", "''");
        var failed = "Add-Content -LiteralPath '" + counter + "' -Value attempt; Write-Output 'Error: missing dependency'; exit 7";
        await using var server = new LoopbackHttp(async (request, stream, ct) =>
        {
            using var doc = JsonDocument.Parse(request.Body); var body = doc.RootElement; var n = Interlocked.Increment(ref calls);
            var tools = body.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("function").Text("name")).ToArray();
            string Name(string suffix) => tools.Single(t => t.EndsWith(suffix, StringComparison.Ordinal));
            var lastTool = body.GetProperty("messages").EnumerateArray().LastOrDefault(m => m.Text("role") == "tool");
            object? call = null;
            if (n is 2 or 3) call = new { name = Name("hermes_command"), arguments = JsonSerializer.Serialize(new { purpose = "dependency repair", command = failed }) };
            if (n == 4)
            {
                recovered = lastTool.Text("content").Contains("자동 개선 전환");
                call = new { name = Name("hermes_command"), arguments = JsonSerializer.Serialize(new { purpose = "dependency repair", command = "Write-Output LOOP_VERIFIED_RESULT" }) };
            }
            if (n == 5)
            {
                var evidence = long.Parse(Regex.Match(lastTool.Text("content"), @"evidenceId=(\d+)").Groups[1].Value);
                call = new { name = Name("hermes_task"), arguments = JsonSerializer.Serialize(new { action = "complete", verification = "Changed the command and observed LOOP_VERIFIED_RESULT, verifying the fixture objective.", evidenceId = evidence }) };
            }
            if (n == 8) call = new { name = Name("hermes_task"), arguments = JsonSerializer.Serialize(new { action = "blocked", verification = "Fixture requires a credential supplied by the user before further work." }) };
            if (n == 10) call = new { name = Name("hermes_command"), arguments = JsonSerializer.Serialize(new { purpose = "pending command stop fixture", command = "Start-Sleep -Seconds 25; Write-Output SHOULD_NOT_COMPLETE" }) };
            object response = call == null
                ? new { id = "loop" + n, model = "loop-model", choices = new[] { new { message = new { role = "assistant", content = "Fixture response " + n }, finish_reason = "stop" } } }
                : new { id = "loop" + n, model = "loop-model", choices = new[] { new { message = new { role = "assistant", tool_calls = new[] { new { id = "loop-call-" + n, type = "function", function = call } } }, finish_reason = "tool_calls" } } };
            await LoopbackHttp.Reply(stream, 200, "application/json", JsonSerializer.Serialize(response), ct);
        });
        await using var desk = new DeskController(Path.Combine(root, "data"), a => queue.Enqueue(a));
        desk.Configure(new() { Endpoint = server.BaseUri.AbsoluteUri + "v1/chat/completions", Deployment = "loop-model", Protocol = ProviderProtocol.GatewayChatCompletions, UseAdvancedSettings = true }, "SYNTHETIC_LOOP_ENGINE_TOKEN");
        var project = desk.Register(root); project.BrowserEnabled = project.ComputerEnabled = project.LocalMcpEnabled = false;
        void Pump() { while (queue.TryDequeue(out var action)) action(); }
        async Task Wait(Func<bool> predicate)
        { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!predicate() && watch.Elapsed < TimeSpan.FromSeconds(150)) { Pump(); await Task.Delay(20); } Pump(); if (!predicate()) throw new IOException("Loop engine timeout: " + JsonSerializer.Serialize(desk.Processes) + " calls=" + calls); }
        await desk.ConnectAsync(exe, root); Pump(); var task = desk.NewTask(project, "loop fixture");
        await desk.StartAsync(task, "Repair dependency and verify fixture output until complete.", "loop-model", true, continueUntilComplete: true);
        await Wait(() => desk.Processes.Single().State == "완료");
        var scope = ExecutionMemory.Scope(root);
        if (!recovered || calls != 6 || desk.Processes.Single().Round != 2 || File.ReadAllLines(Path.Combine(root, "counter.txt")).Length != 2 || desk.Memory.Errors(scope).Single().Occurrences != 2 || desk.Memory.Recoveries(scope).Count != 1)
            throw new IOException("Loop/recovery did not match expected two turns, two failures and successful alternative");
        var stop = desk.NewTask(project, "stop queued loop"); await desk.StartAsync(stop, "Remain incomplete until user stops.", "loop-model", true, continueUntilComplete: true);
        await Wait(() => stop.Status == "계속 실행 대기"); var stopProcess = desk.Processes.Single(p => p.TaskId == stop.Id); await desk.StopProcessAsync(stopProcess.Id);
        await Task.Delay(1400); Pump(); if (calls != 7 || stopProcess.Active) throw new IOException("Stopped queued loop restarted");
        var blocked = desk.NewTask(project, "external blocker"); await desk.StartAsync(blocked, "Work requires fixture credential.", "loop-model", true, continueUntilComplete: true);
        await Wait(() => blocked.Status == "사용자 확인 필요");
        var active = desk.NewTask(project, "stop running command"); await desk.StartAsync(active, "Run fixture command until stopped.", "loop-model", true, continueUntilComplete: true);
        await Wait(() => active.Transcript.Any(t => t.Kind == "작업 진행" && t.Text.Contains("Start-Sleep")));
        await desk.StopProcessAsync(desk.Processes.Single(p => p.TaskId == active.Id).Id); await Wait(() => active.Status == "중단됨");
        await Task.Delay(1200); Pump(); if (desk.Processes.Single(p => p.TaskId == active.Id).Active) throw new IOException("Stopped active command loop resurrected");
        File.WriteAllText(Path.Combine(root, "task-loop-engine-report.json"), JsonSerializer.Serialize(new { passed = true, calls, completionLoopRounds = 2, errorCount = 2, recovered, queuedStop = true, activeCommandStop = true, blocked = true, actualOfficialEngine = true, realModelCalls = 0 }));
        Console.WriteLine("PASS official engine: completion loop, automatic recovery, error/improvement RAG, queued and active process stop, external blocker");
    }
}
