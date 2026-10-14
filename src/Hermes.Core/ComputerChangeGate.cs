namespace Hermes.Core;

// Pixel heuristics are not a semantic classifier. Content remains untrusted.
public sealed record ComputerChangeOptions(int IntervalMs = 300, int PixelThreshold = 12,
    double ChangedFraction = 0.01, int DebounceMs = 300, int MinimumNotificationMs = 1000,
    int MaximumCoalescingMs = 1000);
public sealed record ComputerCursor(int X, int Y);
public sealed record ComputerChangeDecision(bool Notify, bool PixelChanged, bool CursorChanged,
    double PreviousDifference, double DeliveredDifference, long Captures, long Changes, long Coalesced);

public sealed class ComputerChangeGate
{
    private readonly ComputerChangeOptions options;
    private byte[]? previous, delivered;
    private ComputerCursor? previousCursor, deliveredCursor;
    private DateTimeOffset? pendingSince, lastChange, lastNotification;
    private bool pendingPixels, pendingCursor, stopped;
    private long captures, changes, coalesced;
    public ComputerChangeGate(ComputerChangeOptions? options = null)
    {
        this.options = options ?? new();
        if (this.options.IntervalMs != 300 || this.options.PixelThreshold is < 1 or > 255 ||
            this.options.ChangedFraction is <= 0 or > 1 || !double.IsFinite(this.options.ChangedFraction) ||
            this.options.DebounceMs < 0 || this.options.MinimumNotificationMs < 1000 ||
            this.options.MaximumCoalescingMs < this.options.DebounceMs)
            throw new IOException("Invalid local change thresholds/rate limit");
    }
    public void SetDelivered(byte[] pixels, ComputerCursor? cursor, DateTimeOffset? at = null)
    { delivered = pixels.ToArray(); deliveredCursor = cursor; lastNotification = at; }
    public void Stop() { stopped = true; previous = delivered = null; pendingSince = null; pendingPixels = pendingCursor = false; }
    public ComputerChangeDecision Observe(byte[] pixels, ComputerCursor? cursor, DateTimeOffset now)
    {
        if (stopped) return new(false, false, false, 0, 0, captures, changes, coalesced);
        captures++;
        if (previous == null) { previous = pixels.ToArray(); previousCursor = cursor; if (delivered == null) { delivered = pixels.ToArray(); deliveredCursor = cursor; } }
        var recent = Difference(previous, pixels); var accumulated = Difference(delivered!, pixels);
        var pixelChange = accumulated >= options.ChangedFraction;
        var cursorChange = cursor != deliveredCursor;
        if (recent >= options.ChangedFraction || cursor != previousCursor || (pixelChange && !pendingPixels))
        {
            changes++; if (pendingSince != null) coalesced++;
            pendingSince ??= now; lastChange = now; pendingPixels |= pixelChange; pendingCursor |= cursorChange;
        }
        previous = pixels.ToArray(); previousCursor = cursor;
        var stable = lastChange != null && (now - lastChange.Value).TotalMilliseconds >= options.DebounceMs;
        var maximumWait = pendingSince != null && (now - pendingSince.Value).TotalMilliseconds >= options.MaximumCoalescingMs;
        var rateReady = lastNotification == null || (now - lastNotification.Value).TotalMilliseconds >= options.MinimumNotificationMs;
        var notify = pendingSince != null && rateReady && (stable || maximumWait) && (pendingPixels || pendingCursor);
        var result = new ComputerChangeDecision(notify, pendingPixels, pendingCursor, recent, accumulated, captures, changes, coalesced);
        return result;
    }
    public void Acknowledge(byte[] pixels, ComputerCursor? cursor, DateTimeOffset now)
    {
        SetDelivered(pixels, cursor, now); pendingSince = lastChange = null; pendingPixels = pendingCursor = false;
    }
    private double Difference(byte[] first, byte[] second)
    {
        if (first.Length != second.Length || second.Length == 0) return 1;
        int changed = 0;
        for (var i = 0; i < second.Length; i++) if (Math.Abs(first[i] - second[i]) >= options.PixelThreshold) changed++;
        return changed / (double)second.Length;
    }
}

public static class ComputerExecutionPolicy
{
    public static bool CanAutoExecute(ComputerAction action, bool armed, bool allowedWindow, bool automatic, bool sensitive)
        => armed && allowedWindow && automatic && !sensitive && action.Action is "click" or "type" or "scroll";
}
