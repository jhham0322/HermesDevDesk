using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
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
        Check(activeBrowserView == first && first.Visibility == Visibility.Visible && second.Visibility == Visibility.Hidden && browserAddress.Text == first.Source.AbsoluteUri, "tab button restores correct visible page and address");
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
        Check(browserTabOwners.Count == 9 && !browserSurface.Children.Contains(last) && activeBrowserView != last && browserNewTab.IsEnabled, "current-tab close disposes view and selects remaining tab");
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
        var other = desk.NewTask(CurrentProject!, "탭 격리 검사"); Tasks.ItemsSource = new[] { ownTask, other }; Tasks.SelectedItem = other;
        var otherView = await AddBrowserTab();
        Check(browserTabStrip.Children.Count == 1 && chatBrowsers[ownTask.Id] == popupParent, "chat tab strip and agent browser remain isolated");
        Tasks.SelectedItem = ownTask; ShowChatBrowser();
        Check(activeBrowserView == popupParent && otherView.Visibility == Visibility.Hidden, "returning to chat restores its selected browser");
        RemoveBrowserTab(otherView);
        var extras = browserControls.Children.OfType<Expander>().Single();
        Check(!extras.IsExpanded && ((StackPanel)((ScrollViewer)extras.Content).Content).Children.Contains(browserEnable), "bookmark capture and browser tool options start collapsed");
        extras.IsExpanded = true; Check(extras.IsExpanded, "browser extra features can expand"); extras.IsExpanded = false;
    }
}
