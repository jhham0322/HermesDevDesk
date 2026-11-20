using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;
internal static class ResponseWaitingTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        check(ConnectionErrors.Describe(new HttpRequestException("NETWORK_ORIGINAL")) == "NETWORK_ORIGINAL", "network error message is unchanged");
        check(ConnectionErrors.Describe(new ModelServerException("MODEL_ORIGINAL")) == "MODEL_ORIGINAL", "model error message is unchanged");
        check(ConnectionErrors.Describe(new HermesException("local validation")) == "[HERMES_ERROR] local validation", "local errors have exactly one HERMES_ERROR marker");
        check(ConnectionErrors.Describe(new System.Text.Json.JsonException("bad shape")).StartsWith("[HERMES_ERROR]"), "adapter parsing errors identify Hermes origin");
        using var wire = new MemoryStream();
        await using (var alive = new ModelResponseStream(wire, CancellationToken.None, TimeSpan.FromMilliseconds(10)))
        { await alive.StartAsync(CancellationToken.None); await Task.Delay(65); }
        var events = Encoding.UTF8.GetString(wire.ToArray());
        check(events.Split("event: response.in_progress").Length >= 3 && !events.Contains("output_text") && !events.Contains("completed"), "waiting stream sends repeated progress without fabricated model answer or completion");
        var length = wire.Length; await Task.Delay(35); check(wire.Length == length, "heartbeat stops when request ends");
        var adaptation = ChatAdapter.Request(new JsonObject { ["model"] = "pause-test", ["input"] = "hello" });
        await using var paused = new PausedStream(); using var output = new MemoryStream();
        using var cancel = new CancellationTokenSource();
        var waiting = ChatStream.Translate(paused, output, adaptation, cancel.Token);
        await Task.Delay(60); check(!waiting.IsCompleted, "silent model stream remains pending until cancellation");
        cancel.Cancel();
        try { await waiting.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("cancel was ignored"); }
        catch (OperationCanceledException) { check(true, "manual cancellation interrupts silent model stream"); }
        var raw = "{\"error\":{\"message\":\"UPSTREAM_ORIGINAL\",\"code\":\"upstream_code\"}}";
        await using var server = new LoopbackHttp((_, stream, ct) => LoopbackHttp.Reply(stream, 502, "application/json", raw, ct));
        var settings = new ProviderSettings { Endpoint = server.BaseUri.AbsoluteUri, Deployment = "error-source", Authentication = ProviderAuth.None, Protocol = ProviderProtocol.GatewayResponses, UseAdvancedSettings = true };
        await using var bridge = new CompanyBridge(settings, new ProtectedKeyStore(Path.Combine(root, "error-origin-keys")), true);
        using var client = new HttpClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bridge.Capability);
        using var response = await client.PostAsync(bridge.BaseUri.AbsoluteUri + "/responses", new StringContent("{\"input\":\"test\"}"));
        check(response.StatusCode == HttpStatusCode.BadGateway && await response.Content.ReadAsStringAsync() == raw, "upstream HTTP 502 status and complete original error body pass through unchanged");
        check(ProviderErrors.ReadableMessage(raw) == "UPSTREAM_ORIGINAL", "display does not append Hermes advice or code labels to upstream message");
        await using var capServer = new LoopbackHttp(async (request, stream, ct) =>
        {
            using var sent = JsonDocument.Parse(request.Body);
            var cap = sent.RootElement.Property("max_tokens") ?? sent.RootElement.Property("max_output_tokens");
            await LoopbackHttp.Reply(stream, 400, "text/plain", cap?.ToString() ?? "provider-default", ct);
        });
        foreach (var protocol in new[] { ProviderProtocol.GatewayResponses, ProviderProtocol.GatewayChatCompletions, ProviderProtocol.AnthropicMessages })
        {
            var capSettings = new ProviderSettings { Endpoint = capServer.BaseUri.AbsoluteUri, Deployment = "cap-fixture",
                Authentication = ProviderAuth.None, Protocol = protocol, MaxOutputTokens = 4096 };
            await using var capBridge = new CompanyBridge(capSettings, new ProtectedKeyStore(Path.Combine(root, "cap-keys")), true);
            UsageEntry? observedCap = null;
            capBridge.Usage += entry => observedCap = entry;
            async Task<string> Forward(string body)
            {
                using var post = new HttpRequestMessage(HttpMethod.Post, capBridge.BaseUri.AbsoluteUri + "/responses") { Content = new StringContent(body) };
                post.Headers.Authorization = new AuthenticationHeaderValue("Bearer", capBridge.Capability);
                using var reply = await client.SendAsync(post);
                return await reply.Content.ReadAsStringAsync();
            }
            check(await Forward("{\"input\":\"fixture\",\"max_output_tokens\":16}") == "4096" &&
                  observedCap?.RequestedOutputTokens == 4096, protocol + " uses the selected model connection budget, not a low engine hint");
            if (protocol == ProviderProtocol.GatewayResponses)
                check(await Forward("{\"input\":\"fixture\"}") == "provider-default" &&
                      observedCap?.RequestedOutputTokens == null, "Responses preserves provider default when the engine sends no output cap");
        }
        using var embedded = JsonDocument.Parse(raw);
        try { ChatAdapter.Response(embedded.RootElement, adaptation); throw new Exception("model error was lost"); }
        catch (ModelServerException ex) { check(ex.Message == "UPSTREAM_ORIGINAL", "HTTP 200 model error is preserved before Chat response conversion"); }
        using var rejected = await client.PostAsync(bridge.TaskBaseUri("expired").AbsoluteUri + "/responses", new StringContent("{}"));
        using var localError = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        check(localError.RootElement.GetProperty("error").Text("message").StartsWith("[HERMES_ERROR]"), "expired local session error is marked Hermes");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var silent = new LoopbackHttp(async (_, _, ct) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); });
        settings.Endpoint = silent.BaseUri.AbsoluteUri;
        await using var pendingBridge = new CompanyBridge(settings, new ProtectedKeyStore(Path.Combine(root, "cancel-keys")), true);
        pendingBridge.BeginTask("waiting");
        using var pendingRequest = new HttpRequestMessage(HttpMethod.Post, pendingBridge.TaskBaseUri("waiting").AbsoluteUri + "/responses") { Content = new StringContent("{\"input\":\"wait\"}") };
        pendingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pendingBridge.Capability);
        var pending = client.SendAsync(pendingRequest);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); pendingBridge.CancelTask("waiting");
        using var cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(3));
        var cancellationText = await cancelled.Content.ReadAsStringAsync();
        check((int)cancelled.StatusCode == 499 && cancellationText.Contains("[HERMES_ERROR]") && !cancellationText.Contains("초과"), "manual stop cancels an unanswered upstream request without inventing a timeout");
        await using var networkBridge = new CompanyBridge(settings, new ProtectedKeyStore(Path.Combine(root, "network-keys")), true, new NetworkFailure());
        UsageEntry? failure = null; networkBridge.Usage += entry => failure = entry;
        using var networkRequest = new HttpRequestMessage(HttpMethod.Post, networkBridge.BaseUri.AbsoluteUri + "/responses") { Content = new StringContent("{\"input\":\"network\"}") };
        networkRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", networkBridge.Capability);
        using var networkReply = await client.SendAsync(networkRequest);
        using var networkBody = JsonDocument.Parse(await networkReply.Content.ReadAsStringAsync());
        var networkError = networkBody.RootElement.GetProperty("error");
        check(networkReply.StatusCode == HttpStatusCode.BadGateway &&
              networkError.Text("type") == "network_error" && networkError.Text("message").Contains("NETWORK_ORIGINAL") &&
              failure?.FailureKind == "response_upstream_transport",
              "bridge distinguishes transport failure from upstream 503 and retains its diagnostic");
    }
    private sealed class NetworkFailure : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new HttpRequestException("NETWORK_ORIGINAL");
    }
    private sealed class PausedStream : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { await Task.Delay(Timeout.Infinite, ct); return 0; }
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
