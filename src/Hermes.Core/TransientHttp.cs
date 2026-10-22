namespace Hermes.Core;

// Retry only explicit rejections before any successful response body is consumed.
// Never replay network failures or an interrupted generation.
internal static class TransientHttp
{
    internal static async Task<HttpResponseMessage> Send(HttpClient client,
        Func<HttpRequestMessage> createRequest, Action attempted, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = createRequest();
            attempted();
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
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
