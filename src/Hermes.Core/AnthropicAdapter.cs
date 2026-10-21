using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hermes.Core;

public sealed record AnthropicAdaptation(JsonObject Body, Dictionary<string, ChatTool> Tools);
public static class AnthropicAdapter
{
    public static AnthropicAdaptation Request(JsonObject input, int maxTokens)
    {
        // Reuse the exact namespace/custom-tool name contract used by the existing bridge.
        var catalogInput = new JsonObject { ["tools"] = input["tools"]?.DeepClone() };
        var catalog = ChatAdapter.Request(catalogInput);
        var messages = new JsonArray(); var system = new JsonArray();
        void Append(string role, JsonArray content)
        {
            if (content.Count == 0) return;
            if (messages.LastOrDefault() is JsonObject previous && previous["role"]!.GetValue<string>() == role)
                foreach (var block in content) previous["content"]!.AsArray().Add(block!.DeepClone());
            else messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }
        if (input["instructions"] is JsonValue instructions) system.Add(Text(instructions.GetValue<string>()));
        if (input["input"] is JsonValue plain) Append("user", new(Text(plain.GetValue<string>())));
        else if (input["input"] is JsonArray items) foreach (var node in items)
        {
            var item = node?.AsObject() ?? throw new HermesException("Invalid Responses input item");
            switch (item["type"]?.GetValue<string>() ?? "message")
            {
                case "message":
                    var role = item["role"]?.GetValue<string>() ?? "user";
                    var parts = Parts(item["content"]);
                    if (role is "system" or "developer") foreach (var part in parts) { if (part?["type"]?.GetValue<string>() != "text") throw new HermesException("System images are unsupported"); system.Add(part.DeepClone()); }
                    else if (role is "assistant" or "user") Append(role, parts);
                    else throw new HermesException("Unsupported message role");
                    break;
                case "function_call": case "custom_tool_call":
                    var name = item["name"]!.GetValue<string>(); var ns = item["namespace"]?.GetValue<string>();
                    var encoded = catalog.Tools.FirstOrDefault(t => t.Value.Name == name && t.Value.Namespace == ns).Key;
                    if (encoded == null) throw new HermesException("Historical tool not present in tool catalog");
                    JsonNode args = item["type"]!.GetValue<string>() == "custom_tool_call" ? new JsonObject { ["input"] = item["input"]?.DeepClone() } : JsonNode.Parse(item["arguments"]?.GetValue<string>() ?? "{}")!;
                    if (args is not JsonObject) throw new HermesException("Tool arguments must be an object");
                    Append("assistant", new(new JsonObject { ["type"] = "tool_use", ["id"] = item["call_id"]?.DeepClone(), ["name"] = encoded, ["input"] = args }));
                    break;
                case "function_call_output": case "custom_tool_call_output":
                    Append("user", new(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = item["call_id"]?.DeepClone(), ["content"] = Parts(item["output"]) }));
                    break;
                case "reasoning": break; // Provider-specific encrypted reasoning cannot be reused across protocols.
                default: throw new HermesException("Anthropic adapter cannot represent this input type");
            }
        }
        var requestedTokens = input["max_output_tokens"]?.GetValue<int>() ?? maxTokens;
        if (requestedTokens < 1) throw new HermesException("Invalid max output tokens");
        var body = new JsonObject { ["model"] = input["model"]?.DeepClone(), ["messages"] = messages, ["max_tokens"] = JsonValue.Create(Math.Min(requestedTokens, maxTokens)), ["stream"] = input["stream"]?.DeepClone() ?? JsonValue.Create(false) };
        if (system.Count > 0) body["system"] = system;
        var tools = new JsonArray();
        foreach (var t in catalog.Body["tools"]?.AsArray() ?? new())
        {
            var f = t!["function"]!;
            tools.Add(new JsonObject { ["name"] = f["name"]!.DeepClone(), ["description"] = f["description"]?.DeepClone(), ["input_schema"] = f["parameters"]!.DeepClone() });
        }
        if (tools.Count > 0) body["tools"] = tools;
        if (input["parallel_tool_calls"]?.GetValue<bool>() == false && tools.Count > 0) body["tool_choice"] = new JsonObject { ["type"] = "auto", ["disable_parallel_tool_use"] = true };
        if (input["tool_choice"] is JsonValue choice && choice.TryGetValue<string>(out var value))
            body["tool_choice"] = new JsonObject { ["type"] = value switch { "required" => "any", "auto" => "auto", "none" => "none", _ => throw new HermesException("Unsupported tool choice") } };
        else if (input["tool_choice"] is JsonObject selected)
        {
            var match = catalog.Tools.FirstOrDefault(t => t.Value.Name == selected["name"]?.GetValue<string>() && t.Value.Namespace == selected["namespace"]?.GetValue<string>()).Key ?? throw new HermesException("Unknown selected tool");
            body["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = match };
        }
        return new(body, catalog.Tools);
    }

    private static JsonObject Text(string value) => new() { ["type"] = "text", ["text"] = value };
    private static JsonArray Parts(JsonNode? node)
    {
        if (node == null) return new();
        if (node is JsonValue scalar) return new(Text(scalar.GetValue<string>()));
        var result = new JsonArray();
        foreach (var part in node.AsArray())
        {
            var type = part?["type"]?.GetValue<string>();
            if (type is "input_text" or "output_text" or "text") result.Add(Text(part!["text"]!.GetValue<string>()));
            else if (type == "input_image")
            {
                var url = part!["image_url"]!.GetValue<string>(); JsonObject source;
                if (url.StartsWith("data:", StringComparison.Ordinal))
                {
                    var split = url.IndexOf(";base64,", StringComparison.Ordinal); if (split < 0) throw new HermesException("Unsupported image data URI");
                    var media = url[5..split]; if (media is not ("image/png" or "image/jpeg" or "image/gif" or "image/webp")) throw new HermesException("Unsupported image type");
                    var data = url[(split + 8)..]; if (data.Length > 28_000_000) throw new HermesException("Image size limit");
                    try { Convert.FromBase64String(data); } catch (FormatException) { throw new HermesException("Invalid image base64"); }
                    source = new() { ["type"] = "base64", ["media_type"] = media, ["data"] = data };
                }
                else
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0) throw new HermesException("Unsupported image URL");
                    source = new() { ["type"] = "url", ["url"] = url };
                }
                result.Add(new JsonObject { ["type"] = "image", ["source"] = source });
            }
            else throw new HermesException("Unsupported Anthropic content block");
        }
        return result;
    }

    public static JsonObject Response(JsonElement message, AnthropicAdaptation adaptation)
    {

        if (message.Property("error") != null) throw new ModelServerException(ProviderErrors.ReadableMessage(message.GetRawText()));        if (message.Text("type") != "message" || message.Property("content") is not { ValueKind: JsonValueKind.Array } blocks) throw new HermesException("Invalid Anthropic message response");
        var content = new JsonArray(); var calls = new JsonArray();
        foreach (var block in blocks.EnumerateArray())
        {
            if (block.Text("type") == "text") content.Add(new JsonObject { ["type"] = "output_text", ["text"] = block.Text("text"), ["annotations"] = new JsonArray() });
            else if (block.Text("type") == "tool_use")
            {
                if (!adaptation.Tools.TryGetValue(block.Text("name"), out var tool)) throw new HermesException("Unknown tool returned by Anthropic gateway");
                var item = new JsonObject { ["id"] = "fc_" + block.Text("id"), ["call_id"] = block.Text("id"), ["type"] = tool.Type == "custom" ? "custom_tool_call" : "function_call", ["name"] = tool.Name, ["status"] = "completed" };
                if (tool.Namespace != null) item["namespace"] = tool.Namespace;
                if (tool.Type == "custom") item["input"] = block.GetProperty("input").Text("input");
                else item["arguments"] = block.GetProperty("input").GetRawText();
                calls.Add(item);
            }
            else if (block.Text("type") is not ("thinking" or "redacted_thinking")) throw new HermesException("Unsupported upstream content block");
        }
        var output = new JsonArray();
        // Consolidate text so the official Responses wire emits every text block, exactly once.
        if (content.Count > 0) output.Add(new JsonObject { ["id"] = "msg_" + message.Text("id"), ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = string.Concat(content.Select(c => c!["text"]!.GetValue<string>())), ["annotations"] = new JsonArray() }) });
        foreach (var call in calls) output.Add(call!.DeepClone());
        var reason = message.Text("stop_reason");
        var status = reason == "max_tokens" ? "incomplete" : reason is "end_turn" or "tool_use" or "stop_sequence" or "refusal" ? "completed" : throw new HermesException("Missing or unsupported Anthropic stop reason");
        var response = new JsonObject { ["id"] = "resp_" + message.Text("id"), ["object"] = "response", ["created_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["status"] = status, ["model"] = message.Text("model"), ["output"] = output, ["usage"] = Usage(message) };
        if (status == "incomplete") response["incomplete_details"] = new JsonObject { ["reason"] = "max_output_tokens" };
        return response;
    }
    private static JsonObject? Usage(JsonElement message)
    {
        if (message.Property("usage") is not { } usage) return null;
        long? Number(string name) => usage.Property(name) is { ValueKind: JsonValueKind.Number } n && n.TryGetInt64(out var value) && value is >= 0 and <= 1_000_000_000_000 ? value : null;
        var input = Number("input_tokens"); var output = Number("output_tokens");
        // Anthropic cache tokens are additional input categories, not subsets of input_tokens.
        var read = Number("cache_read_input_tokens"); var create = Number("cache_creation_input_tokens");
        if (input != null) input += (read ?? 0) + (create ?? 0);
        return new() { ["input_tokens"] = input, ["output_tokens"] = output, ["total_tokens"] = input.HasValue && output.HasValue ? input + output : null, ["input_tokens_details"] = new JsonObject { ["cached_tokens"] = read } };
    }
}
