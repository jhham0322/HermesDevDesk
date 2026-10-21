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
        static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject node)
        {
            yield return node;
            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node).OfType<System.Windows.DependencyObject>())
                foreach (var item in Descendants(child)) yield return item;
        }
        void Click(string label) => Descendants(this).OfType<System.Windows.Controls.Button>().Single(b => b.Content as string == label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var labels = Descendants(this).OfType<System.Windows.Controls.Button>().Select(b => b.Content as string).ToList();
        foreach (var label in new[] { "새 모델", "선택 모델 수정", "선택 모델 삭제", "JSON 내보내기", "JSON 가져오기", "모델 저장 / 수정" })
            Check(labels.Contains(label), "installed model UI exposes " + label);
        Check(Title.Contains(AppVersion.Current), "model settings shows loaded assembly version");
        Check(manual.IsChecked != true, "new profile uses simple connection");
        deployment.Text = "alpha-ui"; endpoint.Text = "https://alpha.invalid"; key.Password = "SYNTHETIC_UI_ALPHA_TOKEN"; Save();
        Check(desk.State.Provider.Protocol == ProviderProtocol.GatewayAuto && desk.State.Provider.Authentication == ProviderAuth.BearerKey, "three fields use gateway Chat API discovery and Bearer token");
        Check(key.Password.Length == 0 && savedModels.Items.Count == 1, "save adds model row and clears token field");
        var alpha = desk.State.ModelConnections.Single();
        Click("새 모델");
        Check(editingId == null && savedModels.SelectedItem == null && deployment.Text == "" && endpoint.Text == "" && key.Password == "", "new button clears editing state");
        deployment.Text = "beta-ui"; endpoint.Text = "https://beta.invalid/v1/messages"; key.Password = "SYNTHETIC_UI_BETA_TOKEN"; Click("모델 저장 / 수정");
        Check(savedModels.Items.Count == 2, "second model has independent connection");
        savedModels.SelectedItem = alpha;
        Check(deployment.Text == "alpha-ui" && endpoint.Text == "https://alpha.invalid", "selection restores model and URL");
        deployment.Text = "alpha-renamed-ui"; endpoint.Text = "https://alpha-new.invalid"; Click("모델 저장 / 수정");
        Check(savedModels.SelectedItem is ModelConnection active && active.Id == alpha.Id, "saved model remains selected for edit and delete");
        Check(desk.State.ModelConnections.Count == 2 && desk.KeyStore.ReadOwnKey() == "SYNTHETIC_UI_ALPHA_TOKEN", "edit retains own token");
        Check(!desk.DisplayModels.Contains("alpha-ui"), "rename removes old model name");
        desk.ImportModelConnections(desk.ExportModelConnections());
        Check(desk.State.ModelConnections.Count == 2, "JSON import updates without duplication");
        savedModels.SelectedItem = desk.State.ModelConnections.Single(c => c.Settings.Deployment == "beta-ui");
        Click("선택 모델 삭제");
        Check(savedModels.Items.Count == 1 && !desk.DisplayModels.Contains("beta-ui"), "delete removes model row");
        Click("선택 모델 삭제");
        Check(savedModels.Items.Count == 0 && editingId == null && deployment.Text == "" && endpoint.Text == "", "delete last model clears stale editor");
        Click("선택 모델 삭제");
        Check(status.Text.Contains("선택하세요"), "delete without selection explains action");
        Click("새 모델"); deployment.Text = "replacement-ui"; endpoint.Text = "https://replacement.invalid"; key.Password = "SYNTHETIC_REPLACEMENT_TOKEN"; Click("모델 저장 / 수정");
        Check(savedModels.Items.Count == 1 && desk.State.ModelConnections.Single().Settings.Deployment == "replacement-ui", "can add after deleting all models");
        var reopened = new SettingsWindow(desk);
        Check(reopened.savedModels.SelectedItem is ModelConnection initial && initial.Settings.Deployment == "replacement-ui", "reopened settings selects current model");
        reopened.Close();
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
            File.WriteAllText(Path.Combine(data, "simple-settings-report.json"), JsonSerializer.Serialize(new { passed = true, checks, version = AppVersion.Current, executable = Environment.ProcessPath, simpleThreeFields = true, realCompanyRequests = 0 }));
        }
        finally { settings.Close(); }
    }
}
