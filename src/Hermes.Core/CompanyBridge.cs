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
    private string activeModel = "";
    private sealed class TaskContext(string model)
    {
        public string Model { get; } = model;
        public string TurnId { get; set; } = "";
        public CancellationTokenSource Operation { get; } = new();
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskContext> tasks = new();
    public Uri TaskBaseUri(string id) => new(server.BaseUri.AbsoluteUri.TrimEnd('/') + "/tasks/" + Uri.EscapeDataString(id) + "/v1");
    public void SetTurn(string id, string turn) { if (tasks.TryGetValue(id, out var context)) context.TurnId = turn; }
    public void CancelTask(string id) { if (tasks.TryGetValue(id, out var context)) context.Operation.Cancel(); }
    private readonly HashSet<string> availableModels = new(StringComparer.Ordinal);
    public List<string> DiscoveredModels { get; } = [];
    public int? LastCheckHttpStatus { get; private set; }
    public bool LastCheckSucceeded { get; private set; }
    public event Action<UsageEntry>? Usage;
    public string Capability => capability;
    public Uri BaseUri => new(server.BaseUri.AbsoluteUri.TrimEnd('/') + "/v1");
    public CompanyBridge(ProviderSettings settings, ProtectedKeyStore keys, bool allowLoopbackForTests = false, HttpMessageHandler? handler = null)
    {
        this.settings = settings; this.keys = keys; activeModel = settings.Deployment; availableModels.Add(settings.Deployment); testLoopback = allowLoopbackForTests; settings.Validate(testLoopback);
        http = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { Timeout = TimeSpan.FromMinutes(4) };
        server = new(Handle);
    }
    public void SetAvailableModels(IEnumerable<string> models) { availableModels.Clear(); availableModels.Add(settings.Deployment); foreach (var model in models) availableModels.Add(model); }
    public void BeginTask(string id, string? model = null) { if (model != null) { if (!availableModels.Contains(model)) throw new IOException("설정/서버 목록에 없는 모델입니다."); activeModel = model; } operation.Dispose(); operation = new(); taskId = id; turnId = "";
        if (tasks.TryRemove(id, out var previous)) { previous.Operation.Cancel(); previous.Operation.Dispose(); }
        tasks[id] = new(activeModel); }
    public void SetTurn(string id) => turnId = id;
    public void CancelCurrent() => operation.Cancel();
    private HttpRequestMessage Request(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        if (settings.IsAnthropic) request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        if (settings.Authentication != ProviderAuth.None)
        {
            var key = keys.ReadOwnKey();
            if (settings.Authentication == ProviderAuth.ApiKeyHeader) request.Headers.TryAddWithoutValidation(settings.IsAnthropic ? "x-api-key" : "api-key", key);
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
        return request;
    }
    public async Task<string> CheckConnectionAsync(CancellationToken ct = default)
    {
        var entry = StartEntry(); LastCheckSucceeded = false; LastCheckHttpStatus = null; DiscoveredModels.Clear();
        try
        {
            using var request = Request(settings.IsAnthropic ? HttpMethod.Post : HttpMethod.Get, settings.RequestUri(true, testLoopback));
            if (settings.IsAnthropic) request.Content = new StringContent(JsonSerializer.Serialize(new { model = settings.Deployment, max_tokens = 1, stream = false, messages = new[] { new { role = "user", content = "Reply OK." } } }), Encoding.UTF8, "application/json");
            entry.Requests = 1; using var response = await http.SendAsync(request, ct); entry.HttpStatus = (int)response.StatusCode; LastCheckHttpStatus = entry.HttpStatus;
            entry.Status = response.IsSuccessStatusCode ? "completed" : "failed"; entry.Note = settings.IsAnthropic ? "Anthropic messages 최소 생성 검사 (최대 출력 1토큰)" : "GET models 검사: 생성 요청/토큰 사용량 없음. 배포별 추론 가능 여부는 보장하지 않음"; Emit(entry);
            if (settings.IsAnthropic && response.IsSuccessStatusCode)
            {
                var text = Safety.Redact(await response.Content.ReadAsStringAsync(ct));
                if (text.Length > 2_000_000) throw new IOException("Connection response size limit");
                using var doc = JsonDocument.Parse(text); var empty = new AnthropicAdaptation(new(), new());
                var converted = AnthropicAdapter.Response(doc.RootElement, empty); using var usage = JsonDocument.Parse(converted.ToJsonString());
                entry.Tokens = TokenCounts.FromResponse(usage.RootElement); entry.Note = "Anthropic POST messages · max_tokens=1 최소 생성 검사"; Emit(entry);
                LastCheckSucceeded = true;
                return "설정 모델 " + settings.Deployment + " · Anthropic messages 응답 확인 (최대 출력 1토큰 검사)";
            }
            if (!settings.IsAnthropic && response.IsSuccessStatusCode)
            {
                using var list = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (list.RootElement.Property("data") is { ValueKind: JsonValueKind.Array } models) foreach (var item in models.EnumerateArray().Take(200))
                {
                    var id = item.Text("id"); if (id.Length is > 0 and <= 200 && !id.Any(char.IsControl) && !DiscoveredModels.Contains(id)) DiscoveredModels.Add(id);
                }
                LastCheckSucceeded = true;
            }
            if ((int)response.StatusCode is >= 300 and < 400) return "Redirect 차단. 다른 서버로 전송하지 않았습니다.";
            return response.IsSuccessStatusCode ? "설정된 서버의 models 경로 응답 확인. 실제 추론은 아직 확인하지 않았습니다." : $"서버 응답 HTTP {(int)response.StatusCode}. 요청 경로: {settings.RequestUri(true, testLoopback).AbsolutePath}. 경로/모델/권한을 확인하세요. 모델 목록 경로 404만으로 추론 실패를 판단하지 않습니다. 외부 fallback 없음.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or InvalidOperationException) { entry.Status = "failed"; entry.Note = ConnectionErrors.Describe(ex, ct) + " · 요청 경로: " + settings.RequestUri(true, testLoopback).AbsolutePath; if (entry.Requests == null) entry.Requests = 0; Emit(entry); return entry.Note; }
    }
    private void Emit(UsageEntry entry)
    {
        if (tasks.TryGetValue(entry.TaskId, out var context) && context.TurnId.Length > 0) entry.TurnId = context.TurnId;
        Usage?.Invoke(JsonSerializer.Deserialize<UsageEntry>(JsonSerializer.Serialize(entry))!);
    }
    private UsageEntry StartEntry(string? id = null, TaskContext? context = null)
    {
        var entry = new UsageEntry { Id = Guid.NewGuid().ToString("N"), At = DateTimeOffset.UtcNow, TaskId = id ?? taskId, TurnId = context?.TurnId ?? turnId, Deployment = context?.Model ?? activeModel, Model = context?.Model ?? activeModel, Source = "company-api-response", Requests = null };
        Emit(entry); return entry;
    }
    private async Task Handle(LoopRequest incoming, NetworkStream stream, CancellationToken ct)
    {
        if (!incoming.Headers.TryGetValue("Authorization", out var auth) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(auth), Encoding.UTF8.GetBytes("Bearer " + capability))) { await LoopbackHttp.Reply(stream, 403, "application/json", "{\"error\":{\"message\":\"Local capability required\"}}", ct); return; }
        var route = incoming.Path.Split('?')[0];
        var segments = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? requestTask = null; TaskContext? context = null;
        if (segments.Length == 4 && segments[0] == "tasks" && segments[2] == "v1" && segments[3] == "responses")
        { requestTask = Uri.UnescapeDataString(segments[1]); if (tasks.TryGetValue(requestTask, out context)) route = "/v1/responses"; }
        if (incoming.Method != "POST" || route != "/v1/responses") { await LoopbackHttp.Reply(stream, 404, "application/json", "{\"error\":{\"message\":\"Unsupported local route\"}}", ct); return; }
        var entry = StartEntry(requestTask, context); var streaming = false; using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, context?.Operation.Token ?? operation.Token);
        try
        {
            if (incoming.Headers.ContainsKey("Content-Encoding")) throw new IOException("Compressed request body not supported by the local adapter");
            var body = JsonNode.Parse(incoming.Body)?.AsObject() ?? throw new IOException("Invalid request JSON"); body["model"] = context?.Model ?? activeModel; body["store"] = false;
            var wantsStream = body["stream"]?.GetValue<bool>() == true;
            ChatAdaptation? adaptation = settings.IsChat ? ChatAdapter.Request(body) : null;
            AnthropicAdaptation? anthropic = settings.IsAnthropic ? AnthropicAdapter.Request(body, settings.MaxOutputTokens) : null;
            using var request = Request(HttpMethod.Post, settings.RequestUri(false, testLoopback));
            request.Content = new StringContent((anthropic?.Body ?? adaptation?.Body ?? body).ToJsonString(), Encoding.UTF8, "application/json");
            entry.Requests = 1; using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token); entry.HttpStatus = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                entry.Status = "failed"; entry.Note = $"HTTP {(int)response.StatusCode}; no fallback; usage not provided"; Emit(entry);
                await LoopbackHttp.Reply(stream, (int)response.StatusCode is >= 300 and < 400 ? 502 : (int)response.StatusCode, "application/json", JsonSerializer.Serialize(new { error = new { message = entry.Note, type = "company_endpoint_error" } }), ct); return;
            }
            if (anthropic != null && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                if (!wantsStream) throw new IOException("Unexpected Anthropic SSE response");
                streaming = true;
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n"), linked.Token);
                var converted = await AnthropicStream.Translate(await response.Content.ReadAsStreamAsync(linked.Token), stream, anthropic, linked.Token);
                using var parsed = JsonDocument.Parse(converted.ToJsonString()); entry.Tokens = TokenCounts.FromResponse(parsed.RootElement); entry.Model = parsed.RootElement.Text("model"); entry.Status = parsed.RootElement.Text("status"); entry.Note = "Anthropic SSE → Responses 변환"; Emit(entry); return;
            }
            if (anthropic == null && adaptation == null && wantsStream && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                streaming = true;
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n"), linked.Token);
                await stream.FlushAsync(linked.Token);
                using var upstream = new StreamReader(await response.Content.ReadAsStreamAsync(linked.Token), Encoding.UTF8);
                var frame = new StringBuilder(); JsonElement? final = null; long total = 0;
                async Task Forward()
                {
                    if (frame.Length == 0) return;
                    var eventText = Safety.Redact(frame.ToString());
                    total += eventText.Length; if (total > 64_000_000) throw new IOException("Company stream size limit");
                    var observed = ResponseWire.LastResponse(eventText); if (observed.HasValue) final = observed;
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(eventText + "\n"), linked.Token);
                    await stream.FlushAsync(linked.Token); frame.Clear();
                }
                while (await upstream.ReadLineAsync(linked.Token) is { } line)
                {
                    if (line.Length == 0) await Forward();
                    else { frame.AppendLine(line); if (frame.Length > 2_000_000) throw new IOException("Company event size limit"); }
                }
                await Forward();
                entry.Tokens = final.HasValue ? TokenCounts.FromResponse(final.Value) : TokenCounts.Missing;
                entry.Model = final?.Text("model") is { Length: > 0 } streamModel ? streamModel : entry.Deployment;
                entry.Status = final?.Text("status") ?? "unknown";
                entry.Note = "실시간 Responses SSE 전달 · 최종 서버 usage만 집계"; Emit(entry); return;
            }
            var text = Safety.Redact(await response.Content.ReadAsStringAsync(linked.Token));
            string output; string contentType; string finalStatus = "completed";
            if (anthropic != null)
            {
                using var message = JsonDocument.Parse(text); var converted = AnthropicAdapter.Response(message.RootElement, anthropic);
                using var parsed = JsonDocument.Parse(converted.ToJsonString()); entry.Tokens = TokenCounts.FromResponse(parsed.RootElement); entry.Model = parsed.RootElement.Text("model"); finalStatus = parsed.RootElement.Text("status");
                output = wantsStream ? ResponseWire.Sse(converted) : converted.ToJsonString(); contentType = wantsStream ? "text/event-stream" : "application/json";
            }
            else if (adaptation != null)
            {
                using var chat = JsonDocument.Parse(text); entry.Tokens = TokenCounts.FromResponse(chat.RootElement); entry.Model = chat.RootElement.Text("model") is { Length: > 0 } model ? model : entry.Deployment;
                var converted = ChatAdapter.Response(chat.RootElement, adaptation); output = wantsStream ? ResponseWire.Sse(converted) : converted.ToJsonString(); contentType = wantsStream ? "text/event-stream" : "application/json";
            }
            else if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                JsonElement? final = ResponseWire.LastResponse(text);
                entry.Tokens = final.HasValue ? TokenCounts.FromResponse(final.Value) : TokenCounts.Missing; entry.Model = final?.Text("model") is { Length: > 0 } model ? model : entry.Deployment;
                finalStatus = final?.Text("status") ?? "unknown";
                output = text; contentType = "text/event-stream";
            }
            else
            {
                using var parsed = JsonDocument.Parse(text); entry.Tokens = TokenCounts.FromResponse(parsed.RootElement); entry.Model = parsed.RootElement.Text("model") is { Length: > 0 } model ? model : entry.Deployment;
                finalStatus = parsed.RootElement.Text("status") is { Length: > 0 } status ? status : "unknown";
                var node = JsonNode.Parse(text)!.AsObject(); output = wantsStream ? ResponseWire.Sse(node) : text; contentType = wantsStream ? "text/event-stream" : "application/json";
            }
            entry.Status = finalStatus; entry.Note = "응답 usage 관측. reasoning/cache는 부분집합, 별도 합산 안 함"; Emit(entry);
            await LoopbackHttp.Reply(stream, 200, contentType, output, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or JsonException or InvalidOperationException)
        {
            entry.Status = ex is OperationCanceledException ? "cancelled" : "failed"; entry.Note = "요청 실패/중단 또는 호환 불가; usage 미제공; 외부 fallback 없음"; Emit(entry);
            if (!streaming && !ct.IsCancellationRequested) await LoopbackHttp.Reply(stream, 502, "application/json", JsonSerializer.Serialize(new { error = new { message = entry.Note, type = "company_adapter_error" } }), ct);
        }
    }
    public async ValueTask DisposeAsync() { operation.Cancel(); foreach (var context in tasks.Values) context.Operation.Cancel(); await server.DisposeAsync(); http.Dispose(); operation.Dispose(); foreach (var context in tasks.Values) context.Operation.Dispose(); }
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
