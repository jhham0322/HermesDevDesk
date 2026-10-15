using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hermes.Core;

public sealed record GatewayConnection(ProviderSettings Settings, string Note, TokenCounts Tokens);
public static class GatewayConnector
{
    public static async Task<GatewayConnection> Connect(ProviderSettings input, ProtectedKeyStore keys, CancellationToken ct)
    {
        var uri = input.Validate(); var key = keys.ReadOwnKey(); Safety.RegisterSecret(key);
        var attempts = new List<string>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { Timeout = TimeSpan.FromSeconds(15) };
        foreach (var candidate in Candidates(input))
        {
            var target = candidate.RequestUri(false);
            if (target.Scheme != uri.Scheme || target.Authority != uri.Authority || !target.AbsolutePath.StartsWith(uri.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal)) throw new IOException("자동 연결 대상이 입력한 서버 경로를 벗어났습니다.");
            var primary = candidate.Authentication;
            foreach (var auth in new[] { primary, primary == ProviderAuth.BearerKey ? ProviderAuth.ApiKeyHeader : ProviderAuth.BearerKey })
            {
                candidate.Authentication = auth;
                using var request = new HttpRequestMessage(HttpMethod.Post, target);
                if (auth == ProviderAuth.BearerKey) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                else request.Headers.TryAddWithoutValidation(candidate.IsAnthropic ? "x-api-key" : "api-key", key);
                if (candidate.IsAnthropic) request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                var body = candidate.IsAnthropic ? new JsonObject { ["model"] = input.Deployment, ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Reply only OK." }), ["max_tokens"] = 1, ["stream"] = false }
                    : candidate.IsChat ? new JsonObject { ["model"] = input.Deployment, ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Reply only OK." }), ["max_completion_tokens"] = 1, ["stream"] = false }
                    : new JsonObject { ["model"] = input.Deployment, ["input"] = "Reply only OK.", ["max_output_tokens"] = 1, ["stream"] = false, ["store"] = false };
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); }
                catch (HttpRequestException ex) { throw new IOException(ConnectionErrors.Describe(ex) + " · 요청 경로: " + target.AbsolutePath + " · 설정 모델: " + input.Deployment); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("서버 응답 시간이 초과됐습니다. 모델명은 유지했습니다."); }
                using (response)
                {
                    attempts.Add("POST " + target.AbsolutePath + ": HTTP " + (int)response.StatusCode);
                    if ((int)response.StatusCode is >= 300 and < 400) throw new IOException("서버가 다른 주소로 이동시켰습니다. 키를 다른 주소로 보내지 않았습니다. 회사에서 제공한 최종 URL을 입력하세요.");
                    using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); using var memory = new MemoryStream(); var buffer = new byte[8192]; int read;
                    while ((read = await stream.ReadAsync(buffer, timeout.Token)) != 0) { if (memory.Length + read > 2_000_000) throw new IOException("연결 검사 응답 크기 제한"); memory.Write(buffer, 0, read); }
                    JsonDocument? parsed = null;
                    try { parsed = JsonDocument.Parse(memory.ToArray()); } catch (JsonException) { }
                    using (parsed)
                    {
                        var error = parsed?.RootElement.Property("error");
                        var detail = error?.ValueKind == JsonValueKind.Object ? error.Value.Text("message") : "";
                        detail = Safety.Redact(detail); if (detail.Length > 400) detail = detail[..400];
                        var lower = detail.ToLowerInvariant();
                        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        {
                            if (auth == primary) continue;
                            throw new IOException("인증/권한 오류 HTTP " + (int)response.StatusCode + ". API 키와 모델 권한을 확인하세요. " + detail);
                        }
                        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
                        {
                            if (lower.Contains("model") || lower.Contains("deployment") || detail.Contains("모델") || detail.Contains("배포") || (error?.ValueKind == JsonValueKind.Object && error.Value.Text("code").Contains("model", StringComparison.OrdinalIgnoreCase))) throw new IOException("모델 " + input.Deployment + " 호출 실패: " + detail);
                            break; // Only an unsupported route permits checking another route on the same supplied server.
                        }
                        if (!response.IsSuccessStatusCode) throw new IOException("모델 " + input.Deployment + " 호출 실패 · " + attempts[^1] + " · " + detail);
                        if (parsed == null) { attempts[^1] += " (JSON 응답이 아님: 로그인/웹 페이지 여부 확인)"; break; }
                        var root = parsed.RootElement; JsonObject converted;
                        try
                        {
                            if (candidate.IsAnthropic) converted = AnthropicAdapter.Response(root, new(new(), new()));
                            else if (candidate.IsChat) converted = ChatAdapter.Response(root, new(new(), new()));
                            else if (root.Text("object") == "response" || root.Property("output") is { ValueKind: JsonValueKind.Array }) converted = JsonNode.Parse(root.GetRawText())!.AsObject();
                            else break;
                        }
                        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { break; }
                        if (converted["status"]?.GetValue<string>() == "failed") throw new IOException("서버가 설정 모델의 생성 요청을 실패로 반환했습니다.");
                        using var final = JsonDocument.Parse(converted.ToJsonString());
                        return new(candidate, "모델 " + input.Deployment + " 연결 확인 · " + target.AbsolutePath + " · " + candidate.Protocol + " (최대 출력 1토큰 검사)", TokenCounts.FromResponse(final.RootElement));
                    }
                }
            }
        }
        throw new IOException("서버의 지원 API 경로를 확인하지 못했습니다. 설정 모델 " + input.Deployment + "은 유지했습니다. " + string.Join("; ", attempts));
    }

    public static IReadOnlyList<ProviderSettings> Candidates(ProviderSettings input)
    {
        var uri = input.Validate(); var url = uri.AbsoluteUri.TrimEnd('/'); var result = new List<ProviderSettings>();
        void Add(string endpoint, ProviderProtocol protocol, ProviderAuth auth = ProviderAuth.BearerKey)
        {
            var value = JsonSerializer.Deserialize<ProviderSettings>(JsonSerializer.Serialize(input))!;
            value.Endpoint = endpoint; value.Protocol = protocol; value.Authentication = auth;
            if (!result.Any(r => r.RequestUri(false) == value.RequestUri(false) && r.Protocol == protocol)) result.Add(value);
        }
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) { Add(url[..^17], ProviderProtocol.GatewayChatCompletions); return result; }
        if (url.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)) { Add(url[..^10], ProviderProtocol.GatewayResponses); return result; }
        if (url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase)) { Add(url, ProviderProtocol.AnthropicMessages); return result; }
        var bases = uri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? new[] { url } : new[] { url, url + "/v1" };
        foreach (var path in bases) Add(path, ProviderProtocol.GatewayResponses);
        foreach (var path in bases) Add(path, ProviderProtocol.GatewayChatCompletions);
        Add(url, ProviderProtocol.AnthropicMessages);
        return result;
    }
}
