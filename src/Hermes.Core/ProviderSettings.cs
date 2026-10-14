using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hermes.Core;
public enum ProviderProtocol { AzureV1Responses, AzureLegacyResponses, GatewayResponses, AzureV1ChatCompletions, AzureLegacyChatCompletions, GatewayChatCompletions }
public enum ProviderAuth { ApiKeyHeader, BearerKey, None }
public enum ConnectionMode { CompanyApi, ExistingChatGptCli }
public sealed class ProviderSettings
{
    public ConnectionMode Mode { get; set; } = ConnectionMode.CompanyApi;
    public string Endpoint { get; set; } = "";
    public string Deployment { get; set; } = "";
    public string ApiVersion { get; set; } = "";
    public ProviderProtocol Protocol { get; set; } = ProviderProtocol.AzureV1Responses;
    public ProviderAuth Authentication { get; set; } = ProviderAuth.ApiKeyHeader;
    public int KeyRevision { get; set; }
    public bool IsChat => Protocol is ProviderProtocol.AzureV1ChatCompletions or ProviderProtocol.AzureLegacyChatCompletions or ProviderProtocol.GatewayChatCompletions;
    public bool IsLegacy => Protocol is ProviderProtocol.AzureLegacyResponses or ProviderProtocol.AzureLegacyChatCompletions;
    public string Fingerprint => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Mode, Endpoint, Deployment, ApiVersion, Protocol, Authentication, KeyRevision }))))[..16];
    public Uri Validate(bool allowLoopbackForTests = false)
    {
        if (!Uri.TryCreate(Endpoint.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new IOException("API base 주소는 사용자 정보·query·fragment 없는 절대 HTTPS URL이어야 합니다.");
        if (uri.Scheme != "https" && !(allowLoopbackForTests && uri.Scheme == "http" && uri.IsLoopback)) throw new IOException("HTTPS가 필요합니다. TLS 검증을 끄는 옵션은 제공하지 않습니다.");
        if (uri.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)) throw new IOException("회사 API 모드에서는 공개 OpenAI 주소를 사용하지 않습니다. 외부 fallback도 없습니다.");
        if (string.IsNullOrWhiteSpace(Deployment) || Deployment.Length > 200 || Deployment.Any(char.IsControl)) throw new IOException("회사에서 제공한 정확한 deployment/model 이름을 입력하세요.");
        if (IsLegacy && !Regex.IsMatch(ApiVersion, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$")) throw new IOException("구버전 Azure에는 회사에서 확인한 api-version이 필요합니다.");
        if (!IsLegacy && ApiVersion.Length > 0) throw new IOException("v1/gateway 모드에는 api-version을 비워 두세요.");
        return uri;
    }
    public Uri RequestUri(bool models, bool allowLoopbackForTests = false)
    {
        var baseUri = Validate(allowLoopbackForTests); var basePath = baseUri.AbsoluteUri.TrimEnd('/');
        if (Protocol is ProviderProtocol.AzureV1Responses or ProviderProtocol.AzureV1ChatCompletions)
        { if (!baseUri.AbsolutePath.TrimEnd('/').EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase)) basePath += "/openai/v1"; }
        if (IsLegacy && !baseUri.AbsolutePath.TrimEnd('/').EndsWith("/openai", StringComparison.OrdinalIgnoreCase)) basePath += "/openai";
        if (models) return new(basePath + "/models" + (IsLegacy ? "?api-version=" + Uri.EscapeDataString(ApiVersion) : ""));
        var path = IsChat ? "/chat/completions" : "/responses";
        if (Protocol == ProviderProtocol.AzureLegacyChatCompletions) path = "/deployments/" + Uri.EscapeDataString(Deployment) + path;
        return new(basePath + path + (IsLegacy ? "?api-version=" + Uri.EscapeDataString(ApiVersion) : ""));
    }
}
