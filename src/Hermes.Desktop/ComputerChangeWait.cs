using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hermes.Core;

namespace Hermes.Desktop;

internal sealed record ComputerWaitRequest(ComputerWindow Window, string? BaselineImageUrl,
    ComputerCursor? BaselineCursor, DateTimeOffset? LastDeliveryUtc, int TimeoutMs, ComputerChangeOptions Options);
internal sealed record ComputerWaitMetrics(long Captures, long Changes, long Coalesced, int Notifications,
    long ElapsedMs, int ImageBytes, int IntervalMs, bool ContainsImageContent = false);
internal sealed record ComputerWaitReply(bool Success, string Reason, ComputerFrame? Frame,
    ComputerCursor? Cursor, ComputerWaitMetrics Metrics, string Error);

internal static class ComputerChangeWait
{
    internal static byte[] Sample(string imageUrl)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(imageUrl.Split(',')[1]));
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4; var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
        // Fixed-size local comparison samples. Never persist or transmit this buffer.
        var sample = new byte[64 * 40];
        for (var y = 0; y < 40; y++) for (var x = 0; x < 64; x++)
        {
            var offset = (y * bitmap.PixelHeight / 40) * stride + (x * bitmap.PixelWidth / 64) * 4;
            sample[y * 64 + x] = (byte)((pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3);
        }
        return sample;
    }
    internal static int Main()
    {
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        var clock = Stopwatch.StartNew(); long captures = 0, changes = 0, coalesced = 0;
        var interval = 300;
        ComputerWaitMetrics Stats(int notifications = 0, int bytes = 0) => new(captures, changes, coalesced, notifications, clock.ElapsedMilliseconds, bytes, interval);
        try
        {
            using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            var wire = input.ReadToEnd(); if (wire.Length > 8_000_000) throw new Hermes.Core.HermesException("Change-wait input size limit");
            var request = JsonSerializer.Deserialize<ComputerWaitRequest>(wire) ?? throw new Hermes.Core.HermesException("Missing change-wait request");
            if (request.TimeoutMs is < 300 or > 30000 || request.Options.IntervalMs != 300) throw new Hermes.Core.HermesException("Change-wait requires 300ms local cadence and 300..30000ms timeout");
            var gate = new ComputerChangeGate(request.Options); interval = request.Options.IntervalMs;
            if (request.BaselineImageUrl != null) gate.SetDelivered(Sample(request.BaselineImageUrl), request.BaselineCursor, request.LastDeliveryUtc);
            while (clock.ElapsedMilliseconds < request.TimeoutMs)
            {
                var tick = clock.ElapsedMilliseconds;
                // Read only the selected, identity-validated client area; never activate or inject input.
                captures++; var frame = ComputerNative.ObserveForChange(request.Window, false);
                var decision = gate.Observe(Sample(frame.ImageUrl), frame.Cursor, DateTimeOffset.UtcNow);
                changes = decision.Changes; coalesced = decision.Coalesced;
                if (decision.Notify)
                {
                    captures++; frame = ComputerNative.ObserveForChange(request.Window, true);
                    var bytes = Convert.FromBase64String(frame.ImageUrl.Split(',')[1]).Length;
                    output.WriteLine(JsonSerializer.Serialize(new ComputerWaitReply(true,
                        decision.PixelChanged && decision.CursorChanged ? "pixels+cursor" : decision.PixelChanged ? "pixels" : "cursor",
                        frame, frame.Cursor, Stats(1, bytes), ""))); return 0;
                }
                // Local IPC metadata only. The parent replaces its last counter; no per-frame queue/log.
                output.WriteLine(JsonSerializer.Serialize(new { progress = Stats() }));
                var delay = Math.Min(interval - (int)(clock.ElapsedMilliseconds - tick), request.TimeoutMs - (int)clock.ElapsedMilliseconds);
                if (delay > 0) Thread.Sleep(delay);
            }
            gate.Stop();
            output.WriteLine(JsonSerializer.Serialize(new ComputerWaitReply(true, "timeout", null, null, Stats(), ""))); return 0;
        }
        catch (Exception ex)
        {
            output.WriteLine(JsonSerializer.Serialize(new ComputerWaitReply(false, "error", null, null, Stats(), Safety.Redact(ex.Message)))); return 1;
        }
    }
}

internal sealed class ComputerChangeWaitClient : IDisposable
{
    private Process? process;
    internal ComputerWaitMetrics Latest { get; private set; } = new(0, 0, 0, 0, 0, 0, 300);
    internal async Task<ComputerWaitReply> Execute(ComputerWaitRequest request, CancellationToken cancellation)
    {
        if (process != null) throw new Hermes.Core.HermesException("A change-wait is already running");
        Latest = new(0, 0, 0, 0, 0, 0, 300);
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        info.ArgumentList.Add("--computer-change-wait");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(request.TimeoutMs + 15000);
        var child = Process.Start(info) ?? throw new Hermes.Core.HermesException("Change-wait worker failed to start"); process = child;
        using var cancel = deadline.Token.Register(Stop);
        try
        {
            // Drain diagnostics without retaining text or logging screenshots/credentials.
            var errors = Task.Run(async () => { while (await child.StandardError.ReadLineAsync(deadline.Token) != null) { } }, deadline.Token);
            await child.StandardInput.WriteAsync(JsonSerializer.Serialize(request).AsMemory(), deadline.Token); child.StandardInput.Close();
            ComputerWaitReply? result = null;
            while (await child.StandardOutput.ReadLineAsync(deadline.Token) is { } line)
            {
                if (line.Length > 8_000_000) throw new Hermes.Core.HermesException("Change-wait result size limit");
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("progress", out var progress)) Latest = progress.Deserialize<ComputerWaitMetrics>()!;
                else { result = doc.RootElement.Deserialize<ComputerWaitReply>(); if (result != null) Latest = result.Metrics; }
            }
            await child.WaitForExitAsync(deadline.Token); await errors;
            return result ?? throw new Hermes.Core.HermesException("Change-wait worker ended without a result");
        }
        finally { Stop(); child.Dispose(); process = null; }
    }
    internal void Stop() { var child = process; if (child != null) try { if (!child.HasExited) child.Kill(true); } catch (InvalidOperationException) { } }
    public void Dispose() => Stop();
}
