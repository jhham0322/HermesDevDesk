using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using Hermes.Core;

namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunRelaySendChecks(string data)
    {
        int probes = 0, generations = 0, models = 0;
        await using var server = new LoopbackHttp(async (request, stream, ct) =>
        {
            if (request.Path.EndsWith("/models")) models++;
            if (request.Path != "/v1/responses") { await LoopbackHttp.Reply(stream, 404, "application/json", "{}", ct); return; }
            using var body = JsonDocument.Parse(request.Body);
            if (body.RootElement.Text("model") != "gpt-6-luna") throw new IOException("Wrong relay model");
            bool probe = body.RootElement.Property("max_output_tokens")?.GetInt32() == 1;
            if (probe) probes++; else generations++;
            var response = new JsonObject { ["id"] = "resp_owned_" + (probes + generations), ["object"] = "response", ["model"] = "gpt-6-luna", ["status"] = "completed", ["created_at"] = 1,
                ["output"] = new JsonArray(new JsonObject { ["id"] = "msg_owned_" + (probes + generations), ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = probe ? "OK" : "OWNED_RELAY_SEND_OK", ["annotations"] = new JsonArray() }) }) };
            await LoopbackHttp.Reply(stream, 200, "application/json", response.ToJsonString(), ct);
        });
        var root = Path.Combine(data, "owned-project"); Directory.CreateDirectory(root);
        var project = desk.Register(root); Projects.SelectedItem = project;
        desk.Configure(new() { Endpoint = server.BaseUri.AbsoluteUri.TrimEnd('/') + "/v1", Deployment = "gpt-6-luna", Protocol = ProviderProtocol.GatewayAuto }, "SYNTHETIC_RELAY_SEND_ONLY");
        desk.RememberModel("gpt-6-luna"); Refresh();
        if (CurrentTask != null || desk.HasPendingModel || !StartButton.IsEnabled) throw new IOException("New conversation send remains disabled");
        UpdateShellState(); if (!StartButton.IsEnabled) throw new IOException("Shell overwrote send readiness");
        Prompt.Text = "Reply with the owned fixture result.";
        StartButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until && !desk.State.Tasks.Any(t => t.Status == "완료" && t.Transcript.Any(x => x.Text.Contains("OWNED_RELAY_SEND_OK"))))
            await Task.Delay(100);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (generations != 1 || probes != 1 || models != 0 || !desk.State.Tasks.Any(t => t.Status == "완료")) throw new IOException("Relay send did not complete: " + desk.AccountStatus);
        File.WriteAllText(Path.Combine(data, "relay-send-report.json"), JsonSerializer.Serialize(new { passed = true, exactModel = "gpt-6-luna", probes, generations, models, noExistingTask = true, missingUsageSupported = true, actualOfficialEngine = true, programmaticWpfButton = true, realCompanyRequests = false }));
    }
}
