using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;
namespace Hermes.Desktop;
public sealed class McpWindow : Window
{
    private readonly DeskController desk; private readonly ProjectEntry project; private readonly string cli;
    private ProjectMcpSession? session;
    private readonly ListBox tools = new(), requests = new();
    private readonly TextBox arguments = new() { Text = "{}", AcceptsReturn = true, MinHeight = 70 };
    private readonly TextBox detail = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 80 };
    private readonly TextBox output = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 130 };
    private readonly CheckBox writes = new() { Content = "이 점검 세션의 새 파일 쓰기 도구 허용 (각 호출에서 별도 승인)" };
    private readonly CheckBox useInTasks = new() { Content = "이 프로젝트의 새 에이전트 작업에 로컬 MCP 사용" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private bool closing;
    private Button connectControl = null!, callControl = null!, acceptControl = null!, declineControl = null!;
    public McpWindow(DeskController desk, ProjectEntry project, string cli)
    {
        this.desk = desk; this.project = project; this.cli = cli; Title = "Hermes 로컬 MCP · " + project.Name; Width = 860; Height = 840; WindowStartupLocation = WindowStartupLocation.CenterOwner; Style = Application.Current.TryFindResource(typeof(Window)) as Style;
        var panel = new StackPanel { Margin = new Thickness(18) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "프로젝트: " + project.Root + "\n앱에 포함된 자격증명 없는 파일 도구 서버만 연결합니다. 경로/비밀/링크 검증 적용. 외부 서버·OAuth·토큰·URL 확인 요청은 지원하지 않습니다.\nMCP 점검에는 모델/회사 API를 호출하지 않습니다. MCP 프로세스 자체는 shell sandbox가 아니며 이 서버의 제한된 구현으로 파일 범위를 지킵니다.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(writes); panel.Children.Add(useInTasks); useInTasks.IsChecked = project.LocalMcpEnabled;
        useInTasks.Click += (_, _) => { if (desk.State.Tasks.Any(DeskController.IsRunning)) { useInTasks.IsChecked = project.LocalMcpEnabled; status.Text = "작업 중에는 MCP 설정을 바꿀 수 없습니다."; return; } project.LocalMcpEnabled = useInTasks.IsChecked == true; desk.Save(); status.Text = "새 작업부터 적용. 시작한 thread의 MCP 설정은 고정됩니다."; };
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        Button Button(string text) { var button = new Button { Content = text }; buttons.Children.Add(button); return button; }
        var connect = Button("연결 / 새로 연결"); var refresh = Button("목록 새로고침"); var disconnect = Button("연결 종료");
        connectControl = connect;
        connect.Click += async (_, _) => await Guard(async () => { if (session != null) await session.DisposeAsync(); session = new(project.Root, Path.Combine(desk.DataDirectory, "mcp-home", project.Id), Environment.ProcessPath!, writes.IsChecked == true, a => Dispatcher.BeginInvoke(a)); session.Changed += Render; session.Log += s => Dispatcher.BeginInvoke(() => { output.AppendText(Safety.Redact(s) + "\n"); if (output.Text.Length > 150000) output.Text = output.Text[^100000..]; }); await session.ConnectAsync(cli); Render(); });
        refresh.Click += async (_, _) => await Guard(async () => { if (session != null) await session.RefreshAsync(); Render(); });
        disconnect.Click += async (_, _) => await Guard(async () => { if (session != null) await session.DisposeAsync(); Render(); });
        panel.Children.Add(status); tools.Height = 110; panel.Children.Add(tools); panel.Children.Add(detail);
        tools.SelectionChanged += (_, _) => { if (tools.SelectedItem is McpToolView tool) detail.Text = tool.Description + "\n\n" + tool.InputSchema.GetRawText(); };
        panel.Children.Add(new TextBlock { Text = "선택 도구의 인수 (JSON object)" }); panel.Children.Add(arguments); var call = new Button { Content = "선택한 로컬 도구 호출" }; panel.Children.Add(call);
        callControl = call;
        call.Click += async (_, _) => await Guard(async () => { if (session == null || tools.SelectedItem is not McpToolView tool) throw new Hermes.Core.HermesException("연결 후 도구를 선택하세요."); using var args = JsonDocument.Parse(arguments.Text); call.IsEnabled = false; try { var result = await session.CallAsync(tool.Name, args.RootElement.Clone()); output.Text = Safety.Redact(result.GetRawText()); } finally { call.IsEnabled = true; Render(); } });
        panel.Children.Add(new TextBlock { Text = "MCP 사용자 확인 · 이 요청 한 번에 승인 / 거절" }); requests.Height = 90; panel.Children.Add(requests);
        var approve = new Button { Content = "선택 요청 승인" }; var decline = new Button { Content = "선택 요청 거절" }; var confirmation = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 }; panel.Children.Add(confirmation);
        acceptControl = approve; declineControl = decline;
        requests.SelectionChanged += (_, _) => confirmation.Text = requests.SelectedItem is ServerRequest r ? Safety.Redact(r.Params.GetRawText()) : "";
        panel.Children.Add(new WrapPanel { Children = { approve, decline } }); panel.Children.Add(output);
        approve.Click += async (_, _) => await Guard(async () => { if (session != null && requests.SelectedItem is ServerRequest r) await session.AnswerAsync(r, true); Render(); });
        decline.Click += async (_, _) => await Guard(async () => { if (session != null && requests.SelectedItem is ServerRequest r) await session.AnswerAsync(r, false); Render(); });
        Closed += async (_, _) => { closing = true; if (session != null) await session.DisposeAsync(); };
    }
    private void Render()
    {
        if (closing) return; status.Text = session?.Status ?? "연결 전"; var selected = (tools.SelectedItem as McpToolView)?.Name;
        tools.ItemsSource = session?.Tools.ToList(); tools.SelectedItem = tools.Items.Cast<McpToolView>().FirstOrDefault(t => t.Name == selected) ?? tools.Items.Cast<McpToolView>().FirstOrDefault();
        requests.ItemsSource = session?.Approvals.ToList(); if (requests.Items.Count > 0) requests.SelectedIndex = 0;
    }
    private async Task Guard(Func<Task> action) { try { await action(); } catch (Exception e) { status.Text = Safety.Redact(e.Message); } }
    internal async Task<int> RunProgrammaticChecks()
    {
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Hermes.Core.HermesException("MCP UI: " + message + " / " + status.Text); count++; }
        async Task Wait(Func<bool> condition) { var until = DateTime.UtcNow.AddSeconds(20); while (!condition() && DateTime.UtcNow < until) await Task.Delay(25); Check(condition(), "control event completed"); }
        writes.IsChecked = true; connectControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(() => session?.Connected == true && tools.Items.Count == 5); Check(status.Text.Contains("connected"), "connected inventory visible");
        tools.SelectedItem = tools.Items.Cast<McpToolView>().Single(t => t.Name == "write_project_note"); arguments.Text = "{\"relativePath\":\"mcp-ui-denied.txt\",\"text\":\"FIXTURE_ONLY\"}"; callControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(() => requests.Items.Count == 1); Check(!File.Exists(Path.Combine(project.Root, "mcp-ui-denied.txt")), "waits for user confirmation");
        declineControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => callControl.IsEnabled && output.Text.Contains("DECLINED"));
        arguments.Text = "{\"relativePath\":\"mcp-ui-approved.txt\",\"text\":\"FIXTURE_ONLY\"}"; callControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(() => requests.Items.Count == 1); acceptControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => callControl.IsEnabled && File.Exists(Path.Combine(project.Root, "mcp-ui-approved.txt")));
        Check(File.ReadAllText(Path.Combine(project.Root, "mcp-ui-approved.txt")) == "FIXTURE_ONLY", "approve creates scoped fixture");
        Check(session?.RejectedModelRequests == 0, "inspection has no model request"); return count;
    }
}
