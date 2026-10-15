using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;

internal static class SimpleUiEngineTests
{
    internal static async Task Run(string root, string app, string report)
    {
        Directory.CreateDirectory(root); string model = "gpt-6-luna"; int checks = 0, generations = 0;
        await using var mock = new LoopbackHttp(async (request, stream, ct) =>
        {
            if (request.Path != "/v1/responses") { await LoopbackHttp.Reply(stream, 404, "application/json", "{}", ct); return; }
            using var doc = JsonDocument.Parse(request.Body); if (doc.RootElement.Text("model") != model) throw new IOException("UI selected wrong model");
            var probe = doc.RootElement.Property("max_output_tokens")?.GetInt32() == 1;
            if (probe) checks++; else generations++;
            var response = new JsonObject { ["id"] = "resp_owned_ui_" + (checks + generations), ["object"] = "response", ["model"] = model, ["status"] = "completed", ["created_at"] = 1, ["output"] = new JsonArray(new JsonObject { ["id"] = "msg_owned_ui_" + (checks + generations), ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = probe ? "OK" : "OWNED_UI_FLOW_OK", ["annotations"] = new JsonArray() }) }), ["usage"] = new JsonObject { ["input_tokens"] = 2, ["output_tokens"] = 1, ["total_tokens"] = 3 } };
            await LoopbackHttp.Reply(stream, 200, "application/json", response.ToJsonString(), ct);
        });
        var history = Path.Combine(root, "owned-history"); var projectRoot = Path.Combine(root, "owned-project"); Directory.CreateDirectory(projectRoot);
        await using (var desk = new DeskController(history))
        {
            desk.Configure(new() { Endpoint = mock.BaseUri.AbsoluteUri, Deployment = model, Protocol = ProviderProtocol.GatewayAuto, Authentication = ProviderAuth.BearerKey }, "SYNTHETIC_UI_FLOW_KEY");
            var project = desk.Register(projectRoot); desk.NewTask(project, "Owned simple connection flow");
        }
        var info = new ProcessStartInfo(app) { UseShellExecute = false }; info.ArgumentList.Add("--data-dir"); info.ArgumentList.Add(history);
        using var process = Process.Start(info) ?? throw new IOException("Owned app launch failed");
        Console.WriteLine("READY owned application PID " + process.Id + "; enter a synthetic prompt in this window");
        try
        {
            var timer = Stopwatch.StartNew(); AppState? final = null;
            while (timer.Elapsed < TimeSpan.FromSeconds(100))
            {
                await Task.Delay(200);
                try { final = new StateStore(history).Load(); } catch (IOException) { continue; } catch (JsonException) { continue; }
                if (final.Tasks.Any(t => t.Status == "완료" && t.Transcript.Any(x => x.Text.Contains("OWNED_UI_FLOW_OK")))) break;
            }
            if (final == null || !final.Tasks.Any(t => t.Status == "완료") || generations != 1 || checks != 1 || final.Provider.Protocol != ProviderProtocol.GatewayResponses) throw new IOException("Owned UI auto-connect workflow did not complete");
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, model, minimalConnectionChecks = checks, actualFixtureGenerations = generations, automaticEngineConnect = true, realCompanyOrModelRequests = false, normalUiSendAction = true }));
            Console.WriteLine("PASS normal UI send auto-connects engine and receives exact selected model response");
        }
        finally { if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(5000)) process.Kill(true); } }
    }
}
