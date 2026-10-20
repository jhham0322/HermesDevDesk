using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;

internal static class BrowserBridgeTests
{
    internal static async Task Run(string directory, Action<bool, string> check)
    {
        Directory.CreateDirectory(directory); AppLog.Configure(Path.Combine(directory, "logs"));
        var image = "data:image/png;base64," + NativeFeatureTests.Png;
        var request = new JsonObject
        {
            ["model"] = "fixture",
            ["input"] = new JsonArray(new JsonObject { ["type"] = "function_call_output", ["call_id"] = "screen-call", ["output"] = new JsonArray(
                new JsonObject { ["type"] = "input_text", ["text"] = "SCREEN_TEXT_087" },
                new JsonObject { ["type"] = "input_image", ["image_url"] = image }) })
        };
        var chat = ChatAdapter.Request(request).Body["messages"]!.AsArray();
        check(chat[0]!["role"]!.GetValue<string>() == "tool" && !chat[0]!["content"]!.GetValue<string>().Contains("base64"), "chat screen tool result keeps raster out of plain tool text");
        check(chat[1]!["content"]![0]!["image_url"]!["url"]!.GetValue<string>() == image, "chat screen tool result delivers a real image content block");
        var anthropic = AnthropicAdapter.Request(request, 8192).Body["messages"]![0]!["content"]![0]!["content"]!.AsArray();
        check(anthropic[1]!["type"]!.GetValue<string>() == "image", "Anthropic tool screen image remains multimodal");
        check(new BrowserToolResult("screen", image).ContentItems.Length == 2, "browser result includes official text and image content items");
        using var settings = new HttpClient();
        bool malformed = false;
        await using var upstream = new LoopbackHttp((_, stream, ct) => LoopbackHttp.Reply(stream, malformed ? 200 : 422, "application/json", malformed ? "{}" : "{\"error\":{\"message\":\"MODEL_DOES_NOT_SUPPORT_SCREEN_IMAGES\"}}", ct));
        var config = new ProviderSettings { Endpoint = upstream.BaseUri.AbsoluteUri, Deployment = "fixture", Authentication = ProviderAuth.None, Protocol = ProviderProtocol.GatewayResponses, UseAdvancedSettings = true };
        var keys = new ProtectedKeyStore(Path.Combine(directory, "keys"));
        await using var bridge = new CompanyBridge(config, keys, true);
        settings.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bridge.Capability);
        using var failed = await settings.PostAsync(new Uri(bridge.BaseUri.AbsoluteUri + "/responses"), new StringContent("{\"input\":\"fixture\"}", Encoding.UTF8, "application/json"));
        check(failed.StatusCode == HttpStatusCode.UnprocessableEntity && (await failed.Content.ReadAsStringAsync()).Contains("MODEL_DOES_NOT_SUPPORT_SCREEN_IMAGES"), "local bridge exposes upstream image error instead of only loopback URL");
        malformed = true;
        var chatConfig = new ProviderSettings { Endpoint = upstream.BaseUri.AbsoluteUri, Deployment = "fixture", Authentication = ProviderAuth.None, Protocol = ProviderProtocol.GatewayChatCompletions, UseAdvancedSettings = true };
        await using (var chatBridge = new CompanyBridge(chatConfig, keys, true))
        {
            using var invalid = new HttpRequestMessage(HttpMethod.Post, chatBridge.BaseUri.AbsoluteUri + "/responses") { Content = new StringContent("{\"input\":\"fixture\"}", Encoding.UTF8, "application/json") };
            invalid.Headers.Authorization = new AuthenticationHeaderValue("Bearer", chatBridge.Capability);
            using var invalidResponse = await settings.SendAsync(invalid);
            using var errorPayload = JsonDocument.Parse(await invalidResponse.Content.ReadAsStringAsync());
            check(invalidResponse.StatusCode == HttpStatusCode.BadGateway && errorPayload.RootElement.GetProperty("error").Text("message").Contains("응답 형식"), "missing provider fields return a clear 502 rather than dropping local connection");
        }
        using var expired = await settings.PostAsync(new Uri(bridge.TaskBaseUri("missing-task").AbsoluteUri + "/responses"), new StringContent("{}", Encoding.UTF8, "application/json"));
        check(expired.StatusCode == HttpStatusCode.Conflict && (await expired.Content.ReadAsStringAsync()).Contains("local_session_expired"), "expired local task returns a specific reconnect error");
        int received = 0;
        await using var receiver = new LoopbackHttp((incoming, stream, ct) => { received = incoming.Body.Length; return LoopbackHttp.Reply(stream, 200, "application/json", "{}", ct); });
        using var large = await settings.PostAsync(receiver.BaseUri, new ByteArrayContent(new byte[9_000_000]));
        check(large.IsSuccessStatusCode && received == 9_000_000, "loopback accepts screenshot history requests above previous 8MB limit");
        var oversizeHeader = Encoding.ASCII.GetBytes($"POST /v1/responses HTTP/1.1\r\nContent-Length: {LoopbackHttp.MaxBodyBytes + 1L}\r\n\r\n");
        using var input = new MemoryStream(oversizeHeader);
        try { await LoopbackHttp.Read(input, CancellationToken.None); throw new IOException("oversized body accepted"); }
        catch (LoopbackRequestException ex) { check(ex.StatusCode == 413, "oversized local request has explicit HTTP 413 instead of silent disconnect"); }
    }
}
