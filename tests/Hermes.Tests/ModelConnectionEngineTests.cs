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
    internal static async Task RunQuotaRotation(string root, string exe)
    {
        Directory.CreateDirectory(root);
        var queue = new ConcurrentQueue<Action>();
        var routes = new ConcurrentQueue<(string Server, string Model, string Auth)>();
        var exhausted = true;
        LoopbackHttp Server(string name) => new(async (request, stream, ct) =>
        {
            using var json = JsonDocument.Parse(request.Body);
            var model = json.RootElement.Text("model");
            routes.Enqueue((name, model, request.Headers.GetValueOrDefault("Authorization", "")));
            if (name == "A" && exhausted)
            {
                await LoopbackHttp.Reply(stream, 400, "application/json",
                    """{"error":{"code":"daily_token_limit_exceeded","message":"Daily token limit exceeded"}}""", ct);
                return;
            }
            var answer = AnthropicTests.Message("MODEL_" + name + "_OK");
            answer["model"] = model;
            await LoopbackHttp.Reply(stream, 200, "text/event-stream", AnthropicTests.Events(answer), ct);
        });
        await using var a = Server("A"); await using var b = Server("B");
        await using var desk = new DeskController(Path.Combine(root, "history"), queue.Enqueue);
        var project = desk.Register(root); project.LocalMcpEnabled = project.BrowserEnabled = project.ComputerEnabled = false;
        ProviderSettings Settings(Uri uri) => new() { Deployment = "shared", Endpoint = uri.AbsoluteUri.TrimEnd('/') + "/v1/messages",
            Protocol = ProviderProtocol.AnthropicMessages, UseAdvancedSettings = false, Authentication = ProviderAuth.BearerKey };
        var first = desk.SaveModelConnection(Settings(a.BaseUri), "SYNTHETIC_ROTATION_ALPHA", alias: "first");
        var backup = desk.SaveModelConnection(Settings(b.BaseUri), "SYNTHETIC_ROTATION_BETA", alias: "second");
        if (!desk.DisplayModels.SequenceEqual(new[] { "first", "second" })) throw new IOException("Alias order mismatch");
        desk.SelectModelConnection("first");
        await desk.ConnectAsync(exe, root);
        var task = desk.NewTask(project, "Quota rotation fixture");
        await desk.StartAsync(task, "출력 토큰의 의미를 설명해줘", "shared", true, continueUntilComplete: true, requirePlan: true);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (task.Status != "완료" && timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            while (queue.TryDequeue(out var action)) action();
            await Task.Delay(20);
        }
        if (task.Status != "완료" || task.ModelConnectionId != backup.Id || first.QuotaExhaustedUntil == null)
            throw new IOException("Quota rotation failed: " + task.Status + " " + string.Join(" / ", task.Transcript.Select(t => t.Text).TakeLast(8)));
        if (!routes.Select(r => r.Server).SequenceEqual(new[] { "A", "B" }) ||
            !routes.Select(r => r.Auth).SequenceEqual(new[] { "Bearer SYNTHETIC_ROTATION_ALPHA", "Bearer SYNTHETIC_ROTATION_BETA" }) ||
            routes.Any(r => r.Model != "shared")) throw new IOException("Quota rotation sent the wrong request");
        exhausted = false;
        await desk.RunDueDailyQuotaResumesAsync(first.QuotaExhaustedUntil.Value.AddSeconds(1));
        if (desk.ActiveModelConnection?.Id != first.Id)
            throw new IOException("Reset did not restore the first saved connection");
        Console.WriteLine("PASS daily quota rotates between identical model names with separate keys and resets to the first connection");
    }

}
