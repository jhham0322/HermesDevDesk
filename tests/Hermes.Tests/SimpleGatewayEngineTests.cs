using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;

internal static class SimpleGatewayEngineTests
{
    internal static async Task Run(string root, string exe, string reportPath)
    {
        Directory.CreateDirectory(root); var queue = new ConcurrentQueue<Action>(); var requests = 0; var callbacks = 0; bool catalog = false, image = false, result = false, stop = false, stopPhase = false;
        var pending = new TaskCompletionSource<ComputerToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var mock = new LoopbackHttp(async (request, stream, ct) =>
        {
            if (request.Path != "/v1/messages") { await LoopbackHttp.Reply(stream, 404, "application/json", "{}", ct); return; }
            using var probe = JsonDocument.Parse(request.Body);
            if (probe.RootElement.Property("max_tokens")?.GetInt32() == 1) { await LoopbackHttp.Reply(stream, 200, "application/json", AnthropicTests.Message().ToJsonString(), ct); return; }
            using var doc = JsonDocument.Parse(request.Body); var body = doc.RootElement; var count = Interlocked.Increment(ref requests); var output = new JsonArray();
            if (count is 1 or 3)
            {
                var name = body.GetProperty("tools").EnumerateArray().Select(t => t.Text("name")).FirstOrDefault(n => n.EndsWith("hermes_computer", StringComparison.Ordinal)) ?? "";
                catalog = name.Length > 0; if (!catalog) throw new IOException("Computer tool absent from actual Anthropic catalog");
                output.Add(new JsonObject { ["type"] = "tool_use", ["id"] = "call_computer_" + count, ["name"] = name, ["input"] = JsonNode.Parse("{\"action\":\"inspect\",\"windowId\":\"00000000000000000000000000000001\"}") });
            }
            else
            {
                var raw = body.GetRawText(); image |= raw.Contains("image/png") && raw.Contains("base64"); result |= raw.Contains("COMPUTER_HOST_FIXTURE");
                output.Add(new JsonObject { ["type"] = "text", ["text"] = "COMPUTER_ENGINE_OK" });
            }
            var response = AnthropicTests.Message(); response["id"] = "msg_engine_" + count; response["content"] = output; response["stop_reason"] = count is 1 or 3 ? "tool_use" : "end_turn";
            if (request.Headers.GetValueOrDefault("anthropic-version") != "2023-06-01" || !request.Headers.GetValueOrDefault("Authorization", "").StartsWith("Bearer ")) throw new IOException("Anthropic authentication missing");
            await LoopbackHttp.Reply(stream, 200, "text/event-stream", AnthropicTests.Events(response), ct);
        });
        await using var desk = new DeskController(Path.Combine(root, "history"), a => queue.Enqueue(a), true);
        desk.Configure(new() { Endpoint = mock.BaseUri.AbsoluteUri, Deployment = "computer-fixture", Protocol = ProviderProtocol.GatewayAuto, Authentication = ProviderAuth.BearerKey }, "SYNTHETIC_COMPUTER_ENGINE_KEY");
        desk.ComputerTool = (_, args) => { ComputerTools.Parse(args); callbacks++; return callbacks == 1 ? Task.FromResult(new ComputerToolResult("COMPUTER_HOST_FIXTURE", "data:image/png;base64," + NativeFeatureTests.Png)) : pending.Task; };
        desk.ComputerStopRequested += task => { if (task != null && stopPhase) { stop = true; pending.TrySetException(new IOException("fixture emergency stop")); } };
        var project = desk.Register(root); project.ComputerEnabled = true; await desk.ConnectAsync(exe, root);
        void Pump() { while (queue.TryDequeue(out var action)) action(); }
        async Task Wait(Func<bool> until) { var clock = System.Diagnostics.Stopwatch.StartNew(); while (!until() && clock.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); await Task.Delay(20); } Pump(); if (!until()) throw new IOException("Computer engine fixture timed out: callbacks=" + callbacks + ";requests=" + requests); }
        var chat = desk.NewTask(project, "Computer tool fixture"); await desk.StartAsync(chat, "Inspect approved computer fixture", "computer-fixture", false); await Wait(() => chat.Status == "완료");
        if (!catalog || !image || !result || callbacks != 1 || requests != 2) throw new IOException($"Computer round trip failed: catalog={catalog} image={image} result={result} calls={callbacks} requests={requests}");
        Console.WriteLine("PASS official engine through Anthropic: computer tool catalog, call correlation and screenshot image round trip");
        stopPhase = true; await desk.StartAsync(chat, "Pending computer fixture", "computer-fixture", false); await Wait(() => callbacks == 2); await desk.StopAsync(chat); await Wait(() => chat.Status == "중단됨");
        if (!stop) throw new IOException("Computer stop callback missing"); Console.WriteLine("PASS interrupt stops pending computer host request");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { version = "0.8.3", catalog, image, result, callbacks, stop, status = chat.Status, actualOfficialEngine = true, syntheticProviderOnly = true, realModelCalls = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
