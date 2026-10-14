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
            project.BrowserEnabled = browserEnable.IsChecked == true; desk.Save(); browserStatus.Text = "새 채팅부터 적용됩니다. 모델 요청은 주소/동작을 검토한 뒤 실행합니다.";
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
            browserView.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            browserView.CoreWebView2.PermissionRequested += (_, args) => { args.State = CoreWebView2PermissionState.Deny; browserStatus.Text = "페이지의 카메라/마이크/위치 권한 요청은 차단됐습니다."; };
            browserView.CoreWebView2.NewWindowRequested += (_, args) => { args.Handled = true; _ = Guard(() => NavigateBrowser(args.Uri)); };
            browserView.CoreWebView2.DownloadStarting += (_, args) => { args.Cancel = true; browserStatus.Text = "앱 브라우저에서 자동 다운로드하지 않습니다."; };
            browserView.CoreWebView2.NavigationStarting += (_, args) => { try { BrowserTools.ValidateUrl(args.Uri); } catch (IOException) { args.Cancel = true; } };
            browserView.CoreWebView2.NavigationCompleted += (_, args) => { browserAddress.Text = browserView.Source?.AbsoluteUri ?? ""; browserStatus.Text = args.IsSuccess ? browserView.CoreWebView2.DocumentTitle : "페이지 로드 실패: " + args.WebErrorStatus; };
            browserStatus.Text = "브라우저 준비 완료";
        }
        catch (Exception ex) { browserInitialization = null; browserStatus.Text = "WebView2 초기화 실패. Microsoft Edge WebView2 Runtime이 필요합니다. " + Safety.Redact(ex.Message); throw new IOException(browserStatus.Text, ex); }
    }
    private async Task NavigateBrowser(string address)
    {
        var uri = BrowserTools.ValidateUrl(address); await EnsureBrowser(); browserView!.CoreWebView2.Navigate(uri.AbsoluteUri);
    }
    private async Task<string> BrowserAction(WorkItem task, JsonElement args)
    {
        if (closing || CurrentTask != task || task.ThreadBrowserEnabled != true) throw new IOException("선택한 실행 채팅에서만 브라우저를 사용할 수 있습니다.");
        var action = args.Text("action");
        if (action is not ("navigate" or "read" or "click" or "type")) throw new IOException("지원하지 않는 브라우저 동작입니다.");
        if (action == "navigate") BrowserTools.ValidateUrl(args.Text("url"));
        if (action is "click" or "type") BrowserTools.Selector(args);
        var details = action == "navigate" ? args.Text("url") : action == "read" ? browserAddress.Text + "\n현재 페이지의 텍스트를 선택 모델로 보냅니다." : args.Text("selector") + (action == "type" ? "\n입력: " + Safety.Redact(args.Text("text")) : "");
        if (MessageBox.Show(this, "모델의 브라우저 요청\n\n" + action + "\n" + details, "브라우저 요청 검토", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) throw new IOException("사용자가 브라우저 요청을 거절했습니다.");
        if (CurrentTask != task || task.Status != "실행 중") throw new IOException("브라우저 요청의 작업이 더 이상 실행 중이 아닙니다.");
        OpenPanel(8); await EnsureBrowser();
        if (action == "navigate")
        {
            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) { if (e.IsSuccess) done.TrySetResult("페이지 로드됨: " + browserView!.Source); else done.TrySetException(new IOException("페이지 로드 실패: " + e.WebErrorStatus)); }
            browserView!.CoreWebView2.NavigationCompleted += Completed;
            try { await NavigateBrowser(args.Text("url")); return await done.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
            finally { browserView.CoreWebView2.NavigationCompleted -= Completed; }
        }
        string script;
        if (action == "read") script = "JSON.stringify({url:location.href,title:document.title,text:(document.body?.innerText||'').slice(0,50000),elements:Array.from(document.querySelectorAll('a,button,input,textarea,select')).slice(0,100).map(e=>({tag:e.tagName,id:e.id,name:e.getAttribute('name'),text:e.textContent?.slice(0,100)}))})";
        else if (action == "click") script = "(()=>{const e=document.querySelector(" + BrowserTools.Selector(args) + ");if(!e)throw Error('Element not found');e.click();return 'Clicked';})()";
        else
        {
            var value = args.Text("text"); if (value.Length > 10000) throw new IOException("브라우저 입력 길이 제한");
            script = "(()=>{const e=document.querySelector(" + BrowserTools.Selector(args) + ");if(!e || !['INPUT','TEXTAREA'].includes(e.tagName)||e.type==='password'||e.type==='file')throw Error('Unsupported input');const setter=Object.getOwnPropertyDescriptor(e.tagName==='INPUT'?HTMLInputElement.prototype:HTMLTextAreaElement.prototype,'value').set;setter.call(e," + JsonSerializer.Serialize(value) + ");e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));return 'Typed';})()";
        }
        var result = await browserView!.ExecuteScriptAsync(script);
        if (result == "null") throw new IOException("브라우저 동작에서 결과를 받지 못했습니다.");
        return JsonSerializer.Deserialize<string>(result) ?? result;
    }
    private void BrowserPanel_Click(object sender, RoutedEventArgs e) => OpenPanel(8);
}
