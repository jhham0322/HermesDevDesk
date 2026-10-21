using System.Text;
namespace Hermes.Core;

// Keep the local engine stream active while the actual model is thinking.
// Progress events carry no answer text, token usage or completion claims.
public sealed class ModelResponseStream : Stream
{
    private readonly Stream destination;
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly CancellationTokenSource stop;
    private readonly TimeSpan interval;
    private Task? heartbeat;
    public ModelResponseStream(Stream destination, CancellationToken ct, TimeSpan? interval = null)
    { this.destination = destination; stop = CancellationTokenSource.CreateLinkedTokenSource(ct); this.interval = interval ?? TimeSpan.FromSeconds(15); }
    public async Task StartAsync(CancellationToken ct)
    {
        await WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n"), ct);
        await FlushAsync(ct);
        await Progress(ct);
        heartbeat = KeepAlive();
    }
    private async Task Progress(CancellationToken ct)
    { await WriteAsync(Encoding.UTF8.GetBytes("event: response.in_progress\ndata: {\"type\":\"response.in_progress\"}\n\n"), ct); await FlushAsync(ct); }
    private async Task KeepAlive()
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                await Progress(stop.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    { await writer.WaitAsync(ct); try { await destination.WriteAsync(buffer, ct); } finally { writer.Release(); } }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override async Task FlushAsync(CancellationToken ct)
    { await writer.WaitAsync(ct); try { await destination.FlushAsync(ct); } finally { writer.Release(); } }
    public override async ValueTask DisposeAsync()
    { stop.Cancel(); if (heartbeat != null) await heartbeat; stop.Dispose(); writer.Dispose(); GC.SuppressFinalize(this); }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
