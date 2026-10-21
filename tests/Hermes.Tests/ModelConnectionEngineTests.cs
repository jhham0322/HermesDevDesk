using System.Collections.Concurrent;
using System.Text.Json;
using Hermes.Core;
internal static class ModelConnectionEngineTests
{
    internal static async Task Run(string root, string exe)
    {
        Directory.CreateDirectory(root);
        var queue = new ConcurrentQueue<Action>();
        var observed = new ConcurrentQueue<(string Server, string Model, string Auth)>();
        LoopbackHttp Server(string name) => new(async (request, stream, ct) =>
        {
            using var doc = JsonDocument.Parse(request.Body);
            observed.Enqueue((name, doc.RootElement.Text("model"), request.Headers.GetValueOrDefault("Authorization", "")));
            var answer = AnthropicTests.Message("MODEL_" + name + "_OK"); answer["model"] = doc.RootElement.Text("model");
            await LoopbackHttp.Reply(stream, 200, "text/event-stream", AnthropicTests.Events(answer), ct);
        });
        await using var a = Server("A"); await using var b = Server("B");
        await using var desk = new DeskController(Path.Combine(root, "history"), action => queue.Enqueue(action));
        var project = desk.Register(root); project.LocalMcpEnabled = project.BrowserEnabled = project.ComputerEnabled = false;
        foreach (var item in new[] { ("alpha", a.BaseUri.AbsoluteUri, "SYNTHETIC_ENGINE_ALPHA"), ("beta", b.BaseUri.AbsoluteUri, "SYNTHETIC_ENGINE_BETA") })
            desk.SaveModelConnection(new() { Deployment = item.Item1, Endpoint = item.Item2, Protocol = ProviderProtocol.AnthropicMessages, UseAdvancedSettings = true, Authentication = ProviderAuth.BearerKey }, item.Item3);
        void Pump() { while (queue.TryDequeue(out var action)) action(); }
        var chat = desk.NewTask(project, "Selected model routing");
        foreach (var model in new[] { "alpha", "beta", "alpha" })
        {
            desk.SelectModelConnection(model); await desk.ConnectAsync(exe, root); Pump();
            await desk.StartAsync(chat, "Reply with your fixture marker", model, true);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (DeskController.IsRunning(chat) && timer.Elapsed < TimeSpan.FromSeconds(25)) { Pump(); await Task.Delay(20); }
            Pump();
            if (chat.Status != "완료") throw new IOException("Model turn failed: " + chat.Status + " " + string.Join(";", chat.Transcript.Where(t => t.Kind == "오류").Select(t => t.Text)));
        }
        var rows = observed.ToArray();
        if (rows.Length != 3 || !rows.Select(r => r.Server).SequenceEqual(new[] { "A", "B", "A" }) || !rows.Select(r => r.Model).SequenceEqual(new[] { "alpha", "beta", "alpha" }) || !rows.Select(r => r.Auth).SequenceEqual(new[] { "Bearer SYNTHETIC_ENGINE_ALPHA", "Bearer SYNTHETIC_ENGINE_BETA", "Bearer SYNTHETIC_ENGINE_ALPHA" })) throw new IOException("Selected model routing mismatch");
        File.WriteAllText(Path.Combine(root, "model-engine-report.json"), JsonSerializer.Serialize(new { passed = true, turns = rows.Length, routes = rows.Select(r => new { r.Server, r.Model }), sameChat = true, realModelCalls = 0 }));
        Console.WriteLine("PASS official engine: same chat alpha -> beta -> alpha uses exact model, URL and token; no real model calls");
    }
}
