using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Hermes.Core;

namespace Hermes.Desktop;

public sealed partial class SettingsWindow
{
    internal async Task<int> RunProviderSettingsChecks(Func<string, bool> visible)
    {
        var checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new IOException(label); checks++; }
        var before = desk.State.Provider.Endpoint;
        ApplyProviderExample(0);
        Check(protocol.SelectedItem is ProviderProtocol.AzureV1Responses && endpoint.Text.EndsWith("/openai/v1") && version.Text == "" && authentication.SelectedItem is ProviderAuth.ApiKeyHeader, "Azure v1 example fields");
        Check(desk.State.Provider.Endpoint == before, "example does not save settings or connect");
        ApplyProviderExample(2); Check(protocol.SelectedItem is ProviderProtocol.AzureLegacyChatCompletions && version.Text.Length > 0, "legacy Azure example includes api version");
        ApplyProviderExample(3); Check(protocol.SelectedItem is ProviderProtocol.AnthropicMessages && authentication.SelectedItem is ProviderAuth.BearerKey, "Anthropic example selects token authentication");
        var failure = true; var hits = 0;
        await using var mock = new LoopbackHttp((request, stream, ct) =>
        {
            hits++;
            return LoopbackHttp.Reply(stream, failure ? 404 : 200, "application/json", failure ? "{}" : "{\"data\":[{\"id\":\"server-model-second\"},{\"id\":\"server-model-third\"}]}", ct);
        });
        endpoint.Text = mock.BaseUri.AbsoluteUri; protocol.SelectedItem = ProviderProtocol.GatewayResponses; deployment.Text = "owned-anthropic-model[1M]"; authentication.SelectedItem = ProviderAuth.ApiKeyHeader; key.Password = "SYNTHETIC_PROVIDER_UI_ONLY"; Save();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(visible("owned-anthropic-model[1M]") && !desk.SubscriptionReady, "configured model shown before connecting, without false readiness");
        await desk.CheckProviderAsync(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(visible("owned-anthropic-model[1M]") && desk.ProviderCheckStatus.Contains("404") && !desk.SubscriptionReady, "404 keeps configured model and separately displays failure");
        failure = false; await desk.CheckProviderAsync(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(desk.Models.SequenceEqual(new[] { "owned-anthropic-model[1M]", "server-model-second", "server-model-third" }) && visible("server-model-second"), "successful server model discovery populates selectable models");
        Check(hits == 2 && networkConsent.IsChecked != true, "examples and saving never make hidden requests");
        return checks;
    }
}

public partial class MainWindow
{
    internal async Task RunProviderSettingsChecks(string data)
    {
        var settings = new SettingsWindow(desk) { Owner = this }; settings.Show();
        try
        {
            var checks = await settings.RunProviderSettingsChecks(name =>
            {
                if (!Model.Items.Cast<string>().Contains(name)) return false;
                Model.SelectedItem = name; return Model.SelectedItem as string == name && !StartButton.IsEnabled;
            });
            var configured = desk.State.Provider.Deployment;
            // Force a normal engine connection error: no process or network can start at this nonexistent executable.
            var rejected = false;
            try { await desk.ConnectAsync(Path.Combine(data, "codex.exe"), data); }
            catch (IOException) { rejected = true; }
            if (!rejected) throw new IOException("Expected engine failure");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (!Model.Items.Cast<string>().Contains(configured) || desk.SubscriptionReady) throw new IOException("Engine failure lost configured model");
            File.WriteAllText(Path.Combine(data, "provider-settings-report.json"), JsonSerializer.Serialize(new { passed = true, checks = checks + 1, configuredModelPreserved = true, serverModelsSelectable = true, azureExamples = true, realCompanyRequests = 0, realModelCalls = false }));
        }
        finally { settings.Close(); }
    }
}
