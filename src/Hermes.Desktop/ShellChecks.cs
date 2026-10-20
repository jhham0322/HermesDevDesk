using System.IO;
using System.Text.Json;
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
        Check(openChats.Contains(first.Id) && openChats.Contains(second.Id), "multiple chat tabs stay open");
        var png = Path.Combine(Path.GetDirectoryName(project.Root)!, "external-shell.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        AddImage(png); Check(pendingImages.Count == 1, "external image attaches through real WPF decoder");
        SelectTask(first); Check(pendingImages.Count == 0, "first chat has independent attachments");
        SelectTask(second); Check(pendingImages.Count == 1, "second chat attachment restored");
        first.Status = "실행 중"; desk.Save(); Check(CanSubmit(), "another running chat does not disable send");
        first.Status = "대기"; ImageClear_Click(this, new());
        SelectTask(second); first.Status = "실행 중"; desk.Save();
        var tab = ChatTabs.Children.OfType<StackPanel>().First(row => row.Children.OfType<Button>().First().ToolTip?.ToString() == second.Title + " · " + second.Status);
        tab.Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!openChats.Contains(second.Id) && first.Status == "실행 중", "closing selected tab keeps it closed while other chat continues");
        SelectTask(second); Check(openChats.Contains(second.Id), "sidebar can reopen a closed chat tab"); first.Status = "대기";
        first.Pinned = true; ChatMessages.Rename(desk.State, first, "고정된 UI 채팅"); desk.Save();
        Check(SidebarGroups.Children.OfType<TextBlock>().Any(t => t.Text == "고정됨"), "pinned section renders");
        SelectTask(first); Check(TaskTitle.Text == "고정된 UI 채팅", "renamed header renders");
        desk.State.Desktop.SidebarVisible = false; SetSidebar(false); Check(SidebarColumn.Width.Value == 0, "sidebar collapses");
        desk.State.Desktop.SidebarVisible = true; SetSidebar(true); Check(SidebarColumn.Width.Value == 358, "sidebar restores width");
        var originalInspectorWidth = desk.State.Desktop.InspectorWidth;
        OpenPanel(1); InspectorColumn.Width = new GridLength(820); UpdateLayout();
        InspectorSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
        Check(Math.Abs(desk.State.Desktop.InspectorWidth - 820) < 2 && Math.Abs(desk.Store.Load().Desktop.InspectorWidth - 820) < 2, "splitter completion persists user-selected panel width above previous 700px cap");
        var previousWindowWidth = Width; Width = 1160; await Task.Delay(30); Width = previousWindowWidth; UpdateLayout();
        desk.Save(); OpenPanel(8); OpenPanel(1);
        Check(Math.Abs(InspectorColumn.ActualWidth - 820) < 2, "panel width survives window resize refresh and repeated tool panel opening");
        SetInspector(false); SetInspector(true); UpdateLayout();
        Check(Math.Abs(InspectorColumn.ActualWidth - 820) < 2, "panel toggle restores user-selected width");
        SetInspector(false); desk.State.Desktop.InspectorWidth = originalInspectorWidth; SetInspector(true);
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
        ProgressMessages.Delta(first, "ui-summary", "0", "파일과 실행 결과를 확인 중입니다.");
        first.Transcript.Add(new("작업 진행", "명령 실행 시작", DateTime.UtcNow)); desk.Save();
        Check(MessageStack.Children.OfType<Expander>().Count(e => e.IsExpanded) >= 2, "progress summaries and tool activity expanded by default");
        ProgressMessages.Complete(first, "ui-summary", "0", "파일 확인 완료"); desk.Save();
        Check(first.Transcript.Count(e => e.Kind == "진행 요약") == 1, "stream summary completion has one visible entry");
        var existingMessage = MessageStack.Children[0]; var existingSidebar = SidebarGroups.Children[0];
        first.Transcript[^1] = first.Transcript[^1] with { Text = first.Transcript[^1].Text + "\nUI 업데이트 검증." }; desk.Save();
        Check(ReferenceEquals(existingMessage, MessageStack.Children[0]), "unchanged message views reused on streaming update");
        Check(ReferenceEquals(existingSidebar, SidebarGroups.Children[0]), "sidebar reused when only transcript text changes");
        OpenPanel(8); await EnsureBrowser();
        await using var mock = new LoopbackHttp((_, stream, ct) => LoopbackHttp.Reply(stream, 200, "text/html", "<html><head><title>Hermes Browser Fixture</title></head><body><h1>BROWSER_UI_OK</h1><button id='fixture-button' onclick=\"this.innerText='CLICK_OK'\">Click fixture</button><input id='fixture-input'></body></html>", ct));
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Complete(object? sender, CoreWebView2NavigationCompletedEventArgs e) => ready.TrySetResult(e.IsSuccess);
        var manualView = await EnsureChatBrowser(first);
        manualView.CoreWebView2.NavigationCompleted += Complete;
        try { await NavigateBrowser(mock.BaseUri.AbsoluteUri); Check(await ready.Task.WaitAsync(TimeSpan.FromSeconds(20)), "real WebView2 navigates to owned fixture"); }
        finally { manualView.CoreWebView2.NavigationCompleted -= Complete; }
        var text = await manualView.ExecuteScriptAsync("document.body.innerText"); Check(text.Contains("BROWSER_UI_OK"), "real browser DOM read");
        var clicked = await manualView.ExecuteScriptAsync("document.querySelector('#fixture-button').click();document.querySelector('#fixture-button').innerText"); Check(clicked.Contains("CLICK_OK"), "real browser fixture click");
        browserEnable.IsChecked = true; project.BrowserEnabled = true; desk.Save(); Check(browserEnable.IsChecked == true, "project browser setting reflects host state");
        await using var pages = new LoopbackHttp((request, stream, ct) => LoopbackHttp.Reply(stream, 200, "text/html", "<html><body><h1>" + request.Path + "</h1><input id='password' type='password'><input id='upload' type='file'></body></html>", ct));
        first.ThreadBrowserEnabled = true; second.ThreadBrowserEnabled = true; first.Status = second.Status = "실행 중";
        var manualRead = await BrowserAction(first, JsonSerializer.SerializeToElement(new { action = "read" }));
        Check(manualRead.Text.Contains("BROWSER_UI_OK") && manualRead.ImageUrl?.StartsWith("data:image/png;base64,") == true, "AI read shares manually opened page and returns a real screenshot");
        static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);
        await BrowserAction(first, Args(new { action = "navigate", url = pages.BaseUri.AbsoluteUri + "first" }));
        await BrowserAction(second, Args(new { action = "navigate", url = pages.BaseUri.AbsoluteUri + "second" }));
        var firstText = await BrowserAction(first, Args(new { action = "read" }));
        var secondText = await BrowserAction(second, Args(new { action = "read" }));
        Check(firstText.ImageUrl != null && secondText.ImageUrl != null, "foreground and background browser reads include screenshots");
        Check(firstText.Text.Contains("/first") && !firstText.Text.Contains("/second") && secondText.Text.Contains("/second"), "background chats use isolated browser pages without approval dialogs");
        await BrowserAction(first, Args(new { action = "type", selector = "#password", text = "OWNED_FIXTURE" }));
        var typed = await BrowserAction(first, Args(new { action = "evaluate", script = "document.querySelector('#password').value" }));
        Check(typed.Text == "OWNED_FIXTURE", "authorized browser supports password input and page JavaScript");
        var promised = await BrowserAction(first, Args(new { action = "evaluate", script = "Promise.resolve('ASYNC_PAGE_OK')" }));
        Check(promised.Text == "ASYNC_PAGE_OK", "async page script is awaited to a completed result");
        try { await BrowserAction(first, Args(new { action = "evaluate", script = "(()=>{throw Error('OWNED_BROWSER_ERROR')})()" })); Check(false, "script exception was swallowed"); }
        catch (IOException ex) { Check(ex.Message.Contains("OWNED_BROWSER_ERROR"), "page script exception returns an explicit tool error"); }
        File.WriteAllText(Path.Combine(project.Root, "upload-fixture.txt"), "LOCAL_FIXTURE");
        await BrowserAction(second, Args(new { action = "upload", selector = "#upload", path = "upload-fixture.txt" }));
        var uploaded = await BrowserAction(second, Args(new { action = "evaluate", script = "document.querySelector('#upload').files[0].name" }));
        Check(uploaded.Text == "upload-fixture.txt", "browser uploads selected project file without per-action approval");
        await BrowserAction(first, Args(new { action = "navigate", url = pages.BaseUri.AbsoluteUri + "tall" }));
        await BrowserAction(first, Args(new { action = "evaluate", script = "document.body.style.margin='0';document.body.innerHTML='<div style=\"height:9000px;background:#ff0000\">PAGE_TOP</div><div style=\"height:500px;background:#0000ff\">PAGE_BOTTOM</div>'; 'TALL_READY'" }));
        var fullPage = await BrowserAction(first, Args(new { action = "screenshot" }));
        using (var fullMetadata = JsonDocument.Parse(fullPage.Text))
        {
            var capturePath = fullMetadata.RootElement.Text("originalPath");
            Check(fullMetadata.RootElement.GetProperty("originalHeight").GetInt32() >= 9500 && File.Exists(capturePath), "full page capture saves original PNG including content below viewport");
            Check(fullPage.Images is { Count: >= 2 } && fullPage.ContentItems.Length == fullPage.Images.Count + 1, "full page browser tool delivers ordered image parts to AI");
            using var captureStream = File.OpenRead(capturePath);
            var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(captureStream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
            var sample = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixel = new byte[4]; sample.CopyPixels(new Int32Rect(20, frame.PixelHeight - 20, 1, 1), pixel, 4, 0);
            Check(pixel[0] > 200 && pixel[2] < 50, "full page PNG contains rendered bottom section pixels");
        }
        SelectTask(first); ImageClear_Click(this, new());
        await CaptureBrowserToComposerAsync();
        Check(pendingImages.Count >= 2 && pendingImages.All(x => x.Height <= 8192) && pendingImages.Sum(x => x.Height) >= 9500, "manual full-page capture attaches complete long page as AI-readable images");
        ImageClear_Click(this, new());
        first.Status = second.Status = "완료";
        project.BrowserEnabled = false;
        desk.State.Desktop.InspectorVisible = false; SetInspector(false); desk.Save();
        var removed = desk.NewTask(project, "UI 삭제 검증"); SelectTask(removed); Prompt.Text = "삭제할 초안";
        DeleteChat(removed, false);
        Check(!desk.State.Tasks.Contains(removed) && !openChats.Contains(removed.Id) && !desk.State.Desktop.Drafts.ContainsKey(removed.Id), "chat delete removes list tab and composer draft");
        var commitWindow = new GitCommitWindow(project.Root) { Owner = this }; commitWindow.Show();
        checks += await commitWindow.RunProgrammaticChecks(); commitWindow.Close();
        first.ThreadWritable = false; first.WritablePreference = null; SelectTask(first);
        Check(Writable.IsChecked == true, "previous read-only chat defaults to current project full permission");
        Writable.IsChecked = false; Writable_Click(this, new()); desk.Save();
        Check(Writable.IsChecked == false, "explicit read-only preference survives refresh");
        Writable.IsChecked = true; Writable_Click(this, new());
        Check(Icon != null, "window has custom application icon");
        ImageClear_Click(this, new());
        var badImage = Path.Combine(project.Root, "broken-image.png"); File.WriteAllBytes(badImage, new byte[20]);
        await Guard(() => { AddImage(badImage); return Task.CompletedTask; }, "image-import-test", false);
        Check(pendingImages.Count == 0 && Directory.GetFiles(AppLog.DirectoryPath, "errors-*.log").Any(), "invalid image is caught and logged without changing attachments");
        await Guard(() => throw new System.Runtime.InteropServices.ExternalException("clipboard busy fixture"), "image-paste-test", false);
        Check(File.ReadAllText(Directory.GetFiles(AppLog.DirectoryPath, "errors-*.log").Single()).Contains("clipboard busy fixture"), "clipboard exception persists in diagnostic log");
        var tooWide = System.Windows.Media.Imaging.BitmapSource.Create(8193, 1, 96, 96, PixelFormats.Bgra32, null, new byte[8193 * 4], 8193 * 4);
        await Guard(() => { AddClipboardImage(tooWide); return Task.CompletedTask; }, "image-size-test", false);
        Check(pendingImages.Count == 0, "oversized clipboard image rejected before encoding");
        var tiny = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4);
        AddClipboardImage(tiny); Check(pendingImages.Count == 1, "valid clipboard bitmap attaches after previous errors"); ImageClear_Click(this, new());
        var app = (App)Application.Current; var dispatcherFault = new IOException("dispatcher recovery fixture"); app.ExpectedUiTestException = dispatcherFault;
        _ = Dispatcher.BeginInvoke(new Action(() => throw dispatcherFault));
        var recoveryUntil = DateTime.UtcNow.AddSeconds(5); while (app.ExpectedUiTestException != null && DateTime.UtcNow < recoveryUntil) await Task.Delay(20);
        Check(app.ExpectedUiTestException == null && !Dispatcher.HasShutdownStarted, "unhandled dispatcher exception is logged and app remains open");
        checks += await RunAttachmentChecks();
        return checks;
    }
}
