using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;

internal static class CompanyTests
{
    internal static async Task Run(string baseDir, Action<bool, string> assert)
    {
        var config = new ProviderSettings { Endpoint = "https://company.invalid/v1", Deployment = "company-deployment", Protocol = ProviderProtocol.GatewayResponses, Authentication = ProviderAuth.ApiKeyHeader };
        assert(config.RequestUri(false).AbsoluteUri == "https://company.invalid/v1/responses", "relay route preserves supplied v1 prefix");
        config.Protocol = ProviderProtocol.GatewayChatCompletions;
        assert(config.RequestUri(false).AbsoluteUri == "https://company.invalid/v1/chat/completions", "relay chat route has no deployment or API version additions");
        void Reject(Action a, string label) { try { a(); } catch (IOException) { assert(true, label); return; } throw new Exception(label); }
        config.Endpoint = "http://company.invalid"; assert(config.Validate().Scheme == "http", "explicit HTTP company route supported without TLS downgrade");
        config.Endpoint = "https://api.openai.com"; Reject(() => config.Validate(), "public OpenAI company fallback endpoint blocked");
        config.Endpoint = "https://company.invalid/?key=unsafe"; Reject(() => config.Validate(), "credential/query in base URL blocked");
        var secretDir = Path.Combine(baseDir, "company-secrets"); var store = new ProtectedKeyStore(secretDir); const string key = "SYNTHETIC_ONLY_company_key_fixture_03";
        store.Save(key); assert(store.HasKey && store.ReadOwnKey() == key, "Windows DPAPI round-trip uses only synthetic test key");
        assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(secretDir, "provider-key.dpapi"))).Contains(key), "protected key file has no plaintext key");
        assert(!Safety.Redact("server echoed " + key).Contains(key), "non-sk company-style key redacted by exact secret registration");
        var state = new AppState(); var stateStore = new StateStore(Path.Combine(baseDir, "usage-history"));
        var at = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
        var row = new UsageEntry { Id = "unique-request", TaskId = "a", At = at, Model = "m", Deployment = "d", Source = "test", Status = "completed", Requests = 1, Tokens = new(100, 30, 40, 10, 130) };
        UsageLedger.Upsert(state, row); UsageLedger.Upsert(state, row); assert(state.Usage.Count == 1, "duplicate response update does not double count request or tokens");
        assert(state.Usage[0].Tokens.Total == 130, "cache/reasoning not added to input/output total");
        UsageLedger.Upsert(state, new() { Id = "missing", TaskId = "b", At = at.AddDays(1), Model = "n", Status = "failed", Requests = 1 });
        assert(UsageLedger.Select(state, at, at.AddDays(1)).Count == 1 && UsageLedger.Select(state, at.AddDays(1), at.AddDays(2), "b", "n").Count == 1, "UTC date interval and task/model filters");
        assert(UsageLedger.Summary(state.Usage).Contains("미제공") && state.Usage[1].Tokens.Output == null, "missing/failed usage remains unknown, not invented zero");
        stateStore.Save(state); var recovered = stateStore.Load(); assert(recovered.Usage.Count == 2 && recovered.Usage[0].Tokens.Input == 100, "usage persists across restart");
        using var invalid = JsonDocument.Parse("{\"usage\":{\"input_tokens\":-1,\"output_tokens\":\"bad\"}}"); assert(TokenCounts.FromResponse(invalid.RootElement).Input == null && TokenCounts.FromResponse(invalid.RootElement).Output == null, "invalid token field rejected without zero fallback");
        var task = new WorkItem { Id = "legacy", ThreadId = "thread", Model = "model", UsageBaselineKnown = true };
        var totals = JsonSerializer.SerializeToElement(new { threadId = "thread", turnId = "turn", tokenUsage = new { total = new { inputTokens = 100, outputTokens = 20, cachedInputTokens = 10, reasoningOutputTokens = 5, totalTokens = 120 } } });
        UsageLedger.RecordThreadUpdate(state, task, totals); UsageLedger.RecordThreadUpdate(state, task, totals); assert(state.Usage.Count == 3 && state.Usage[^1].Requests == null, "AppServer cumulative event dedup and no guessed request count");
        var pendingStore = new StateStore(Path.Combine(baseDir, "pending-usage")); var pendingState = new AppState();
        pendingState.Usage.Add(new() { Id = "unfinished", Status = "started", Requests = 0 }); pendingStore.Save(pendingState);
        var pendingRecovered = pendingStore.Load(); assert(pendingRecovered.Usage.Single().Requests == null && pendingRecovered.Usage.Single().Status == "unknown-after-restart", "unfinished request count remains unknown after crash/restart");
        var output = new JsonObject { ["id"] = "resp_mock", ["object"] = "response", ["status"] = "completed", ["created_at"] = at.ToUnixTimeSeconds(), ["model"] = "server-model", ["output"] = new JsonArray(new JsonObject { ["id"] = "msg_mock", ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "COMPANY_MOCK_OK", ["annotations"] = new JsonArray() }) }), ["usage"] = JsonNode.Parse("{\"input_tokens\":100,\"output_tokens\":30,\"total_tokens\":130,\"input_tokens_details\":{\"cached_tokens\":40},\"output_tokens_details\":{\"reasoning_tokens\":10}}") };
        var observed = new List<UsageEntry>(); int hits = 0; bool authenticated = false; string lastPath = ""; var replyMode = "success";
        var releaseFinal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var mock = new LoopbackHttp(async (request, stream, ct) =>
        {
            Interlocked.Increment(ref hits); lastPath = request.Path; authenticated = request.Headers.TryGetValue("api-key", out var value) && value == key;
            if (replyMode == "redirect") { await LoopbackHttp.Reply(stream, 302, "application/json", "{}", ct, new() { ["Location"] = "https://api.openai.com/v1/responses" }); return; }
            if (replyMode == "fail") { await LoopbackHttp.Reply(stream, 401, "application/json", JsonSerializer.Serialize(new { error = new { message = key } }), ct); return; }
            if (replyMode == "failed-response") { var failedResponse = (JsonObject)output.DeepClone(); failedResponse["status"] = "failed"; await LoopbackHttp.Reply(stream, 200, "text/event-stream", ResponseWire.Sse(failedResponse), ct); return; }
            if (replyMode == "missing-final") { await LoopbackHttp.Reply(stream, 200, "text/event-stream", "data: {\"type\":\"response.created\"}\n\n", ct); return; }
            if (replyMode == "delayed-stream")
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"FIRST_CHUNK\"}\n\n"), ct);
                await stream.FlushAsync(ct); await releaseFinal.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(ResponseWire.Sse(output)), ct); await stream.FlushAsync(ct); return;
            }
            if (request.Method == "GET") { await LoopbackHttp.Reply(stream, 200, "application/json", "{\"data\":[]}", ct); return; }
            using var body = JsonDocument.Parse(request.Body); assert(body.RootElement.Text("model") == "company-deployment", "configured relay deployment reaches company request");
            await LoopbackHttp.Reply(stream, 200, "text/event-stream", ResponseWire.Sse(output), ct);
        });
        var settings = new ProviderSettings { Endpoint = mock.BaseUri.AbsoluteUri, Deployment = "company-deployment", Protocol = ProviderProtocol.GatewayResponses, Authentication = ProviderAuth.ApiKeyHeader };
        assert(settings.Validate().Scheme == "http", "explicit loopback HTTP uses the normal validation path");
        await using var bridge = new CompanyBridge(settings, store, true); bridge.Usage += e => { var copy = JsonSerializer.Deserialize<UsageEntry>(JsonSerializer.Serialize(e))!; lock (observed) { var index = observed.FindIndex(x => x.Id == copy.Id); if (index < 0) observed.Add(copy); else observed[index] = copy; } };
        var checkResult = await bridge.CheckConnectionAsync(); Console.WriteLine("MOCK_CHECK: " + checkResult + "; hits=" + hits + "; headerMatched=" + authenticated);
        assert(checkResult.Contains("models") && authenticated, "mock connection check sends owned key only to configured server");
        bridge.BeginTask("company-task"); bridge.SetTurn("turn-company");
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
        async Task<string> Send(bool authorized = true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, bridge.BaseUri.AbsoluteUri + "/responses"); if (authorized) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bridge.Capability); request.Content = new StringContent("{\"model\":\"not-used\",\"input\":\"mock prompt\",\"stream\":true,\"tools\":[]}", Encoding.UTF8, "application/json"); using var reply = await client.SendAsync(request); return await reply.Content.ReadAsStringAsync();
        }
        var text = await Send(); assert(text.Contains("COMPANY_MOCK_OK") && lastPath == "/responses", "relay mock streaming contract forwarded to local App Server route");
        assert(observed[^1].Requests == 1 && observed[^1].Tokens.Cached == 40 && observed[^1].Tokens.Reasoning == 10 && observed[^1].Tokens.Total == 130, "actual response fields and request count captured");
        var beforeHits = hits; await Send(false); assert(hits == beforeHits, "unauthorized local bridge call never reaches company endpoint");
        replyMode = "redirect"; beforeHits = hits; var redirect = await Send(); assert(hits == beforeHits + 1 && redirect.Contains("no fallback"), "redirect does not forward data/key to public OpenAI");
        replyMode = "fail"; var failed = await Send(); assert(!failed.Contains(key) && observed[^1].Status == "failed" && observed[^1].Tokens.Input == null, "upstream failure does not leak key or invent usage");
        replyMode = "failed-response"; await Send(); assert(observed[^1].Status == "failed" && observed[^1].Tokens.Total == 130, "HTTP 200 failed Responses status and supplied usage retained");
        replyMode = "missing-final"; await Send(); assert(observed[^1].Status == "unknown" && observed[^1].Tokens.Total == null, "unfinished SSE stays unknown with no invented usage");
        replyMode = "delayed-stream";
        using (var streamingRequest = new HttpRequestMessage(HttpMethod.Post, bridge.BaseUri.AbsoluteUri + "/responses"))
        {
            streamingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bridge.Capability);
            streamingRequest.Content = new StringContent("{\"model\":\"unused\",\"input\":\"stream fixture\",\"stream\":true}", Encoding.UTF8, "application/json");
            using var streamingReply = await client.SendAsync(streamingRequest, HttpCompletionOption.ResponseHeadersRead);
            using var reader = new StreamReader(await streamingReply.Content.ReadAsStreamAsync());
            var firstLine = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            assert(firstLine?.Contains("FIRST_CHUNK") == true && !releaseFinal.Task.IsCompleted, "Responses SSE first chunk reaches client before final response");
            releaseFinal.TrySetResult(); var remaining = await reader.ReadToEndAsync();
            assert(remaining.Contains("response.completed") && observed[^1].Tokens.Total == 130, "stream completion retains final actual usage");
        }
        var adaptation = ChatAdapter.Request(JsonNode.Parse("{\"model\":\"deployment\",\"input\":[{\"type\":\"message\",\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"hello\"}]}],\"tools\":[{\"type\":\"namespace\",\"name\":\"functions\",\"tools\":[{\"type\":\"function\",\"name\":\"read\",\"parameters\":{\"type\":\"object\"}}]}]}")!.AsObject());
        using var chat = JsonDocument.Parse("{\"id\":\"chat-mock\",\"model\":\"server-model\",\"choices\":[{\"message\":{\"content\":\"reply\",\"tool_calls\":[{\"id\":\"call1\",\"function\":{\"name\":\"functions__read\",\"arguments\":\"{}\"}}]}}],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":3,\"total_tokens\":8}}");
        var converted = ChatAdapter.Response(chat.RootElement, adaptation); assert(converted["output"]![1]!["namespace"]!.GetValue<string>() == "functions" && TokenCounts.FromResponse(JsonSerializer.SerializeToElement(converted)).Total == 8, "Chat Completions namespace tools and usage adapt to Responses");
        store.DeleteOwnKey(); assert(!store.HasKey, "only synthetic app-owned protected key deleted");
        assert(!JsonSerializer.Serialize(settings).Contains(key) && !JsonSerializer.Serialize(observed).Contains(key), "settings/usage export never serializes plaintext key");
    }
}
