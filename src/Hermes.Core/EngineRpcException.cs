using System.Text.Json;
using System.Text.Json.Nodes;
namespace Hermes.Core;

// Only the correlated RPC response reader creates these errors. Do not classify
// arbitrary IOException text, transport failure or model prose as admission refusal.
public sealed class EngineRpcException : ModelServerException
{
    public string RequestMethod { get; }
    public JsonElement Error { get; }
    private EngineRpcException(string method, JsonElement error) : base(error.Text("message"))
    { RequestMethod = method; Error = error; }
    internal static EngineRpcException FromResponse(string method, JsonElement error)
    {
        JsonObject Project(JsonElement source, int depth)
        {
            var result = new JsonObject();
            foreach (var key in new[] { "code", "type", "message" })
                if (source.Property(key) is { } value)
                {
                    if (value.ValueKind == JsonValueKind.String) result[key] = Safety.Redact(value.GetString() ?? "");
                    else if (key == "code" && value.ValueKind == JsonValueKind.Number) result[key] = JsonNode.Parse(value.GetRawText());
                }
            if (depth == 0 && source.Property("data") is { ValueKind: JsonValueKind.Object } data) result["data"] = Project(data, 1);
            return result;
        }
        using var sanitized = JsonDocument.Parse(Project(error, 0).ToJsonString());
        return new EngineRpcException(method, sanitized.RootElement.Clone());
    }
}
