using System.Collections.Concurrent;
using System.Text.Json;
using Hermes.Core;
internal static class McpTests
{
    internal static async Task Run(string root, string cli, string reportPath)
    {
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "hello.txt"), "HERMES_MCP_READ_OK"); File.WriteAllText(Path.Combine(root, ".env"), "TEST_ONLY_EXCLUDED");
        var queue = new ConcurrentQueue<Action>(); var records = new List<string>();
        await using var session = new ProjectMcpSession(root, Path.Combine(Path.GetDirectoryName(root)!, "mcp-engine-home"), Environment.ProcessPath!, true, a => queue.Enqueue(a));
        session.Log += s => Console.WriteLine("MCP: " + s);
        void Pump() { while (queue.TryDequeue(out var a)) a(); }
        await session.ConnectAsync(cli); Pump();
        void Check(bool condition, string text) { if (!condition) throw new Exception(text); records.Add("PASS " + text); Console.WriteLine(records[^1]); }
        Check(session.Tools.Count == 5 && session.Status.Contains("connected"), "official MCP discovery uses credential-free local server");
        var read = await session.CallAsync("read_project_text", JsonSerializer.SerializeToElement(new { relativePath = "hello.txt" })); Pump(); Check(read.GetRawText().Contains("HERMES_MCP_READ_OK"), "official MCP direct tool call reads selected project");
        var list = await session.CallAsync("list_project_files", JsonSerializer.SerializeToElement(new { })); Pump(); Check(!list.GetRawText().Contains(".env"), "MCP listing excludes secret paths");
        var escape = await session.CallAsync("read_project_text", JsonSerializer.SerializeToElement(new { relativePath = "../outside.txt" })); Pump(); Check(escape.Property("isError") is { ValueKind: JsonValueKind.True }, "MCP outside path returns tool error");
        var error = await session.CallAsync("error_demo", JsonSerializer.SerializeToElement(new { })); Pump(); Check(error.GetRawText().Contains("HERMES_MCP_EXPECTED_ERROR") && error.Property("isError") is { ValueKind: JsonValueKind.True }, "MCP error is distinguished from success");
        async Task<JsonElement> ConfirmCall(bool accept, string path)
        {
            var call = session.CallAsync("write_project_note", JsonSerializer.SerializeToElement(new { relativePath = path, text = "HERMES_MCP_WRITE_OK" }));
            var until = DateTime.UtcNow.AddSeconds(15);
            while (session.Approvals.Count == 0 && !call.IsCompleted && DateTime.UtcNow < until) { Pump(); await Task.Delay(25); }
            Pump(); if (session.Approvals.Count != 1) throw new Exception("MCP elicitation absent");
            Check(!File.Exists(Path.Combine(root, path)), "MCP file not created before explicit approval");
            await session.AnswerAsync(session.Approvals.Single(), accept); var result = await call; Pump(); return result;
        }
        var declined = await ConfirmCall(false, "declined.txt"); Check(!File.Exists(Path.Combine(root, "declined.txt")) && declined.GetRawText().Contains("DECLINED"), "MCP decline preserves filesystem");
        await ConfirmCall(true, "approved.txt"); Check(File.ReadAllText(Path.Combine(root, "approved.txt")) == "HERMES_MCP_WRITE_OK", "MCP accepted one-off confirmation creates only new scoped file");
        var conflict = await session.CallAsync("write_project_note", JsonSerializer.SerializeToElement(new { relativePath = "approved.txt", text = "NO_OVERWRITE" })); Pump(); Check(conflict.Property("isError") is { ValueKind: JsonValueKind.True } && File.ReadAllText(Path.Combine(root, "approved.txt")) == "HERMES_MCP_WRITE_OK", "MCP existing data conflict rejected");
        await session.RefreshAsync(); Pump(); Check(session.Tools.Count == 5, "MCP inventory refresh after errors");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { checks = records, modelCalls = 0, credentials = "none", source = "official public CLI + built-in local MCP", root }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
