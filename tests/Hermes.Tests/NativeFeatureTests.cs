using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;
internal static class NativeFeatureTests
{
    internal const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC";
    internal static async Task Run(string root, string cli, string reportPath)
    {
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "hello.txt"), "HERMES_AGENT_MCP_READ");
        var png = Path.Combine(root, "pixel.png"); File.WriteAllBytes(png, Convert.FromBase64String(Png));
        var records = new List<string>();
        void Check(bool value, string message) { if (!value) throw new Exception(message); records.Add("PASS " + message); Console.WriteLine(records[^1]); }
        void Reject(Action act, string message) { try { act(); } catch (IOException) { Check(true, message); return; } throw new Exception(message); }
        var image = ImageAttachments.Read(root, "pixel.png"); Check(image.Width == 1 && image.Height == 1, "PNG CRC/dimension/header accepted");
        Reject(() => ImageAttachments.Read(root, "../outside.png"), "image outside selected project blocked");
        Reject(() => ImageAttachments.Validate(root, [image, image]), "duplicate image blocked");
        var bad = Convert.FromBase64String(Png); bad[45] ^= 1; File.WriteAllBytes(Path.Combine(root, "bad.png"), bad);
        Reject(() => ImageAttachments.Read(root, "bad.png"), "corrupt PNG CRC rejected");
        File.AppendAllText(png, "changed"); Reject(() => ImageAttachments.Validate(root, [image]), "changed attachment rejected before engine send"); File.WriteAllBytes(png, Convert.FromBase64String(Png));
        var queue = new ConcurrentQueue<Action>(); int requests = 0; bool imageSeen = false, toolResultSeen = false, reviewMode = false; string toolName = "", toolNamespace = "";
        (string Name, string Namespace) FindTool(JsonElement tools, string suffix, string ns = "")
        {
            foreach (var tool in tools.EnumerateArray())
            {
                if (tool.Text("type") == "namespace") { var found = FindTool(tool.GetProperty("tools"), suffix, tool.Text("name")); if (found.Name.Length > 0) return found; }
                if (tool.Text("name").EndsWith(suffix, StringComparison.Ordinal)) return (tool.Text("name"), ns);
            }
            return ("", "");
        }
        await using var mock = new LoopbackHttp(async (request, stream, ct) =>
        {
            using var document = JsonDocument.Parse(request.Body); var body = document.RootElement;
            Interlocked.Increment(ref requests); var raw = body.GetRawText(); imageSeen |= raw.Contains("input_image") && raw.Contains("data:image/");
            var output = new JsonArray();
            if (requests == 1)
            {
                var tool = FindTool(body.GetProperty("tools"), "read_project_text"); toolName = tool.Name; toolNamespace = tool.Namespace;
                if (toolName.Length == 0) throw new Exception("Project MCP tool absent from actual engine catalog");
                var call = new JsonObject { ["id"] = "fc_mcp_04", ["type"] = "function_call", ["status"] = "completed", ["call_id"] = "call_mcp_04", ["name"] = toolName, ["arguments"] = "{\"relativePath\":\"hello.txt\"}" }; if (toolNamespace.Length > 0) call["namespace"] = toolNamespace; output.Add(call);
            }
            else if (requests == 2)
            {
                toolResultSeen |= raw.Contains("HERMES_AGENT_MCP_READ");
                var tool = FindTool(body.GetProperty("tools"), "write_project_note");
                if (tool.Name.Length == 0) throw new Exception("MCP write tool absent");
                var call = new JsonObject { ["id"] = "fc_write_04", ["type"] = "function_call", ["status"] = "completed", ["call_id"] = "call_write_04", ["name"] = tool.Name, ["arguments"] = "{\"relativePath\":\"agent-approved.txt\",\"text\":\"HERMES_AGENT_MCP_WRITE\"}" }; if (tool.Namespace.Length > 0) call["namespace"] = tool.Namespace; output.Add(call);
            }
            else
            {
                toolResultSeen |= raw.Contains("HERMES_AGENT_MCP_READ");
                output.Add(new JsonObject { ["id"] = "msg_native_04", ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = reviewMode ? "{\"findings\":[],\"overall_correctness\":\"patch is correct\",\"overall_explanation\":\"SYNTHETIC_REVIEW_FIXTURE_ONLY\",\"overall_confidence_score\":1.0}" : "HERMES_NATIVE_IMAGE_MCP_OK", ["annotations"] = new JsonArray() }) });
            }
            var response = new JsonObject { ["id"] = "resp_native_" + requests, ["object"] = "response", ["status"] = "completed", ["created_at"] = 1, ["model"] = "company-native-test", ["output"] = output, ["usage"] = JsonNode.Parse("{\"input_tokens\":100,\"output_tokens\":20,\"total_tokens\":120}") };
            await LoopbackHttp.Reply(stream, 200, "text/event-stream", ResponseWire.Sse(response), ct);
        });
        await using var desk = new DeskController(Path.Combine(Path.GetDirectoryName(root)!, "native-history"), a => queue.Enqueue(a), true);
        desk.Configure(new() { Endpoint = mock.BaseUri.AbsoluteUri, Deployment = "company-native-test", Authentication = ProviderAuth.None });
        desk.Diagnostic += line => Console.WriteLine("ENGINE: " + line);
        var project = desk.Register(root); project.LocalMcpEnabled = true; desk.Save();
        await desk.ConnectAsync(cli, root); var task = desk.NewTask(project, "Actual image + MCP engine contract");
        await desk.StartAsync(task, "Use the configured project MCP read_project_text tool on hello.txt, then reply HERMES_NATIVE_IMAGE_MCP_OK. This attached image is synthetic. Do not call other tools.", desk.Models.Single(), true, [image], Environment.ProcessPath);
        void Pump() { while (queue.TryDequeue(out var a)) a(); }
        var until = DateTime.UtcNow.AddSeconds(45);
        while (DeskController.IsRunning(task) && DateTime.UtcNow < until) { Pump(); foreach (var approval in desk.Approvals.ToList()) await desk.AnswerAsync(approval, approval.Method == "mcpServer/elicitation/request"); await Task.Delay(50); }
        Pump();
        if (DeskController.IsRunning(task)) { await desk.StopAsync(task); throw new Exception("Native features engine timeout"); }
        Check(imageSeen, "actual official localImage becomes provider input_image data");
        Check(toolResultSeen && requests == 3, "actual official engine invokes MCP and forwards real tool result");
        Check(task.Transcript.Any(t => t.Text.Contains("HERMES_NATIVE_IMAGE_MCP_OK")) && task.Transcript.Any(t => t.Kind == "MCP 도구"), "MCP result and final answer stored/displayed");
        Check(task.Attachments.Count == 1 && desk.Store.Load().Tasks.Single().Attachments.Count == 1, "image metadata persists without base64 raster in history");
        Check(desk.State.Usage.Count == 3 && desk.State.Usage.Sum(e => e.Tokens.Total ?? 0) == 360, "three observed provider response usages counted once each");
        Check(File.ReadAllText(Path.Combine(root, "agent-approved.txt")) == "HERMES_AGENT_MCP_WRITE", "actual agent MCP elicitation approved through task controller");
        var countBeforeFork = requests; var fork = await desk.ForkAsync(task, Environment.ProcessPath);
        Check(fork.ThreadId != task.ThreadId && desk.State.Tasks.Count == 2 && requests == countBeforeFork, "official conversation fork preserves source and requires no model call");
        Check(fork.Attachments.Count == 1 && fork.Transcript.Any(t => t.Kind == "대화 분기"), "fork keeps app-owned attachment/history metadata");
        foreach (var args in new[] { new[] { "init" }, new[] { "add", "hello.txt" }, new[] { "-c", "user.name=Hermes Review Fixture", "-c", "user.email=fixture@example.invalid", "commit", "-m", "review fixture" } })
        { var result = await GitRunner.Run(root, args); if (result.Exit != 0) throw new Exception(result.Error); }
        File.AppendAllText(Path.Combine(root, "hello.txt"), "\nREVIEW_CHANGE"); reviewMode = true;
        var reviewTask = await desk.StartReviewAsync(project, desk.Models.Single()); until = DateTime.UtcNow.AddSeconds(40);
        while (DeskController.IsRunning(reviewTask) && DateTime.UtcNow < until) { Pump(); foreach (var approval in desk.Approvals.ToList()) await desk.AnswerAsync(approval, false); await Task.Delay(50); }
        Pump(); if (DeskController.IsRunning(reviewTask)) { await desk.StopAsync(reviewTask); throw new Exception("Review engine timeout"); }
        Check(reviewTask.IsReview && reviewTask.ThreadWritable == false && reviewTask.Transcript.Any(e => e.Text.Contains("SYNTHETIC_REVIEW_FIXTURE_ONLY")), "official review/start read-only lifecycle and result captured (fixture, not real review quality)");
        Check(File.ReadAllText(Path.Combine(root, "hello.txt")).EndsWith("REVIEW_CHANGE"), "AI review preserves actual project files");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { checks = records, imageSeen, toolName, toolNamespace, toolResultSeen, providerRequests = requests, companyMockOnly = true, realCompanyVisionVerified = false, realPaidModelCalls = 0, root }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
