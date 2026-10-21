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
        Check(manual.IsChecked != true, "new profile uses simple connection");
        deployment.Text = "alpha-ui"; endpoint.Text = "https://alpha.invalid"; key.Password = "SYNTHETIC_UI_ALPHA_TOKEN"; Save();
        Check(desk.State.Provider.IsAnthropic && desk.State.Provider.Authentication == ProviderAuth.BearerKey, "three fields use Claude Messages and Bearer token");
        Check(key.Password.Length == 0 && savedModels.Items.Count == 1, "save adds model row and clears token field");
        var alpha = desk.State.ModelConnections.Single();
        editingId = null; deployment.Text = "beta-ui"; endpoint.Text = "https://beta.invalid/v1/messages"; key.Password = "SYNTHETIC_UI_BETA_TOKEN"; Save();
        Check(savedModels.Items.Count == 2, "second model has independent connection");
        savedModels.SelectedItem = alpha;
        Check(deployment.Text == "alpha-ui" && endpoint.Text == "https://alpha.invalid", "selection restores model and URL");
        deployment.Text = "alpha-renamed-ui"; endpoint.Text = "https://alpha-new.invalid"; Save();
        Check(desk.State.ModelConnections.Count == 2 && desk.KeyStore.ReadOwnKey() == "SYNTHETIC_UI_ALPHA_TOKEN", "edit retains own token");
        Check(!desk.DisplayModels.Contains("alpha-ui"), "rename removes old model name");
        desk.ImportModelConnections(desk.ExportModelConnections());
        Check(desk.State.ModelConnections.Count == 2, "JSON import updates without duplication");
        desk.DeleteModelConnection(desk.State.ModelConnections.Single(c => c.Settings.Deployment == "beta-ui").Id);
        savedModels.ItemsSource = desk.State.ModelConnections.ToList();
        Check(savedModels.Items.Count == 1 && !desk.DisplayModels.Contains("beta-ui"), "delete removes model row");
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        status.Text = "모델 관리 및 JSON 검사 통과";
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
            File.WriteAllText(Path.Combine(data, "simple-settings-report.json"), JsonSerializer.Serialize(new { passed = true, checks, simpleThreeFields = true, realCompanyRequests = 0 }));
        }
        finally { settings.Close(); }
    }
}
