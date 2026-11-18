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
        var incomplete = ChatAdapter.Response(empty.RootElement, adaptation);
        check(incomplete["status"]!.GetValue<string>() == "incomplete" && incomplete["incomplete_details"]!["reason"]!.GetValue<string>() == "max_output_tokens" && incomplete["output"]!.AsArray().Count == 0, "length-only output is valid incomplete, not a malformed response or success");
        check(!incomplete.ToJsonString().Contains("PRIVATE"), "private reasoning is not exposed in normalized incomplete response");
        using var trulyEmpty = JsonDocument.Parse("""{"choices":[{"message":{"role":"assistant","content":null},"finish_reason":"stop"}]}""");
        var rejected = false; try { ChatAdapter.Response(trulyEmpty.RootElement, adaptation); } catch (IOException) { rejected = true; }
        check(rejected, "empty successful response remains a protocol error");
        using var cutTool = JsonDocument.Parse("""{"choices":[{"message":{"role":"assistant","tool_calls":[{"id":"cut","function":{"name":"unknown","arguments":"{"}}]},"finish_reason":"length"}]}""");
        check(ChatAdapter.Response(cutTool.RootElement, adaptation)["output"]!.AsArray().Count == 0, "truncated tool call is not emitted or executed");
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
        var limitedEvents = Frame(new { reasoning_content = "PRIVATE_LIMITED" }) + Frame(new { }, "length") + "data: [DONE]\r\n\r\n";
        using var limitedUpstream = new MemoryStream(Encoding.UTF8.GetBytes(limitedEvents)); using var limitedDownstream = new MemoryStream();
        var limited = await ChatStream.Translate(limitedUpstream, limitedDownstream, adaptation, CancellationToken.None);
        var limitedWire = Encoding.UTF8.GetString(limitedDownstream.ToArray());
        check(limited["status"]!.GetValue<string>() == "incomplete" && limitedWire.Contains("response.incomplete") && !limitedWire.Contains("response.completed") && !limitedWire.Contains("PRIVATE_LIMITED"), "reasoning-only SSE length ends as incomplete without leaked reasoning or false success");
        using var truncated = new MemoryStream(Encoding.UTF8.GetBytes(Frame(new { content = "partial" }))); using var partial = new MemoryStream();
        rejected = false; try { await ChatStream.Translate(truncated, partial, adaptation, CancellationToken.None); } catch (IOException) { rejected = true; }
        check(rejected && !Encoding.UTF8.GetString(partial.ToArray()).Contains("response.completed"), "truncated Chat stream never emits false completion");
    }
}
