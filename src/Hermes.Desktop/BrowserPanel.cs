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
    private Task? browserInitialization;
    private readonly Dictionary<string, WebView2> chatBrowsers = [];
    private readonly Dictionary<string, SemaphoreSlim> browserLocks = [];
    private void ConfigureBrowser(WebView2 view, Func<string?> projectRoot)
    {
        view.CoreWebView2.Settings.AreHostObjectsAllowed = false;
        view.CoreWebView2.PermissionRequested += (_, args) => { args.State = CoreWebView2PermissionState.Allow; args.Handled = true; };
        view.CoreWebView2.NewWindowRequested += (_, args) => { args.Handled = true; try { view.CoreWebView2.Navigate(BrowserTools.ValidateUrl(args.Uri).AbsoluteUri); } catch (IOException) { } };
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
        view.CoreWebView2.NavigationCompleted += (_, args) => { if (browserHost.Content == view) { browserAddress.Text = view.Source?.AbsoluteUri ?? ""; browserStatus.Text = args.IsSuccess ? view.CoreWebView2.DocumentTitle : "페이지 로드 실패: " + args.WebErrorStatus; } };
    }
    private void ShowChatBrowser()
    {
        if (CurrentTask is { } task && chatBrowsers.TryGetValue(task.Id, out var view)) browserHost.Content = view;
        else if (browserView != null) browserHost.Content = browserView;
    }
    private void DisposeBrowsers() { foreach (var view in chatBrowsers.Values) view.Dispose(); browserView?.Dispose(); }


    private void InitializeBrowserPanel()
    {
        var panel = new DockPanel(); var toolbar = new StackPanel(); DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar);
        var address = new DockPanel(); var go = new Button { Content = "열기" }; DockPanel.SetDock(go, Dock.Right); address.Children.Add(go); address.Children.Add(browserAddress); toolbar.Children.Add(address);
        toolbar.Children.Add(browserEnable); toolbar.Children.Add(browserStatus); panel.Children.Add(browserHost);
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
        try { await (browserInitialization ??= InitializeBrowser()); }
        catch { browserInitialization = null; throw; }
    }
    private async Task InitializeBrowser()
    {
        try
        {
            browserView = new WebView2(); browserHost.Content = browserView;
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(desk.DataDirectory, "browser-profile"));
            await browserView.EnsureCoreWebView2Async(environment);
            ConfigureBrowser(browserView, () => CurrentProject?.Root);
            browserStatus.Text = "브라우저 준비 완료";
        }
        catch (Exception ex) { browserInitialization = null; browserStatus.Text = "WebView2 초기화 실패. Microsoft Edge WebView2 Runtime이 필요합니다. " + Safety.Redact(ex.Message); throw new IOException(browserStatus.Text, ex); }
    }
    private async Task NavigateBrowser(string address)
    {
        var uri = BrowserTools.ValidateUrl(address); await EnsureBrowser(); var view = browserHost.Content as WebView2 ?? browserView!; view.CoreWebView2.Navigate(uri.AbsoluteUri);
    }
    private async Task<string> BrowserAction(WorkItem task, JsonElement args)
    {
        if (closing || task.ThreadBrowserEnabled != true || task.Status != "실행 중") throw new IOException("실행 중인 브라우저 채팅이 아닙니다.");
        if (!browserLocks.TryGetValue(task.Id, out var gate)) browserLocks[task.Id] = gate = new(1, 1);
        await gate.WaitAsync();
        try
        {
            if (closing || task.Status != "실행 중") throw new IOException("작업이 중단되었습니다.");
            if (!chatBrowsers.TryGetValue(task.Id, out var view))
            {
                view = new WebView2(); browserHost.Content = view;
                try
                {
                    var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(desk.DataDirectory, "browser-profile"));
                    await view.EnsureCoreWebView2Async(environment);
                    ConfigureBrowser(view, () => desk.State.Projects.First(p => p.Id == task.ProjectId).Root);
                    chatBrowsers[task.Id] = view;
                }
                catch { view.Dispose(); throw; }
                ShowChatBrowser();
            }
            if (CurrentTask == task) { browserHost.Content = view; OpenPanel(8); }
            var action = args.Text("action");
            if (action == "navigate")
            {
                var uri = BrowserTools.ValidateUrl(args.Text("url"));
                var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) { if (e.IsSuccess) done.TrySetResult("페이지 로드됨: " + view.Source); else done.TrySetException(new IOException("페이지 로드 실패: " + e.WebErrorStatus)); }
                view.CoreWebView2.NavigationCompleted += Completed;
                try { view.CoreWebView2.Navigate(uri.AbsoluteUri); return await done.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
                finally { view.CoreWebView2.NavigationCompleted -= Completed; }
            }
            if (action == "upload")
            {
                var root = desk.State.Projects.First(p => p.Id == task.ProjectId).Root;
                var file = Safety.Resolve(root, args.Text("path")); if (!File.Exists(file)) throw new IOException("업로드 파일이 없습니다.");
                var document = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{}");
                using var parsed = JsonDocument.Parse(document);
                var node = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.querySelector", JsonSerializer.Serialize(new { nodeId = parsed.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32(), selector = args.Text("selector") }));
                using var selected = JsonDocument.Parse(node); var nodeId = selected.RootElement.GetProperty("nodeId").GetInt32();
                if (nodeId == 0) throw new IOException("업로드 요소가 없습니다.");
                await view.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.setFileInputFiles", JsonSerializer.Serialize(new { nodeId, files = new[] { file } }));
                return "파일 업로드 입력 완료";
            }
            string script;
            if (action == "read") script = "JSON.stringify({url:location.href,title:document.title,text:(document.body?.innerText||'').slice(0,50000),elements:Array.from(document.querySelectorAll('a,button,input,textarea,select')).slice(0,100).map(e=>({tag:e.tagName,id:e.id,name:e.getAttribute('name'),text:e.textContent?.slice(0,100)}))})";
            else if (action == "click") script = "(()=>{const e=document.querySelector(" + BrowserTools.Selector(args) + ");if(!e)throw Error('Element not found');e.click();return 'Clicked';})()";
            else if (action == "type")
            {
                var value = args.Text("text"); if (value.Length > 100000) throw new IOException("브라우저 입력 길이 제한");
                script = "(()=>{const e=document.querySelector(" + BrowserTools.Selector(args) + ");if(!e || !['INPUT','TEXTAREA'].includes(e.tagName)||e.type==='file')throw Error('Unsupported input');const setter=Object.getOwnPropertyDescriptor(e.tagName==='INPUT'?HTMLInputElement.prototype:HTMLTextAreaElement.prototype,'value').set;setter.call(e," + JsonSerializer.Serialize(value) + ");e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));return 'Typed';})()";
            }
            else if (action == "evaluate") script = args.Text("script");
            else throw new IOException("지원하지 않는 브라우저 동작입니다.");
            var result = await view.ExecuteScriptAsync(script);
            if (result.Length > 200000) throw new IOException("브라우저 응답 길이 제한");
            using var output = JsonDocument.Parse(result);
            return output.RootElement.ValueKind == JsonValueKind.String ? output.RootElement.GetString() ?? "" : result;
        }
        finally { gate.Release(); }
    }
    private void BrowserPanel_Click(object sender, RoutedEventArgs e) => OpenPanel(8);
}
