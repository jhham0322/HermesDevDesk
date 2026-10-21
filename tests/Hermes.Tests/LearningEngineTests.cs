using System.Collections.Concurrent;
using System.Text.Json;
using Hermes.Core;
internal static class LearningEngineTests
{
    internal static async Task Run(string root, string exe)
    {
        Directory.CreateDirectory(root); var queue = new ConcurrentQueue<Action>(); var calls = 0; var blocked = false; var autoRetrieved = false; var improvement = "Check dependency first, change the method, then verify RAG_LEARNED_OK.";
        var counter = Path.Combine(root, "counter.txt").Replace("'", "''");
        var failed = "Add-Content -LiteralPath '" + counter + "' -Value attempt; Write-Output 'Error: missing dependency'; exit 7";
        await using var server = new LoopbackHttp(async (request, stream, ct) =>
        {
            using var doc = JsonDocument.Parse(request.Body); var body = doc.RootElement; var n = Interlocked.Increment(ref calls);
            var tools = body.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("function").Text("name")).ToArray();
            if (tools.Any(t => t.Contains("exec_command") || t.Contains("shell_command"))) throw new IOException("Native unguarded shell still exposed");
            string Name(string suffix) => tools.Single(t => t.EndsWith(suffix, StringComparison.Ordinal));
            var messages = body.GetProperty("messages").EnumerateArray().ToArray();
            var lastTool = messages.LastOrDefault(m => m.Text("role") == "tool");
            object? call = null; string answer = "RAG_ENGINE_DONE";
            if (n <= 3) call = new { name = Name("hermes_command"), arguments = JsonSerializer.Serialize(new { purpose = "repair dependency fixture", command = failed }) };
            if (n == 4) { blocked = lastTool.Text("content").Contains("2회 반복"); call = new { name = Name("hermes_command"), arguments = JsonSerializer.Serialize(new { purpose = "repair dependency fixture", command = "Write-Output RAG_LEARNED_OK" }) }; }
            if (n == 5) call = new { name = Name("hermes_memory"), arguments = JsonSerializer.Serialize(new { action = "search", query = "repair dependency fixture" }) };
            if (n == 6)
            {
                var text = lastTool.Text("content"); var line = text.Split('\n').First(l => l.StartsWith('{') && l.Contains("EvidenceId") && l.Contains("command")); using var method = JsonDocument.Parse(line);
                call = new { name = Name("hermes_memory"), arguments = JsonSerializer.Serialize(new { action = "improve", methodId = method.RootElement.GetProperty("Id").GetInt64(), evidenceId = method.RootElement.GetProperty("EvidenceId").GetInt64(), notes = improvement }) };
            }
            if (n == 9) call = new { name = Name("hermes_command"), arguments = JsonSerializer.Serialize(new { purpose = "read-only fixture", command = "Set-Content -LiteralPath '" + Path.Combine(root, "readonly-should-not-exist.txt").Replace("'", "''") + "' -Value forbidden" }) };
            if (n == 8) autoRetrieved = messages.Any(m => m.Text("content").Contains(improvement));
            object response = call == null
                ? new { id = "learn" + n, model = "learning-model", choices = new[] { new { message = new { role = "assistant", content = answer }, finish_reason = "stop" } } }
                : new { id = "learn" + n, model = "learning-model", choices = new[] { new { message = new { role = "assistant", tool_calls = new[] { new { id = "learn-call-" + n, type = "function", function = call } } }, finish_reason = "tool_calls" } } };
            await LoopbackHttp.Reply(stream, 200, "application/json", JsonSerializer.Serialize(response), ct);
        });
        await using var desk = new DeskController(Path.Combine(root, "data"), a => queue.Enqueue(a));
        desk.Configure(new() { Endpoint = server.BaseUri.AbsoluteUri + "v1/chat/completions", Deployment = "learning-model", Protocol = ProviderProtocol.GatewayChatCompletions, UseAdvancedSettings = true }, "SYNTHETIC_LEARNING_ENGINE_TOKEN");
        var project = desk.Register(root); project.BrowserEnabled = project.ComputerEnabled = project.LocalMcpEnabled = false;
        void Pump() { while (queue.TryDequeue(out var action)) action(); }
        async Task Complete(WorkItem task) { var watch = System.Diagnostics.Stopwatch.StartNew(); while (DeskController.IsRunning(task) && watch.Elapsed < TimeSpan.FromSeconds(90)) { Pump(); await Task.Delay(20); } Pump(); if (task.Status != "완료") throw new IOException("Learning engine failed: " + JsonSerializer.Serialize(task.Transcript.TakeLast(8))); }
        await desk.ConnectAsync(exe, root); Pump(); var chat = desk.NewTask(project, "Learning fixture");
        await desk.StartAsync(chat, "repair dependency fixture", "learning-model", true); await Complete(chat);
        if (!blocked || !File.Exists(Path.Combine(root, "counter.txt")) || File.ReadAllLines(Path.Combine(root, "counter.txt")).Length != 2) throw new IOException("Third failed command was executed or not blocked; calls=" + calls + "; blocked=" + blocked + "; evidence=" + JsonSerializer.Serialize(desk.Memory.Recent(ExecutionMemory.Scope(root))) + "; transcript=" + JsonSerializer.Serialize(chat.Transcript.TakeLast(15)));
        var guide = desk.Memory.Search(ExecutionMemory.Scope(root), "repair dependency fixture").Single(m => m.Tool == "command");
        if (guide.Notes != improvement) throw new IOException("Agent method improvement missing");
        var next = desk.NewTask(project, "Similar task"); await desk.StartAsync(next, "repair dependency fixture again", "learning-model", true); await Complete(next);
        if (!autoRetrieved) throw new IOException("Automatic chat RAG injection missing");
        var readOnly = desk.NewTask(project, "Read-only RAG fixture"); await desk.StartAsync(readOnly, "read-only fixture", "learning-model", false); await Complete(readOnly);
        if (File.Exists(Path.Combine(root, "readonly-should-not-exist.txt")) || !desk.Memory.Recent(ExecutionMemory.Scope(root), readOnly.Id).Any(a => a.Tool == "command" && !a.Succeeded)) throw new IOException("Learning command bypassed read-only sandbox");
        File.WriteAllText(Path.Combine(root, "learning-engine-report.json"), JsonSerializer.Serialize(new { passed = true, calls, blockedBeforeExecution = true, autoRetrieved, realModelCalls = 0 }));
        Console.WriteLine("PASS official engine: two failures, third blocked before execution, changed command succeeds, agent updates RAG, new chat retrieves guide automatically");
    }
}
