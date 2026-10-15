using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Hermes.Core;

namespace Hermes.Desktop;
public sealed partial class SettingsWindow
{
    internal async Task<int> RunSimpleChecks(Func<string, bool> visible)
    {
        int checks = 0; void Check(bool value, string name) { if (!value) throw new IOException(name); checks++; }
        Check(manual.IsChecked != true, "new profile uses simple connection without protocol choice");
        deployment.Text = "gpt-6-luna";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(visible("gpt-6-luna") && desk.HasPendingModel, "typing model updates main combo before saving or connecting");
        endpoint.Text = "invalid URL"; key.Password = "SYNTHETIC_SIMPLE_UI_KEY";
        var failed = false; try { Save(); } catch (IOException) { failed = true; }
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(failed && visible("gpt-6-luna") && desk.Store.Load().PendingModel == "gpt-6-luna", "URL validation failure persists model instead of placeholder");
        var reopened = new SettingsWindow(desk); Check(reopened.deployment.Text == "gpt-6-luna", "reopened window restores typed model after failed save"); reopened.Close();
        bool reject = false; var hits = 0;
        await using var server = new LoopbackHttp((request, stream, ct) =>
        {
            hits++;
            if (reject) return LoopbackHttp.Reply(stream, 404, "application/json", "{\"error\":{\"message\":\"model not found\"}}", ct);
            return LoopbackHttp.Reply(stream, request.Path == "/v1/chat/completions" ? 200 : 404, "application/json", request.Path == "/v1/chat/completions" ? "{\"id\":\"owned\",\"model\":\"gpt-6-luna\",\"choices\":[{\"message\":{\"content\":\"OK\"}}]}" : "{}", ct);
        });
        endpoint.Text = server.BaseUri.AbsoluteUri;
        // Stale advanced settings must never prevent the basic three-field form from saving.
        protocol.SelectedItem = ProviderProtocol.GatewayChatCompletions;
        Save(); Check(desk.State.Provider.Protocol == ProviderProtocol.GatewayAuto && !desk.HasPendingModel, "three-field save ignores stale manual details");
        Check(hits == 0, "save alone sends no request");
        var status = await desk.CheckProviderAsync(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(desk.State.Provider.Protocol == ProviderProtocol.GatewayChatCompletions && status.Contains("연결 확인") && visible("gpt-6-luna"), "same-server route detection succeeds with exact configured model");
        reject = true; Save(); status = await desk.CheckProviderAsync(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(status.Contains("model not found") && visible("gpt-6-luna") && !status.Contains("usage"), "model rejection keeps name and shows actual server error");
        desk.KeyStore.DeleteOwnKey(); status = await desk.CheckProviderAsync();
        Check(status.Contains("저장된 회사 키가 없습니다") && !status.Contains("TLS/") && visible("gpt-6-luna"), "missing key reports local cause separately from model display");
        this.status.Text = status;
        return checks;
    }
}
public partial class MainWindow
{
    internal async Task RunSimpleSettingsChecks(string data)
    {
        var settings = new SettingsWindow(desk) { Owner = this }; settings.Show();
        try
        {
            var checks = await settings.RunSimpleChecks(model => Model.SelectedItem as string == model);
            settings.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)settings.ActualWidth, (int)settings.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(settings);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using (var output = File.Create(Path.Combine(data, "simple-settings.png"))) encoder.Save(output);
            File.WriteAllText(Path.Combine(data, "simple-settings-report.json"), JsonSerializer.Serialize(new { passed = true, checks, exactModel = "gpt-6-luna", simpleThreeFields = true, realCompanyRequests = 0 }));
        }
        finally { settings.Close(); }
    }
}
