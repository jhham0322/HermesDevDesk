using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Hermes.Core;
public static class ChatStream
{
    public static async Task<JsonObject> Translate(Stream upstream, Stream downstream, ChatAdaptation adaptation, CancellationToken ct, Action<string, bool>? observed = null)
    {
        using var reader = new StreamReader(upstream, Encoding.UTF8);
        var responseId = "resp_" + Guid.NewGuid().ToString("N"); var messageId = "msg_" + Guid.NewGuid().ToString("N");
        var text = new StringBuilder(); var tools = new SortedDictionary<int, JsonObject>(); var frame = new StringBuilder();
        string model = "", finish = ""; JsonNode? usage = null;
        int sequence = 0; long bytes = 0; bool messageStarted = false, done = false;
        async Task Event(string name, JsonObject fields)
        {
            fields["type"] = name; fields["sequence_number"] = sequence++;
            await downstream.WriteAsync(Encoding.UTF8.GetBytes("event: " + name + "\ndata: " + fields.ToJsonString() + "\n\n"), ct); await downstream.FlushAsync(ct);
        }
        await Event("response.created", new() { ["response"] = new JsonObject { ["id"] = responseId, ["object"] = "response", ["created_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["model"] = model, ["status"] = "in_progress", ["output"] = new JsonArray() } });
        async Task Frame()
        {
            var data = string.Join('\n', frame.ToString().Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].Trim())); frame.Clear();
            if (data.Length == 0) return;
            if (data == "[DONE]") { done = true; return; }
            using var parsed = JsonDocument.Parse(data); var root = parsed.RootElement;
            if (root.Property("error") != null) throw new ModelServerException(ProviderErrors.ReadableMessage(root.GetRawText()));
            if (root.Text("model").Length > 0) { model = root.Text("model"); observed?.Invoke(model, false); }
            if (root.Property("usage") is { ValueKind: JsonValueKind.Object } counts) usage = JsonNode.Parse(counts.GetRawText());
            if (root.Property("choices") is not { ValueKind: JsonValueKind.Array } choices) return;
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.Property("index") is { } index && index.GetInt32() != 0) continue;
                if (choice.Text("finish_reason").Length > 0) finish = choice.Text("finish_reason");
                if (choice.Property("delta") is not { ValueKind: JsonValueKind.Object } delta) continue;
                var chunk = delta.Text("content");
                observed?.Invoke(model, chunk.Length > 0 || delta.Property("tool_calls") is { ValueKind: JsonValueKind.Array } usefulCalls && usefulCalls.GetArrayLength() > 0);
                if (chunk.Length > 0)
                {
                    if (!messageStarted)
                    {
                        messageStarted = true;
                        await Event("response.output_item.added", new() { ["output_index"] = 0, ["item"] = new JsonObject { ["id"] = messageId, ["type"] = "message", ["role"] = "assistant", ["status"] = "in_progress", ["content"] = new JsonArray() } });
                        await Event("response.content_part.added", new() { ["item_id"] = messageId, ["output_index"] = 0, ["content_index"] = 0, ["part"] = new JsonObject { ["type"] = "output_text", ["text"] = "", ["annotations"] = new JsonArray() } });
                    }
                    text.Append(chunk);
                    await Event("response.output_text.delta", new() { ["item_id"] = messageId, ["output_index"] = 0, ["content_index"] = 0, ["delta"] = Safety.Redact(chunk) });
                }
                if (delta.Property("tool_calls") is not { ValueKind: JsonValueKind.Array } calls) continue;
                foreach (var call in calls.EnumerateArray())
                {
                    var position = call.GetProperty("index").GetInt32(); if (position is < 0 or > 1000) throw new HermesException("Chat tool stream index limit");
                    if (!tools.TryGetValue(position, out var item)) tools[position] = item = new JsonObject { ["id"] = "", ["type"] = "function", ["function"] = new JsonObject { ["name"] = "", ["arguments"] = "" } };
                    if (call.Text("id").Length > 0) item["id"] = call.Text("id");
                    if (call.Property("function") is { ValueKind: JsonValueKind.Object } function)
                    { item["function"]!["name"] = item["function"]!["name"]!.GetValue<string>() + function.Text("name"); item["function"]!["arguments"] = item["function"]!["arguments"]!.GetValue<string>() + function.Text("arguments"); }
                }
            }
        }
        while (!done)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) { await Frame(); break; }
            bytes += line.Length; if (bytes > 64_000_000) throw new HermesException("Chat stream size limit");
            if (line.Length == 0) await Frame(); else { frame.AppendLine(line); if (frame.Length > 2_000_000) throw new HermesException("Chat event size limit"); }
        }
        if (finish.Length == 0) throw new HermesException("Chat 스트림이 finish_reason 없이 종료되었습니다. 완료 응답으로 처리하지 않았습니다.");
        var message = new JsonObject { ["role"] = "assistant", ["content"] = Safety.Redact(text.ToString()) };
        if (tools.Count > 0) message["tool_calls"] = new JsonArray(tools.Values.Select(v => (JsonNode)v).ToArray());
        var chat = new JsonObject { ["id"] = responseId, ["model"] = model, ["choices"] = new JsonArray(new JsonObject { ["message"] = message, ["finish_reason"] = finish }), ["usage"] = usage };
        using var final = JsonDocument.Parse(chat.ToJsonString()); var converted = ChatAdapter.Response(final.RootElement, adaptation); converted["id"] = responseId;
        var output = converted["output"]!.AsArray();
        for (var i = 0; i < output.Count; i++)
        {
            var item = output[i]!.AsObject();
            if (item["type"]?.GetValue<string>() == "message" && messageStarted)
            { item["id"] = messageId; await Event("response.output_text.done", new() { ["item_id"] = messageId, ["output_index"] = i, ["content_index"] = 0, ["text"] = message["content"]!.DeepClone() }); }
            else await Event("response.output_item.added", new() { ["output_index"] = i, ["item"] = item.DeepClone() });
            await Event("response.output_item.done", new() { ["output_index"] = i, ["item"] = item.DeepClone() });
        }
        await Event(converted["status"]?.GetValue<string>() == "incomplete" ? "response.incomplete" : "response.completed", new() { ["response"] = converted.DeepClone() });
        return converted;
    }
}
