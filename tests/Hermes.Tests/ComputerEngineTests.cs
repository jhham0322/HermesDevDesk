using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;

internal static class ComputerEngineTests
{
    internal static async Task Run(string root, string exe, string reportPath)
    {
        Directory.CreateDirectory(root); var queue = new ConcurrentQueue<Action>(); var requests = 0; var callbacks = 0; bool catalog = false, image = false, result = false, stop = false;
        var pending = new TaskCompletionSource<ComputerToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var mock = new LoopbackHttp(async (request, stream, ct) =>
        {
            using var doc = JsonDocument.Parse(request.Body); var body = doc.RootElement; var count = Interlocked.Increment(ref requests); var output = new JsonArray();
            if (count is 1 or 3)
            {
                string name = "", ns = "";
                foreach (var tool in body.GetProperty("tools").EnumerateArray())
                {
                    if (tool.Text("name") == "hermes_computer") name = "hermes_computer";
                    if (tool.Text("type") == "namespace") foreach (var member in tool.GetProperty("tools").EnumerateArray()) if (member.Text("name") == "hermes_computer") { name = "hermes_computer"; ns = tool.Text("name"); }
                }
                catalog = name.Length > 0; if (!catalog) throw new IOException("Computer tool absent from actual engine catalog");
                var call = new JsonObject { ["id"] = "fc_computer_" + count, ["type"] = "function_call", ["status"] = "completed", ["call_id"] = "call_computer_" + count, ["name"] = name, ["arguments"] = "{\"action\":\"inspect\",\"windowId\":\"00000000000000000000000000000001\"}" }; if (ns.Length > 0) call["namespace"] = ns; output.Add(call);
            }
            else
            {
                var raw = body.GetRawText(); image |= raw.Contains("data:image/png;base64,"); result |= raw.Contains("COMPUTER_HOST_FIXTURE");
                output.Add(new JsonObject { ["id"] = "message_computer", ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "COMPUTER_ENGINE_OK", ["annotations"] = new JsonArray() }) });
            }
            var response = new JsonObject { ["id"] = "resp_computer_" + count, ["object"] = "response", ["status"] = "completed", ["created_at"] = 1, ["model"] = "computer-fixture", ["output"] = output, ["usage"] = new JsonObject { ["input_tokens"] = 5, ["output_tokens"] = 3, ["total_tokens"] = 8 } };
            await LoopbackHttp.Reply(stream, 200, "text/event-stream", ResponseWire.Sse(response), ct);
        });
        await using var desk = new DeskController(Path.Combine(root, "history"), a => queue.Enqueue(a), true);
        desk.Configure(new() { Endpoint = mock.BaseUri.AbsoluteUri, Deployment = "computer-fixture" }, "SYNTHETIC_COMPUTER_ENGINE_KEY");
        desk.ComputerTool = (_, args) => { ComputerTools.Parse(args); callbacks++; return callbacks == 1 ? Task.FromResult(new ComputerToolResult("COMPUTER_HOST_FIXTURE", "data:image/png;base64," + NativeFeatureTests.Png)) : pending.Task; };
        desk.ComputerStopRequested += task => { if (task != null) { stop = true; pending.TrySetException(new IOException("fixture emergency stop")); } };
        var project = desk.Register(root); project.ComputerEnabled = true; await desk.ConnectAsync(exe, root);
        void Pump() { while (queue.TryDequeue(out var action)) action(); }
        async Task Wait(Func<bool> until) { var clock = System.Diagnostics.Stopwatch.StartNew(); while (!until() && clock.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); await Task.Delay(20); } Pump(); if (!until()) throw new IOException("Computer engine fixture timed out"); }
        var chat = desk.NewTask(project, "Computer tool fixture"); await desk.StartAsync(chat, "Inspect approved computer fixture", "computer-fixture", false); await Wait(() => chat.Status == "완료");
        if (!catalog || !image || !result || callbacks != 1 || requests != 2) throw new IOException($"Computer round trip failed: catalog={catalog} image={image} result={result} calls={callbacks} requests={requests}");
        Console.WriteLine("PASS official computer tool catalog, call correlation and screenshot image round trip");
        await desk.StartAsync(chat, "Pending computer fixture", "computer-fixture", false); await Wait(() => callbacks == 2); await desk.StopAsync(chat); await Wait(() => chat.Status == "중단됨");
        if (!stop) throw new IOException("Computer stop callback missing"); Console.WriteLine("PASS interrupt stops pending computer host request");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { version = "0.7.0", catalog, image, result, callbacks, stop, status = chat.Status, actualOfficialEngine = true, syntheticProviderOnly = true, realModelCalls = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
