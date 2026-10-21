using System.Text.Json;
using System.Text.RegularExpressions;
namespace Hermes.Core;
public static class ProviderErrors
{
    public static string ReadableMessage(string value)
    {
        for (var depth = 0; depth < 4; depth++)
        {
            var start = value.IndexOf('{'); var end = value.LastIndexOf('}');
            if (start < 0 || end <= start) break;
            try
            {
                using var json = JsonDocument.Parse(value[start..(end + 1)]);
                var message = Detail(json.RootElement);
                if (message.Length == 0 || message == value) break;
                value = (start == 0 ? "" : value[..start].TrimEnd() + " · ") + message;
            }
            catch (JsonException) { break; }
        }
        return Safety.Redact(Regex.Replace(value, @"\\u([0-9a-fA-F]{4})", match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString(), RegexOptions.None, TimeSpan.FromMilliseconds(100)));
    }
    public static string Detail(JsonElement root)
    {
        var error = root.Property("error") ?? root;
        var message = error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" : error.ValueKind == JsonValueKind.Object ? error.Text("message") : "";
        if (message.Length == 0 && root.Property("detail") is { } detail)
            message = detail.ValueKind == JsonValueKind.String ? detail.GetString() ?? "" : detail.GetRawText();
        if (error.ValueKind == JsonValueKind.Object)
        {
            var param = error.Text("param"); var code = error.Text("code");
            if (param.Length > 0) message += " · param=" + param;
            if (code.Length > 0) message += " · code=" + code;
        }
        message = Regex.Replace(message, @"\\u([0-9a-fA-F]{4})", match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString(), RegexOptions.None, TimeSpan.FromMilliseconds(100));
        return Safety.Redact(message[..Math.Min(message.Length, 6000)]);
    }
    public static string Guidance(int status, ProviderSettings settings, string detail)
    {
        if (status is 401 or 403) return "토큰 또는 모델 접근 권한을 확인하세요.";
        var lower = detail.ToLowerInvariant();
        if (status is 400 or 422 && (lower.Contains("enable-auto-tool-choice") || lower.Contains("tool-call-parser") || (lower.Contains("tool") && (lower.Contains("not support") || lower.Contains("unsupported")))))
            return "기본 API 채팅과 에이전트 도구 호출은 별도 기능입니다. 서버가 도구 호출을 거절했습니다. 게이트웨이 관리자가 해당 모델의 tool calling 지원을 확인해야 합니다. 서버 원인: " + detail;
        if (status is 400 or 422 && (settings.Protocol == ProviderProtocol.GatewayResponses || GatewayConnector.IsProtocolRejection(detail)))
            return "API 요청 형식 호환 오류 가능성: 연결 설정에서 기본 연결로 저장하고 연결 확인을 실행하세요. 같은 서버의 Claude Messages / Chat Completions 지원을 확인합니다. 서버 원인: " + detail;
        return detail;
    }
}
