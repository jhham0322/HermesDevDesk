using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Hermes.Core;
public sealed record LoopRequest(string Method, string Path, Dictionary<string, string> Headers, byte[] Body);
public sealed class LoopbackRequestException(int statusCode, string message) : IOException(message)
{ public int StatusCode { get; } = statusCode; }
public sealed class LoopbackHttp : IAsyncDisposable
{
    public const int MaxBodyBytes = 96 * 1024 * 1024;
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Func<LoopRequest, NetworkStream, CancellationToken, Task> handler;
    public Uri BaseUri { get; private set; } = null!;
    public LoopbackHttp(Func<LoopRequest, NetworkStream, CancellationToken, Task> handler) { this.handler = handler; listener.Start(); BaseUri = new($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}"); _ = Accept(); }
    private async Task Accept()
    {
        try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); _ = Serve(client); } }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream(); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
            try { var request = await Read(stream, timeout.Token); timeout.CancelAfter(Timeout.InfiniteTimeSpan); await handler(request, stream, timeout.Token); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or FormatException or InvalidOperationException or OverflowException)
            {
                if (!timeout.IsCancellationRequested)
                {
                    var status = ex is LoopbackRequestException requestError ? requestError.StatusCode : 400;
                    var message = ConnectionErrors.Describe(ex);
                    try { await Reply(stream, status, "application/json", JsonSerializer.Serialize(new { error = new { message, type = "local_bridge_error" } }), timeout.Token); }
                    catch (Exception sendError) when (sendError is IOException or OperationCanceledException or ObjectDisposedException) { }
                }
            }
        }
    }
    public static async Task<LoopRequest> Read(Stream stream, CancellationToken ct)
    {
        async Task<string> Line()
        {
            using var bytes = new MemoryStream(); var single = new byte[1];
            while (await stream.ReadAsync(single, ct) == 1) { if (single[0] == '\n') return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r'); bytes.WriteByte(single[0]); if (bytes.Length > 65536) throw new HermesException("HTTP header limit"); }
            throw new HermesException("HTTP header incomplete");
        }
        var first = (await Line()).Split(' ', 3); if (first.Length != 3) throw new HermesException("HTTP request line invalid");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; ; i++) { if (i > 100) throw new HermesException("HTTP header count limit"); var line = await Line(); if (line.Length == 0) break; var parts = line.Split(':', 2); if (parts.Length != 2) throw new HermesException("HTTP header invalid"); headers[parts[0].Trim()] = parts[1].Trim(); }
        using var body = new MemoryStream();
        if (headers.TryGetValue("Transfer-Encoding", out var transfer) && transfer.Equals("chunked", StringComparison.OrdinalIgnoreCase))
        {
            while (true) { var size = Convert.ToInt32((await Line()).Split(';')[0], 16); if (size == 0) { while ((await Line()).Length > 0) { } break; } await Copy(size); if ((await Line()).Length > 0) throw new HermesException("HTTP chunk invalid"); }
        }
        else if (headers.TryGetValue("Content-Length", out var length)) await Copy(int.Parse(length, System.Globalization.CultureInfo.InvariantCulture));
        async Task Copy(int size)
        {
            if (size < 0) throw new LoopbackRequestException(400, "로컬 API 요청 크기가 유효하지 않습니다.");
            if (body.Length + size > MaxBodyBytes) throw new LoopbackRequestException(413, "로컬 API 요청이 96MB를 초과했습니다. 첨부를 줄이거나 새 대화에서 다시 시도하세요."); var block = new byte[Math.Min(65536, Math.Max(1, size))];
            while (size > 0) { var n = await stream.ReadAsync(block.AsMemory(0, Math.Min(size, block.Length)), ct); if (n == 0) throw new HermesException("HTTP body incomplete"); await body.WriteAsync(block.AsMemory(0, n), ct); size -= n; }
        }
        return new(first[0], first[1], headers, body.ToArray());
    }
    public static async Task Reply(Stream stream, int status, string type, string body, CancellationToken ct, Dictionary<string, string>? headers = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body); var extra = headers == null ? "" : string.Join("", headers.Select(h => h.Key + ": " + h.Value + "\r\n"));
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Result\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n{extra}\r\n");
        await stream.WriteAsync(head, ct); await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
    }
    public ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); stop.Dispose(); return ValueTask.CompletedTask; }
}
