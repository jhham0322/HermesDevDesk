using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hermes.Core;

public static class AnthropicStream
{
    public static async Task<JsonObject> Translate(Stream upstream, Stream downstream, AnthropicAdaptation adaptation, CancellationToken ct)
    {
        using var reader = new StreamReader(upstream, Encoding.UTF8);
        var frame = new StringBuilder(); var blocks = new SortedDictionary<int, JsonObject>(); var arguments = new Dictionary<int, StringBuilder>(); var open = new HashSet<int>();
        JsonObject? message = null; var sequence = 0; long size = 0; bool stopped = false, textStarted = false; string textId = "";
        async Task Event(string type, JsonObject fields)
        {
            fields["type"] = type; fields["sequence_number"] = sequence++;
            await downstream.WriteAsync(Encoding.UTF8.GetBytes("event: " + type + "\ndata: " + fields.ToJsonString() + "\n\n"), ct); await downstream.FlushAsync(ct);
        }
        async Task Delta(string text)
        {
            if (!textStarted)
            {
                textStarted = true; textId = "msg_" + message!["id"]!.GetValue<string>();
                await Event("response.output_item.added", new() { ["output_index"] = 0, ["item"] = new JsonObject { ["id"] = textId, ["type"] = "message", ["role"] = "assistant", ["status"] = "in_progress", ["content"] = new JsonArray() } });
                await Event("response.content_part.added", new() { ["item_id"] = textId, ["output_index"] = 0, ["content_index"] = 0, ["part"] = new JsonObject { ["type"] = "output_text", ["text"] = "", ["annotations"] = new JsonArray() } });
            }
            if (text.Length > 0) await Event("response.output_text.delta", new() { ["item_id"] = textId, ["output_index"] = 0, ["content_index"] = 0, ["delta"] = text });
        }
        async Task Frame()
        {
            if (frame.Length == 0) return;
            var raw = frame.ToString(); frame.Clear();
            var data = string.Join('\n', raw.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
            if (data.Length == 0) return;
            var node = JsonNode.Parse(Safety.Redact(data))!.AsObject(); var type = node["type"]?.GetValue<string>();
            if (stopped) throw new HermesException("Event after message_stop");
            if (type == "ping") return;
            if (type == "error") throw new ModelServerException(node["error"]?["message"]?.GetValue<string>() ?? data);
            if (type == "message_start")
            {
                if (message != null) throw new HermesException("Duplicate message_start");
                message = node["message"]!.DeepClone().AsObject();
                await Event("response.created", new() { ["response"] = new JsonObject { ["id"] = "resp_" + message["id"]!.GetValue<string>(), ["object"] = "response", ["created_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["status"] = "in_progress", ["model"] = message["model"]?.DeepClone(), ["output"] = new JsonArray() } }); return;
            }
            if (message == null) throw new HermesException("Missing message_start");
            if (type == "content_block_start")
            {
                var index = node["index"]!.GetValue<int>(); if (index < 0 || index > 1000 || blocks.ContainsKey(index)) throw new HermesException("Invalid block index");
                var block = node["content_block"]!.DeepClone().AsObject(); blocks[index] = block; open.Add(index);
                if (block["type"]!.GetValue<string>() == "text") await Delta(block["text"]?.GetValue<string>() ?? "");
                else if (block["type"]!.GetValue<string>() == "tool_use") arguments[index] = new();
            }
            else if (type == "content_block_delta")
            {
                var index = node["index"]!.GetValue<int>(); if (!open.Contains(index) || !blocks.TryGetValue(index, out var block)) throw new HermesException("Unknown stream block");
                var delta = node["delta"]!; var deltaType = delta["type"]!.GetValue<string>();
                if (deltaType == "text_delta") { var text = delta["text"]!.GetValue<string>(); block["text"] = (block["text"]?.GetValue<string>() ?? "") + text; await Delta(text); }
                else if (deltaType == "input_json_delta") { if (!arguments.TryGetValue(index, out var args)) throw new HermesException("Tool delta without tool"); args.Append(delta["partial_json"]!.GetValue<string>()); }
                else if (deltaType is not ("thinking_delta" or "signature_delta")) throw new HermesException("Unsupported Anthropic stream delta");
            }
            else if (type == "content_block_stop")
            {
                var index = node["index"]!.GetValue<int>(); if (!open.Remove(index)) throw new HermesException("Unknown stopped block");
                if (arguments.TryGetValue(index, out var args) && args.Length > 0) blocks[index]["input"] = JsonNode.Parse(args.ToString())?.AsObject() ?? throw new HermesException("Invalid streamed tool arguments");
            }
            else if (type == "message_delta")
            {
                if (node["delta"] is JsonObject delta) foreach (var property in delta) message[property.Key] = property.Value?.DeepClone();
                if (node["usage"] is JsonObject usage)
                {
                    if (message["usage"] is not JsonObject) message["usage"] = new JsonObject();
                    foreach (var property in usage) message["usage"]![property.Key] = property.Value?.DeepClone();
                }
            }
            else if (type == "message_stop") { if (open.Count != 0) throw new HermesException("Message stopped with unfinished blocks"); stopped = true; }
            else throw new HermesException("Unsupported Anthropic stream event");
        }
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            size += line.Length; if (size > 64_000_000) throw new HermesException("Anthropic stream size limit");
            if (line.Length == 0) { await Frame(); if (stopped) break; } else { frame.AppendLine(line); if (frame.Length > 2_000_000) throw new HermesException("Anthropic event size limit"); }
        }
        await Frame(); if (!stopped || message == null) throw new HermesException("Anthropic stream ended before message_stop");
        message["content"] = new JsonArray(blocks.Values.Select(b => (JsonNode)b.DeepClone()).ToArray());
        using var parsed = JsonDocument.Parse(message.ToJsonString()); var response = AnthropicAdapter.Response(parsed.RootElement, adaptation);
        var output = response["output"]!.AsArray();
        for (var i = 0; i < output.Count; i++)
        {
            var item = output[i]!;
            if (item["type"]!.GetValue<string>() == "message" && textStarted)
                await Event("response.output_text.done", new() { ["item_id"] = textId, ["output_index"] = i, ["content_index"] = 0, ["text"] = item["content"]![0]!["text"]!.DeepClone() });
            else await Event("response.output_item.added", new() { ["output_index"] = i, ["item"] = item.DeepClone() });
            await Event("response.output_item.done", new() { ["output_index"] = i, ["item"] = item.DeepClone() });
        }
        await Event(response["status"]!.GetValue<string>() == "incomplete" ? "response.incomplete" : "response.completed", new() { ["response"] = response.DeepClone() });
        return response;
    }
}
