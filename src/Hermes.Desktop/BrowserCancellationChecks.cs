using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunBrowserCancellationChecks(string data)
    {
        var checks = new List<string>();
        long manualStopMs = -1;
        void Stage(string value) => File.WriteAllText(Path.Combine(data, "browser-cancellation-stage.txt"), value);
        Stage("fixture-start");
        void Check(bool ok, string label) { if (!ok) throw new IOException(label); checks.Add(label); }
        var root = Path.Combine(data, "owned-project"); Directory.CreateDirectory(root);
        var project = desk.Register(root);
        var task = desk.NewTask(project, "Owned browser cancellation fixture");
        Projects.SelectedItem = project; Tasks.ItemsSource = new[] { task }; Tasks.SelectedItem = task;
        task.ThreadBrowserEnabled = true; task.Status = "실행 중";
        using var script = JsonDocument.Parse("""{"action":"evaluate","script":"new Promise(()=>{})"}""");
        Task<BrowserToolResult> Pending(CancellationToken token = default) => BrowserAction(task, script.RootElement, token);
        async Task Canceled(Task operation, string label)
        {
            try { await operation.WaitAsync(TimeSpan.FromSeconds(3)); throw new IOException(label + " returned success"); }
            catch (HermesException ex) when (ex.InnerException is OperationCanceledException)
            { Check(ex.Message.Contains("취소") && !ex.Message.Contains("시간 초과"), label); }
        }
        // Start cold: preparation itself must remain cancellable without a 35s timer.
        var pending = Pending(); Stage("cold-request-waiting");
        await Task.Delay(TimeSpan.FromSeconds(36));
        Stage("36-seconds-observed");
        Check(!pending.IsCompleted && browserLocks[task.Id].CurrentCount == 0, "browser action remains pending beyond former 35 second deadline");
        using (var queuedToken = new CancellationTokenSource())
        {
            var queued = Pending(queuedToken.Token); queuedToken.Cancel();
            await Canceled(queued, "queued caller cancellation is classified as cancellation");
            Check(!pending.IsCompleted && browserLocks[task.Id].CurrentCount == 0, "canceling queue neither releases active gate nor cancels active request");
        }
        var other = desk.NewTask(project, "Other owned chat"); other.Status = "실행 중"; other.ThreadBrowserEnabled = true;
        using (var otherToken = new CancellationTokenSource())
        using (var held = new SemaphoreSlim(0, 1))
        {
            browserLocks[other.Id] = held;
            var otherPending = BrowserAction(other, script.RootElement, otherToken.Token);
            var stop = Stopwatch.StartNew(); await desk.StopAsync(task); await Canceled(pending, "user Stop cancels pending browser promptly");
            manualStopMs = stop.ElapsedMilliseconds;
            Check(stop.Elapsed < TimeSpan.FromSeconds(2), "manual Stop completes within two seconds");
            Check(!otherPending.IsCompleted, "Stop preserves other chat request");
            otherToken.Cancel(); await Canceled(otherPending, "other chat retains its independent caller cancellation");
            Check(held.CurrentCount == 0, "unacquired other-chat gate is not released");
            browserLocks.Remove(other.Id);
        }
        Check(browserLocks[task.Id].CurrentCount == 1 && !browserRequests.ContainsKey(task.Id), "Stop releases active gate and unregisters cancellation");
        Stage("active-token-or-shutdown"); task.Status = "실행 중"; OpenPanel(8); await EnsureChatBrowser(task).WaitAsync(TimeSpan.FromSeconds(10));
        using (var caller = new CancellationTokenSource())
        {
            var called = Pending(caller.Token); caller.Cancel(); await Canceled(called, "original caller token cancels active browser request");
        }
        task.Status = "실행 중"; OpenPanel(8); var tab = await EnsureChatBrowser(task).WaitAsync(TimeSpan.FromSeconds(10));
        var closingTab = Pending(); RemoveBrowserTab(tab); await Canceled(closingTab, "tab disposal cancels its pending request");
        Stage("active-token-or-shutdown"); task.Status = "실행 중"; OpenPanel(8); await EnsureChatBrowser(task).WaitAsync(TimeSpan.FromSeconds(10));
        var shuttingDown = Pending(); DisposeBrowsers(); await Canceled(shuttingDown, "application browser disposal cancels pending request");
        Check(browserRequests.Count == 0 && browserLocks[task.Id].CurrentCount == 1, "all cancellation registrations and acquired gates are cleaned up");
        Stage("navigation-timeout");
        // Preserve the separate existing page-navigation deadline and its timeout classification.
        await using var slow = new LoopbackHttp((_, stream, ct) => Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(25), ct).ConfigureAwait(false); await LoopbackHttp.Reply(stream, 200, "text/html", "owned delayed fixture", ct).ConfigureAwait(false); }, ct));
        task.Status = "실행 중";
        using var navigate = JsonDocument.Parse(JsonSerializer.Serialize(new { action = "navigate", url = slow.BaseUri.AbsoluteUri }));
        try { await BrowserAction(task, navigate.RootElement).WaitAsync(TimeSpan.FromSeconds(25)); throw new IOException("Navigation timeout was removed"); }
        catch (HermesException ex) when (ex.InnerException is TimeoutException)
        { Check(ex.Message.Contains("초과") && !ex.Message.Contains("취소"), "existing navigation deadline remains a timeout, not user cancellation"); }
        foreach (var owned in desk.State.Tasks) if (DeskController.IsRunning(owned)) owned.Status = "중단됨";
        desk.Save(); Stage("complete");
        File.WriteAllText(Path.Combine(data, "browser-cancellation-report.json"), JsonSerializer.Serialize(new { passed = true, checks, manualStopMs, realModelRequests = 0, formerDeadlineObservedSeconds = 36, host40SecondGuardChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
