using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hermes.Core;

namespace Hermes.Desktop;
public sealed partial class SettingsWindow : Window
{
    private readonly DeskController desk;
    private string? editingId;
    private readonly ListBox savedModels = new() { MaxHeight = 110 };
    private readonly ComboBox endpointScheme = new(), mode = new(), protocol = new(), authentication = new();
    private readonly TextBox endpoint = new(), deployment = new(), maxTokens = new();
    private readonly PasswordBox key = new() { Background = System.Windows.Media.Brushes.White, Foreground = System.Windows.Media.Brushes.Black, Padding = new Thickness(10) };
    private readonly TextBlock keyHint = new() { FontSize = 12, Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 12, 4, 8) };
    private readonly TextBlock transportNotice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 4, 8), FontSize = 12 };
    private readonly CheckBox manual = new() { Content = "프로토콜과 인증 방식을 직접 지정" };
    private readonly CheckBox networkConsent = new(); // Retained only for isolated legacy UI checks; no extra consent step.
    private readonly CancellationTokenSource cancellation = new();
    private readonly DispatcherTimer draftTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    public SettingsWindow(DeskController desk)
    {
        this.desk = desk; Title = "모델 관리 · " + AppVersion.Current; Width = 720; Height = 780; MinWidth = 560; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = Application.Current.TryFindResource(typeof(Window)) as Style;
        var content = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        content.Children.Add(new TextBlock { Text = "모델 관리 · " + AppVersion.Current, FontSize = 25, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "회사에서 받은 URL, API 키, 모델명을 입력하세요.", Margin = new Thickness(4, 8, 4, 12), TextWrapping = TextWrapping.Wrap });
        static void Row(Panel panel, string label, Control field) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4, 10, 4, 4) }); field.Margin = new Thickness(4); panel.Children.Add(field); }
        endpoint.Text = desk.State.Provider.Endpoint; Row(content, "모델 URL", endpoint);
        endpoint.ToolTip = "http:// 또는 https://를 포함한 회사 서버 주소. 완전한 API 호출 URL도 입력할 수 있습니다.";
        Row(content, "토큰", key);
        UpdateKeyHint(); content.Children.Add(keyHint);
        deployment.Text = desk.HasPendingModel ? desk.State.PendingModel : desk.State.Provider.Deployment; Row(content, "모델명", deployment);
        deployment.ToolTip = "회사에서 받은 모델명을 그대로 입력하세요. 연결 실패와 별개로 표시·보존합니다.";
         draftTimer.Tick += (_, _) => { draftTimer.Stop(); desk.Save(); };
        savedModels.ItemsSource = desk.State.ModelConnections.ToList();
        editingId = desk.State.ModelConnections.FirstOrDefault(c => c.Settings.Deployment == desk.State.Provider.Deployment)?.Id;
        content.Children.Add(new TextBlock { Text = "모델별 연결 · 모델명 / URL / 토큰", Margin = new Thickness(4, 10, 4, 4) });
        content.Children.Add(savedModels);
        var modelButtons = new WrapPanel(); content.Children.Add(modelButtons);
        var addModel = new Button { Content = "새 모델" };
        var editModel = new Button { Content = "선택 모델 수정" };
        var removeModel = new Button { Content = "선택 모델 삭제" };
        var exportModels = new Button { Content = "JSON 내보내기" };
        var importModels = new Button { Content = "JSON 가져오기" };
        foreach (var button in new[] { addModel, editModel, removeModel, exportModels, importModels }) modelButtons.Children.Add(button);
        content.Children.Add(new TextBlock { Text = "내보낸 JSON에는 토큰이 포함됩니다. 가져오기는 같은 모델명을 갱신하고 다른 모델을 추가합니다.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        addModel.Click += (_, _) => { editingId = null; savedModels.SelectedItem = null; deployment.Clear(); endpoint.Clear(); key.Clear(); manual.IsChecked = false; status.Text = "새 모델의 모델명, URL, 토큰을 입력한 뒤 저장하세요."; };
        editModel.Click += (_, _) => {
            var selected = savedModels.SelectedItem as ModelConnection ?? desk.State.ModelConnections.FirstOrDefault(c => c.Id == editingId);
            if (selected == null) { status.Text = "수정할 모델을 목록에서 선택하세요."; return; }
            savedModels.SelectedItem = selected; deployment.Focus(); status.Text = "모델명, URL, 토큰을 수정한 뒤 모델 저장 / 수정을 누르세요.";
        };
        savedModels.SelectionChanged += (_, _) => {
            if (savedModels.SelectedItem is not ModelConnection selected) return;
            editingId = selected.Id; endpoint.Text = selected.Settings.Endpoint; deployment.Text = selected.Settings.Deployment;
            protocol.SelectedItem = selected.Settings.Protocol; authentication.SelectedItem = selected.Settings.Authentication;
            maxTokens.Text = selected.Settings.MaxOutputTokens.ToString(); manual.IsChecked = selected.Settings.UseAdvancedSettings;
            key.Clear(); status.Text = "선택 모델 수정 · 토큰을 비워두면 기존 토큰을 유지합니다.";
        };
        removeModel.Click += (_, _) => Try(() => { if (savedModels.SelectedItem is ModelConnection selected) { desk.DeleteModelConnection(selected.Id); editingId = null; savedModels.ItemsSource = desk.State.ModelConnections.ToList(); status.Text = "모델 삭제됨"; } });
        exportModels.Click += (_, _) => Try(() => { var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "hermes-models.json" }; if (dialog.ShowDialog(this) == true) { File.WriteAllText(dialog.FileName, desk.ExportModelConnections()); status.Text = "JSON 내보내기 완료 · 토큰 포함"; } });
        importModels.Click += (_, _) => Try(() => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" }; if (dialog.ShowDialog(this) == true) { if (new FileInfo(dialog.FileName).Length > 2_000_000) throw new IOException("파일 크기 제한은 2MB입니다."); desk.ImportModelConnections(File.ReadAllText(dialog.FileName)); savedModels.ItemsSource = desk.State.ModelConnections.ToList(); savedModels.SelectedItem = desk.State.ModelConnections.FirstOrDefault(c => c.Settings.Deployment == desk.State.Provider.Deployment); status.Text = "JSON 가져오기 완료"; } });
        mode.ItemsSource = Enum.GetValues<ConnectionMode>(); mode.SelectedItem = desk.State.Provider.Mode;
        protocol.ItemsSource = Enum.GetValues<ProviderProtocol>(); protocol.SelectedItem = desk.State.Provider.Protocol;
        authentication.ItemsSource = Enum.GetValues<ProviderAuth>(); authentication.SelectedItem = desk.State.Provider.Authentication;
        endpointScheme.ItemsSource = new[] { "주소에 http:// 또는 https:// 직접 입력", "스킴 없는 주소에 https:// 적용", "스킴 없는 주소에 http:// 적용" }; endpointScheme.SelectedIndex = 0;
        maxTokens.Text = desk.State.Provider.MaxOutputTokens.ToString();
        manual.IsChecked = desk.State.Provider.UseAdvancedSettings;
        content.Children.Add(new TextBlock { Text = "기본 연결은 Claude 호환 Messages API입니다. 모델명, URL, 토큰만 입력하세요.", FontSize = 12, Margin = new Thickness(4, 10, 4, 4), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(transportNotice);
        void Transport() { try { transportNotice.Text = ProviderSettings.TransportNotice(ProviderSettings.ResolveEndpointInput(endpoint.Text, (EndpointSchemeChoice)endpointScheme.SelectedIndex)); } catch (IOException) { transportNotice.Text = "URL에 http:// 또는 https://를 포함해 입력하세요."; } }
        endpoint.TextChanged += (_, _) => Transport(); endpointScheme.SelectionChanged += (_, _) => Transport(); Transport();
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; content.Children.Add(buttons);
        var save = new Button { Content = "모델 저장 / 수정" }; var check = new Button { Content = "저장하고 연결 확인" }; var close = new Button { Content = "닫기" };
        buttons.Children.Add(save); buttons.Children.Add(check); buttons.Children.Add(close);
        content.Children.Add(status);
        content.Children.Add(new TextBlock { Text = "실행 파일: " + Environment.ProcessPath, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4), ToolTip = Environment.ProcessPath });
        content.Children.Add(new TextBlock { Text = "연결 확인은 최대 출력 1토큰의 짧은 요청을 보냅니다. 지원하지 않는 경로만 같은 서버 안에서 확인하며 사용량이 발생할 수 있습니다.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(4, 0, 4, 12) });
        var details = new StackPanel { Margin = new Thickness(4) }; var advanced = new Expander { Header = "고급 설정 · 일반 연결에는 필요 없음", Content = details, IsExpanded = manual.IsChecked == true }; content.Children.Add(advanced);
        details.Children.Add(manual); var controls = new StackPanel(); details.Children.Add(controls);
        Row(controls, "연결 방식", mode); Row(controls, "프로토콜", protocol); Row(controls, "인증 헤더", authentication); Row(controls, "최대 출력 토큰", maxTokens); Row(controls, "스킴을 생략한 URL", endpointScheme);
        void Manual() => controls.IsEnabled = manual.IsChecked == true; manual.Checked += (_, _) => Manual(); manual.Unchecked += (_, _) => Manual(); Manual();
        details.Children.Add(ProviderExamples());
        protocol.SelectionChanged += (_, _) => { if (protocol.SelectedItem is ProviderProtocol.AnthropicMessages) authentication.SelectedItem = ProviderAuth.BearerKey; };
        var delete = new Button { Content = "저장한 API 키 삭제" }; details.Children.Add(delete);
        save.Click += (_, _) => Try(() => { Save(); status.Text = "설정 저장됨 · 모델: " + desk.State.Provider.Deployment; });
        check.Click += async (_, _) =>
        {
            try { Save(); check.IsEnabled = save.IsEnabled = false; status.Text = "모델 연결 확인 중…"; status.Text = await desk.CheckProviderAsync(cancellation.Token); }
            catch (Exception ex) { status.Text = ConnectionErrors.Describe(ex, cancellation.Token); }
            finally { check.IsEnabled = save.IsEnabled = true; }
        };
        delete.Click += (_, _) => Try(() => { if (MessageBox.Show(this, "이 앱에 저장한 API 키를 삭제할까요?", "키 삭제", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { desk.DeleteModelToken(editingId); key.Clear(); status.Text = "저장한 키 삭제됨"; } });
        close.Click += (_, _) => Close();
        Closed += (_, _) => { draftTimer.Stop(); cancellation.Cancel(); desk.Save(); key.Clear(); };
    }
    private ProviderSettings Current()
    {
        var advanced = manual.IsChecked == true;
        return new() { UseAdvancedSettings = advanced, Mode = advanced ? (ConnectionMode)mode.SelectedItem : ConnectionMode.CompanyApi, Protocol = advanced ? (ProviderProtocol)protocol.SelectedItem : ProviderProtocol.AnthropicMessages, Authentication = advanced ? (ProviderAuth)authentication.SelectedItem : ProviderAuth.BearerKey, MaxOutputTokens = advanced ? int.TryParse(maxTokens.Text, out var max) ? max : throw new IOException("최대 출력 토큰은 정수로 입력하세요.") : 8192, Endpoint = advanced && mode.SelectedItem is ConnectionMode.ExistingChatGptCli ? endpoint.Text.Trim() : ProviderSettings.ResolveEndpointInput(endpoint.Text, (EndpointSchemeChoice)endpointScheme.SelectedIndex), Deployment = deployment.Text.Trim() };
    }
    private void Save()
    {
        var settings = Current();
        if (settings.Mode == ConnectionMode.ExistingChatGptCli) { desk.Configure(settings); return; }
        desk.SaveModelConnection(settings, key.Password, editingId);
        editingId = desk.State.ModelConnections.First(c => c.Settings.Deployment == settings.Deployment).Id;
        key.Clear(); endpoint.Text = settings.Endpoint; UpdateKeyHint();
        savedModels.ItemsSource = desk.State.ModelConnections.ToList();

    }
    private void UpdateKeyHint() => keyHint.Text = desk.KeyStore.HasKey ? "저장된 키가 있습니다. 새 키로 바꿀 때만 입력하세요." : "키는 이 PC의 Windows 사용자 보호 저장소에 저장합니다.";
    private void Try(Action action) { try { action(); } catch (Exception ex) { status.Text = ConnectionErrors.Describe(ex); } }
}
