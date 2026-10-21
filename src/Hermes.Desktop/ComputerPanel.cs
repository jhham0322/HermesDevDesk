using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hermes.Core;

namespace Hermes.Desktop;

internal sealed record AllowedComputerWindow(string Id, ComputerWindow Window)
{ public override string ToString() => Window.ToString(); }

public partial class MainWindow
{
    private readonly ComputerWorkerClient computerWorker = new();
    private readonly ComputerChangeWaitClient computerWaiter = new();
    private readonly CheckBox computerAuto = new() { Content = "허용한 창의 일반 클릭·입력·스크롤 자동 실행 (세션 한정)" };
    private bool computerAutomatic;
    private CancellationTokenSource? computerWaitWake;
    private readonly HashSet<string> computerLifecycle = [];
    private (WorkItem Task, ComputerToolResult Result, ComputerFrame Frame, string WindowId)? pendingComputerDelivery;
    private ComputerFrame? deliveredComputerFrame;
    private string? deliveredComputerWindow;
    private DateTimeOffset? deliveredComputerAt;
    private readonly ComputerSnapshotGate computerSnapshots = new();
    private readonly Dictionary<string, (int Width, int Height, int ImageWidth, int ImageHeight)> computerSizes = [];
    private readonly List<AllowedComputerWindow> computerWindows = [];
    private readonly SemaphoreSlim computerGate = new(1, 1);
    private readonly CheckBox computerEnable = new() { Content = "새 채팅에 Windows 컴퓨터 도구 연결" };
    private readonly ListBox computerCandidates = new() { MaxHeight = 140, MinHeight = 70 };
    private readonly ListBox computerAllowed = new() { MaxHeight = 100, MinHeight = 50 };
    private readonly TextBlock computerStatus = new() { Text = "중지됨 · 대상 창을 추가한 뒤 제어를 허용하세요.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 10, 4, 8), FontSize = 13 };
    private readonly Image computerPreview = new() { MaxHeight = 310, Stretch = Stretch.Uniform };
    private CancellationTokenSource computerCancellation = new();
    private Window? computerApproval;
    private HwndSource? computerSource;
    private bool computerArmed;
    private string? computerProjectId;
    private long computerEpoch;
    private WorkItem? computerRequestTask;
    private const int ComputerHotkeyId = 0x4843;

    private void InitializeComputerPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(4) }; var tab = new TabItem { Header = "컴퓨터", Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }; InspectorTabs.Items.Add(tab);
        panel.Children.Add(new TextBlock { Text = "Windows 컴퓨터 제어", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(computerEnable); panel.Children.Add(computerAuto);
        panel.Children.Add(new TextBlock { Text = "모든 브라우저 포함: 열린 Windows 창을 직접 선택합니다. DOM 도구는 내장 브라우저 전용입니다. 변화 대기: 로컬 300ms, 64×40 비교 표본, 밝기 차이 12/255 이상·표본 1% 이상, debounce 300ms, 알림 최대 초당 1회. 이는 픽셀 휴리스틱이며 의미 판단은 모델이 합니다. 로그인·결제·삭제·전송·보안 변경은 자동 허용 범위가 아닙니다.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "허용한 창의 화면과 UI 텍스트가 연결한 모델로 전송됩니다. 요청별 내용을 검토한 뒤 실행합니다.", TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(4, 8, 4, 8) });
        var find = new Button { Content = "열린 창 새로고침" }; panel.Children.Add(find); panel.Children.Add(computerCandidates);
        var add = new Button { Content = "선택 창을 대상에 추가" }; panel.Children.Add(add); panel.Children.Add(new TextBlock { Text = "이번 실행에서 허용할 대상 창", Margin = new Thickness(4, 10, 4, 4) }); panel.Children.Add(computerAllowed);
        var controls = new WrapPanel(); var remove = new Button { Content = "대상 제외" }; var arm = new Button { Content = "선택한 창 제어 허용" }; var stop = new Button { Content = "즉시 중단", Background = Brushes.MistyRose, Foreground = Brushes.DarkRed };
        foreach (var button in new[] { remove, arm, stop }) controls.Children.Add(button); panel.Children.Add(controls); panel.Children.Add(computerStatus);
        panel.Children.Add(new TextBlock { Text = "Ctrl+Alt+F12: 전역 즉시 중단\n스크린샷은 최대 1600×1000이며 좌표는 해당 이미지 기준입니다. 관리자 앱·UAC·잠금 화면은 지원하지 않습니다. 창 캡처를 지원하지 않는 앱은 오류를 표시합니다.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(4, 4, 4, 12) }); panel.Children.Add(computerPreview);
        find.Click += (_, _) => { try { computerCandidates.ItemsSource = ComputerNative.Windows(); } catch (Exception ex) { computerStatus.Text = Safety.Redact(ex.Message); } };
        add.Click += (_, _) =>
        {
            if (computerCandidates.SelectedItem is not ComputerWindow window) { computerStatus.Text = "추가할 창을 선택하세요."; return; }
            StopComputer();
            if (!computerWindows.Any(w => w.Window.Handle == window.Handle && w.Window.StartTicks == window.StartTicks)) computerWindows.Add(new(Guid.NewGuid().ToString("N"), window));
            computerAllowed.ItemsSource = computerWindows.ToList(); computerStatus.Text = "대상 창 추가됨 · 제어 허용 버튼으로 활성화하세요.";
        };
        remove.Click += (_, _) => { StopComputer(); if (computerAllowed.SelectedItem is AllowedComputerWindow window) computerWindows.Remove(window); computerAllowed.ItemsSource = computerWindows.ToList(); };
        arm.Click += (_, _) =>
        {
            if (CurrentProject == null || computerWindows.Count == 0) { computerStatus.Text = "프로젝트와 허용할 대상 창을 선택하세요."; return; }
            if (!CurrentProject.ComputerEnabled) { computerStatus.Text = "먼저 새 채팅에 컴퓨터 도구 연결을 선택하세요."; return; }
            StopComputer(); computerAutomatic = computerAuto.IsChecked == true; computerArmed = true; computerProjectId = CurrentProject.Id; ComputerGlobalStop.Visibility = Visibility.Visible;
            computerStatus.Text = $"허용됨 · 대상 {computerWindows.Count}개 · 연결 모델이 요청할 때마다 검토합니다. 중단: Ctrl+Alt+F12";
        };
        stop.Click += ComputerStop_Click;
        computerAuto.Click += (_, _) => { StopComputer(); computerStatus.Text = "자동 실행 범위가 철회되었습니다. 선택 창을 확인하고 제어 허용을 다시 눌러 새 세션을 시작하세요."; };
        computerEnable.Click += (_, _) =>
        {
            var project = CurrentProject;
            if (project == null) { computerEnable.IsChecked = false; computerStatus.Text = "프로젝트를 선택하세요."; return; }
            if (desk.State.Tasks.Any(t => t.ProjectId == project.Id && DeskController.IsRunning(t))) { computerEnable.IsChecked = project.ComputerEnabled; computerStatus.Text = "실행을 중단한 뒤 도구 설정을 바꾸세요."; return; }
            StopComputer(); project.ComputerEnabled = computerEnable.IsChecked == true; desk.Save(); computerStatus.Text = "새 채팅부터 적용됩니다. 대상 창을 선택하고 제어를 허용하세요.";
        };
        desk.ComputerTool = ComputerActionAsync;
        desk.ComputerLifecycleEvent += (task, reason) => { if (task == CurrentTask) { computerLifecycle.Add(reason); computerWaitWake?.Cancel(); } };
        desk.ComputerResultDelivered += (task, result) =>
        {
            if (pendingComputerDelivery is { } pending && pending.Task == task && ReferenceEquals(pending.Result, result))
            { deliveredComputerFrame = pending.Frame; deliveredComputerWindow = pending.WindowId; deliveredComputerAt = DateTimeOffset.UtcNow; pendingComputerDelivery = null; }
        };
        desk.ComputerStopRequested += task => { if (task == null || task == computerRequestTask || task == CurrentTask) StopComputer(); };
        SourceInitialized += (_, _) =>
        {
            computerSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); computerSource?.AddHook(ComputerHotkey);
            if (!ComputerNative.RegisterHotKey(new WindowInteropHelper(this).Handle, ComputerHotkeyId, 0x4003, 0x7B)) computerStatus.Text = "중지됨 · 전역 중단 단축키가 다른 앱에서 사용 중입니다. 상단 즉시 중단 버튼을 사용하세요.";
        };
        Closed += (_, _) => { StopComputer(); ComputerNative.UnregisterHotKey(new WindowInteropHelper(this).Handle, ComputerHotkeyId); computerSource?.RemoveHook(ComputerHotkey); computerWorker.Dispose(); computerWaiter.Dispose(); computerCancellation.Dispose(); };
    }
    private IntPtr ComputerHotkey(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (message == 0x0312 && wparam.ToInt32() == ComputerHotkeyId) { handled = true; ComputerStop_Click(this, new RoutedEventArgs()); } return IntPtr.Zero;
    }
    private void StopComputer()
    {
        computerArmed = false; computerAutomatic = false; computerProjectId = null; computerEpoch++; computerCancellation.Cancel(); computerWorker.Stop(); computerWaiter.Stop(); computerWaitWake?.Cancel(); computerLifecycle.Clear(); deliveredComputerFrame = null; deliveredComputerWindow = null; deliveredComputerAt = null; pendingComputerDelivery = null; computerPreview.Source = null; computerSnapshots.Clear(); computerSizes.Clear();
        var prior = computerCancellation; computerCancellation = new(); prior.Dispose();
        if (computerApproval?.IsVisible == true) computerApproval.DialogResult = false;
        if (ComputerGlobalStop != null) ComputerGlobalStop.Visibility = Visibility.Collapsed;
        computerStatus.Text = "중지됨 · 기존 화면과 대기 요청은 만료됐습니다. 제어 허용 버튼으로 다시 시작하세요.";
    }
    private async void ComputerStop_Click(object sender, RoutedEventArgs e)
    {
        var task = computerRequestTask ?? CurrentTask; StopComputer();
        if (task != null && DeskController.IsRunning(task)) await Guard(() => desk.StopAsync(task));
    }
    private async Task<ComputerToolResult> ComputerActionAsync(WorkItem task, JsonElement arguments)
    {
        var action = ComputerTools.Parse(arguments);
        if (!await computerGate.WaitAsync(0)) throw new Hermes.Core.HermesException("다른 컴퓨터 요청이 진행 중입니다. 순차 요청하세요.");
        var epoch = computerEpoch; var turn = task.TurnId; var token = computerCancellation.Token; computerRequestTask = task;
        void Check()
        {
            if (closing || !computerArmed || epoch != computerEpoch || token.IsCancellationRequested || task != CurrentTask || task.ProjectId != CurrentProject?.Id || task.ProjectId != computerProjectId || task.ThreadComputerEnabled != true || task.Status != "실행 중" || task.TurnId != turn) throw new Hermes.Core.HermesException("컴퓨터 제어가 중단되었거나 선택 작업이 바뀌었습니다.");
        }
        try
        {
            Check();
            if (action.Action == "windows") return new(JsonSerializer.Serialize(new { windows = computerWindows.Select(w => new { windowId = w.Id, title = Safety.Redact(w.Window.Title) }), note = "사용자가 이번 실행에서 허용한 창만 표시합니다." }));
            var target = computerWindows.FirstOrDefault(w => w.Id == action.WindowId) ?? throw new Hermes.Core.HermesException("사용자가 허용하지 않은 창입니다. 컴퓨터 패널에서 대상 창을 추가하세요.");
            if (action.Action == "type" && Safety.Redact(action.Text) != action.Text) throw new Hermes.Core.HermesException("비밀 값으로 인식된 문자열은 컴퓨터 도구로 입력하지 않습니다. 직접 입력하세요.");
            var expected = computerSizes.GetValueOrDefault(target.Id);
            computerStatus.Text = "요청 검토 대기 · " + target.Window.Title + " · " + action.Action;
            var sensitive = SensitiveComputerAction(target, action);
            var auto = ComputerExecutionPolicy.CanAutoExecute(action, computerArmed, computerWindows.Contains(target), computerAutomatic, sensitive) || (computerAutomatic && computerArmed && action.Action is "inspect" or "wait_for_change");
            task.Transcript.Add(new("컴퓨터 요청", $"action={action.Action}; automatic={auto}; scopeCount={computerWindows.Count}; inputTextOmitted=true", DateTime.UtcNow));
            if (!auto && !ReviewComputerRequest(target, action)) throw new Hermes.Core.HermesException("사용자가 컴퓨터 요청을 거절했습니다.");
            Check(); if (action.IsInput) computerSnapshots.Consume(target.Id, action.SnapshotId, DateTimeOffset.UtcNow);
            computerStatus.Text = "실행 중 · " + target.Window.Title + " · " + action.Action + " · 중단 Ctrl+Alt+F12";
            ComputerFrame frame; ComputerObservationStats? observation = null;
            if (action.Action == "wait_for_change")
            {
                if (computerLifecycle.Count > 0) { var reasons = computerLifecycle.ToArray(); computerLifecycle.Clear(); return new(JsonSerializer.Serialize(new { reason = "lifecycle", events = reasons, imageSent = false })); }
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(token); computerWaitWake = wake;
                ComputerWaitReply reply;
                try { reply = await computerWaiter.Execute(new(target.Window, deliveredComputerWindow == target.Id ? deliveredComputerFrame?.ImageUrl : null, deliveredComputerWindow == target.Id ? deliveredComputerFrame?.Cursor : null, deliveredComputerAt, action.WaitMilliseconds, new()), wake.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && computerLifecycle.Count > 0)
                { var reasons = computerLifecycle.ToArray(); computerLifecycle.Clear(); return new(JsonSerializer.Serialize(new { reason = "lifecycle", events = reasons, captures = computerWaiter.Latest.Captures, imageSent = false })); }
                finally { computerWaitWake = null; }
                Check();
                observation = new(reply.Metrics.Captures, reply.Metrics.Changes, reply.Metrics.Coalesced, reply.Metrics.Notifications);
                if (!reply.Success) throw new Hermes.Core.HermesException(reply.Error);
                if (reply.Frame == null) return new(JsonSerializer.Serialize(new { reason = reply.Reason, metrics = reply.Metrics, imageSent = false, actualModelRequests = (int?)null }), null, observation);
                task.Transcript.Add(new("컴퓨터 변화 계측", JsonSerializer.Serialize(reply.Metrics), DateTime.UtcNow));
                frame = reply.Frame;
            }
            else frame = await computerWorker.Execute(new(target.Window, action, expected.Width, expected.Height, expected.ImageWidth, expected.ImageHeight), token);
            Check(); computerSizes[target.Id] = (frame.ClientWidth, frame.ClientHeight, frame.ImageWidth, frame.ImageHeight); var id = computerSnapshots.Record(target.Id, DateTimeOffset.UtcNow);
            using var image = new MemoryStream(Convert.FromBase64String(frame.ImageUrl.Split(',')[1])); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = image; bitmap.EndInit(); bitmap.Freeze(); computerPreview.Source = bitmap;
            computerStatus.Text = "완료 · " + target.Window.Title + " · " + action.Action;
            var text = JsonSerializer.Serialize(new { windowId = target.Id, snapshotId = id, title = Safety.Redact(target.Window.Title), width = frame.ImageWidth, height = frame.ImageHeight, coordinates = "returned image pixels; client area only", elements = frame.Elements, note = frame.Note, foreground = frame.IsForeground, dpi = frame.Dpi });
            task.Transcript.Add(new("컴퓨터 실행 방식", $"action={action.Action}; automatic={auto}; armedScopeCount={computerWindows.Count}", DateTime.UtcNow));
            var result = new ComputerToolResult(text, frame.ImageUrl, observation);
            pendingComputerDelivery = (task, result, frame, target.Id);
            return result;
        }
        catch (Exception ex) { if (action.Action == "wait_for_change") task.Transcript.Add(new("컴퓨터 변화 오류 계측", JsonSerializer.Serialize(computerWaiter.Latest), DateTime.UtcNow)); if (ex.Message.StartsWith("CaptureProtected", StringComparison.Ordinal) || ex.Message.StartsWith("CaptureBlockedOrBlank", StringComparison.Ordinal)) StopComputer(); computerSnapshots.Clear(); computerSizes.Clear(); computerStatus.Text = "실패 · " + Safety.Redact(ex.Message); throw; }
        finally { computerApproval = null; computerRequestTask = null; computerGate.Release(); }
    }
    private bool SensitiveComputerAction(AllowedComputerWindow allowed, ComputerAction action)
    {
        if (action.Action == "type") return Safety.Redact(action.Text) != action.Text;
        if (action.Action != "click") return false;
        if (deliveredComputerFrame == null || deliveredComputerWindow != allowed.Id) return true;
        var target = deliveredComputerFrame.Elements.Where(e => action.X >= e.X && action.Y >= e.Y && action.X <= e.X + e.Width && action.Y <= e.Y + e.Height).OrderBy(e => e.Width * e.Height).FirstOrDefault();
        if (target == null || target.Role is not ("ControlType.Button" or "ControlType.Hyperlink" or "ControlType.Edit" or "ControlType.ScrollBar")) return true; // Unknown target requires review, never assume harmless.
        var terms = new[] { "password", "login", "sign in", "delete", "remove", "pay", "purchase", "send", "submit", "security", "credential", "로그인", "인증", "비밀번호", "삭제", "결제", "구매", "전송", "보내", "보안" };
        return target.Password || terms.Any(term => target.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
    private bool ReviewComputerRequest(AllowedComputerWindow target, ComputerAction action)
    {
        var destination = desk.State.Provider.Mode == ConnectionMode.CompanyApi ? desk.State.Provider.Endpoint : "선택한 ChatGPT CLI 연결";
        var details = $"대상: {target.Window.Title}\n동작: {action.Action}\n연결: {destination}\n\n";
        if (action.Action is "click" or "scroll" or "drag") details += $"이미지 좌표: ({action.X}, {action.Y})\n";
        if (action.Action == "drag") details += $"끝 좌표: ({action.ToX}, {action.ToY})\n";
        if (action.Action == "click") details += $"버튼: {action.Button}, 횟수: {action.Clicks}\n";
        if (action.Action == "scroll") details += $"스크롤: {action.Steps} 단계, {(action.Horizontal ? "수평" : "수직")}\n";
        if (action.Action == "key") details += "키: " + action.Key + "\n";
        if (action.Action == "type") details += "입력할 내용:\n" + action.Text + "\n";
        details += "\n완료 후 대상 창의 화면과 UI 텍스트를 연결한 모델로 보냅니다. 화면에 표시된 요청을 검토하세요. 중단: Ctrl+Alt+F12";
        var window = new Window { Owner = this, Title = "Windows 컴퓨터 제어 요청", Width = 620, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(18) }; var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var allow = new Button { Content = "이번 요청 실행" }; var decline = new Button { Content = "거절", IsCancel = true }; buttons.Children.Add(allow); buttons.Children.Add(decline);
        panel.Children.Add(new TextBox { Text = details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); window.Content = panel;
        allow.Click += (_, _) => window.DialogResult = true; decline.Click += (_, _) => window.DialogResult = false; computerApproval = window;
        return window.ShowDialog() == true;
    }
    private void ComputerPanel_Click(object sender, RoutedEventArgs e) => OpenPanel(9);
}
