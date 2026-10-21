using System.Text.Json;

namespace Hermes.Core;
public enum EndpointSchemeChoice { RequireExplicitScheme, Https, Http }
public enum ProviderProtocol { GatewayResponses = 2, GatewayChatCompletions = 5, AnthropicMessages = 6, GatewayAuto = 7 }
public enum ProviderAuth { ApiKeyHeader, BearerKey, None }
public enum ConnectionMode { CompanyApi, ExistingChatGptCli }
public sealed class ProviderSettings
{
    public ConnectionMode Mode { get; set; } = ConnectionMode.CompanyApi;
    public string Endpoint { get; set; } = "";
    public string Deployment { get; set; } = "";
    public ProviderProtocol Protocol { get; set; } = ProviderProtocol.GatewayAuto;
    public ProviderAuth Authentication { get; set; } = ProviderAuth.BearerKey;
    public bool UseAdvancedSettings { get; set; }
    public int KeyRevision { get; set; }
    public int MaxOutputTokens { get; set; } = 8192;
    public bool IsAnthropic => Protocol == ProviderProtocol.AnthropicMessages;
    public bool IsChat => Protocol == ProviderProtocol.GatewayChatCompletions;
    public string Fingerprint => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Mode, Endpoint, Deployment, Protocol, Authentication, KeyRevision, MaxOutputTokens }))))[..16];
    public static string ResolveEndpointInput(string input, EndpointSchemeChoice choice = EndpointSchemeChoice.RequireExplicitScheme)
    {
        var value = input.Trim();
        if (value.Contains("://", StringComparison.Ordinal)) return value;
        if (choice == EndpointSchemeChoice.RequireExplicitScheme)
            throw new IOException("주소에 http:// 또는 https://를 직접 입력하거나 스킴을 명시적으로 선택하세요.");
        if (value.StartsWith("//", StringComparison.Ordinal)) value = value[2..];
        return (choice == EndpointSchemeChoice.Http ? "http://" : "https://") + value;
    }
    public static string TransportNotice(string resolvedEndpoint)
        => Uri.TryCreate(resolvedEndpoint, UriKind.Absolute, out var uri) && uri.Scheme == "http"
            ? "HTTP: 암호화되지 않습니다. API 키·요청·응답 내용을 네트워크에서 볼 수 있으므로 승인된 사내 게이트웨이에만 사용하세요."
            : "HTTPS: 기본 인증서 검증을 사용합니다. 실패 시 HTTP로 자동 전환하지 않습니다.";
    public Uri Validate(bool allowLoopbackForTests = false)
    {
        if (!Uri.TryCreate(Endpoint.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) || uri.Host.Length == 0 || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new IOException("API base 주소는 사용자 정보·query·fragment 없는 명시적인 http:// 또는 https:// URL이어야 합니다.");
        if (uri.Scheme is not ("https" or "http"))
            throw new IOException("http:// 또는 https:// 주소만 지원합니다. HTTPS 인증서 검증 해제나 자동 HTTP 전환은 하지 않습니다.");
        if (uri.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)) throw new IOException("회사 API 모드에서는 공개 OpenAI 주소를 사용하지 않습니다. 외부 fallback도 없습니다.");
        if (string.IsNullOrWhiteSpace(Deployment) || Deployment.Length > 200 || Deployment.Any(char.IsControl)) throw new IOException("회사에서 제공한 정확한 deployment/model 이름을 입력하세요.");
        if (MaxOutputTokens is < 1 or > 128000) throw new IOException("최대 출력 토큰은 1~128000입니다.");
        return uri;
    }
    public bool NormalizeRelaySettings()
    {
        // Numeric slots 0, 1, 3 and 4 belonged to removed direct-provider modes.
        if (Enum.IsDefined(Protocol)) return false;
        Protocol = ProviderProtocol.GatewayAuto; Authentication = ProviderAuth.BearerKey;
        UseAdvancedSettings = false;
        return true;
    }
    public bool NormalizeInferenceEndpoint()
    {
        var url = Endpoint.Trim().TrimEnd('/');
        var protocol = url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase) ? ProviderProtocol.AnthropicMessages
            : url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? ProviderProtocol.GatewayChatCompletions
            : url.EndsWith("/responses", StringComparison.OrdinalIgnoreCase) ? ProviderProtocol.GatewayResponses : Protocol;
        if (protocol == Protocol) return false;
        Protocol = protocol; return true;
    }
    public Uri RequestUri(bool models, bool allowLoopbackForTests = false)
    {
        var baseUri = Validate(allowLoopbackForTests); var basePath = baseUri.AbsoluteUri.TrimEnd('/');
        if (IsAnthropic)
        {
            if (baseUri.AbsolutePath.TrimEnd('/').EndsWith("/messages", StringComparison.OrdinalIgnoreCase)) return baseUri;
            if (!baseUri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) basePath += "/v1";
            return new(basePath + "/messages");
        }
        if (basePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) basePath = basePath[..^17];
        else if (basePath.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)) basePath = basePath[..^10];
        if (models) return new(basePath + "/models");
        var path = IsChat ? "/chat/completions" : "/responses";
        return new(basePath + path);
    }
}
