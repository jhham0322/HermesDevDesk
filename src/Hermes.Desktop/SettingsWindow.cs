using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;

namespace Hermes.Desktop;
public sealed partial class SettingsWindow : Window
{
    private readonly DeskController desk;
    private readonly ComboBox endpointScheme = new();
    private readonly TextBlock transportNotice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6, 4, 8) };
    private readonly ComboBox mode = new(); private readonly ComboBox protocol = new(); private readonly ComboBox authentication = new();
    private readonly TextBox endpoint = new(); private readonly TextBox deployment = new(); private readonly TextBox version = new();
    private readonly PasswordBox key = new() { Background = System.Windows.Media.Brushes.White, Foreground = System.Windows.Media.Brushes.Black, Padding = new Thickness(10) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 14, 4, 6) };
    private readonly CheckBox networkConsent = new() { Content = "입력한 회사 endpoint에 연결 검사 요청을 보내는 것을 확인함" };
    private readonly CancellationTokenSource cancellation = new();
    public SettingsWindow(DeskController desk)
    {
        this.desk = desk; Title = "Hermes DevDesk · 회사 Azure API 설정"; Width = 700; Height = 790; WindowStartupLocation = WindowStartupLocation.CenterOwner; Style = Application.Current.TryFindResource(typeof(Window)) as Style;
        var content = new StackPanel { Margin = new Thickness(20) }; Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Row(string label, Control field) { content.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4, 10, 4, 4) }); field.Margin = new Thickness(4); content.Children.Add(field); }
        mode.ItemsSource = Enum.GetValues<ConnectionMode>(); mode.SelectedItem = desk.State.Provider.Mode; Row("인증/연결 방식 · 회사 API 기본, 기존 CLI는 명시적 선택", mode);
        protocol.ItemsSource = Enum.GetValues<ProviderProtocol>(); protocol.SelectedItem = desk.State.Provider.Protocol; Row("프로토콜 · Responses 권장 / Chat Completions는 호환 어댑터", protocol);
        endpoint.Text = desk.State.Provider.Endpoint; Row("회사 Azure resource 또는 사내 gateway API base URL (http:// 또는 https://)", endpoint);
        endpointScheme.ItemsSource = new[] { "URL에 스킴 직접 입력 (자동 추정 없음)", "스킴 없는 주소에 https:// 적용", "스킴 없는 주소에 http:// 적용 (암호화 없음)" };
        endpointScheme.SelectedIndex = 0; Row("스킴을 생략한 주소의 명시적 선택", endpointScheme); content.Children.Add(transportNotice);
        void UpdateTransport()
        {
            try { transportNotice.Text = ProviderSettings.TransportNotice(ProviderSettings.ResolveEndpointInput(endpoint.Text, (EndpointSchemeChoice)endpointScheme.SelectedIndex)); }
            catch (IOException) { transportNotice.Text = "스킴 없는 주소는 HTTP/HTTPS를 명시적으로 선택해야 저장됩니다. 포트와 경로는 그대로 유지됩니다."; }
            transportNotice.Foreground = transportNotice.Text.StartsWith("HTTP:", StringComparison.Ordinal) ? System.Windows.Media.Brushes.DarkOrange : System.Windows.Media.Brushes.DimGray;
        }
        endpoint.TextChanged += (_, _) => UpdateTransport(); endpointScheme.SelectionChanged += (_, _) => UpdateTransport(); UpdateTransport();
        deployment.Text = desk.State.Provider.Deployment; Row("회사에서 제공한 정확한 Azure deployment / gateway model 이름", deployment);
        version.Text = desk.State.Provider.ApiVersion; Row("api-version (Azure 구버전에만 필요, v1은 비워 둠)", version);
        authentication.ItemsSource = Enum.GetValues<ProviderAuth>(); authentication.SelectedItem = desk.State.Provider.Authentication; Row("키 전달 방식 · Azure 기본 api-key / gateway BearerKey 또는 None", authentication);
        Row("API 키 · 사용자 직접 입력 / 저장된 키를 화면에 다시 표시하지 않음", key);
        content.Children.Add(new TextBlock { Text = "키는 Windows 현재 사용자 DPAPI로 보호합니다. 기존 Codex 키/토큰을 읽거나 복사하지 않습니다. Entra 로그인·인증서 설치·TLS 검증 해제·외부 fallback은 제공하지 않습니다. API 키만으로 음성/이미지/컴퓨터 제어/계정 클라우드가 자동 제공되지 않습니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 10, 4, 8) });
        var buttons = new WrapPanel(); content.Children.Add(buttons);
        var save = new Button { Content = "설정 / 새 키 저장" }; var delete = new Button { Content = "이 앱의 저장 키 삭제" }; var check = new Button { Content = "회사 연결 검사" }; var close = new Button { Content = "닫기" };
        foreach (var button in new[] { save, delete, check, close }) buttons.Children.Add(button);
        content.Children.Add(networkConsent); content.Children.Add(status);
        content.Children.Add(new TextBlock { Text = "연결 검사는 GET models 경로만 호출합니다. 이 경로가 미지원일 수 있으며 실제 추론/배포 권한 검증과 다릅니다. 실패 시 다른 외부 서버로 전송하지 않습니다. 키는 채팅에 보내지 말고 이 화면에서 직접 입력하세요.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 12, 4, 4) });
        save.Click += (_, _) => Try(() => { Save(); status.Text = "저장 완료 · " + (desk.KeyStore.HasKey ? "보호 키 있음" : "보호 키 없음") + " · 다시 엔진 연결하세요."; });
        delete.Click += (_, _) => Try(() => { if (MessageBox.Show(this, "이 앱 0.5의 보호 키만 삭제할까요? 기존 Codex 인증은 건드리지 않습니다.", "키 삭제", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { desk.Configure(desk.State.Provider, deleteKey: true); key.Clear(); status.Text = "앱 보호 키 삭제됨"; } });
        check.Click += async (_, _) => { if (networkConsent.IsChecked != true) { status.Text = "회사 endpoint 전송 확인을 선택하세요."; return; } try { Save(); check.IsEnabled = false; status.Text = "검사 중…"; status.Text = await desk.CheckProviderAsync(cancellation.Token); } catch (Exception ex) { status.Text = Safety.Redact(ex.Message); } finally { check.IsEnabled = true; } };
        close.Click += (_, _) => Close(); status.Text = desk.KeyStore.HasKey ? "이 앱의 보호 키 저장됨 (내용 숨김)" : "이 앱의 보호 키 없음";
        Closed += (_, _) => { key.Clear(); cancellation.Cancel(); };
    }
    private ProviderSettings Current() => new() { Mode = (ConnectionMode)mode.SelectedItem, Protocol = (ProviderProtocol)protocol.SelectedItem, Authentication = (ProviderAuth)authentication.SelectedItem, Endpoint = (ConnectionMode)mode.SelectedItem == ConnectionMode.CompanyApi ? ProviderSettings.ResolveEndpointInput(endpoint.Text, (EndpointSchemeChoice)endpointScheme.SelectedIndex) : endpoint.Text.Trim(), Deployment = deployment.Text.Trim(), ApiVersion = version.Text.Trim() };
    private void Save() { var value = key.Password; var settings = Current(); desk.Configure(settings, string.IsNullOrWhiteSpace(value) ? null : value); endpoint.Text = settings.Endpoint; key.Clear(); }
    private void Try(Action action) { try { action(); } catch (Exception ex) { key.Clear(); status.Text = Safety.Redact(ex.Message); } }
    internal int RunProgrammaticChecks()
    {
        mode.SelectedItem = ConnectionMode.CompanyApi; endpoint.Text = "https://company.invalid"; deployment.Text = "fixture-deployment";
        protocol.SelectedItem = ProviderProtocol.AzureV1Responses; version.Clear(); authentication.SelectedItem = ProviderAuth.ApiKeyHeader;
        key.Password = "SYNTHETIC_ONLY_WPF_SETTINGS_TEST"; Save();
        if (key.Password.Length != 0 || !desk.KeyStore.HasKey || !desk.KeyStore.ReadOwnKey().Contains("SYNTHETIC_ONLY")) throw new IOException("Protected settings save failed");
        var revision = desk.State.Provider.KeyRevision; key.Password = "SYNTHETIC_ONLY_WPF_SETTINGS_REPLACEMENT"; Save();
        if (desk.State.Provider.KeyRevision != revision + 1) throw new IOException("Key replacement revision failed");
        endpoint.Text = "ftp://unsupported.invalid"; var failed = false; try { Save(); } catch (IOException) { failed = true; }
        if (!failed || desk.State.Provider.Endpoint != "https://company.invalid") throw new IOException("Invalid endpoint altered settings");
        desk.Configure(desk.State.Provider, deleteKey: true);
        if (desk.KeyStore.HasKey) throw new IOException("Own synthetic key deletion failed");
        return 4;
    }
}
