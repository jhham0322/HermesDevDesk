using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;

internal static class MultiChatTests
{
    internal static async Task Run(string directory, string exe)
    {
        Directory.CreateDirectory(directory);
        void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
        var root = Path.Combine(directory, "project"); Directory.CreateDirectory(root);
        var outside = Path.Combine(directory, "outside.png");
        File.WriteAllBytes(outside, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII="));
        // Generate the same CRC-valid PNG fixture used by the existing image tests.
        var fixture = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        File.WriteAllBytes(outside, fixture);
        var imported = ImageAttachments.Import(root, outside);
        Check(ImageAttachments.Validate(root, [imported]).Count == 1 && File.Exists(outside), "external image imported without changing original");
        var data = Path.Combine(directory, "history");
        var queue = new ConcurrentQueue<Action>();
        var seen = new ConcurrentDictionary<string, TaskCompletionSource>();
        var release = new ConcurrentDictionary<string, TaskCompletionSource>();
        foreach (var model in new[] { "model-a", "model-b" }) { seen[model] = new(TaskCreationOptions.RunContinuationsAsynchronously); release[model] = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        var imagesSeen = false;
        await using var mock = new LoopbackHttp(async (request, stream, ct) =>
        {
            var body = JsonNode.Parse(request.Body)!.AsObject(); var model = body["model"]!.GetValue<string>();
            if (body.ToJsonString().Contains("data:image/png;base64,")) imagesSeen = true;
            seen[model].TrySetResult();
            await release[model].Task.WaitAsync(TimeSpan.FromSeconds(40), ct);
            var response = new JsonObject { ["id"] = "resp_" + model, ["object"] = "response", ["created_at"] = 1, ["status"] = "completed", ["model"] = model,
                ["output"] = new JsonArray(new JsonObject { ["id"] = "msg_" + model, ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "OK_" + model, ["annotations"] = new JsonArray() }) }),
                ["usage"] = new JsonObject { ["input_tokens"] = 10, ["output_tokens"] = 5, ["total_tokens"] = 15 } };
            await LoopbackHttp.Reply(stream, 200, "text/event-stream", ResponseWire.Sse(response), ct);
        });
        await using var desk = new DeskController(data, a => queue.Enqueue(a), true);
        desk.Configure(new() { Endpoint = mock.BaseUri.AbsoluteUri, Deployment = "model-a", Protocol = ProviderProtocol.GatewayResponses, UseAdvancedSettings = true, Authentication = ProviderAuth.None });
        desk.AddModel("model-b");
        var loaded = new StateStore(data).Load();
        Check(loaded.SavedModels.Values.Single().SequenceEqual(new[] { "model-a", "model-b" }), "multiple models persist across restart");
        var project = desk.Register(root);
        desk.Diagnostic += s => Console.WriteLine("ENGINE " + s);
        await desk.ConnectAsync(exe, root);
        void Pump() { while (queue.TryDequeue(out var action)) action(); }
        async Task Until(Func<bool> ready, string message)
        {
            var until = DateTime.UtcNow.AddSeconds(30);
            while (!ready() && DateTime.UtcNow < until) { Pump(); await Task.Delay(30); }
            Pump(); Check(ready(), message);
        }
        var a = desk.NewTask(project, "a"); var b = desk.NewTask(project, "b");
        await desk.StartAsync(a, "Describe this image. Do not call tools.", "model-a", false, [imported]);
        await desk.StartAsync(b, "Reply OK. Do not call tools.", "model-b", false);
        await Until(() => seen.Values.All(x => x.Task.IsCompleted), "two chats in same project reach upstream concurrently with separate models");
        Check(imagesSeen, "official engine sends attached PNG to provider");
        Check(DeskController.IsRunning(a) && DeskController.IsRunning(b), "both chat turns run simultaneously");
        await desk.StopAsync(a);
        await Until(() => !DeskController.IsRunning(a) && desk.State.Usage.Any(e => e.TaskId == a.Id && e.Status == "cancelled"), "stop cancels only first chat request");
        Check(DeskController.IsRunning(b), "second chat continues after first chat stops");
        release["model-b"].TrySetResult();
        await Until(() => !DeskController.IsRunning(b), "second chat finishes independently");
        Check(b.Status == "완료" && b.Transcript.Any(e => e.Text.Contains("OK_model-b")), "response belongs to correct chat");
        Check(desk.State.Usage.Any(e => e.TaskId == b.Id && e.Model == "model-b" && e.Tokens.Total == 15 && e.Status == "completed"), "usage belongs to correct chat and model");
        Check(desk.State.Usage.All(e => e.TaskId == a.Id || e.TaskId == b.Id), "no usage assigned to another chat");
        release["model-a"].TrySetResult();
        // Resume the interrupted thread, keeping its private bridge routing.
        await desk.StartAsync(a, "Reply again.", "model-a", true);
        await Until(() => !DeskController.IsRunning(a), "interrupted chat resumes");
        Check(a.Status == "완료", "resumed chat completes");
        Check(a.ThreadWritable == true && desk.Approvals.Count == 0, "existing chat upgrades to project write/network access with approval never");
        var originalThread = a.ThreadId;
        project.ComputerEnabled = true; project.LocalMcpEnabled = true;
        var changedSettings = JsonSerializer.Deserialize<ProviderSettings>(JsonSerializer.Serialize(desk.State.Provider))!;
        changedSettings.MaxOutputTokens++;
        desk.Configure(changedSettings); await desk.ConnectAsync(exe, root);
        await desk.StartAsync(a, "Continue this previous conversation with project write permission.", "model-b", true);
        await Until(() => !DeskController.IsRunning(a), "previous chat resumes after reconnect and changed settings");
        Check(a.ThreadId == originalThread && a.Status == "완료" && a.Model == "model-b", "same thread history preserved without creating a new chat after permission or provider settings change");
        File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { version = "0.8.5", passed = true, officialEngine = true, syntheticServer = true, actualModelCalls = 0, attachments = true, simultaneousChats = 2, separateCancellation = true, separateUsage = true, resume = true }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
