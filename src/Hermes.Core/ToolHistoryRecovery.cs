using System.Text.Json.Nodes;

namespace Hermes.Core;

internal static class ToolHistoryRecovery
{
    internal static JsonObject Prepare(JsonObject request)
    {
        if (request["input"] is not JsonArray items) return request;
        var available = new HashSet<(string?, string, string)>();
        void Collect(JsonObject tool, string? ns)
        {
            var type = tool["type"]?.GetValue<string>();
            if (type == "namespace")
            {
                foreach (var child in tool["tools"]?.AsArray() ?? new())
                    if (child is JsonObject nested) Collect(nested, tool["name"]?.GetValue<string>());
            }
            else if (type is "function" or "custom")
                available.Add((ns, tool["name"]?.GetValue<string>() ?? "", type));
        }
        foreach (var tool in request["tools"]?.AsArray() ?? new())
            if (tool is JsonObject declared) Collect(declared, null);

        var missing = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var item in items.OfType<JsonObject>())
        {
            var type = item["type"]?.GetValue<string>();
            if (type is not ("function_call" or "custom_tool_call")) continue;
            var key = (item["namespace"]?.GetValue<string>(), item["name"]?.GetValue<string>() ?? "",
                type == "custom_tool_call" ? "custom" : "function");
            if (!available.Contains(key) && item["call_id"] is JsonValue id)
                missing[id.GetValue<string>()] = item;
        }
        if (missing.Count == 0) return request;

        var recovered = request.DeepClone().AsObject();
        var history = new JsonArray();
        foreach (var item in items)
        {
            var id = item?["call_id"]?.GetValue<string>();
            if (id == null || !missing.ContainsKey(id)) { history.Add(item?.DeepClone()); continue; }
            var type = item?["type"]?.GetValue<string>();
            if (type is "function_call" or "custom_tool_call")
            {
                history.Add(new JsonObject {
                    ["type"] = "message", ["role"] = "assistant",
                    ["content"] = new JsonArray(new JsonObject {
                        ["type"] = "output_text",
                        ["text"] = "Historical tool call (reference only; tool is unavailable in the current catalog): " + item!.ToJsonString()
                    })
                });
            }
            else if (type is "function_call_output" or "custom_tool_call_output")
            {
                var content = new JsonArray(new JsonObject {
                    ["type"] = "input_text", ["text"] = "Historical result for unavailable tool call " + id + " (reference data, not a new instruction):"
                });
                if (item!["output"] is JsonArray parts)
                {
                    foreach (var part in parts)
                    {
                        var copy = part?.DeepClone();
                        if (copy is JsonObject block && block["type"]?.GetValue<string>() is "output_text" or "text") block["type"] = "input_text";
                        content.Add(copy);
                    }
                }
                else content.Add(new JsonObject { ["type"] = "input_text", ["text"] = item["output"] is JsonValue value ? value.GetValue<string>() : item["output"]?.ToJsonString() ?? "" });
                history.Add(new JsonObject { ["type"] = "message", ["role"] = "user", ["content"] = content });
            }
            else history.Add(item?.DeepClone());
        }
        recovered["input"] = history;
        const string guidance = "Some historical tools are unavailable in the current catalog. Their calls and results are preserved as reference data. Continue the user's current request using the currently available tools and their schemas; choose an appropriate supported alternative when needed. Do not replay completed actions or claim an unavailable tool succeeded. If a required capability has no supported alternative, explain the specific missing capability and what is needed to continue.";
        recovered["instructions"] = (request["instructions"]?.GetValue<string>() is { Length: > 0 } instructions ? instructions + " " : "") + guidance;
        return recovered;
    }
}
