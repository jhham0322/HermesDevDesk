using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;

namespace Hermes.Desktop;
// Deliberately separate from API-key import/export; OAuth credentials never enter that UI.
public sealed class ChatGptPlanWindow : Window
{
    private readonly DeskController desk;
    private readonly ComboBox accounts = new() { MinWidth = 350, DisplayMemberPath = "Label", Margin = new(0, 6, 0, 6) };
    private readonly ComboBox models = new() { MinWidth = 350, Margin = new(0, 6, 0, 6) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 6) };
    private readonly List<Button> actions = [];
    private CancellationTokenSource? operation;
    private string catalogAccount = "";
    private readonly Action<Uri> browser;
    private readonly string cwd;
    public ChatGptPlanWindow(DeskController desk, Action<Uri>? browser = null, string? cwd = null)
    {
        this.desk = desk; this.cwd = cwd ?? desk.DataDirectory; this.browser = browser ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })); Title = "Sign in with ChatGPT"; Width = 680; Height = 650; MinWidth = 580; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new(24) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Sign in with ChatGPT", FontSize = 24, Margin = new(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "API 키 없이 공식 브라우저 로그인으로 ChatGPT 플랜 사용을 승인합니다. 추론은 공식 Responses API를 사용합니다. 계정·워크스페이스의 모델 접근, 앱별 사용 한도와 플랜 정책이 적용됩니다. 자동 결제·다른 공급자로 전환하지 않습니다.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(accounts);
        accounts.SelectionChanged += (_, _) => { models.ItemsSource = null; catalogAccount = ""; status.Text = "선택 계정의 모델 목록을 새로 확인하세요."; };
        Add(panel, "Continue with ChatGPT (새 계정/워크스페이스)", () => SignIn(null, false));
        Add(panel, "선택 계정 다시 로그인", () => SignIn(Selected().Id, false));
        Add(panel, "선택 계정 플랜 사용 승인", () => SignIn(Selected().Id, true));
        Add(panel, "선택 계정 모델 목록 새로 확인", Catalog);
        panel.Children.Add(models);
        Add(panel, "이 계정·모델로 에이전트 연결", Connect);
        Add(panel, "선택 계정 로그아웃", SignOut);
        if (desk.State.Provider.UseHermesChatGpt)
            Add(panel, "이전 Codex 계정 관리", () => { new OfficialChatGptWindow(desk, this.cwd) { Owner = this }.ShowDialog(); return Task.CompletedTask; });
        var cancel = new Button { Content = "진행 취소", Margin = new(0, 6, 0, 6), Padding = new(8) }; cancel.Click += (_, _) => operation?.Cancel(); panel.Children.Add(cancel);
        var usage = new Button { Content = "ChatGPT 설정 → 사용량 / 앱 권한", Margin = new(0, 6, 0, 6), Padding = new(8) };
        usage.Click += (_, _) => Process.Start(new ProcessStartInfo("https://chatgpt.com/settings/usage") { UseShellExecute = true }); panel.Children.Add(usage);
        panel.Children.Add(new TextBlock { Text = "기존 ChatGPT 대화·기억은 이전되지 않습니다. 이 앱의 로컬 대화와 작업만 사용합니다. 미지원: 이미지 생성·파일 검색·Code Interpreter·호스팅 MCP·오디오/비디오·Files 업로드 API. 로컬 도구 승인은 기존 정책을 따릅니다. 긴 실행 중 토큰이 만료되면 작업을 보존하고 중지한 뒤 재연결하세요. 정상 응답 완료 전에는 모델 접근 검증 성공으로 표시하지 않습니다.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) });
        panel.Children.Add(status); Reload(); status.Text = "미인증 / 실제 계정 접근 미확인";
        Closing += (_, _) => operation?.Cancel();
    }
    private void Add(Panel p, string label, Func<Task> run)
    {
        var b = new Button { Content = label, Margin = new(0, 3, 0, 3), Padding = new(8) }; actions.Add(b); p.Children.Add(b);
        b.Click += async (_, _) =>
        {
            if (operation != null) return;
            operation = new(); foreach (var a in actions) a.IsEnabled = false; accounts.IsEnabled = models.IsEnabled = false;
            try { await run(); }
            catch (OperationCanceledException) { status.Text = "취소됨. 진행 중이던 인증·대화는 완료로 처리하지 않았습니다."; }
            catch (Exception e) { status.Text = Safety.Redact(e.Message); }
            finally { operation.Dispose(); operation = null; foreach (var a in actions) a.IsEnabled = true; accounts.IsEnabled = models.IsEnabled = true; }
        };
    }
    private CancellationToken Token => operation?.Token ?? CancellationToken.None;
    private ChatGptAccount Selected() => accounts.SelectedItem as ChatGptAccount ?? throw new IOException("ChatGPT 계정을 선택하세요.");
    private void Idle() { if (desk.State.Tasks.Any(DeskController.IsRunning)) throw new IOException("진행 중인 작업을 먼저 중지하세요. 계정 전환·로그아웃은 실행 중에 적용하지 않습니다."); }
    private void Reload(string? selected = null)
    { var list = desk.ChatGptPlan.Accounts(); accounts.ItemsSource = list; accounts.SelectedItem = list.FirstOrDefault(a => a.Id == (selected ?? desk.State.Provider.OAuthAccountId)) ?? list.FirstOrDefault(); }
    private async Task SignIn(string? id, bool enable)
    {
        Idle(); status.Text = "공식 브라우저에서 로그인·동의를 완료하세요. 비밀번호를 이 앱에 입력하지 마세요.";
        var signedIn = await desk.ChatGptPlan.SignInAsync(id, browser, enable, Token);
        Reload(signedIn);
        if (!Selected().PlanAllowed)
        {
            status.Text = "로그인했습니다. ChatGPT 모델을 사용하려면 선택 계정의 플랜 사용을 승인하세요.";
            return;
        }
        await Catalog();
        if (models.SelectedItem != null) await Connect();
    }
    private async Task Catalog()
    {
        var selected = Selected(); var list = await desk.ChatGptPlan.ModelsAsync(selected.Id, Token); models.ItemsSource = list; models.SelectedIndex = list.Count > 0 ? 0 : -1;
        catalogAccount = selected.Id; status.Text = $"서버 모델 목록 {list.Count}개. 목록 조회는 실제 추론 성공을 의미하지 않습니다.";
    }
    private async Task Connect()
    {
        Idle(); var account = Selected(); if (catalogAccount != account.Id || models.SelectedItem is not ChatGptModel model) throw new IOException("현재 계정의 서버 모델 목록에서 모델을 선택하세요.");
        var exe = AppServerClient.FindInstalledCodex() ?? (File.Exists(desk.State.CodexPath) ? desk.State.CodexPath : null)
            ?? throw new IOException("동봉된 Codex 엔진을 찾을 수 없습니다.");
        await desk.DisconnectOAuthAsync(); Token.ThrowIfCancellationRequested();
        desk.Configure(new ProviderSettings { Mode = ConnectionMode.ExistingChatGptCli, OAuthAccountId = account.Id, Endpoint = "", Deployment = model.Slug });
        await desk.ConnectAsync(exe, cwd, Token); status.Text = "선택 계정: " + account.Label + " / " + model.DisplayName + ". 에이전트 연결됨; 실제 모델 추론은 아직 미확인.";
    }
    private async Task SignOut()
    {
        Idle(); var selected = Selected();
        var active = desk.State.Provider.IsChatGptPlanOAuth && desk.State.Provider.OAuthAccountId == selected.Id;
        if (active) await desk.DisconnectOAuthAsync();
        var revoked = await desk.ChatGptPlan.SignOutAsync(selected.Id, Token); Reload(selected.Id);
        if (active) desk.UseSavedCompanyConnection();
        status.Text = revoked ? "로그아웃 완료. 계정 등록·호스트 식별자는 다음 로그인을 위해 유지합니다." : "로컬 로그아웃 완료. 원격 세션 해제는 확인하지 못했습니다. ChatGPT 설정에서 앱 연결을 해제하세요.";
    }
}
