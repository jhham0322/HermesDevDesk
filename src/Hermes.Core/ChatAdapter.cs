using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hermes.Core;
public sealed record ChatTool(string Name, string? Namespace, string Type);
public sealed record ChatAdaptation(JsonObject Body, Dictionary<string, ChatTool> Tools);
public static class ChatAdapter
{
    public static ChatAdaptation Request(JsonObject responseRequest)
    {
        var messages = new JsonArray(); var mapping = new Dictionary<string, ChatTool>(StringComparer.Ordinal); var tools = new JsonArray();
        if (responseRequest["instructions"] is { } instructions) messages.Add(new JsonObject { ["role"] = "system", ["content"] = instructions.DeepClone() });
        if (responseRequest["input"] is JsonValue text) messages.Add(new JsonObject { ["role"] = "user", ["content"] = text.DeepClone() });
        else if (responseRequest["input"] is JsonArray inputs) foreach (var node in inputs)
        {
            if (node is not JsonObject item) throw new IOException("Unsupported chat adapter input");
            var type = item["type"]?.GetValue<string>() ?? "message";
            if (type == "message")
            {
                var role = item["role"]?.GetValue<string>() ?? "user"; if (role == "developer") role = "system";
                var content = new JsonArray();
                if (item["content"] is JsonValue plain) content.Add(new JsonObject { ["type"] = "text", ["text"] = plain.DeepClone() });
                else if (item["content"] is JsonArray parts) foreach (var part in parts)
                {
                    var partType = part?["type"]?.GetValue<string>();
                    if (partType is "input_text" or "output_text" or "text") content.Add(new JsonObject { ["type"] = "text", ["text"] = part?["text"]?.DeepClone() });
                    else if (partType == "input_image") content.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = part?["image_url"]?.DeepClone() } });
                    else throw new IOException("Chat adapter cannot represent this content type");
                }
                messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
            }
            else if (type is "function_call" or "custom_tool_call")
            {
                var name = Encoded(item["namespace"]?.GetValue<string>(), item["name"]!.GetValue<string>());
                var arguments = type == "custom_tool_call" ? new JsonObject { ["input"] = item["input"]?.DeepClone() }.ToJsonString() : item["arguments"]?.GetValue<string>() ?? "{}";
                messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = null, ["tool_calls"] = new JsonArray(new JsonObject { ["id"] = item["call_id"]?.DeepClone(), ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["arguments"] = arguments } }) });
            }
            else if (type is "function_call_output" or "custom_tool_call_output")
            {
                var images = new JsonArray(); var toolText = new List<string>();
                if (item["output"] is JsonArray resultParts)
                {
                    foreach (var part in resultParts)
                    {
                        if (part?["type"]?.GetValue<string>() == "input_image")
                            images.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = part["image_url"]?.DeepClone() } });
                        else if (part?["text"] is JsonValue textPart) toolText.Add(textPart.GetValue<string>());
                        else toolText.Add(part?.ToJsonString() ?? "");
                    }
                }
                else toolText.Add(item["output"] is JsonValue value ? value.GetValue<string>() : item["output"]?.ToJsonString() ?? "");
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = item["call_id"]?.DeepClone(), ["content"] = string.Join("\n", toolText) });
                // Chat tool messages only support text. Send raster content as a real user image, never JSON text.
                if (images.Count > 0) messages.Add(new JsonObject { ["role"] = "user", ["content"] = images });
            }
            else if (type != "reasoning") throw new IOException("Unsupported chat adapter input item");
        }
        void Tool(JsonObject t, string? ns)
        {
            var type = t["type"]?.GetValue<string>();
            if (type == "namespace") { foreach (var inner in t["tools"]!.AsArray()) Tool(inner!.AsObject(), t["name"]!.GetValue<string>()); return; }
            if (type is not ("function" or "custom")) throw new IOException("Chat adapter does not emulate hosted tools");
            var name = t["name"]!.GetValue<string>(); var encoded = Encoded(ns, name);
            if (!mapping.TryAdd(encoded, new(name, ns, type))) throw new IOException("Tool name collision");
            var schema = type == "custom" ? JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"input\":{\"type\":\"string\"}},\"required\":[\"input\"],\"additionalProperties\":false}") : t["parameters"]?.DeepClone() ?? new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
            tools.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = encoded, ["description"] = t["description"]?.DeepClone() ?? "", ["parameters"] = schema } });
        }
        if (responseRequest["tools"] is JsonArray declared) foreach (var t in declared) Tool(t!.AsObject(), null);
        var body = new JsonObject { ["model"] = responseRequest["model"]?.DeepClone(), ["messages"] = messages, ["stream"] = false };
        if (tools.Count > 0) body["tools"] = tools;
        if (responseRequest["max_output_tokens"] != null) body["max_completion_tokens"] = responseRequest["max_output_tokens"]!.DeepClone();
        if (responseRequest["parallel_tool_calls"] != null) body["parallel_tool_calls"] = responseRequest["parallel_tool_calls"]!.DeepClone();
        return new(body, mapping);
    }
    private static string Encoded(string? ns, string name)
    {
        var value = ns == null ? name : ns + "__" + name;
        if (value.Length > 64 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))) throw new IOException("Chat tool name is incompatible; use Responses protocol");
        return value;
    }
    public static JsonObject Response(JsonElement chat, ChatAdaptation adaptation)
    {
        var choice = chat.GetProperty("choices")[0]; var message = choice.GetProperty("message"); var output = new JsonArray();
        var text = message.Text("content");
        if (text.Length > 0) output.Add(new JsonObject { ["id"] = "msg_" + Guid.NewGuid().ToString("N"), ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text, ["annotations"] = new JsonArray() }) });
        if (message.Property("tool_calls") is { ValueKind: JsonValueKind.Array } calls) foreach (var call in calls.EnumerateArray())
        {
            var function = call.GetProperty("function"); var encoded = function.Text("name"); if (!adaptation.Tools.TryGetValue(encoded, out var tool)) throw new IOException("Unknown tool returned by gateway");
            var item = new JsonObject { ["id"] = "fc_" + Guid.NewGuid().ToString("N"), ["call_id"] = call.Text("id"), ["type"] = tool.Type == "custom" ? "custom_tool_call" : "function_call", ["name"] = tool.Name, ["status"] = "completed" };
            if (tool.Namespace != null) item["namespace"] = tool.Namespace;
            if (tool.Type == "custom") { using var arguments = JsonDocument.Parse(function.Text("arguments")); item["input"] = arguments.RootElement.Text("input"); }
            else item["arguments"] = function.Text("arguments"); output.Add(item);
        }
        var usage = TokenCounts.FromResponse(chat); JsonObject? tokens = usage.Input.HasValue && usage.Output.HasValue && usage.Total.HasValue ? new() { ["input_tokens"] = usage.Input, ["output_tokens"] = usage.Output, ["total_tokens"] = usage.Total } : null;
        if (tokens != null && usage.Cached.HasValue) tokens["input_tokens_details"] = new JsonObject { ["cached_tokens"] = usage.Cached };
        if (tokens != null && usage.Reasoning.HasValue) tokens["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = usage.Reasoning };
        return new() { ["id"] = chat.Text("id") is { Length: > 0 } id ? "resp_" + id : "resp_" + Guid.NewGuid().ToString("N"), ["object"] = "response", ["created_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["status"] = "completed", ["model"] = chat.Text("model"), ["output"] = output, ["usage"] = tokens };
    }
}
