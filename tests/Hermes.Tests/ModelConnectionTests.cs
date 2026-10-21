using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hermes.Core;

internal static class ModelConnectionTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        const string tokenA = "SYNTHETIC_MODEL_A_TOKEN_123", tokenB = "SYNTHETIC_MODEL_B_TOKEN_456";
        var requests = new List<(string Host, string Model, string Auth, string Path)>();
        LoopbackHttp Server(string host) => new(async (request, stream, ct) =>
        {
            using var body = JsonDocument.Parse(request.Body);
            requests.Add((host, body.RootElement.Text("model"), request.Headers.GetValueOrDefault("Authorization", ""), request.Path));
            await LoopbackHttp.Reply(stream, 200, "application/json", AnthropicTests.Message(host).ToJsonString(), ct);
        });
        await using var serverA = Server("A"); await using var serverB = Server("B");
        var directory = Path.Combine(root, "model-connections");
        await using var desk = new DeskController(directory);
        ProviderSettings Settings(string name, string url) => new() { Deployment = name, Endpoint = url, Protocol = ProviderProtocol.AnthropicMessages, Authentication = ProviderAuth.BearerKey };
        desk.SaveModelConnection(Settings("alpha", serverA.BaseUri.AbsoluteUri), tokenA);
        desk.SaveModelConnection(Settings("beta", serverB.BaseUri.AbsoluteUri + "v1/messages"), tokenB);
        check(desk.State.ModelConnections.Count == 2, "two independent model connections saved");
        foreach (var model in new[] { "alpha", "beta", "alpha" })
        {
            check(desk.SelectModelConnection(model), "saved model selected " + model);
            await using var bridge = new CompanyBridge(desk.State.Provider, desk.KeyStore);
            bridge.BeginTask("selected", model);
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, bridge.TaskBaseUri("selected").AbsoluteUri + "/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bridge.Capability);
            request.Content = new StringContent("{\"model\":\"wrong-model\",\"input\":\"hello\",\"stream\":false}", Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            check(response.IsSuccessStatusCode, "selected model request completes");
        }
        check(requests.Select(r => r.Host).SequenceEqual(new[] { "A", "B", "A" }), "actual requests switch between independent server URLs");
        check(requests.Select(r => r.Model).SequenceEqual(new[] { "alpha", "beta", "alpha" }), "actual upstream body uses the selected model each time");
        check(requests.Select(r => r.Auth).SequenceEqual(new[] { "Bearer " + tokenA, "Bearer " + tokenB, "Bearer " + tokenA }), "each server receives only its model token");
        check(requests.All(r => r.Path == "/v1/messages"), "base and full Claude URLs resolve without duplicate suffix");
        var exported = desk.ExportModelConnections();
        check(!File.ReadAllText(Path.Combine(directory, "state.json")).Contains(tokenA), "state JSON excludes plaintext model tokens");
        await using var imported = new DeskController(Path.Combine(root, "imported-models"));
        imported.ImportModelConnections(exported);
        imported.SelectModelConnection("beta"); check(imported.KeyStore.ReadOwnKey() == tokenB, "JSON round-trip restores distinct tokens");
        var beta = imported.State.ModelConnections.Single(c => c.Settings.Deployment == "beta");
        imported.SaveModelConnection(Settings("beta-renamed", serverA.BaseUri.AbsoluteUri), null, beta.Id);
        check(imported.State.ModelConnections.Count == 2 && !imported.DisplayModels.Contains("beta") && imported.KeyStore.ReadOwnKey() == tokenB, "edit supports rename and URL change while retaining own token");
        var before = imported.ExportModelConnections();
        try { imported.ImportModelConnections("[{\"ModelName\":\"invalid\",\"ModelUrl\":\"ftp://bad\",\"Token\":\"SYNTHETIC_INVALID_TOKEN\"}]"); throw new Exception("invalid import accepted"); }
        catch (IOException) { check(imported.ExportModelConnections() == before, "invalid import preserves existing connections"); }
        imported.DeleteModelConnection(beta.Id);
        check(!imported.DisplayModels.Contains("beta-renamed") && !imported.KeyStore.HasKey, "delete removes model and active token");
        await using var restart = new DeskController(directory);
        restart.SelectModelConnection("beta"); check(restart.State.Provider.Endpoint.Contains(serverB.BaseUri.Authority) && restart.KeyStore.ReadOwnKey() == tokenB, "connection selection survives restart");
    }
}
