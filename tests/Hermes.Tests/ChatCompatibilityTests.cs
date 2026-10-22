using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;
internal static class ChatCompatibilityTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var request = JsonNode.Parse("""{"model":"chat-model","stream":true,"max_output_tokens":100,"parallel_tool_calls":true,"input":[{"role":"user","content":[{"type":"input_text","text":"Hello!"}]}]}""")!.AsObject();
        var adaptation = ChatAdapter.Request(request);
        check(adaptation.Body["messages"]![0]!["content"]!.GetValue<string>() == "Hello!", "text-only chat gateway receives curl-compatible string content");
        check(adaptation.Body["stream"]!.GetValue<bool>() && adaptation.Body["max_tokens"]!.GetValue<int>() == 100 && adaptation.Body["max_completion_tokens"] == null && adaptation.Body["parallel_tool_calls"] == null, "chat gateway receives streaming and legacy token field without optional parallel flag");
        var grouped = ChatAdapter.Request(JsonNode.Parse("""{"tools":[{"type":"function","name":"one"},{"type":"function","name":"two"}],"input":[{"type":"function_call","name":"one","call_id":"a","arguments":"{}"},{"type":"function_call","name":"two","call_id":"b","arguments":"{}"}]}""")!.AsObject());
        check(grouped.Body["messages"]!.AsArray().Count == 1 && grouped.Body["messages"]![0]!["tool_calls"]!.AsArray().Count == 2, "parallel calls share one assistant tool-call message");
        using var empty = JsonDocument.Parse("""{"choices":[{"message":{"role":"assistant","content":null,"reasoning_content":"PRIVATE"},"finish_reason":"length"}]}""");
        var rejected = false; try { ChatAdapter.Response(empty.RootElement, adaptation); } catch (IOException ex) { rejected = ex.Message.Contains("finish_reason=length"); }
        check(rejected, "reasoning-only empty final answer is not silently reported as success");
        check(ChatAdapter.Response(empty.RootElement, adaptation, allowEmptyProbe: true)["status"]!.GetValue<string>() == "incomplete", "one-token connection probe can finish without visible content but is labelled incomplete");
        var nested = JsonSerializer.Serialize(new { error = new { message = "회사 API 요청 시간 초과" } });
        check(ProviderErrors.ReadableMessage(nested) == "회사 API 요청 시간 초과", "engine nested JSON error becomes readable Korean");
        string Frame(object delta, string? reason = null) => "data: " + JsonSerializer.Serialize(new { model = "chat-model", choices = new[] { new { index = 0, delta, finish_reason = reason } } }) + "\r\n\r\n";
        var events = Frame(new { reasoning_content = "PRIVATE_REASONING" }) + Frame(new { content = "First " }) + Frame(new { content = "last" }, "stop") + "data: [DONE]\r\n\r\n";
        using var upstream = new MemoryStream(Encoding.UTF8.GetBytes(events)); using var downstream = new MemoryStream();
        var final = await ChatStream.Translate(upstream, downstream, adaptation, CancellationToken.None);
        var wire = Encoding.UTF8.GetString(downstream.ToArray());
        check(final["output"]![0]!["content"]![0]!["text"]!.GetValue<string>() == "First last" && wire.Contains("response.output_text.delta") && wire.Contains("response.completed"), "CRLF Chat SSE and DONE produce complete visible Responses stream");
        check(!wire.Contains("PRIVATE_REASONING"), "Chat reasoning deltas are not exposed as public answer");
        using var truncated = new MemoryStream(Encoding.UTF8.GetBytes(Frame(new { content = "partial" }))); using var partial = new MemoryStream();
        rejected = false; try { await ChatStream.Translate(truncated, partial, adaptation, CancellationToken.None); } catch (IOException) { rejected = true; }
        check(rejected && !Encoding.UTF8.GetString(partial.ToArray()).Contains("response.completed"), "truncated Chat stream never emits false completion");
    }
}
