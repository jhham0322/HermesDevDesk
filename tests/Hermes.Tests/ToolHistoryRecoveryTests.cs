using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;

internal static class ToolHistoryRecoveryTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var input = JsonNode.Parse("""
            {"model":"fixture","stream":false,"instructions":"Keep the user's goal.",
            "tools":[{"type":"function","name":"current_tool","parameters":{"type":"object","properties":{}}}],
            "input":[{"type":"function_call","namespace":"obsolete","name":"tool/with incompatible name","call_id":"old","arguments":"{}"},
            {"type":"function_call_output","call_id":"old","output":[{"type":"input_text","text":"OLD_RESULT_OK"},{"type":"input_image","image_url":"data:image/png;base64,AQID"}]},
            {"type":"message","role":"user","content":[{"type":"input_text","text":"Continue my current request."}]}]}
            """)!.AsObject();
        var original = input.ToJsonString();
        var recovered = ToolHistoryRecovery.Prepare(input);
        check(input.ToJsonString() == original, "history recovery leaves saved conversation unchanged");
        check(recovered["instructions"]!.GetValue<string>().Contains("Keep the user's goal.") && recovered["instructions"]!.GetValue<string>().Contains("supported alternative"), "history recovery preserves instructions and requests replanning with supported tools");
        check(recovered["input"]![1]!["content"]![2]!["image_url"]!.GetValue<string>() == "data:image/png;base64,AQID", "recovery preserves result images");
        check(recovered["input"]![2]!["content"]![0]!["text"]!.GetValue<string>() == "Continue my current request.", "current user request is preserved");
        check(ToolHistoryRecovery.Prepare(recovered).ToJsonString() == recovered.ToJsonString(), "recovery is idempotent");
        var chat = ChatAdapter.Request(input);
        var anthropic = AnthropicAdapter.Request(input, 1024);
        check(anthropic.Body["messages"]!.AsArray().All(m => m!["content"]!.AsArray().All(p => p!["type"]!.GetValue<string>() is not ("tool_use" or "tool_result"))), "removed tool produces no invalid native tool history");
        check(chat.Tools.Count == 1 && anthropic.Tools.Count == 1, "recovery keeps only currently supported tools executable");
        var known = input.DeepClone().AsObject(); known["input"]![0]!["namespace"] = null; known["input"]![0]!["name"] = "current_tool";
        check(ReferenceEquals(ToolHistoryRecovery.Prepare(known), known), "supported tool history remains native");
        var changedType = known.DeepClone().AsObject(); changedType["input"]![0]!["type"] = "custom_tool_call"; changedType["input"]![0]!["input"] = "old format";
        check(!ReferenceEquals(ToolHistoryRecovery.Prepare(changedType), changedType), "same-name tool with changed schema type is recovered");

        int hits = 0;
        await using var server = new LoopbackHttp(async (request, stream, ct) =>
        {
            using var json = JsonDocument.Parse(request.Body); var body = json.RootElement;
            var text = body.GetRawText();
            if (!text.Contains("OLD_RESULT_OK") || !text.Contains("supported alternative") ||
                body.GetProperty("tools").GetArrayLength() != 1 ||
                body.GetProperty("tools")[0].Text("name") != "current_tool")
                throw new IOException("recovered request did not reach upstream correctly");
            var response = AnthropicTests.Message("RECOVERED_REQUEST_OK");
            if (++hits == 1)
            {
                response["stop_reason"] = "tool_use";
                response["content"] = JsonNode.Parse("""[{"type":"tool_use","id":"alternative","name":"current_tool","input":{}}]""");
            }
            await LoopbackHttp.Reply(stream, 200, "application/json", response.ToJsonString(), ct);
        });
        var store = new ProtectedKeyStore(Path.Combine(root, "recovery-key"));
        store.Save("SYNTHETIC_RECOVERY_KEY_ONLY");
        await using var bridge = new CompanyBridge(new ProviderSettings {
            Endpoint = server.BaseUri.AbsoluteUri, Deployment = "fixture",
            Protocol = ProviderProtocol.AnthropicMessages, Authentication = ProviderAuth.BearerKey
        }, store);
        using var client = new HttpClient();
        async Task<JsonObject> Send(JsonObject body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, bridge.BaseUri.AbsoluteUri + "/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bridge.Capability);
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            var wire = await response.Content.ReadAsStringAsync();
            check(response.IsSuccessStatusCode, "recovered request proceeds through HTTP bridge");
            return JsonNode.Parse(wire)!.AsObject();
        }
        var first = await Send(input);
        check(first["output"]![0]!["name"]!.GetValue<string>() == "current_tool", "supported alternative call survives model response conversion");
        input["input"]!.AsArray().Add(first["output"]![0]!.DeepClone());
        input["input"]!.AsArray().Add(JsonNode.Parse("""{"type":"function_call_output","call_id":"alternative","output":"SYNTHETIC_ALTERNATIVE_RESULT"}"""));
        var final = await Send(input);
        check(hits == 2 && final["output"]!.ToJsonString().Contains("RECOVERED_REQUEST_OK"), "recovered history supports alternative call and final response in local fixture");
    }
}
