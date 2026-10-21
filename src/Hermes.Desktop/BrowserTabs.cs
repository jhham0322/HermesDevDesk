using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace Hermes.Desktop;
public partial class MainWindow
{
    private const int BrowserTabLimit = 10;
    private readonly Dictionary<WebView2, string?> browserTabOwners = [];
    private readonly Dictionary<WebView2, TaskCompletionSource> browserTabReady = [];
    private Task<CoreWebView2Environment>? browserEnvironment;
    private Task<CoreWebView2Environment> BrowserEnvironment() => browserEnvironment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(desk.DataDirectory, "browser-profile"));
    private readonly WrapPanel browserTabStrip = new();
    private readonly Button browserNewTab = new() { Content = "+ 새 탭" };
    private readonly Button browserCloseTab = new() { Content = "현재 탭 닫기" };
    private readonly Button browserCloseAll = new() { Content = "모든 탭 닫기" };
    private WebView2 CreateBrowserTab(string? owner)
    {
        if (browserTabOwners.Count >= BrowserTabLimit) throw new Hermes.Core.HermesException("브라우저는 최대 10개까지 열 수 있습니다. 사용하지 않는 탭을 닫으세요.");
        var view = new WebView2(); browserTabOwners.Add(view, owner); browserTabReady[view] = new(TaskCreationOptions.RunContinuationsAsynchronously); return view;
    }
    private void RefreshBrowserTabs()
    {
        browserTabStrip.Children.Clear();
        foreach (var (view, owner) in browserTabOwners)
        {
            if (owner != CurrentTask?.Id) continue;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var title = view.CoreWebView2?.DocumentTitle;
            var select = new Button { Content = string.IsNullOrWhiteSpace(title) ? "새 탭" : title.Length > 22 ? title[..22] + "…" : title,
                ToolTip = view.Source?.AbsoluteUri ?? "새 탭", FontWeight = activeBrowserView == view ? FontWeights.Bold : FontWeights.Normal };
            select.Click += (_, _) => SetBrowserView(view);
            var close = new Button { Content = "×", ToolTip = "이 탭 닫기" }; close.Click += (_, _) => TryCloseBrowserTab(view);
            row.Children.Add(select); row.Children.Add(close); browserTabStrip.Children.Add(row);
        }
        browserNewTab.IsEnabled = browserTabOwners.Count < BrowserTabLimit;
        browserNewTab.ToolTip = $"열린 브라우저 {browserTabOwners.Count}/{BrowserTabLimit} (전체 채팅 합계)";
        browserCloseTab.IsEnabled = activeBrowserView != null;
    }
    private bool BrowserTabBusy(WebView2 view) => (browserTabReady.TryGetValue(view, out var ready) && !ready.Task.IsCompleted) || (browserTabOwners.TryGetValue(view, out var owner) && owner != null && browserLocks.TryGetValue(owner, out var gate) && gate.CurrentCount == 0);
    private void TryCloseBrowserTab(WebView2 view)
    {
        if (BrowserTabBusy(view)) { browserStatus.Text = "에이전트의 브라우저 요청이 끝난 뒤 탭을 닫으세요."; return; }
        RemoveBrowserTab(view);
    }
    private void RemoveBrowserTab(WebView2 view)
    {
        if (!browserTabOwners.ContainsKey(view)) return;
        var owner = browserTabOwners.GetValueOrDefault(view);
        var active = activeBrowserView == view;
        browserTabOwners.Remove(view); browserSurface.Children.Remove(view);
        if (browserTabReady.Remove(view, out var ready)) ready.TrySetCanceled();
        sourcePickers.Remove(view); browserNavigationTargets.Remove(view); ResetLocalBrowserAttempts(view);
        foreach (var id in chatBrowsers.Where(p => p.Value == view).Select(p => p.Key).ToArray()) chatBrowsers.Remove(id);
        if (browserView == view) { browserView = null; browserInitialization = null; }
        view.Dispose();
        var replacement = browserTabOwners.FirstOrDefault(p => p.Value == owner).Key;
        if (owner != null && replacement != null && !chatBrowsers.ContainsKey(owner)) chatBrowsers[owner] = replacement;
        else if (owner == null && browserView == null) browserView = replacement;
        if (active)
        {
            activeBrowserView = null;
            if (replacement != null) SetBrowserView(replacement);
            else { browserAddress.Clear(); browserBack.IsEnabled = browserForward.IsEnabled = false; browserBookmark.Content = "☆ 북마크"; browserAttachSource.IsEnabled = false; browserStatus.Text = "열린 탭이 없습니다. + 새 탭으로 브라우저를 여세요."; }
        }
        RefreshBrowserTabs();
    }
    private async Task<WebView2> AddBrowserTab(string? address = null)
    {
        var owner = CurrentTask?.Id;
        var view = CreateBrowserTab(owner); SetBrowserView(view);
        try
        {
            var environment = await BrowserEnvironment();
            await view.EnsureCoreWebView2Async(environment);
            ConfigureBrowser(view, () => owner == null ? CurrentProject?.Root : desk.State.Projects.FirstOrDefault(p => p.Id == desk.State.Tasks.FirstOrDefault(t => t.Id == owner)?.ProjectId)?.Root);
            if (owner == null) browserView = view; else chatBrowsers[owner] = view;
            if (CurrentTask?.Id == owner) SetBrowserView(view);
            if (address != null) view.CoreWebView2.Navigate(Hermes.Core.BrowserTools.ValidateUrl(address).AbsoluteUri);
            else browserStatus.Text = "새 탭 · 주소를 입력하세요.";
            return view;
        }
        catch { RemoveBrowserTab(view); throw; }
    }
    private void InitializeBrowserTabs(Panel toolbar)
    {
        var controls = new WrapPanel(); controls.Children.Add(browserNewTab); controls.Children.Add(browserCloseTab); controls.Children.Add(browserCloseAll);
        toolbar.Children.Add(controls); toolbar.Children.Add(new ScrollViewer { Content = browserTabStrip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 76 });
        browserNewTab.Click += async (_, _) => await Guard(async () => { await AddBrowserTab(); }, "browser-new-tab");
        browserCloseTab.Click += (_, _) => { if (activeBrowserView != null) TryCloseBrowserTab(activeBrowserView); };
        browserCloseAll.Click += (_, _) => {
            var views = browserTabOwners.Keys.ToArray();
            if (views.Any(BrowserTabBusy)) { browserStatus.Text = "에이전트의 브라우저 요청이 끝난 뒤 모든 탭을 닫으세요."; return; }
            foreach (var view in views) RemoveBrowserTab(view);
        };
        RefreshBrowserTabs();
    }
}
