using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hermes.Core;
using Microsoft.Web.WebView2.Core;

namespace Hermes.Desktop;
public partial class MainWindow
{
    private async Task<int> RunShellChecks()
    {
        var checks = 0;
        void Check(bool value, string label) { if (!value) throw new IOException("Shell UI check failed: " + label); checks++; }
        var project = CurrentProject!; var first = CurrentTask!; var second = desk.NewTask(project, "두 번째 UI 채팅"); SelectTask(first);
        Prompt.Text = "초안 보존 fixture"; SelectTask(second); Prompt.Text = "다른 초안 fixture"; SelectTask(first);
        Check(Prompt.Text == "초안 보존 fixture", "draft restored when selecting chat");
        SelectTask(second); Check(Prompt.Text == "다른 초안 fixture", "independent draft per chat");
        first.Pinned = true; ChatMessages.Rename(desk.State, first, "고정된 UI 채팅"); desk.Save();
        Check(SidebarGroups.Children.OfType<TextBlock>().Any(t => t.Text == "고정됨"), "pinned section renders");
        SelectTask(first); Check(TaskTitle.Text == "고정된 UI 채팅", "renamed header renders");
        desk.State.Desktop.SidebarVisible = false; SetSidebar(false); Check(SidebarColumn.Width.Value == 0, "sidebar collapses");
        desk.State.Desktop.SidebarVisible = true; SetSidebar(true); Check(SidebarColumn.Width.Value == 358, "sidebar restores width");
        OpenPanel(1); Check(InspectorPane.Visibility == Visibility.Visible && InspectorTabs.SelectedIndex == 1, "review panel opens");
        desk.State.Desktop.InspectorVisible = false; SetInspector(false); Check(InspectorColumn.Width.Value == 0, "inspector collapses");
        Theme_Click(this, new()); Check(((SolidColorBrush)Application.Current.Resources["Canvas"]).Color == (Color)ColorConverter.ConvertFromString("#191919"), "dark theme applies");
        Theme_Click(this, new()); Check(((SolidColorBrush)Application.Current.Resources["Canvas"]).Color == Colors.White, "light theme restores");
        first.Transcript.Clear(); first.Transcript.Add(new("사용자", "현재 프로젝트의 화면과 동작을 검증해주세요.", DateTime.UtcNow));
        first.Transcript.Add(new("에이전트", "화면의 구성과 실제 동작을 확인했습니다.\n\n**프로젝트와 채팅**은 왼쪽에서 관리하고, 작업 지시는 아래 입력창에서 보낼 수 있습니다.\n\n```csharp\nvar project = desk.Register(root);\nvar chat = desk.NewTask(project, \"새 채팅\");\n```\n\n파일과 변경 사항은 오른쪽 패널에서 검토할 수 있습니다.", DateTime.UtcNow));
        first.Transcript.Add(new("명령 결과", "UI fixture: 격리된 테스트 프로젝트를 검사했습니다.", DateTime.UtcNow));
        first.Transcript.Add(new("에이전트", "이 화면은 UI 검증용 대화입니다. 실제 모델을 호출하지 않았습니다.\n\n일반 실행에서는 연결한 모델의 답변과 작업 결과가 이 위치에 표시됩니다.", DateTime.UtcNow));
        first.Title = "Windows 화면과 동작 검증"; Prompt.Clear(); desk.Save();
        Check(MessageStack.Children.Count == 4 && Welcome.Visibility == Visibility.Collapsed, "user, agent, code and activity render");
        var existingMessage = MessageStack.Children[0]; var existingSidebar = SidebarGroups.Children[0];
        first.Transcript[^1] = first.Transcript[^1] with { Text = first.Transcript[^1].Text + "\nUI 업데이트 검증." }; desk.Save();
        Check(ReferenceEquals(existingMessage, MessageStack.Children[0]), "unchanged message views reused on streaming update");
        Check(ReferenceEquals(existingSidebar, SidebarGroups.Children[0]), "sidebar reused when only transcript text changes");
        OpenPanel(8); await EnsureBrowser();
        await using var mock = new LoopbackHttp((_, stream, ct) => LoopbackHttp.Reply(stream, 200, "text/html", "<html><head><title>Hermes Browser Fixture</title></head><body><h1>BROWSER_UI_OK</h1><button id='fixture-button' onclick=\"this.innerText='CLICK_OK'\">Click fixture</button><input id='fixture-input'></body></html>", ct));
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Complete(object? sender, CoreWebView2NavigationCompletedEventArgs e) => ready.TrySetResult(e.IsSuccess);
        browserView!.CoreWebView2.NavigationCompleted += Complete;
        try { await NavigateBrowser(mock.BaseUri.AbsoluteUri); Check(await ready.Task.WaitAsync(TimeSpan.FromSeconds(20)), "real WebView2 navigates to owned fixture"); }
        finally { browserView.CoreWebView2.NavigationCompleted -= Complete; }
        var text = await browserView.ExecuteScriptAsync("document.body.innerText"); Check(text.Contains("BROWSER_UI_OK"), "real browser DOM read");
        var clicked = await browserView.ExecuteScriptAsync("document.querySelector('#fixture-button').click();document.querySelector('#fixture-button').innerText"); Check(clicked.Contains("CLICK_OK"), "real browser fixture click");
        browserEnable.IsChecked = true; project.BrowserEnabled = true; desk.Save(); Check(browserEnable.IsChecked == true, "project browser setting reflects host state");
        project.BrowserEnabled = false;
        desk.State.Desktop.InspectorVisible = false; SetInspector(false); desk.Save();
        return checks;
    }
}
