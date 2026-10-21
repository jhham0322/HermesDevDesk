using System.Diagnostics;
using System.Text.Json;
using Hermes.Core;
internal static class McpMemoryTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var project = Path.Combine(root, "mcp-memory-project"); Directory.CreateDirectory(project); var knowledge = Path.Combine(root, "mcp-memory-store");
        var info = new ProcessStartInfo(Environment.ProcessPath!) { WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "--mcp-local-stdio", "--root", project }) info.ArgumentList.Add(arg);
        info.Environment["HERMES_MEMORY_DIR"] = knowledge; info.Environment["HERMES_MEMORY_TASK"] = "mcp-task";
        using var child = Process.Start(info)!;
        try
        {
            string last = "";
            for (var i = 1; i <= 3; i++)
            {
                await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = i, method = "tools/call", @params = new { name = "error_demo", arguments = new { } } })); await child.StandardInput.FlushAsync();
                last = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) ?? throw new IOException("MCP memory host exited");
            }
            using var reply = JsonDocument.Parse(last);
            check(reply.RootElement.GetProperty("result").GetProperty("content")[0].Text("text").Contains("2회 반복"), "MCP third repeated failure is blocked before tool execution");
            var memory = new ExecutionMemory(knowledge);
            check(memory.Recent(ExecutionMemory.Scope(project)).Count == 2, "MCP memory host persists only two actual failed executions");
            await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "list_project_files", arguments = new { } } })); await child.StandardInput.FlushAsync();
            await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            check(memory.Search(ExecutionMemory.Scope(project), "list_project_files").Single().Tool == "mcp", "MCP successful changed method joins shared local RAG");
        }
        finally { child.StandardInput.Close(); if (!child.WaitForExit(2000)) child.Kill(true); }
    }
}
