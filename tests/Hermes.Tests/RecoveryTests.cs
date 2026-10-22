using System.Net;
using System.Text.Json.Nodes;
using Hermes.Core;

internal static class RecoveryTests
{
    private sealed class Handler(Func<int, HttpResponseMessage> reply) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(reply(++Calls));
    }
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var type in new[] { "function_call", "custom_tool_call" })
        {
            var input = JsonNode.Parse("""
                {"input":[{"type":"function_call","namespace":"old","name":"removed","call_id":"c1","arguments":"{}","input":"historical"},
                {"type":"function_call_output","call_id":"c1","output":"historical result"}],"tools":[]}
                """)!.AsObject();
            input["input"]![0]!["type"] = type;
            var adapted = AnthropicAdapter.Request(input, 1024);
            check(adapted.Body["messages"]![0]!["content"]![0]!["text"]!.GetValue<string>().Contains("removed"), "removed historical tool remains reference context: " + type);
            check(adapted.Tools.Count == 0 && adapted.Body["tools"] == null, "historical tool is not executable: " + type);
            check(adapted.Body["messages"]![1]!["content"]![0]!["text"]!.GetValue<string>().Contains("c1"), "historical result correlation preserved");
        }
        foreach (var status in new[] { 429, 503, 529, 400, 401, 500 })
        {
            using var handler = new Handler(i => {
                var response = new HttpResponseMessage((HttpStatusCode)(i < 3 ? status : 200));
                response.Headers.RetryAfter = new(TimeSpan.Zero); return response;
            });
            using var client = new HttpClient(handler);
            int attempts = 0;
            using var response = await TransientHttp.Send(client, () => new(HttpMethod.Post, "https://fixture.invalid"), () => attempts++, CancellationToken.None);
            check(attempts == (status is 429 or 503 or 529 ? 3 : 1), "bounded rejection retry HTTP " + status);
        }
        using var persistent = new Handler(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("We're current experiencing high demand") };
            response.Headers.RetryAfter = new(TimeSpan.Zero); return response;
        });
        using var persistentClient = new HttpClient(persistent);
        using var final = await TransientHttp.Send(persistentClient, () => new(HttpMethod.Post, "https://fixture.invalid"), () => { }, CancellationToken.None);
        check(persistent.Calls == 3 && await final.Content.ReadAsStringAsync() == "We're current experiencing high demand", "persistent overload preserves original error after three attempts");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try
        {
            using var h = new Handler(_ => new(HttpStatusCode.ServiceUnavailable));
            using var c = new HttpClient(h);
            await TransientHttp.Send(c, () => new(HttpMethod.Post, "https://fixture.invalid"), () => { }, cancelled.Token);
            throw new Exception("Cancellation ignored");
        }
        catch (OperationCanceledException) { check(true, "retry respects cancellation"); }
    }
}
