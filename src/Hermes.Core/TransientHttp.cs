namespace Hermes.Core;

// Retry only explicit rejections before any successful response body is consumed.
// Retry a transport exception only when the connection was never established.
internal static class TransientHttp
{
    private static bool BeforeConnection(HttpRequestException error) =>
        error.HttpRequestError == HttpRequestError.NameResolutionError ||
        (error.HttpRequestError == HttpRequestError.ConnectionError &&
         error.InnerException is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused });

    internal static async Task<HttpResponseMessage> Send(HttpClient client,
        Func<HttpRequestMessage> createRequest, Action attempted, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = createRequest();
            attempted();
            HttpResponseMessage response;
            try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (HttpRequestException ex) when (attempt < 2 && BeforeConnection(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), ct);
                continue;
            }
            if (attempt >= 2 || (int)response.StatusCode is not (429 or 503 or 529)) return response;
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(attempt + 1));
            // Respect long Retry-After values by returning the rejection to the engine.
            if (delay > TimeSpan.FromSeconds(30)) return response;
            response.Dispose();
            await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, ct);
        }
    }
}
