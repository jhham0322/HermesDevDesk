using Hermes.Core;
using WebView2 = Microsoft.Web.WebView2.Wpf.WebView2CompositionControl;

namespace Hermes.Desktop;
public partial class MainWindow
{
    private readonly Dictionary<WebView2, string> browserProjectRoots = [];
    private readonly Dictionary<WebView2, System.Windows.Controls.Border> browserContainers = [];
    private bool BrowserVisible(WebView2 view) => browserContainers.TryGetValue(view, out var container) && container.Opacity == 1;
    private void SetBrowserVisible(WebView2 view, bool visible)
    {
        if (!browserContainers.TryGetValue(view, out var container)) return;
        container.Opacity = visible ? 1 : 0;
        container.IsHitTestVisible = visible;
        view.IsHitTestVisible = view.Focusable = visible;
        System.Windows.Controls.Panel.SetZIndex(container, visible ? 1 : 0);
    }
    private bool CanUseBrowser(WebView2 view, string? taskId) => browserTabOwners.TryGetValue(view, out var owner) && (owner == null || owner == taskId);
    private void ReleaseIdleBrowsers()
    {
        foreach (var (view, owner) in browserTabOwners.ToArray())
        {
            if (owner == null || !browserTabReady[view].Task.IsCompleted) continue;
            var task = desk.State.Tasks.FirstOrDefault(t => t.Id == owner);
            if (task != null && DeskController.IsRunning(task)) continue;
            if (browserLocks.TryGetValue(owner, out var gate) && gate.CurrentCount == 0) continue;
            browserTabOwners[view] = null;
        }
    }
    private void ClaimBrowser(WebView2 view, WorkItem task)
    {
        if (!CanUseBrowser(view, task.Id)) throw new HermesException("다른 채팅에서 사용 중인 브라우저입니다.");
        browserTabOwners[view] = task.Id; chatBrowsers[task.Id] = view;
        browserProjectRoots[view] = desk.State.Projects.First(p => p.Id == task.ProjectId).Root;
        sourcePickers.Remove(view);
        // A background chat may acquire a formerly shared page visible in another chat.
        // Replace that surface immediately, before awaiting any browser operation.
        if (CurrentTask?.Id != task.Id && activeBrowserView == view) ShowChatBrowser();
        else RefreshBrowserTabs();
    }
    private string? BrowserProjectRoot(WebView2 view)
    {
        var owner = browserTabOwners.GetValueOrDefault(view);
        if (owner != null) return desk.State.Projects.FirstOrDefault(p => p.Id == desk.State.Tasks.FirstOrDefault(t => t.Id == owner)?.ProjectId)?.Root;
        return browserProjectRoots.GetValueOrDefault(view) ?? CurrentProject?.Root;
    }
}
