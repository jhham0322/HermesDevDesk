using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;

internal static class CompanyEngineTests
{
    internal static async Task Run(string baseDir, string exe, string reportPath)
    {
        var root = Path.Combine(baseDir, "company-engine-project"); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "hello.txt"), "HERMES_LOCAL_TERMINAL_OK");
        var data = Path.Combine(baseDir, "company-engine-history");
        var queue = new ConcurrentQueue<Action>(); int requests = 0; bool delay = false;
        const string key = "SYNTHETIC_ONLY_OFFICIAL_ENGINE_TEST";
        var response = JsonNode.Parse("""{"id":"resp_company_engine","object":"response","created_at":1,"status":"completed","model":"company-test-deployment","output":[{"id":"msg_engine","type":"message","role":"assistant","status":"completed","content":[{"type":"output_text","text":"COMPANY_ENGINE_OK","annotations":[]}]}],"usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120,"input_tokens_details":{"cached_tokens":10},"output_tokens_details":{"reasoning_tokens":5}}}""")!.AsObject();
        await using var mock = new LoopbackHttp(async (r, s, ct) =>
        {
            if (!r.Headers.TryGetValue("api-key", out var header) || header != key) throw new Exception("Synthetic key not sent as api-key");
            using var body = JsonDocument.Parse(r.Body);
            if (body.RootElement.Text("model") != "company-test-deployment") throw new Exception("Deployment changed");
            Interlocked.Increment(ref requests);
            if (delay) await Task.Delay(30000, ct);
            await LoopbackHttp.Reply(s, 200, "text/event-stream", ResponseWire.Sse(response), ct);
        });
        await using var desk = new DeskController(data, a => queue.Enqueue(a), true);
        desk.Configure(new() { Endpoint = mock.BaseUri.AbsoluteUri, Deployment = "company-test-deployment" }, key);
        desk.Diagnostic += text => Console.WriteLine("CUSTOM_ENGINE_DIAGNOSTIC: " + text);
        var project = desk.Register(root); await desk.ConnectAsync(exe, root);
        void Pump() { while (queue.TryDequeue(out var a)) a(); }
        async Task Finish(WorkItem task, int seconds = 45)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (DeskController.IsRunning(task) && DateTime.UtcNow < until) { Pump(); foreach (var approval in desk.Approvals.ToList()) await desk.AnswerAsync(approval, false); await Task.Delay(50); }
            Pump(); if (DeskController.IsRunning(task)) throw new Exception("Company engine test timeout");
        }
        var task = desk.NewTask(project, "Official custom provider local fixture");
        await desk.StartAsync(task, "Reply COMPANY_ENGINE_OK. Do not call tools.", desk.Models.Single(), false); await Finish(task);
        var messageSeen = task.Transcript.Any(e => e.Text.Contains("COMPANY_ENGINE_OK"));
        if (!messageSeen || requests != 1 || desk.State.Usage.Count != 1 || desk.State.Usage[0].Tokens.Total != 120) throw new Exception("Official engine response or usage mismatch: " + task.Status + "; " + string.Join("\n", task.Transcript.Select(e => e.Text)));
        Console.WriteLine("PASS official Codex custom provider + local company mock + response/usage, no real company/model call");
        delay = true; await desk.StartAsync(task, "Reply after delay.", desk.Models.Single(), false); Pump();
        var until = DateTime.UtcNow.AddSeconds(10); while (requests < 2 && DateTime.UtcNow < until) { Pump(); await Task.Delay(50); }
        await desk.StopAsync(task); await Finish(task, 15);
        if (desk.State.Usage.Count != 2 || desk.State.Usage[^1].Status != "cancelled" || desk.State.Usage[^1].Tokens.Total != null) throw new Exception("Cancelled request recorded incorrectly");
        Console.WriteLine("PASS official turn interrupt cancels company HTTP request, unknown usage preserved");
        await using var terminal = new LocalCommandSession(Path.Combine(data, "terminal-home"));
        var commandOutput = new List<string>(); terminal.Output += s => commandOutput.Add(s);
        var result = await terminal.RunAsync(exe, root, "Get-Content -LiteralPath 'hello.txt'", false);
        var terminalOk = (result + string.Join("", commandOutput)).Contains("HERMES_LOCAL_TERMINAL_OK");
        if (!terminalOk) throw new Exception("Official command/exec result missing: " + result);
        Console.WriteLine("PASS real official command/exec, isolated home, no API credential or generation");
        var terminalStop = terminal.RunAsync(exe, root, "Start-Sleep -Seconds 20; Write-Output 'UNEXPECTED_TERMINAL_END'", false);
        await Task.Delay(1000); await terminal.StopAsync(); var stopped = false;
        try { var stoppedResult = await terminalStop.WaitAsync(TimeSpan.FromSeconds(8)); stopped = !stoppedResult.Contains("UNEXPECTED_TERMINAL_END"); }
        catch (IOException) { stopped = true; }
        if (!stopped || terminal.Running) throw new Exception("Terminal stop did not end the owned command request");
        Console.WriteLine("PASS one-off terminal stop ends only the owned command App Server request");
        desk.KeyStore.DeleteOwnKey(); desk.Save();
        var report = new { officialExecutable = exe, customProvider = true, mockOnly = true, actualCompanyAuthVerified = false, responseObserved = messageSeen, providerHttpAttempts = requests, tokenTotalFirst = 120, interruptVerified = true, terminalVerified = terminalOk, terminalStopVerified = stopped, atUtc = DateTime.UtcNow, scratch = baseDir };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
