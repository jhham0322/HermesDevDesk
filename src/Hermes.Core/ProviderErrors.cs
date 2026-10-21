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
                var root = json.RootElement;
                var error = root.Property("error") ?? root;
                var message = error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" : error.ValueKind == JsonValueKind.Object ? error.Text("message") : "";
                if (message.Length == 0 && root.Property("detail") is { ValueKind: JsonValueKind.String } detail) message = detail.GetString() ?? "";
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
}
