using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hermes.Core;
public sealed class CompanyBridge : IAsyncDisposable
{
    private readonly ProviderSettings settings;
    private readonly ProtectedKeyStore keys;
    private readonly bool testLoopback;
    private readonly HttpClient http;
    private readonly LoopbackHttp server;
    private readonly string capability = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private CancellationTokenSource operation = new();
    private string taskId = "connection-check";
    private string turnId = "";
    public event Action<UsageEntry>? Usage;
    public string Capability => capability;
    public Uri BaseUri => new(server.BaseUri.AbsoluteUri.TrimEnd('/') + "/v1");
    public CompanyBridge(ProviderSettings settings, ProtectedKeyStore keys, bool allowLoopbackForTests = false, HttpMessageHandler? handler = null)
    {
        this.settings = settings; this.keys = keys; testLoopback = allowLoopbackForTests; settings.Validate(testLoopback);
        http = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { Timeout = TimeSpan.FromMinutes(4) };
        server = new(Handle);
    }
    public void BeginTask(string id) { operation.Dispose(); operation = new(); taskId = id; turnId = ""; }
    public void SetTurn(string id) => turnId = id;
    public void CancelCurrent() => operation.Cancel();
    private HttpRequestMessage Request(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        if (settings.Authentication != ProviderAuth.None)
        {
            var key = keys.ReadOwnKey();
            if (settings.Authentication == ProviderAuth.ApiKeyHeader) request.Headers.TryAddWithoutValidation("api-key", key);
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
        return request;
    }
    public async Task<string> CheckConnectionAsync(CancellationToken ct = default)
    {
        var entry = StartEntry();
        try
        {
            using var request = Request(HttpMethod.Get, settings.RequestUri(true, testLoopback)); entry.Requests = 1; using var response = await http.SendAsync(request, ct); entry.HttpStatus = (int)response.StatusCode;
            entry.Status = response.IsSuccessStatusCode ? "completed" : "failed"; entry.Note = "GET models 검사: 생성 요청/토큰 사용량 없음. 배포별 추론 가능 여부는 보장하지 않음"; Emit(entry);
            if ((int)response.StatusCode is >= 300 and < 400) return "Redirect 차단. 다른 서버로 전송하지 않았습니다.";
            return response.IsSuccessStatusCode ? "설정된 서버의 models 경로 응답 확인. 실제 추론은 아직 확인하지 않았습니다." : $"서버 응답 HTTP {(int)response.StatusCode}. 이 경로 미지원 또는 인증/권한 문제일 수 있습니다. 외부 fallback 없음.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { entry.Status = "failed"; entry.Note = "연결/TLS/키 저장 상태 확인 필요; 서버가 usage를 제공하지 않음"; Emit(entry); return entry.Note; }
    }
    private void Emit(UsageEntry entry) => Usage?.Invoke(JsonSerializer.Deserialize<UsageEntry>(JsonSerializer.Serialize(entry))!);
    private UsageEntry StartEntry()
    {
        var entry = new UsageEntry { Id = Guid.NewGuid().ToString("N"), At = DateTimeOffset.UtcNow, TaskId = taskId, TurnId = turnId, Deployment = settings.Deployment, Model = settings.Deployment, Source = "company-api-response", Requests = null };
        Emit(entry); return entry;
    }
    private async Task Handle(LoopRequest incoming, NetworkStream stream, CancellationToken ct)
    {
        if (!incoming.Headers.TryGetValue("Authorization", out var auth) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(auth), Encoding.UTF8.GetBytes("Bearer " + capability))) { await LoopbackHttp.Reply(stream, 403, "application/json", "{\"error\":{\"message\":\"Local capability required\"}}", ct); return; }
        if (incoming.Method != "POST" || incoming.Path.Split('?')[0] != "/v1/responses") { await LoopbackHttp.Reply(stream, 404, "application/json", "{\"error\":{\"message\":\"Unsupported local route\"}}", ct); return; }
        var entry = StartEntry(); using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, operation.Token);
        try
        {
            if (incoming.Headers.ContainsKey("Content-Encoding")) throw new IOException("Compressed request body not supported by the local adapter");
            var body = JsonNode.Parse(incoming.Body)?.AsObject() ?? throw new IOException("Invalid request JSON"); body["model"] = settings.Deployment; body["store"] = false;
            var wantsStream = body["stream"]?.GetValue<bool>() == true;
            ChatAdaptation? adaptation = settings.IsChat ? ChatAdapter.Request(body) : null;
            using var request = Request(HttpMethod.Post, settings.RequestUri(false, testLoopback));
            request.Content = new StringContent((adaptation?.Body ?? body).ToJsonString(), Encoding.UTF8, "application/json");
            entry.Requests = 1; using var response = await http.SendAsync(request, linked.Token); entry.HttpStatus = (int)response.StatusCode;
            var text = Safety.Redact(await response.Content.ReadAsStringAsync(linked.Token));
            if (!response.IsSuccessStatusCode)
            {
                entry.Status = "failed"; entry.Note = $"HTTP {(int)response.StatusCode}; no fallback; usage not provided"; Emit(entry);
                await LoopbackHttp.Reply(stream, (int)response.StatusCode is >= 300 and < 400 ? 502 : (int)response.StatusCode, "application/json", JsonSerializer.Serialize(new { error = new { message = entry.Note, type = "company_endpoint_error" } }), ct); return;
            }
            string output; string contentType; string finalStatus = "completed";
            if (adaptation != null)
            {
                using var chat = JsonDocument.Parse(text); entry.Tokens = TokenCounts.FromResponse(chat.RootElement); entry.Model = chat.RootElement.Text("model") is { Length: > 0 } model ? model : settings.Deployment;
                var converted = ChatAdapter.Response(chat.RootElement, adaptation); output = wantsStream ? ResponseWire.Sse(converted) : converted.ToJsonString(); contentType = wantsStream ? "text/event-stream" : "application/json";
            }
            else if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                JsonElement? final = ResponseWire.LastResponse(text);
                entry.Tokens = final.HasValue ? TokenCounts.FromResponse(final.Value) : TokenCounts.Missing; entry.Model = final?.Text("model") is { Length: > 0 } model ? model : settings.Deployment;
                finalStatus = final?.Text("status") ?? "unknown";
                output = text; contentType = "text/event-stream";
            }
            else
            {
                using var parsed = JsonDocument.Parse(text); entry.Tokens = TokenCounts.FromResponse(parsed.RootElement); entry.Model = parsed.RootElement.Text("model") is { Length: > 0 } model ? model : settings.Deployment;
                finalStatus = parsed.RootElement.Text("status") is { Length: > 0 } status ? status : "unknown";
                var node = JsonNode.Parse(text)!.AsObject(); output = wantsStream ? ResponseWire.Sse(node) : text; contentType = wantsStream ? "text/event-stream" : "application/json";
            }
            entry.Status = finalStatus; entry.Note = "응답 usage 관측. reasoning/cache는 부분집합, 별도 합산 안 함"; Emit(entry);
            await LoopbackHttp.Reply(stream, 200, contentType, output, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or JsonException or InvalidOperationException)
        {
            entry.Status = ex is OperationCanceledException ? "cancelled" : "failed"; entry.Note = "요청 실패/중단 또는 호환 불가; usage 미제공; 외부 fallback 없음"; Emit(entry);
            if (!ct.IsCancellationRequested) await LoopbackHttp.Reply(stream, 502, "application/json", JsonSerializer.Serialize(new { error = new { message = entry.Note, type = "company_adapter_error" } }), ct);
        }
    }
    public async ValueTask DisposeAsync() { operation.Cancel(); await server.DisposeAsync(); http.Dispose(); operation.Dispose(); }
}
public static class ResponseWire
{
    public static JsonElement? LastResponse(string sse)
    {
        JsonElement? result = null;
        foreach (var line in sse.Split('\n').Where(l => l.StartsWith("data: ", StringComparison.Ordinal)))
        {
            try { using var doc = JsonDocument.Parse(line[6..]); if (doc.RootElement.Text("type") is "response.completed" or "response.failed" or "response.incomplete" && doc.RootElement.Property("response") is { } response) result = response.Clone(); } catch (JsonException) { }
        }
        return result;
    }
    public static string Sse(JsonObject response)
    {
        var result = new StringBuilder(); var sequence = 0;
        void Event(string type, JsonObject fields) { fields["type"] = type; fields["sequence_number"] = sequence++; result.Append("event: ").Append(type).Append("\ndata: ").Append(fields.ToJsonString()).Append("\n\n"); }
        var start = (JsonObject)response.DeepClone(); start["output"] = new JsonArray(); start["status"] = "in_progress";
        Event("response.created", new() { ["response"] = start });
        var output = response["output"]?.AsArray() ?? new();
        for (int i = 0; i < output.Count; i++)
        {
            var item = output[i]!.AsObject(); var begun = (JsonObject)item.DeepClone(); var type = item["type"]?.GetValue<string>();
            if (type == "message") begun["content"] = new JsonArray();
            Event("response.output_item.added", new() { ["output_index"] = i, ["item"] = begun });
            if (type == "message")
            {
                var text = item["content"]?[0]?["text"]?.GetValue<string>() ?? "";
                Event("response.content_part.added", new() { ["item_id"] = item["id"]!.DeepClone(), ["output_index"] = i, ["content_index"] = 0, ["part"] = new JsonObject { ["type"] = "output_text", ["text"] = "", ["annotations"] = new JsonArray() } });
                Event("response.output_text.delta", new() { ["item_id"] = item["id"]!.DeepClone(), ["output_index"] = i, ["content_index"] = 0, ["delta"] = text });
                Event("response.output_text.done", new() { ["item_id"] = item["id"]!.DeepClone(), ["output_index"] = i, ["content_index"] = 0, ["text"] = text });
            }
            Event("response.output_item.done", new() { ["output_index"] = i, ["item"] = item.DeepClone() });
        }
        var finalEvent = response["status"]?.GetValue<string>() switch { "failed" => "response.failed", "incomplete" => "response.incomplete", _ => "response.completed" };
        Event(finalEvent, new() { ["response"] = response.DeepClone() }); return result.ToString();
    }
}
