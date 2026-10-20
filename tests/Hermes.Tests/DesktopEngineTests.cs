using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;

internal static class DesktopEngineTests
{
    internal static async Task Run(string root, string exe, string reportPath)
    {
        Directory.CreateDirectory(root); var actions = new ConcurrentQueue<Action>(); int requests = 0; bool toolAdvertised = false, toolResult = false, imageResult = false, steer = false;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var mock = new LoopbackHttp(async (request, stream, ct) =>
        {
            using var doc = JsonDocument.Parse(request.Body); var body = doc.RootElement; var output = new JsonArray(); var count = Interlocked.Increment(ref requests);
            if (count == 1)
            {
                string toolName = "", ns = "";
                foreach (var tool in body.GetProperty("tools").EnumerateArray())
                {
                    if (tool.Text("name") == "hermes_browser") toolName = "hermes_browser";
                    if (tool.Text("type") == "namespace") foreach (var child in tool.GetProperty("tools").EnumerateArray()) if (child.Text("name") == "hermes_browser") { toolName = "hermes_browser"; ns = tool.Text("name"); }
                }
                toolAdvertised = toolName.Length > 0; if (!toolAdvertised) throw new IOException("Browser tool missing from engine provider request");
                var call = new JsonObject { ["id"] = "fc_browser_fixture", ["type"] = "function_call", ["status"] = "completed", ["call_id"] = "call_browser_fixture", ["name"] = toolName, ["arguments"] = "{\"action\":\"read\"}" };
                if (ns.Length > 0) call["namespace"] = ns; output.Add(call);
            }
            else
            {
                toolResult |= body.GetRawText().Contains("BROWSER_HOST_FIXTURE_RESULT");
                imageResult |= body.GetRawText().Split("input_image").Length >= 3 && body.GetRawText().Contains("data:image/png;base64,");
                if (count == 3) await release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
                output.Add(new JsonObject { ["id"] = "message_" + count, ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "DESKTOP_ENGINE_OK", ["annotations"] = new JsonArray() }) });
            }
            var response = new JsonObject { ["id"] = "resp_" + count, ["object"] = "response", ["created_at"] = 1, ["status"] = "completed", ["model"] = "fixture-deployment", ["output"] = output, ["usage"] = new JsonObject { ["input_tokens"] = 5, ["output_tokens"] = 3, ["total_tokens"] = 8 } };
            await LoopbackHttp.Reply(stream, 200, "text/event-stream", ResponseWire.Sse(response), ct);
        });
        await using var desk = new DeskController(Path.Combine(root, "history"), a => actions.Enqueue(a), true);
        desk.Configure(new() { Endpoint = mock.BaseUri.AbsoluteUri, Deployment = "fixture-deployment", Protocol = ProviderProtocol.GatewayResponses, UseAdvancedSettings = true }, "SYNTHETIC_DESKTOP_ENGINE_KEY");
        desk.BrowserTool = (_, arg) => Task.FromResult(new BrowserToolResult(arg.Text("action") == "read" ? "BROWSER_HOST_FIXTURE_RESULT" : "unsupported", Images: new[] { "data:image/png;base64," + NativeFeatureTests.Png, "data:image/png;base64," + NativeFeatureTests.Png }));
        var project = desk.Register(root); project.BrowserEnabled = true; project.ComputerEnabled = false; project.LocalMcpEnabled = false; await desk.ConnectAsync(exe, root);
        void Pump() { while (actions.TryDequeue(out var a)) a(); }
        async Task Finish(WorkItem task) { var until = DateTime.UtcNow.AddSeconds(30); while (DeskController.IsRunning(task) && DateTime.UtcNow < until) { Pump(); await Task.Delay(20); } Pump(); if (task.Status != "완료") throw new IOException("Fixture failed: " + task.Status); }
        var chat = desk.NewTask(project, "Browser host fixture"); await desk.StartAsync(chat, "Use browser fixture", "fixture-deployment", false); await Finish(chat);
        if (!toolAdvertised || !toolResult || !imageResult || requests != 2) throw new IOException("Official browser tool round trip failed");
        if (chat.Transcript.Count(e => e.Kind is "에이전트" or "에이전트 스트림") != 1) throw new IOException("Official streamed final message duplicated");
        Console.WriteLine("PASS official dynamic browser tool catalog / request / response and final-message dedup");
        await desk.StartAsync(chat, "Delayed fixture", "fixture-deployment", false);
        var wait = DateTime.UtcNow.AddSeconds(10); while (requests < 3 && DateTime.UtcNow < wait) { Pump(); await Task.Delay(20); } Pump();
        await desk.SteerAsync(chat, "STEER_FIXTURE_ACCEPTED"); steer = chat.Transcript.Any(e => e.Kind == "사용자" && e.Text.Contains("STEER_FIXTURE_ACCEPTED")); release.TrySetResult(); await Finish(chat);
        if (!steer) throw new IOException("Steer transcript missing");
        Console.WriteLine("PASS official turn/steer active-turn precondition and accepted user message");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { toolAdvertised, toolResult, imageResult, steer, requests, actualOfficialEngine = true, syntheticProviderOnly = true, noRealModelUsage = true }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
