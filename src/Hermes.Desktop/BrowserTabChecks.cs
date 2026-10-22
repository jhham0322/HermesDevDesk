using System.IO;
using System.Windows;
using System.Windows.Controls;
using WebView2 = Microsoft.Web.WebView2.Wpf.WebView2CompositionControl;
namespace Hermes.Desktop;
public partial class MainWindow
{
    private async Task CheckBrowserTabs(string url, List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new IOException(name); checks.Add(name); File.AppendAllText(Path.Combine(desk.DataDirectory, "browser-tab-progress.txt"), name + "\n"); }
        async Task Wait(Func<bool> ready) { var timer = System.Diagnostics.Stopwatch.StartNew(); while (!ready() && timer.Elapsed.TotalSeconds < 12) await Task.Delay(30); if (!ready()) throw new IOException("browser tab operation timed out"); }
        var first = activeBrowserView!;
        var second = await AddBrowserTab(url + "tab-two");
        await Wait(() => second.CoreWebView2.DocumentTitle.Contains("tab-two"));
        await first.ExecuteScriptAsync("window.__tabMarker='FIRST_ONLY'");
        Check(await second.ExecuteScriptAsync("typeof window.__tabMarker") == "\"undefined\"", "tabs have independent page script state");
        var selectFirst = ((StackPanel)browserTabStrip.Children[0]).Children.OfType<Button>().First(); selectFirst.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(activeBrowserView == first && BrowserVisible(first) && !BrowserVisible(second) && !second.IsHitTestVisible && browserAddress.Text == first.Source.AbsoluteUri, "tab button restores correct visible page and address");
        Check(chatBrowsers[CurrentTask!.Id] == first, "agent browser follows selected tab of its own chat");
        for (var i = browserTabOwners.Count; i < BrowserTabLimit; i++) await AddBrowserTab();
        Check(browserTabOwners.Count == 10 && !browserNewTab.IsEnabled, "ten browser tabs allowed and add button disabled at limit");
        try { await AddBrowserTab(); throw new IOException("eleventh browser tab was allowed"); }
        catch (IOException ex) when (ex.Message.Contains("최대 10개")) { checks.Add("eleventh tab rejected before creating WebView"); }
        var last = activeBrowserView!;
        var background = browserTabOwners.Keys.First(v => v != last && v != first && v != second);
        RemoveBrowserTab(background);
        Check(chatBrowsers[CurrentTask!.Id] == last && activeBrowserView == last, "closing background tab preserves selected agent tab");
        await AddBrowserTab(); last = activeBrowserView!;
        var gate = new SemaphoreSlim(0, 1); browserLocks[CurrentTask!.Id] = gate;
        browserCloseTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(browserTabOwners.Count == 10 && activeBrowserView == last, "busy agent tab cannot be closed during request");
        gate.Release(); browserLocks.Remove(CurrentTask.Id); gate.Dispose();
        browserCloseTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(browserTabOwners.Count == 9 && !browserContainers.ContainsKey(last) && activeBrowserView != last && browserNewTab.IsEnabled, "current-tab close disposes view and selects remaining tab");
        var replacement = await AddBrowserTab();
        Check(browserTabOwners.Count == 10 && activeBrowserView == replacement && browserAddress.Text.Length == 0, "closed tab frees capacity for a new blank tab with empty address");
        browserCloseAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(browserTabOwners.Count == 0 && chatBrowsers.Count == 0 && browserSurface.Children.Count == 0 && activeBrowserView == null && !browserCloseTab.IsEnabled, "close all removes tabs and stale active references");
        var popupParent = await AddBrowserTab(url + "popup-parent");
        await Wait(() => popupParent.CoreWebView2.DocumentTitle.Contains("popup-parent"));
        await popupParent.ExecuteScriptAsync("window.open('/popup-child','_blank')");
        await Wait(() => browserTabOwners.Count == 2 && activeBrowserView?.CoreWebView2?.DocumentTitle.Contains("popup-child") == true);
        Check(popupParent.Source.AbsolutePath.Contains("popup-parent"), "new-window link opens a separate tab without replacing parent");
        await activeBrowserView!.ExecuteScriptAsync("setTimeout(()=>window.close(),100)");
        await Wait(() => browserTabOwners.Count == 1);
        Check(activeBrowserView == popupParent, "script close request closes popup and restores parent tab");
        var ownTask = CurrentTask!;
        var otherRoot = Path.Combine(desk.DataDirectory, "shared-browser-other-project"); Directory.CreateDirectory(otherRoot);
        var otherProject = desk.Register(otherRoot);
        var other = desk.NewTask(otherProject, "공통 탭 검사"); Tasks.ItemsSource = new[] { ownTask, other }; Tasks.SelectedItem = other;
        var otherView = await AddBrowserTab();
        Check(browserTabStrip.Children.Count == 2 && chatBrowsers[ownTask.Id] == popupParent, "idle browser tabs are shared across chats without replacing per-chat selection");
        ownTask.Status = "실행 중"; ownTask.ThreadBrowserEnabled = true;
        ClaimBrowser(popupParent, ownTask); ShowChatBrowser();
        Check(browserTabStrip.Children.Count == 1 && !BrowserVisible(popupParent) && !popupParent.IsHitTestVisible && activeBrowserView == otherView, "browser used by another running chat is removed from tabs and visible surface");
        Check(BrowserProjectRoot(popupParent) == desk.State.Projects.Single(p => p.Id == ownTask.ProjectId).Root, "leased browser download context follows background owner project");
        SetBrowserView(popupParent);
        Check(activeBrowserView == otherView && browserAddress.Text.Length == 0, "stale tab selection cannot expose another chat's leased browser");
        var reused = await EnsureChatBrowser(other);
        Check(reused == otherView && browserTabOwners.Count == 2, "second chat reuses free common browser while first chat holds another");
        var read = await BrowserAction(ownTask, System.Text.Json.JsonSerializer.SerializeToElement(new { action = "read" }));
        Check(read.Text.Length > 0 && read.ImageUrl?.StartsWith("data:image/png;base64,") == true && activeBrowserView == otherView && !BrowserVisible(popupParent) && !popupParent.IsHitTestVisible, "background browser tool reads its leased page without showing it in foreground chat");
        browserCloseAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(browserTabOwners.Count == 1 && browserTabOwners.ContainsKey(popupParent), "close visible tabs preserves browsers leased by another chat");
        ownTask.Status = "완료"; ShowChatBrowser();
        Check(browserTabStrip.Children.Count == 1 && activeBrowserView == popupParent && browserTabOwners[popupParent] == null, "completed task returns same live page to common browser pool");
        Check(await EnsureChatBrowser(other) == popupParent && browserTabOwners.Count == 1, "different chat reuses returned browser without creating a new WebView");
        Check(BrowserProjectRoot(popupParent) == otherRoot, "returned browser switches download context to new borrowing project");
        Tasks.SelectedItem = ownTask; ShowChatBrowser();
        Check(activeBrowserView == popupParent, "returning to chat restores shared selected browser");
        ownTask.Status = "계속 실행 대기"; ClaimBrowser(popupParent, ownTask);
        Tasks.SelectedItem = other; ShowChatBrowser();
        Check(browserTabStrip.Children.Count == 0 && activeBrowserView == null && browserAddress.Text.Length == 0, "completion-loop wait retains browser lease and clears other chat address");
        ownTask.Status = "중단됨"; ShowChatBrowser();
        Check(activeBrowserView == popupParent && browserTabOwners[popupParent] == null, "stopped task releases browser for other chats");
        Tasks.SelectedItem = ownTask; ShowChatBrowser();
        var extras = browserControls.Children.OfType<Expander>().Single();
        Check(!extras.IsExpanded && ((StackPanel)((ScrollViewer)extras.Content).Content).Children.Contains(browserEnable), "bookmark capture and browser tool options start collapsed");
        extras.IsExpanded = true; Check(extras.IsExpanded, "browser extra features can expand"); extras.IsExpanded = false;
    }
}
