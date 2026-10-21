using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;
internal static class ChatStreamingEngineTests
{
    internal static async Task Run(string root, string exe)
    {
        Directory.CreateDirectory(root); AppLog.Configure(root); var queue = new ConcurrentQueue<Action>(); int calls = 0, toolRuns = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); bool toolResult = false;
        await using var server = new LoopbackHttp(async (request, stream, ct) =>
        {
            if (request.Path != "/v1/chat/completions") { await LoopbackHttp.Reply(stream, 404, "application/json", "{}", ct); return; }
            using var doc = JsonDocument.Parse(request.Body); var body = doc.RootElement; var number = Interlocked.Increment(ref calls);
            if (body.Property("stream") is not { ValueKind: JsonValueKind.True } || body.Property("parallel_tool_calls") != null || body.Property("max_completion_tokens") != null) throw new IOException("Chat request is not gateway-compatible streaming");
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n"), ct);
            async Task Send(JsonObject delta, string? finish = null)
            {
                var chunk = new JsonObject { ["id"] = "chat_stream_fixture", ["model"] = "stream-model", ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finish }) };
                await stream.WriteAsync(Encoding.UTF8.GetBytes("data: " + chunk.ToJsonString() + "\n\n"), ct); await stream.FlushAsync(ct);
            }
            if (number == 1)
            {
                var name = body.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("function").Text("name")).First(n => n.EndsWith("hermes_browser", StringComparison.Ordinal));
                await Send(new() { ["tool_calls"] = new JsonArray(new JsonObject { ["index"] = 0, ["id"] = "call_stream_browser", ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["arguments"] = "{\"action\":" } }) });
                await Send(new() { ["tool_calls"] = new JsonArray(new JsonObject { ["index"] = 0, ["function"] = new JsonObject { ["arguments"] = "\"read\"}" } }) }, "tool_calls");
            }
            else
            {
                toolResult = body.GetProperty("messages").EnumerateArray().Any(m => m.Text("role") == "tool" && m.Text("content").Contains("STREAM_TOOL_OK"));
                await Send(new() { ["content"] = "CHAT_STREAM_FIRST" });
                await release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
                await Send(new() { ["content"] = "_LAST" }, "stop");
            }
            await stream.WriteAsync(Encoding.UTF8.GetBytes("data: [DONE]\n\n"), ct); await stream.FlushAsync(ct);
        });
        await using var desk = new DeskController(Path.Combine(root, "history"), action => queue.Enqueue(action));
        desk.Configure(new() { Endpoint = server.BaseUri.AbsoluteUri + "v1/chat/completions", Deployment = "stream-model", Protocol = ProviderProtocol.GatewayChatCompletions, UseAdvancedSettings = true }, "SYNTHETIC_STREAM_ENGINE_TOKEN");
        desk.BrowserTool = (_, _) => { toolRuns++; return Task.FromResult(new BrowserToolResult("STREAM_TOOL_OK")); };
        var project = desk.Register(root); project.BrowserEnabled = true; project.ComputerEnabled = project.LocalMcpEnabled = false;
        void Pump() { while (queue.TryDequeue(out var action)) action(); }
        async Task Wait(Func<bool> done) { var timer = System.Diagnostics.Stopwatch.StartNew(); while (!done() && timer.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); await Task.Delay(20); } Pump(); if (!done()) throw new IOException("Chat stream engine timed out: requests=" + calls); }
        await desk.ConnectAsync(exe, root); Pump(); var chat = desk.NewTask(project, "Streaming tool round trip");
        await desk.StartAsync(chat, "Read the owned browser fixture and reply", "stream-model", true);
        await Wait(() => chat.Transcript.Any(t => t.Text.Contains("CHAT_STREAM_FIRST")));
        if (chat.Status == "완료" || release.Task.IsCompleted || toolRuns != 1 || !toolResult) throw new IOException("First streamed answer was buffered or tool result lost");
        release.TrySetResult(); await Wait(() => chat.Status == "완료");
        if (!chat.Transcript.Any(t => t.Kind == "에이전트" && t.Text.Contains("CHAT_STREAM_FIRST_LAST"))) throw new IOException("Completed streamed answer missing");
        File.WriteAllText(Path.Combine(root, "stream-engine-report.json"), JsonSerializer.Serialize(new { passed = true, calls, toolRuns, firstChunkBeforeCompletion = true, realCompanyRequests = 0 }));
        Console.WriteLine("PASS actual Chat API engine: streamed tool round trip and visible first text before generation completes");
    }
}
