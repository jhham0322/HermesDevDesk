using System.Text.Json;
using Hermes.Core;
internal static class HostedVllmTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var keys = new ProtectedKeyStore(Path.Combine(root, "vllm-synthetic-key")); keys.Save("SYNTHETIC_VLLM_TOKEN");
        var paths = new List<string>(); var claude = false;
        await using var mock = new LoopbackHttp(async (request, stream, ct) =>
        {
            paths.Add(request.Path);
            using var body = JsonDocument.Parse(request.Body);
            check(body.RootElement.Text("model") == "hosted-model", "compatibility check retains supplied model");
            if (claude && request.Path == "/v1/messages")
            { await LoopbackHttp.Reply(stream, 200, "application/json", AnthropicTests.Message().ToJsonString(), ct); return; }
            if (request.Path == "/v1/chat/completions")
            { await LoopbackHttp.Reply(stream, 200, "application/json", "{\"id\":\"vllm_chat\",\"model\":\"hosted-model\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}}]}", ct); return; }
            await LoopbackHttp.Reply(stream, 400, "application/json", "{\"error\":{\"message\":\"litellm.BadRequestError: Hosted_vllmException - unsupported endpoint /responses\"}}", ct);
        });
        var input = new ProviderSettings { Endpoint = mock.BaseUri.AbsoluteUri + "v1", Deployment = "hosted-model", Protocol = ProviderProtocol.GatewayAuto };
        var connected = await GatewayConnector.Connect(input, keys, CancellationToken.None);
        check(connected.Settings.IsChat && paths.SequenceEqual(new[] { "/v1/messages", "/v1/chat/completions" }), "v1 base with hosted-vLLM 400 chooses working chat API instead of Responses");
        paths.Clear(); claude = true;
        connected = await GatewayConnector.Connect(input, keys, CancellationToken.None);
        check(connected.Settings.IsAnthropic && paths.SequenceEqual(new[] { "/v1/messages" }), "Claude-compatible gateway never probes Responses first");
        var full = new ProviderSettings { Endpoint = mock.BaseUri.AbsoluteUri + "v1/chat/completions", Deployment = "hosted-model", Protocol = ProviderProtocol.AnthropicMessages };
        full.NormalizeInferenceEndpoint();
        check(full.IsChat && full.RequestUri(false).AbsolutePath == "/v1/chat/completions", "full chat URL overrides stale protocol and is not duplicated");
        full.Endpoint = mock.BaseUri.AbsoluteUri + "v1/messages"; full.NormalizeInferenceEndpoint();
        check(full.IsAnthropic && full.RequestUri(false).AbsolutePath == "/v1/messages", "full Claude URL overrides stale Responses protocol");
        using var error = JsonDocument.Parse("{\"error\":{\"message\":\"Hosted_vllmException \\\\u00B7 unsupported parameter\",\"param\":\"reasoning_effort\"}}");
        var detail = ProviderErrors.Detail(error.RootElement);
        check(detail.Contains('·') && !detail.Contains("\\u00B7") && detail.Contains("reasoning_effort"), "nested Unicode and rejected parameter render as readable diagnostic");
        var history = Path.Combine(root, "legacy-vllm-history");
        await using var desk = new DeskController(history);
        // This matches the old basic settings that had already been resolved to Responses.
        desk.Configure(new() { Endpoint = input.Endpoint, Deployment = input.Deployment, Protocol = ProviderProtocol.GatewayResponses, UseAdvancedSettings = false }, "SYNTHETIC_VLLM_TOKEN");
        paths.Clear(); var status = await desk.CheckProviderAsync();
        check(desk.State.Provider.IsAnthropic && status.Contains("연결 확인") && paths.First() == "/v1/messages", "existing basic Responses configuration is rechecked with Claude first");
    }
}
