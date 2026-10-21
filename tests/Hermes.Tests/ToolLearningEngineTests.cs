using System.Collections.Concurrent;
using System.Text.Json;
using Hermes.Core;
internal static class ToolLearningEngineTests
{
    internal static async Task Run(string root, string exe)
    {
        Directory.CreateDirectory(root); var queue = new ConcurrentQueue<Action>(); int calls = 0, browserRuns = 0, computerRuns = 0, blocked = 0;
        await using var server = new LoopbackHttp(async (request, stream, ct) =>
        {
            using var doc = JsonDocument.Parse(request.Body); var body = doc.RootElement; var n = Interlocked.Increment(ref calls);
            var messages = body.GetProperty("messages").EnumerateArray().ToArray();
            if (n is 4 or 7 && messages.Last(m => m.Text("role") == "tool").Text("content").Contains("2회 반복")) blocked++;
            var tools = body.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("function").Text("name")).ToArray();
            object? function = n <= 3 ? new { name = tools.Single(t => t.EndsWith("hermes_browser")), arguments = "{\"action\":\"click\",\"selector\":\"#missing\",\"purpose\":\"fixture workflow\"}" }
                : n <= 6 ? new { name = tools.Single(t => t.EndsWith("hermes_computer")), arguments = "{\"action\":\"click\",\"windowId\":\"0123456789abcdef0123456789abcdef\",\"snapshotId\":\"old-snapshot\",\"x\":12,\"y\":34,\"purpose\":\"fixture workflow\"}" }
                : n == 7 ? new { name = tools.Single(t => t.EndsWith("hermes_computer")), arguments = "{\"action\":\"inspect\",\"windowId\":\"0123456789abcdef0123456789abcdef\",\"target\":\"Dialog title\",\"purpose\":\"fixture workflow\"}" } : null;
            object response = function == null
                ? new { id = "tool-learn" + n, model = "tool-learning-model", choices = new[] { new { message = new { role = "assistant", content = "WORKFLOW_REPORT: inspect current dialog, verify result, do not reuse old coordinates." }, finish_reason = "stop" } } }
                : new { id = "tool-learn" + n, model = "tool-learning-model", choices = new[] { new { message = new { role = "assistant", tool_calls = new[] { new { id = "tool-learn-" + n, type = "function", function } } }, finish_reason = "tool_calls" } } };
            await LoopbackHttp.Reply(stream, 200, "application/json", JsonSerializer.Serialize(response), ct);
        });
        await using var desk = new DeskController(Path.Combine(root, "data"), a => queue.Enqueue(a));
        desk.Configure(new() { Endpoint = server.BaseUri.AbsoluteUri + "v1/chat/completions", Deployment = "tool-learning-model", Protocol = ProviderProtocol.GatewayChatCompletions, UseAdvancedSettings = true }, "SYNTHETIC_TOOL_LEARNING_TOKEN");
        desk.BrowserTool = (_, _) => { browserRuns++; throw new IOException("Error: fixture target missing"); };
        desk.ComputerTool = (_, args) => { computerRuns++; if (args.Text("action") == "click") throw new IOException("Error: fixture stale snapshot"); return Task.FromResult(new ComputerToolResult("Current fixture dialog observed")); };
        var project = desk.Register(root); project.LocalMcpEnabled = false;
        await desk.ConnectAsync(exe, root); while (queue.TryDequeue(out var action)) action();
        var chat = desk.NewTask(project, "Tool/workflow learning"); await desk.StartAsync(chat, "fixture workflow", "tool-learning-model", false);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (DeskController.IsRunning(chat) && timer.Elapsed < TimeSpan.FromSeconds(30)) { while (queue.TryDequeue(out var action)) action(); await Task.Delay(20); }
        if (chat.Status != "완료" || browserRuns != 2 || computerRuns != 3 || blocked != 2) throw new IOException($"Tool guard failed: {chat.Status}, browser={browserRuns}, computer={computerRuns}, blocked={blocked}");
        var methods = desk.Memory.Search(ExecutionMemory.Scope(root), "fixture workflow");
        var computer = methods.Single(m => m.Tool == "computer"); var workflow = methods.Single(m => m.Tool == "workflow.report");
        var text = JsonSerializer.Serialize(desk.Memory.Recent(ExecutionMemory.Scope(root)));
        if (text.Contains("old-snapshot") || text.Contains("0123456789abcdef0123456789abcdef") || computer.Method.Contains("x=12") || !computer.Method.Contains("Dialog title")) throw new IOException("Computer memory stores stale coordinates/IDs or loses semantic target");
        if (!workflow.Method.Contains("미확인") || !workflow.Method.Contains("WORKFLOW_REPORT") || !workflow.Method.Contains("관찰된 실행 절차")) throw new IOException("Business workflow provenance/verification distinction missing");
        Console.WriteLine("PASS official engine: browser/computer third retry blocked, changed observation succeeds, semantic tool method and unverified business report automatically stored");
    }
}
