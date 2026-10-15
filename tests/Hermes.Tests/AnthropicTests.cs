using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;

internal static class AnthropicTests
{
    internal static JsonObject Message(string text = "ANTHROPIC_OK", string reason = "end_turn") => new() {
        ["id"] = "msg_owned", ["type"] = "message", ["role"] = "assistant", ["model"] = "owned-anthropic-model[1M]", ["stop_reason"] = reason,
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["usage"] = new JsonObject { ["input_tokens"] = 10, ["output_tokens"] = 3, ["cache_read_input_tokens"] = 5, ["cache_creation_input_tokens"] = 2 }
    };
    internal static string Events(JsonObject message)
    {
        var result = new StringBuilder(); void Add(JsonObject node) => result.Append("event: ").Append(node["type"]!.GetValue<string>()).Append("\ndata: ").Append(node.ToJsonString()).Append("\n\n");
        var start = message.DeepClone(); start["content"] = new JsonArray(); start["stop_reason"] = null;
        Add(new() { ["type"] = "message_start", ["message"] = start });
        var i = 0; foreach (var part in message["content"]!.AsArray())
        {
            var block = part!.DeepClone(); var type = block["type"]!.GetValue<string>();
            var value = type == "text" ? block["text"]!.GetValue<string>() : block["input"]!.ToJsonString();
            if (type == "text") block["text"] = ""; else block["input"] = new JsonObject();
            Add(new() { ["type"] = "content_block_start", ["index"] = i, ["content_block"] = block });
            Add(new() { ["type"] = "content_block_delta", ["index"] = i, ["delta"] = type == "text" ? new JsonObject { ["type"] = "text_delta", ["text"] = value } : new JsonObject { ["type"] = "input_json_delta", ["partial_json"] = value } });
            Add(new() { ["type"] = "content_block_stop", ["index"] = i++ });
        }
        Add(new() { ["type"] = "message_delta", ["delta"] = new JsonObject { ["stop_reason"] = message["stop_reason"]!.DeepClone() }, ["usage"] = message["usage"]!.DeepClone() });
        Add(new() { ["type"] = "message_stop" }); return result.ToString();
    }
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var settings = new ProviderSettings { Endpoint = "http://gateway.invalid/company", Deployment = "owned-anthropic-model[1M]", Protocol = ProviderProtocol.AnthropicMessages, Authentication = ProviderAuth.BearerKey };
        check(settings.RequestUri(false).AbsoluteUri == "http://gateway.invalid/company/v1/messages", "Anthropic base route appends v1/messages");
        settings.Endpoint += "/v1"; check(settings.RequestUri(false).AbsolutePath == "/company/v1/messages", "Anthropic v1 suffix is not duplicated");
        settings.Endpoint += "/messages"; check(settings.RequestUri(false).AbsolutePath == "/company/v1/messages", "complete messages URL is accepted");
        void Reject(Action action, string label) { try { action(); } catch (IOException) { check(true, label); return; } throw new IOException(label); }
        settings.MaxOutputTokens = 0; Reject(() => settings.Validate(), "invalid Anthropic token cap rejected"); settings.MaxOutputTokens = 8192;
        var tools = JsonNode.Parse("""[{"type":"namespace","name":"desk","tools":[{"type":"function","name":"inspect","parameters":{"type":"object","properties":{}}},{"type":"custom","name":"patch"}]}]""");
        var input = new JsonObject { ["model"] = settings.Deployment, ["instructions"] = "system fixture", ["stream"] = true, ["max_output_tokens"] = 9000, ["tools"] = tools, ["input"] = JsonNode.Parse("""[{"type":"message","role":"developer","content":[{"type":"input_text","text":"developer fixture"}]},{"role":"user","content":"hello"},{"type":"function_call","name":"inspect","namespace":"desk","call_id":"call1","arguments":"{}"},{"type":"function_call_output","call_id":"call1","output":[{"type":"input_text","text":"owned image"}]}]""") };
        input["input"]![3]!["output"]!.AsArray().Add(new JsonObject { ["type"] = "input_image", ["image_url"] = "data:image/png;base64," + NativeFeatureTests.Png });
        var adaptation = AnthropicAdapter.Request(input, 8192);
        check(adaptation.Body["system"]!.AsArray().Count == 2 && adaptation.Body["messages"]!.AsArray().All(m => m!["role"]!.GetValue<string>() is "user" or "assistant"), "developer/system instructions mapped without invalid system role");
        check(adaptation.Body["max_tokens"]!.GetValue<int>() == 8192 && adaptation.Body["store"] == null, "Anthropic output cap enforced and Responses-only fields removed");
        check(adaptation.Body["messages"]![2]!["content"]![0]!["type"]!.GetValue<string>() == "tool_result" && adaptation.Body.ToJsonString().Contains("\"media_type\":\"image/png\""), "computer tool result image remains an Anthropic image block");
        check(adaptation.Body["tools"]![1]!["input_schema"]!["properties"]!["input"] != null, "custom patch tool uses explicit string input schema");
        var answer = Message(); answer["content"]!.AsArray().Add(new JsonObject { ["type"] = "tool_use", ["id"] = "tool1", ["name"] = "desk__patch", ["input"] = new JsonObject { ["input"] = "owned patch" } }); answer["stop_reason"] = "tool_use";
        using var answerDoc = JsonDocument.Parse(answer.ToJsonString()); var converted = AnthropicAdapter.Response(answerDoc.RootElement, adaptation);
        check(converted["output"]![1]!["type"]!.GetValue<string>() == "custom_tool_call" && converted["output"]![1]!["namespace"]!.GetValue<string>() == "desk", "custom tool and namespace round trip preserved");
        using var responseDoc = JsonDocument.Parse(converted.ToJsonString()); var counts = TokenCounts.FromResponse(responseDoc.RootElement);
        check(counts.Input == 17 && counts.Output == 3 && counts.Cached == 5 && counts.Total == 20, "Anthropic cache categories counted once as additional input");
        using var incomplete = JsonDocument.Parse(Message(reason: "max_tokens").ToJsonString()); check(AnthropicAdapter.Response(incomplete.RootElement, adaptation)["status"]!.GetValue<string>() == "incomplete", "max token stop remains incomplete");
        var unknown = answer.DeepClone(); unknown["content"]![1]!["name"] = "unknown_tool"; using var unknownDoc = JsonDocument.Parse(unknown.ToJsonString()); Reject(() => AnthropicAdapter.Response(unknownDoc.RootElement, adaptation), "undeclared gateway tool rejected");
        using var streamInput = new MemoryStream(Encoding.UTF8.GetBytes(Events(answer))); using var streamOutput = new MemoryStream();
        var streamed = await AnthropicStream.Translate(streamInput, streamOutput, adaptation, CancellationToken.None);
        var wire = Encoding.UTF8.GetString(streamOutput.ToArray()); check(ResponseWire.LastResponse(wire)?.Text("status") == "completed" && streamed["output"]!.AsArray().Count == 2, "Anthropic text and tool SSE translates to completed Responses");
        var truncated = Events(answer).Split("event: message_stop")[0];
        try { await AnthropicStream.Translate(new MemoryStream(Encoding.UTF8.GetBytes(truncated)), new MemoryStream(), adaptation, CancellationToken.None); throw new Exception("truncation accepted"); } catch (IOException) { check(true, "truncated Anthropic SSE never reports completion"); }
        var store = new ProtectedKeyStore(Path.Combine(root, "anthropic-owned-secrets")); const string secret = "SYNTHETIC_ANTHROPIC_ONLY_KEY"; store.Save(secret); Safety.RegisterSecret(secret);
        string mode = "json"; int hits = 0; bool bearer = false, apiKey = false, version = false, model = false, minimal = false;
        await using var mock = new LoopbackHttp(async (request, stream, ct) =>
        {
            hits++; using var parsed = JsonDocument.Parse(request.Body); var body = parsed.RootElement;
            bearer = request.Headers.GetValueOrDefault("Authorization") == "Bearer " + secret;
            apiKey = request.Headers.GetValueOrDefault("x-api-key") == secret && !request.Headers.ContainsKey("api-key");
            version = request.Headers.GetValueOrDefault("anthropic-version") == "2023-06-01";
            model = body.Text("model") == settings.Deployment; minimal = body.GetProperty("max_tokens").GetInt32() == 1;
            if (request.Path != "/owned/v1/messages") throw new IOException("Incorrect upstream route");
            if (mode is "404" or "401") { await LoopbackHttp.Reply(stream, int.Parse(mode), "application/json", "{}", ct); return; }
            if (mode == "redirect") { await LoopbackHttp.Reply(stream, 302, "application/json", "{}", ct, new() { ["Location"] = "https://example.invalid" }); return; }
            await LoopbackHttp.Reply(stream, 200, mode == "sse" ? "text/event-stream" : "application/json", mode == "sse" ? Events(Message()) : Message().ToJsonString(), ct);
        });
        settings.Endpoint = mock.BaseUri.AbsoluteUri.TrimEnd('/') + "/owned";
        await using var bridge = new CompanyBridge(settings, store); var usage = new List<UsageEntry>(); bridge.Usage += e => usage.Add(e);
        check((await bridge.CheckConnectionAsync()).Contains(settings.Deployment) && minimal && bearer && version && model && hits == 1, "real local Anthropic check posts selected model with bearer and max_tokens=1");
        mode = "404"; check((await bridge.CheckConnectionAsync()).Contains("/owned/v1/messages") && !bridge.LastCheckSucceeded, "Anthropic 404 identifies actual inference path");
        settings.Authentication = ProviderAuth.ApiKeyHeader; mode = "json"; await bridge.CheckConnectionAsync(); check(apiKey && version, "Anthropic API key uses x-api-key without Azure api-key header");
        using var client = new HttpClient();
        async Task<string> Send()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, bridge.BaseUri.AbsoluteUri + "/responses"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bridge.Capability);
            request.Content = new StringContent(input.ToJsonString(), Encoding.UTF8, "application/json"); using var response = await client.SendAsync(request); return await response.Content.ReadAsStringAsync();
        }
        mode = "sse"; var streamedWire = await Send(); check(ResponseWire.LastResponse(streamedWire)?.Text("status") == "completed", "HTTP bridge serves native Anthropic SSE to Responses client");
        check(usage[^1].Tokens.Total == 20 && !streamedWire.Contains(secret), "bridge records real server usage without auth token exposure");
        mode = "redirect"; var before = hits; var rejected = await Send(); check(hits == before + 1 && rejected.Contains("no fallback"), "Anthropic redirect is never followed");
        await using var desk = new DeskController(Path.Combine(root, "anthropic-state")); desk.Configure(settings, secret); mode = "401"; await desk.CheckProviderAsync();
        check(desk.State.Provider.Deployment == "owned-anthropic-model[1M]" && desk.Models.Contains(settings.Deployment) && !desk.SubscriptionReady, "failed check preserves configured model without pretending engine connected");
        await using var reopened = new DeskController(desk.DataDirectory); check(reopened.State.Provider.Deployment == settings.Deployment && reopened.State.Provider.Protocol == ProviderProtocol.AnthropicMessages, "configured Anthropic model persists across restart");
        string requestedModel = "";
        await using var azure = new LoopbackHttp(async (request, stream, ct) =>
        {
            if (request.Method == "GET") { await LoopbackHttp.Reply(stream, 200, "application/json", "{\"data\":[{\"id\":\"azure-second-deployment\"}]}", ct); return; }
            using var doc = JsonDocument.Parse(request.Body); requestedModel = doc.RootElement.Text("model");
            await LoopbackHttp.Reply(stream, 200, "application/json", "{\"id\":\"resp_owned_azure\",\"status\":\"completed\",\"output\":[],\"model\":\"azure-second-deployment\"}", ct);
        });
        await using var azureBridge = new CompanyBridge(new() { Endpoint = azure.BaseUri.AbsoluteUri, Deployment = "azure-first-deployment", Protocol = ProviderProtocol.AzureV1Responses }, store);
        await azureBridge.CheckConnectionAsync(); azureBridge.SetAvailableModels(azureBridge.DiscoveredModels); azureBridge.BeginTask("owned-model-choice", "azure-second-deployment");
        using var azureRequest = new HttpRequestMessage(HttpMethod.Post, azureBridge.BaseUri.AbsoluteUri + "/responses"); azureRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", azureBridge.Capability);
        azureRequest.Content = new StringContent("{\"model\":\"azure-first-deployment\",\"input\":\"owned fixture\",\"stream\":false}", Encoding.UTF8, "application/json");
        using var azureReply = await client.SendAsync(azureRequest);
        check(azureBridge.DiscoveredModels.SequenceEqual(new[] { "azure-second-deployment" }) && requestedModel == "azure-second-deployment", "selected discovered Azure model is actually sent upstream");
        Reject(() => azureBridge.BeginTask("owned-unknown-model", "not-in-model-list"), "model outside configured or discovered list rejected");
    }
}
