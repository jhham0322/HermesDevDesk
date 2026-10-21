using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hermes.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hermes.Desktop;

public partial class MainWindow
{
    private WebView2? browserView;
    private readonly TextBox browserAddress = new() { Text = "https://developers.openai.com", MinWidth = 120 };
    private readonly TextBlock browserStatus = new() { Text = "앱 전용 브라우저 · 기존 브라우저 계정을 가져오지 않습니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6), FontSize = 12 };
    private readonly CheckBox browserEnable = new() { Content = "새 채팅에서 브라우저 도구 사용" };
    private readonly ContentControl browserHost = new();
    private readonly Grid browserSurface = new();
    private WebView2? activeBrowserView;
    private void SetBrowserView(WebView2 view)
    {
        browserHost.Content = browserSurface;
        if (!browserSurface.Children.Contains(view)) browserSurface.Children.Add(view);
        if (browserTabOwners.TryGetValue(view, out var taskOwner) && taskOwner != CurrentTask?.Id) { view.Visibility = Visibility.Hidden; RefreshBrowserTabs(); return; }
        foreach (var child in browserSurface.Children.OfType<WebView2>()) { child.Visibility = child == view ? Visibility.Visible : Visibility.Hidden; Panel.SetZIndex(child, child == view ? 1 : 0); }
        activeBrowserView = view;
        browserAddress.Text = view.Source?.AbsoluteUri ?? "";
        if (browserTabOwners.TryGetValue(view, out var owner)) { if (owner == null) browserView = view; else chatBrowsers[owner] = view; }
        RefreshBrowserTabs();
        UpdateBrowserNavigation(view);
        browserAttachSource.IsEnabled = selectedBrowserSourceTask == CurrentTask?.Id && selectedBrowserSource.Length > 0;
        if (view.CoreWebView2 != null) { browserAddress.Text = view.Source?.AbsoluteUri ?? browserAddress.Text; UpdateBrowserBookmark(view); }
    }
    private Task? browserInitialization;
    private readonly Dictionary<string, WebView2> chatBrowsers = [];
    private readonly Dictionary<string, SemaphoreSlim> browserLocks = [];
    private void ConfigureBrowser(WebView2 view, Func<string?> projectRoot)
    {
        view.CoreWebView2.Settings.AreHostObjectsAllowed = false;
        view.CoreWebView2.Settings.IsWebMessageEnabled = true;
        view.CoreWebView2.Settings.AreDevToolsEnabled = true;
        ConfigureLocalBrowser(view);
        browserTabReady[view].TrySetResult();
        view.CoreWebView2.HistoryChanged += (_, _) => UpdateBrowserNavigation(view);
        view.CoreWebView2.Profile.IsPasswordAutosaveEnabled = desk.State.Browser.SavePasswords;
        view.CoreWebView2.Profile.IsGeneralAutofillEnabled = true;
        view.CoreWebView2.WebMessageReceived += (_, args) => { if (!closing) ReceiveBrowserSource(view, args); };
        view.CoreWebView2.SourceChanged += (_, _) => RememberBrowserPage(view);
        view.CoreWebView2.DocumentTitleChanged += (_, _) => { RememberBrowserPage(view); RefreshBrowserTabs(); };
        view.CoreWebView2.PermissionRequested += (_, args) => { args.State = CoreWebView2PermissionState.Allow; args.Handled = true; };
        view.CoreWebView2.NewWindowRequested += async (_, args) => {
            args.Handled = true; using var deferred = args.GetDeferral();
            try {
                // Keep popup authentication context and ownership in the originating chat.
                if (browserTabOwners.GetValueOrDefault(view) != CurrentTask?.Id) { browserStatus.Text = "새 창을 열려면 해당 채팅의 브라우저를 선택하세요."; return; }
                var popup = await AddBrowserTab(); args.NewWindow = popup.CoreWebView2;
            } catch (Exception ex) { browserStatus.Text = Safety.Redact(ex.Message); }
        };
        view.CoreWebView2.WindowCloseRequested += (_, _) => Dispatcher.BeginInvoke(() => { if (browserTabOwners.ContainsKey(view)) TryCloseBrowserTab(view); });
        view.CoreWebView2.DownloadStarting += (_, args) =>
        {
            try
            {
                var root = projectRoot() ?? throw new IOException("다운로드 프로젝트를 선택하세요.");
                var name = Path.GetFileName(args.ResultFilePath);
                var target = Safety.Resolve(root, Path.Combine("Downloads", Guid.NewGuid().ToString("N") + "-" + name));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); args.ResultFilePath = target; args.Handled = true;
            }
            catch (IOException) { args.Cancel = true; }
        };
        view.CoreWebView2.NavigationStarting += (_, args) => { try { BrowserTools.ValidateUrl(args.Uri); } catch (IOException) { args.Cancel = true; } };
        view.CoreWebView2.NavigationCompleted += (_, args) => { if (activeBrowserView == view) { browserAddress.Text = view.Source?.AbsoluteUri ?? ""; browserStatus.Text = args.IsSuccess ? view.CoreWebView2.DocumentTitle : "페이지 로드 실패: " + args.WebErrorStatus; } if (args.IsSuccess) RememberBrowserPage(view); };
    }
    private void ShowChatBrowser()
    {
        if (CurrentTask is { } task && chatBrowsers.TryGetValue(task.Id, out var view)) SetBrowserView(view);
        else if (browserView != null && browserTabOwners.GetValueOrDefault(browserView) == CurrentTask?.Id) SetBrowserView(browserView);
        else { activeBrowserView = null; foreach (var child in browserSurface.Children.OfType<WebView2>()) child.Visibility = Visibility.Hidden; browserAddress.Clear(); browserBack.IsEnabled = browserForward.IsEnabled = false; RefreshBrowserTabs(); }
    }
    private void DisposeBrowsers() { browserSaveTimer.Stop(); desk.Save(); foreach (var view in browserTabOwners.Keys.ToArray()) RemoveBrowserTab(view); chatBrowsers.Clear(); sourcePickers.Clear(); browserNavigationTargets.Clear(); localHttpAttempts.Clear(); localBrowserRecoveries.Clear(); }



    private void InitializeBrowserPanel()
    {
        var panel = new DockPanel(); var toolbar = new StackPanel(); DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar);
        InitializeBrowserTabs(toolbar);
        var navigation = new WrapPanel(); foreach (var button in new[] { browserBack, browserForward, browserReload, browserDevTools }) navigation.Children.Add(button); toolbar.Children.Add(navigation);
        var address = new DockPanel(); var go = new Button { Content = "열기" }; DockPanel.SetDock(go, Dock.Right); address.Children.Add(go); address.Children.Add(browserAddress); toolbar.Children.Add(address);
        var capture = new Button { Content = "전체 캡처 · AI 첨부", ToolTip = "현재 로드된 페이지 전체를 PNG로 저장하고 AI 입력에 이미지로 첨부합니다." };
        capture.Click += async (_, _) => await Guard(CaptureBrowserToComposerAsync, "browser-full-capture");
        var extras = new StackPanel(); extras.Children.Add(capture); InitializeBrowserExperience(extras); InitializeLocalBrowser(extras); extras.Children.Add(browserEnable);
        toolbar.Children.Add(new Expander { Header = "브라우저 추가 기능 · 북마크 / 캡처 / 도구 설정", Content = new ScrollViewer { Content = extras, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, IsExpanded = false });
        toolbar.Children.Add(browserStatus); panel.Children.Add(browserHost);
        InspectorTabs.Items.Add(new TabItem { Header = "브라우저", Content = panel });
        go.Click += async (_, _) => await Guard(() => NavigateBrowser(browserAddress.Text));
        browserAddress.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Guard(() => NavigateBrowser(browserAddress.Text)); } };
        browserEnable.Click += (_, _) =>
        {
            var project = CurrentProject;
            if (project == null) { browserEnable.IsChecked = false; browserStatus.Text = "프로젝트를 선택하세요."; return; }
            if (desk.State.Tasks.Any(t => t.ProjectId == project.Id && DeskController.IsRunning(t))) { browserEnable.IsChecked = project.BrowserEnabled; browserStatus.Text = "실행 중에는 도구 설정을 바꿀 수 없습니다."; return; }
            project.BrowserEnabled = browserEnable.IsChecked == true; desk.Save(); browserStatus.Text = "새 채팅부터 적용됩니다. 모델 요청은 별도 승인 없이 실행합니다.";
        };
        desk.BrowserTool = BrowserAction;
    }
    private async Task EnsureBrowser()
    {
        browserView ??= browserTabOwners.FirstOrDefault(p => p.Value == null).Key;
        try { if (browserView != null && browserTabOwners.GetValueOrDefault(browserView) == null) { await browserTabReady[browserView].Task; return; } await (browserInitialization ??= InitializeBrowser()); }
        catch { browserInitialization = null; throw; }
    }
    private async Task InitializeBrowser()
    {
        try
        {
            browserView = CreateBrowserTab(null); SetBrowserView(browserView);
            var environment = await BrowserEnvironment();
            await browserView.EnsureCoreWebView2Async(environment);
            ConfigureBrowser(browserView, () => CurrentProject?.Root);
            browserStatus.Text = "브라우저 준비 완료";
        }
        catch (Exception ex) { if (browserView != null) RemoveBrowserTab(browserView); browserInitialization = null; browserStatus.Text = "WebView2 초기화 실패. Microsoft Edge WebView2 Runtime이 필요합니다. " + Safety.Redact(ex.Message); throw new IOException(browserStatus.Text, ex); }
    }
    private async Task<WebView2> EnsureChatBrowser(WorkItem task, CancellationToken ct = default)
    {
        if (chatBrowsers.TryGetValue(task.Id, out var existing)) { await browserTabReady[existing].Task.WaitAsync(ct); return existing; }
        if (browserView?.CoreWebView2 != null && !chatBrowsers.Values.Contains(browserView))
        { var shared = browserView; browserView = null; browserInitialization = null; browserTabOwners[shared] = task.Id; chatBrowsers[task.Id] = shared; RefreshBrowserTabs(); return shared; }
        var view = CreateBrowserTab(task.Id); SetBrowserView(view);
        try
        {
            var environment = await BrowserEnvironment().WaitAsync(ct);
            await view.EnsureCoreWebView2Async(environment).WaitAsync(ct);
            ConfigureBrowser(view, () => desk.State.Projects.First(p => p.Id == task.ProjectId).Root);
            chatBrowsers[task.Id] = view; return view;
        }
        catch { RemoveBrowserTab(view); throw; }
        finally { ShowChatBrowser(); }
    }
    private async Task NavigateBrowser(string address)
    {
        var uri = BrowserTools.ValidateUrl(address);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        WebView2 view;
        if (CurrentTask is { } task) view = await EnsureChatBrowser(task, timeout.Token);
        else { await EnsureBrowser().WaitAsync(timeout.Token); view = browserView!; }
        SetBrowserView(view); ResetLocalBrowserAttempts(view); view.CoreWebView2.Navigate(uri.AbsoluteUri);
    }
    private async Task<string> CaptureBrowserScreen(WebView2 view, CancellationToken ct)
    {
        var captured = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\",\"captureBeyondViewport\":false}").WaitAsync(ct);
        using var capture = JsonDocument.Parse(captured);
        using var stream = new MemoryStream(Convert.FromBase64String(capture.RootElement.GetProperty("data").GetString()!));
        var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        var scale = Math.Min(1.0, Math.Min(1600.0 / frame.PixelWidth, 1000.0 / frame.PixelHeight));
        System.Windows.Media.Imaging.BitmapSource image = scale < 1 ? new System.Windows.Media.Imaging.TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale)) : frame;
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var output = new MemoryStream(); encoder.Save(output);
        return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
    }
    private async Task<BrowserToolResult> BrowserAction(WorkItem task, JsonElement args)
    {
        if (closing || task.ThreadBrowserEnabled != true || task.Status != "실행 중") throw new IOException("실행 중인 브라우저 채팅이 아닙니다.");
        if (!browserLocks.TryGetValue(task.Id, out var gate)) browserLocks[task.Id] = gate = new(1, 1);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var ct = timeout.Token;
        await gate.WaitAsync(ct);
        WebView2? operationView = null;
        try
        {
            if (closing || task.Status != "실행 중") throw new IOException("작업이 중단되었습니다.");
            var view = await EnsureChatBrowser(task, ct);
            operationView = view;
            OpenPanel(8);
            if (CurrentTask == task) SetBrowserView(view);
            UpdateLayout();
            var action = args.Text("action");
            if (action == "navigate")
            {
                ResetLocalBrowserAttempts(view);
                var uri = BrowserTools.ValidateUrl(args.Text("url"));
                var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) { if (e.IsSuccess) done.TrySetResult("페이지 로드됨: " + view.Source); else if (!LocalBrowserRecoveryInProgress(view, e)) done.TrySetException(new IOException("페이지 로드 실패: " + e.WebErrorStatus)); }
                view.CoreWebView2.NavigationCompleted += Completed;
                try { view.CoreWebView2.Navigate(uri.AbsoluteUri); return new(await done.Task.WaitAsync(TimeSpan.FromSeconds(20), ct)); }
                finally { view.CoreWebView2.NavigationCompleted -= Completed; }
            }
            if (action == "screenshot" && args.Property("fullPage")?.ValueKind != JsonValueKind.False)
            {
                var root = desk.State.Projects.First(p => p.Id == task.ProjectId).Root;
                var page = await CaptureBrowserPage(view, root, ct);
                return new(page.Description, Images: page.Tiles.Select(PngDataUrl).ToArray());
            }
            if (action == "upload")
            {
                var root = desk.State.Projects.First(p => p.Id == task.ProjectId).Root;
                var file = Safety.Resolve(root, args.Text("path")); if (!File.Exists(file)) throw new IOException("업로드 파일이 없습니다.");
                var document = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{}").WaitAsync(ct);
                using var parsed = JsonDocument.Parse(document);
                var node = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.querySelector", JsonSerializer.Serialize(new { nodeId = parsed.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32(), selector = args.Text("selector") })).WaitAsync(ct);
                using var selected = JsonDocument.Parse(node); var nodeId = selected.RootElement.GetProperty("nodeId").GetInt32();
                if (nodeId == 0) throw new IOException("업로드 요소가 없습니다.");
                await view.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.setFileInputFiles", JsonSerializer.Serialize(new { nodeId, files = new[] { file } })).WaitAsync(ct);
                return new("파일 업로드 입력 완료");
            }
            string script;
            if (action is "read" or "screenshot") script = "JSON.stringify({url:location.href,title:document.title,text:(document.body?.innerText||'').slice(0,50000),elements:Array.from(document.querySelectorAll('a,button,input,textarea,select')).slice(0,100).map(e=>({tag:e.tagName,id:e.id,name:e.getAttribute('name'),text:e.textContent?.slice(0,100)}))})";
            else if (action == "click") script = "(()=>{const e=document.querySelector(" + BrowserTools.Selector(args) + ");if(!e)throw Error('Element not found');e.click();return 'Clicked';})()";
            else if (action == "type")
            {
                var value = args.Text("text"); if (value.Length > 100000) throw new IOException("브라우저 입력 길이 제한");
                script = "(()=>{const e=document.querySelector(" + BrowserTools.Selector(args) + ");if(!e || !['INPUT','TEXTAREA'].includes(e.tagName)||e.type==='file')throw Error('Unsupported input');const setter=Object.getOwnPropertyDescriptor(e.tagName==='INPUT'?HTMLInputElement.prototype:HTMLTextAreaElement.prototype,'value').set;setter.call(e," + JsonSerializer.Serialize(value) + ");e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));return 'Typed';})()";
            }
            else if (action == "evaluate") script = args.Text("script");
            else throw new IOException("지원하지 않는 브라우저 동작입니다.");
            if (action is "read" or "screenshot" && (view.Source == null || view.Source.Scheme is not ("http" or "https"))) throw new IOException("이 채팅의 브라우저에 열린 페이지가 없습니다. 브라우저 패널에서 URL을 먼저 여세요.");
            var executed = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = script, awaitPromise = true, returnByValue = true })).WaitAsync(ct);
            if (executed.Length > 250000) throw new IOException("브라우저 응답 길이 제한");
            using var output = JsonDocument.Parse(executed);
            if (output.RootElement.Property("exceptionDetails") is { } scriptError)
                throw new IOException("브라우저 스크립트 실행 실패: " + Safety.Redact(scriptError.Property("exception")?.Text("description") ?? scriptError.Text("text")));
            var returned = output.RootElement.GetProperty("result");
            var text = returned.Property("value") is { } scriptValue ? scriptValue.ValueKind == JsonValueKind.String ? scriptValue.GetString() ?? "" : scriptValue.GetRawText() : returned.Text("type");
            var image = action is "read" or "screenshot" ? await CaptureBrowserScreen(view, ct) : null;
            browserStatus.Text = "브라우저 요청 완료 · " + action + " · " + view.Source;
            return new(text, image);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            if (operationView != null) RemoveBrowserTab(operationView);
            browserStatus.Text = "브라우저 응답 시간 초과 · 다음 요청에서 다시 연결합니다.";
            throw new IOException(browserStatus.Text, ex);
        }
        finally { gate.Release(); }
    }
    private async void BrowserPanel_Click(object sender, RoutedEventArgs e) { OpenPanel(8); await Guard(OpenSavedBrowser, "browser-restore"); }
}
