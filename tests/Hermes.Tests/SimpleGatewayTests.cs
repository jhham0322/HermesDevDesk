using System.Text.Json;
using Hermes.Core;

internal static class SimpleGatewayTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var store = new ProtectedKeyStore(Path.Combine(root, "owned-gateway-key")); const string token = "SYNTHETIC_GATEWAY_ONLY_KEY"; store.Save(token);
        var paths = new List<string>(); string mode = "chat"; bool model = true, header = true;
        const string response = "{\"id\":\"resp_owned\",\"object\":\"response\",\"status\":\"completed\",\"output\":[],\"model\":\"gpt-6-luna\"}";
        const string chat = "{\"id\":\"owned_chat\",\"model\":\"gpt-6-luna\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}}]}";
        await using var server = new LoopbackHttp(async (request, stream, ct) =>
        {
            paths.Add(request.Path); using var body = JsonDocument.Parse(request.Body);
            model &= body.RootElement.Text("model") == "gpt-6-luna"; header &= request.Headers.GetValueOrDefault("Authorization") == "Bearer " + token || request.Headers.GetValueOrDefault("api-key") == token;
            if (mode == "redirect") { await LoopbackHttp.Reply(stream, 302, "application/json", "{}", ct, new() { ["Location"] = "https://example.invalid" }); return; }
            if (mode == "model404") { await LoopbackHttp.Reply(stream, 404, "application/json", "{\"error\":{\"code\":\"model_not_found\",\"message\":\"model not found\"}}", ct); return; }
            if (mode == "error") { await LoopbackHttp.Reply(stream, 500, "application/json", "{\"error\":{\"message\":\"owned backend down\"}}", ct); return; }
            if (mode == "auth") { await LoopbackHttp.Reply(stream, request.Headers.GetValueOrDefault("api-key") == token ? 200 : 401, "application/json", response, ct); return; }
            if (mode == "html") { await LoopbackHttp.Reply(stream, 200, "text/html", "<html>owned sign in page</html>", ct); return; }
            if (mode == "responses" && request.Path == "/v1/responses") { await LoopbackHttp.Reply(stream, 200, "application/json", response, ct); return; }
            if (mode == "chat" && request.Path == "/v1/chat/completions") { await LoopbackHttp.Reply(stream, 200, "application/json", chat, ct); return; }
            if (mode == "anthropic" && request.Path == "/v1/messages") { await LoopbackHttp.Reply(stream, 200, "application/json", AnthropicTests.Message().ToJsonString(), ct); return; }
            await LoopbackHttp.Reply(stream, 404, "application/json", "{}", ct);
        });
        var settings = new ProviderSettings { Endpoint = server.BaseUri.AbsoluteUri, Deployment = "gpt-6-luna", Protocol = ProviderProtocol.GatewayAuto, Authentication = ProviderAuth.BearerKey };
        var result = await GatewayConnector.Connect(settings, store, CancellationToken.None);
        check(result.Settings.Protocol == ProviderProtocol.GatewayChatCompletions && result.Settings.Endpoint.EndsWith("/v1"), "three field gateway finds actual chat route without a provider-specific suffix");
        check(model && header && !paths.Any(p => p.Contains("openai")), "gateway preserves exact configured model and key on same supplied server");
        paths.Clear(); var complete = new ProviderSettings { Endpoint = server.BaseUri.AbsoluteUri.TrimEnd('/') + "/v1/chat/completions", Deployment = settings.Deployment, Protocol = ProviderProtocol.GatewayAuto };
        await GatewayConnector.Connect(complete, store, CancellationToken.None); check(paths.SequenceEqual(new[] { "/v1/chat/completions" }), "complete inference URL is used once without duplicated route");
        paths.Clear(); mode = "responses"; result = await GatewayConnector.Connect(settings, store, CancellationToken.None); check(result.Settings.Protocol == ProviderProtocol.GatewayResponses && paths.Last() == "/v1/responses", "gateway detects Responses endpoint");
        paths.Clear(); mode = "auth"; result = await GatewayConnector.Connect(settings, store, CancellationToken.None); check(result.Settings.Authentication == ProviderAuth.ApiKeyHeader && paths.Count == 2, "same route selects supported API key header after bearer rejection");
        paths.Clear(); mode = "anthropic"; result = await GatewayConnector.Connect(settings, store, CancellationToken.None); check(result.Settings.Protocol == ProviderProtocol.AnthropicMessages, "three field gateway detects Anthropic endpoint");
        foreach (var failure in new[] { "redirect", "model404", "error", "html" })
        {
            paths.Clear(); mode = failure; var rejected = false;
            try { await GatewayConnector.Connect(settings, store, CancellationToken.None); } catch (IOException ex) { rejected = !ex.Message.Contains(token); }
            check(rejected && (failure == "html" || paths.Count == 1), "gateway stops safely for " + failure + " without exposing key");
        }
        await using var desk = new DeskController(Path.Combine(root, "pending-model-history")); desk.RememberModel("gpt-6-luna"); desk.Save();
        try { desk.Configure(new() { Endpoint = "not a URL", Deployment = "gpt-6-luna" }); } catch (IOException) { }
        check(desk.DisplayModels.First() == "gpt-6-luna" && desk.HasPendingModel && !desk.SubscriptionReady, "save validation failure keeps typed gpt-6-luna separate from connection status");
        await using var reopened = new DeskController(desk.DataDirectory); check(reopened.DisplayModels.First() == "gpt-6-luna", "typed model survives restart even when connection settings were never valid");
        var profiles = Path.Combine(root, "owned-profiles"); Directory.CreateDirectory(Path.Combine(profiles, "0.8.0")); File.WriteAllText(Path.Combine(profiles, "0.8.0", "state.json"), "{}");
        check(AppProfiles.Resolve(profiles) == Path.Combine(profiles, "0.8.0"), "patch update reuses prior app profile without copying credentials");
        Directory.CreateDirectory(Path.Combine(profiles, "data")); File.WriteAllText(Path.Combine(profiles, "data", "state.json"), "{}");
        check(AppProfiles.Resolve(profiles) == Path.Combine(profiles, "data"), "stable profile takes precedence once present");
        check(ConnectionErrors.Describe(new HttpRequestException(HttpRequestError.NameResolutionError, "owned" )).Contains("서버 이름"), "DNS failure has a specific diagnostic");
        check(ConnectionErrors.Describe(new System.Text.Json.JsonException()).Contains("JSON") && !ConnectionErrors.Describe(new IOException("saved key missing")).Contains("usage"), "response parsing and local key failures are not mislabeled as missing usage");
        foreach (var oldProtocol in new[] { 0, 1, 3, 4 })
        {
            var previous = new ProviderSettings { Endpoint = "http://company.invalid/v1", Deployment = "gpt-6-luna", Protocol = (ProviderProtocol)oldProtocol, UseAdvancedSettings = true };
            check(previous.NormalizeRelaySettings() && previous.Protocol == ProviderProtocol.GatewayAuto && !previous.UseAdvancedSettings && previous.Endpoint.EndsWith("/v1"), "removed provider mode migrates to relay auto: " + oldProtocol);
            check(GatewayConnector.Candidates(previous).All(c => !c.RequestUri(false).AbsolutePath.Contains("openai")), "migrated v1 path never receives provider-specific suffix: " + oldProtocol);
        }
        desk.Configure(new() { Endpoint = server.BaseUri.AbsoluteUri, Deployment = "gpt-6-luna", Protocol = ProviderProtocol.GatewayAuto }, token);
        desk.RememberModel("gpt-6-luna"); check(!desk.HasPendingModel && desk.CanConnectCompany, "retyping saved model does not disable sending");
        desk.RememberModel("different-model"); desk.Save(); desk.RememberModel("gpt-6-luna"); check(!desk.HasPendingModel, "reverting model draft restores saved configuration readiness");
        paths.Clear(); mode = "responses"; await desk.CheckProviderAsync(); await desk.CheckProviderAsync();
        check(paths.Count > 0 && paths.All(p => !p.EndsWith("/models")) && desk.ProviderCheckStatus.Contains("연결 확인"), "repeated basic checks use inference even when model listing is unsupported");
        var missing = new ProtectedKeyStore(Path.Combine(root, "owned-missing-key")); await using var bridge = new CompanyBridge(new() { Endpoint = server.BaseUri.AbsoluteUri, Deployment = "gpt-6-luna", Protocol = ProviderProtocol.GatewayResponses }, missing);
        var note = await bridge.CheckConnectionAsync(); check(note.Contains("저장된 회사 키가 없습니다") && !note.Contains("TLS/키") && !note.Contains("usage"), "missing key reports actual local cause without vague TLS or usage message");
    }
}
