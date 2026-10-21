using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;
internal static class ChatGatewayEngineTests
{
    internal static async Task Run(string root, string exe)
    {
        Directory.CreateDirectory(root);
        var queue = new ConcurrentQueue<Action>(); var calls = 0; var errors = new ConcurrentQueue<string>();
        await using var server = new LoopbackHttp(async (request, stream, ct) =>
        {
            if (request.Path != "/v1/chat/completions" || request.Method != "POST") { await LoopbackHttp.Reply(stream, 404, "application/json", "{}", ct); return; }
            Interlocked.Increment(ref calls); using var doc = JsonDocument.Parse(request.Body); var body = doc.RootElement;
            var reason = body.Property("parallel_tool_calls") != null ? "UnsupportedParamsError: parallel_tool_calls" : body.Property("max_completion_tokens") != null ? "UnsupportedParamsError: max_completion_tokens (use max_tokens)" : "";
            if (body.GetProperty("messages").EnumerateArray().Any(m => m.Property("content") is { ValueKind: JsonValueKind.Array })) reason = "messages.content must be a string for text-only gateway";
            if (request.Headers.GetValueOrDefault("Authorization") != "Bearer SYNTHETIC_CHAT_GATEWAY_TOKEN" || body.Text("model") != "gateway-model") reason = "selected model or authentication mismatch";
            if (reason.Length > 0) { errors.Enqueue(reason); await LoopbackHttp.Reply(stream, 400, "application/json", JsonSerializer.Serialize(new { error = new { message = reason } }), ct); return; }
            var answer = new JsonObject { ["id"] = "chat_fixture_" + calls, ["object"] = "chat.completion", ["model"] = "gateway-model", ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = "CHAT_GATEWAY_AGENT_OK" }, ["finish_reason"] = "stop" }) };
            await LoopbackHttp.Reply(stream, 200, "application/json", answer.ToJsonString(), ct);
        });
        await using var desk = new DeskController(Path.Combine(root, "data"), a => queue.Enqueue(a));
        desk.Configure(new() { Endpoint = server.BaseUri.AbsoluteUri + "v1/chat/completions", Deployment = "gateway-model", Protocol = ProviderProtocol.GatewayChatCompletions, Authentication = ProviderAuth.BearerKey, UseAdvancedSettings = true }, "SYNTHETIC_CHAT_GATEWAY_TOKEN");
        var project = desk.Register(root); project.BrowserEnabled = project.ComputerEnabled = project.LocalMcpEnabled = false;
        void Pump() { while (queue.TryDequeue(out var action)) action(); }
        await desk.ConnectAsync(exe, root); Pump();
        var task = desk.NewTask(project, "Chat gateway agent"); await desk.StartAsync(task, "Hello!", "gateway-model", true);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (DeskController.IsRunning(task) && timer.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); await Task.Delay(20); }
        Pump();
        var text = string.Join("\n", task.Transcript.Where(t => t.Kind == "에이전트").Select(t => t.Text));
        if (task.Status != "완료" || !text.Contains("CHAT_GATEWAY_AGENT_OK") || !errors.IsEmpty) throw new IOException("Agent chat failed: status=" + task.Status + "; upstream=" + string.Join(";", errors) + "; history=" + string.Join(";", task.Transcript.Where(t => t.Kind == "오류").Select(t => t.Text)));
        File.WriteAllText(Path.Combine(root, "chat-gateway-report.json"), JsonSerializer.Serialize(new { passed = true, calls, actualOfficialEngine = true, chatCompletionsOnly = true, realCompanyRequests = 0 }));
        Console.WriteLine("PASS actual engine receives Chat Completions answer using gateway-compatible request");
    }
}
