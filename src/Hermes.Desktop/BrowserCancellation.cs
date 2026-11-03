using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    private readonly Dictionary<string, HashSet<CancellationTokenSource>> browserRequests = [];
    // The existing controller stop lifecycle is also raised on disconnect and disposal.
    // A task-specific stop never cancels another chat's browser operation.
    private void CancelBrowserRequests(WorkItem? task) => CancelBrowserRequestsById(task?.Id);
    private void CancelBrowserRequestsById(string? taskId)
    {
        var sources = taskId == null ? browserRequests.Values.SelectMany(s => s).ToArray()
            : browserRequests.TryGetValue(taskId, out var pending) ? pending.ToArray() : [];
        foreach (var source in sources)
        {
            try { if (!source.IsCancellationRequested) source.Cancel(); }
            catch (ObjectDisposedException) { /* A nested tab removal may already finish this request. */ }
        }
    }
}
