using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hermes.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace Hermes.Desktop;
public partial class MainWindow
{
    private readonly DispatcherTimer browserSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly Button browserBookmark = new() { Content = "☆ 북마크" };
    private readonly Button browserAttachSource = new() { Content = "선택 소스 첨부", IsEnabled = false };
    private readonly CheckBox browserPasswords = new() { Content = "비밀번호 저장 · 자동완성" };
    private readonly Dictionary<WebView2, (string Token, string Task)> sourcePickers = [];
    private string selectedBrowserSource = "";
    private string selectedBrowserSourceTask = "";
    private void InitializeBrowserExperience(Panel toolbar)
    {
        if (desk.State.Browser.LastUrl.Length > 0) browserAddress.Text = desk.State.Browser.LastUrl;
        browserSaveTimer.Tick += (_, _) => { browserSaveTimer.Stop(); desk.Save(); };
        var actions = new WrapPanel();
        var back = new Button { Content = "뒤로" }; var forward = new Button { Content = "앞으로" }; var reload = new Button { Content = "새로고침" };
        var bookmarks = new Button { Content = "북마크 목록" }; var history = new Button { Content = "방문 기록" }; var inspect = new Button { Content = "요소 소스 선택" };
        foreach (var button in new[] { back, forward, reload, browserBookmark, bookmarks, history, inspect, browserAttachSource }) actions.Children.Add(button);
        toolbar.Children.Add(actions); toolbar.Children.Add(browserPasswords);
        browserPasswords.IsChecked = desk.State.Browser.SavePasswords;
        browserPasswords.Click += (_, _) => {
            desk.State.Browser.SavePasswords = browserPasswords.IsChecked == true;
            foreach (var view in browserSurface.Children.OfType<WebView2>()) if (view.CoreWebView2 != null) view.CoreWebView2.Profile.IsPasswordAutosaveEnabled = desk.State.Browser.SavePasswords;
            desk.Save();
        };
        back.Click += (_, _) => { if (activeBrowserView?.CoreWebView2?.CanGoBack == true) activeBrowserView.CoreWebView2.GoBack(); };
        forward.Click += (_, _) => { if (activeBrowserView?.CoreWebView2?.CanGoForward == true) activeBrowserView.CoreWebView2.GoForward(); };
        reload.Click += (_, _) => activeBrowserView?.CoreWebView2?.Reload();
        browserBookmark.Click += (_, _) => _ = Guard(() => { ToggleCurrentBookmark(); return Task.CompletedTask; }, "browser-bookmark");
        bookmarks.Click += (_, _) => ShowBrowserLibrary(true); history.Click += (_, _) => ShowBrowserLibrary(false);
        inspect.Click += async (_, _) => await Guard(PickBrowserSource, "browser-element-source");
        browserAttachSource.Click += (_, _) => { if (selectedBrowserSource.Length > 0 && selectedBrowserSourceTask == CurrentTask?.Id) { Prompt.AppendText((Prompt.Text.Length > 0 ? "\n\n" : "") + selectedBrowserSource); Prompt.Focus(); } };
    }
    private void RememberBrowserPage(WebView2 view)
    {
        if (view.Source?.Scheme is not ("http" or "https")) return;
        var last = desk.State.Browser.LastUrl;
        BrowserLibrary.Visit(desk.State.Browser, view.Source.AbsoluteUri, view.CoreWebView2.DocumentTitle);
        var url = desk.State.Browser.LastUrl;
        var id = chatBrowsers.FirstOrDefault(p => ReferenceEquals(p.Value, view)).Key;
        if (id != null && desk.State.Tasks.FirstOrDefault(t => t.Id == id) is { } task) task.BrowserLastUrl = url;
        if (activeBrowserView != view) desk.State.Browser.LastUrl = last;
        else { browserAddress.Text = url; UpdateBrowserBookmark(view); }
        browserSaveTimer.Stop(); browserSaveTimer.Start();
    }
    private void UpdateBrowserBookmark(WebView2 view)
    {
        var url = view.Source?.AbsoluteUri;
        browserBookmark.Content = desk.State.Browser.Bookmarks.Any(p => p.Url == url) ? "★ 북마크 해제" : "☆ 북마크";
    }
    private void ToggleCurrentBookmark()
    {
        var view = activeBrowserView ?? throw new IOException("북마크할 페이지를 먼저 여세요.");
        var url = view.Source?.AbsoluteUri ?? throw new IOException("열린 페이지가 없습니다.");
        var added = BrowserLibrary.ToggleBookmark(desk.State.Browser, url, view.CoreWebView2.DocumentTitle);
        desk.Save(); UpdateBrowserBookmark(view); browserStatus.Text = added ? "북마크 저장됨" : "북마크 삭제됨";
    }
    private void ShowBrowserLibrary(bool bookmarks)
    {
        var window = new Window { Owner = this, Title = bookmarks ? "브라우저 북마크" : "브라우저 방문 기록", Width = 680, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(16) }; window.Content = panel;
        var search = new TextBox { Margin = new Thickness(4), ToolTip = "제목 또는 URL 검색" }; DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
        var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var list = new ListBox { Margin = new Thickness(4) }; panel.Children.Add(list);
        var open = new Button { Content = "선택 페이지 열기" }; var remove = new Button { Content = "선택 항목 삭제" }; buttons.Children.Add(open); buttons.Children.Add(remove);
        List<BrowserPageEntry> Entries() => bookmarks ? desk.State.Browser.Bookmarks : desk.State.Browser.History;
        void RefreshList() => list.ItemsSource = Entries().Where(p => p.Title.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || p.Url.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        RefreshList(); search.TextChanged += (_, _) => RefreshList();
        async Task Open() { if (list.SelectedItem is BrowserPageEntry page) { window.Close(); await NavigateBrowser(page.Url); } }
        open.Click += async (_, _) => await Guard(Open); list.MouseDoubleClick += async (_, _) => await Guard(Open);
        remove.Click += (_, _) => { if (list.SelectedItem is BrowserPageEntry page) { Entries().Remove(page); desk.Save(); RefreshList(); if (activeBrowserView != null) UpdateBrowserBookmark(activeBrowserView); } };
        window.ShowDialog();
    }
    private async Task OpenSavedBrowser()
    {
        WebView2 view;
        if (CurrentTask is { } task) view = await EnsureChatBrowser(task); else { await EnsureBrowser(); view = browserView!; }
        SetBrowserView(view);
        if (view.Source?.Scheme is not ("http" or "https"))
        {
            var url = CurrentTask?.BrowserLastUrl;
            if (string.IsNullOrWhiteSpace(url)) url = desk.State.Browser.LastUrl;
            if (string.IsNullOrWhiteSpace(url)) url = browserAddress.Text;
            view.CoreWebView2.Navigate(BrowserTools.ValidateUrl(url).AbsoluteUri);
        }
        UpdateBrowserBookmark(view);
    }
    private async Task PickBrowserSource()
    {
        if (CurrentTask == null) { if (CurrentProject == null) throw new IOException("소스를 표시할 채팅의 프로젝트를 선택하세요."); NewTask_Click(this, new RoutedEventArgs()); }
        await OpenSavedBrowser(); var view = activeBrowserView!; var token = Guid.NewGuid().ToString("N");
        sourcePickers[view] = (token, CurrentTask!.Id);
        var script = """
        (()=>{
          if(window.__hermesSourcePicker)document.removeEventListener('click',window.__hermesSourcePicker,true);
          const handler=e=>{
            e.preventDefault();e.stopImmediatePropagation();document.removeEventListener('click',handler,true);delete window.__hermesSourcePicker;
            const node=e.composedPath().find(n=>n instanceof Element);if(!node)return;
            const clone=node.cloneNode(true);
            for(const el of [clone,...clone.querySelectorAll('*')]){
              for(const attr of [...el.attributes])if(/password|token|secret|session|authorization|cookie/i.test(attr.name)||(['INPUT','TEXTAREA'].includes(el.tagName)&&attr.name==='value'))el.removeAttribute(attr.name);
              if(el.tagName==='TEXTAREA')el.textContent='';
            }
            window.chrome.webview.postMessage({kind:'hermes.element-source',token:TOKEN,url:location.href,tag:node.tagName,html:clone.outerHTML.slice(0,120000)});
          };
          window.__hermesSourcePicker=handler;document.addEventListener('click',handler,true);return 'ready';
        })()
        """;
        await view.ExecuteScriptAsync(script.Replace("TOKEN", JsonSerializer.Serialize(token), StringComparison.Ordinal));
        browserStatus.Text = "페이지에서 요소를 클릭하면 해당 요소의 HTML을 채팅에 표시합니다.";
    }
    private void ReceiveBrowserSource(WebView2 view, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!sourcePickers.TryGetValue(view, out var picker) || activeBrowserView != view || CurrentTask?.Id != picker.Task) return;
        var json = args.WebMessageAsJson; if (json.Length > 250000) return;
        try
        {
            using var message = JsonDocument.Parse(json); var data = message.RootElement;
            if (data.Text("kind") != "hermes.element-source" || data.Text("token") != picker.Token) return;
            if (!Uri.TryCreate(args.Source, UriKind.Absolute, out var source) || view.Source == null || source.GetLeftPart(UriPartial.Authority) != view.Source.GetLeftPart(UriPartial.Authority)) return;
            var html = data.Text("html"); if (html.Length == 0 || html.Length > 120000) return;
            sourcePickers.Remove(view);
            var url = BrowserLibrary.PageUrl(view.Source.AbsoluteUri);
            var fence = html.Contains("````", StringComparison.Ordinal) ? "`````" : "````";
            selectedBrowserSource = "브라우저 선택 요소 · 렌더링된 DOM HTML\n" + url + "\n" + fence + "html\n" + Safety.Redact(html) + "\n" + fence;
            CurrentTask.Transcript.Add(new("브라우저 소스", selectedBrowserSource, DateTime.UtcNow)); desk.Save();
            selectedBrowserSourceTask = CurrentTask.Id; browserAttachSource.IsEnabled = true; browserStatus.Text = "선택한 HTML을 채팅에 표시했습니다. 선택 소스 첨부로 AI 입력에 넣을 수 있습니다.";
        }
        catch (JsonException) { }
    }
}
