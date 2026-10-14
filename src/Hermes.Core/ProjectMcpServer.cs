using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Hermes.Core;

// Credential-free built-in MCP server. Only these scoped operations exist; no shell/network/auth API.
public sealed class ProjectMcpServer
{
    private readonly string root; private readonly bool writable;
    private readonly SemaphoreSlim output = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
    private readonly ConcurrentDictionary<string, byte> active = new();
    public ProjectMcpServer(string root, bool writable) { this.root = Safety.ProjectRoot(root); this.writable = writable; }
    public async Task RunAsync()
    {
        Console.InputEncoding = Encoding.UTF8; Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > 1_100_000) continue;
                JsonElement message;
                try { using var document = JsonDocument.Parse(line); message = document.RootElement.Clone(); } catch (JsonException) { continue; }
                if (message.Property("method") is not { } method)
                {
                    if (message.Property("id") is { } replyId && pending.TryRemove(replyId.ToString(), out var completion))
                        completion.TrySetResult(message.Property("result")?.Clone() ?? JsonSerializer.SerializeToElement(new { action = "cancel" }));
                    continue;
                }
                if (message.Property("id") is not { } id) continue;
                var key = id.ToString(); if (!active.TryAdd(key, 0)) continue;
                _ = Handle(message, id.Clone(), method.GetString() ?? "", key);
            }
        }
        finally { foreach (var p in pending.Values) p.TrySetResult(JsonSerializer.SerializeToElement(new { action = "cancel" })); }
    }
    private async Task Send(object value) { await output.WaitAsync().ConfigureAwait(false); try { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value)).ConfigureAwait(false); await Console.Out.FlushAsync().ConfigureAwait(false); } finally { output.Release(); } }
    private async Task Handle(JsonElement message, JsonElement id, string method, string key)
    {
        try
        {
            object result = method switch
            {
                "initialize" => new { protocolVersion = "2025-11-25", capabilities = new { tools = new { listChanged = false }, resources = new { subscribe = false, listChanged = false } }, serverInfo = new { name = "hermes-project-local", title = "Hermes project files (local, no credentials)", version = "0.4.0" } },
                "ping" => new { },
                "tools/list" => new { tools = Tools() },
                "resources/list" => new { resources = Array.Empty<object>() },
                "resources/templates/list" => new { resourceTemplates = Array.Empty<object>() },
                "tools/call" => await Call(message.Property("params") ?? JsonSerializer.SerializeToElement(new { })).ConfigureAwait(false),
                _ => throw new IOException("Unsupported local MCP method")
            };
            await Send(new { jsonrpc = "2.0", id, result }).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or TimeoutException or DecoderFallbackException)
        {
            await Send(new { jsonrpc = "2.0", id, result = Result("Local MCP error: " + Safety.Redact(e.Message), true) }).ConfigureAwait(false);
        }
        finally { active.TryRemove(key, out _); }
    }
    private object[] Tools()
    {
        object Tool(string name, string description, object properties, string[] required, bool readOnly = true) => new { name, description, inputSchema = new { type = "object", properties, required, additionalProperties = false }, annotations = new { readOnlyHint = readOnly, destructiveHint = false, openWorldHint = false } };
        var result = new List<object>
        {
            Tool("list_project_files", "List one directory inside the selected project, excluding secret/link paths.", new { relativeDirectory = new { type = "string" } }, []),
            Tool("read_project_text", "Read at most 1MB of UTF-8 project text; secrets, links and outside paths are blocked.", new { relativePath = new { type = "string" } }, ["relativePath"]),
            Tool("request_confirmation", "Ask the user to approve or decline this local operation. No credentials are requested.", new { message = new { type = "string", maxLength = 2000 } }, ["message"]),
            Tool("error_demo", "Return a deliberate local MCP tool error to verify error handling.", new { }, [])
        };
        if (writable) result.Add(Tool("write_project_note", "Create a NEW UTF-8 project file after explicit user approval; no overwrite/deletion.", new { relativePath = new { type = "string" }, text = new { type = "string", maxLength = 10000 } }, ["relativePath", "text"], false));
        return result.ToArray();
    }
    private static object Result(string text, bool error = false) => new { content = new[] { new { type = "text", text = Safety.Redact(text) } }, isError = error };
    private async Task<bool> Confirm(string message)
    {
        var id = "hermes-confirm-" + Guid.NewGuid().ToString("N"); var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = completion;
        try
        {
            await Send(new { jsonrpc = "2.0", id, method = "elicitation/create", @params = new { mode = "form", message = Safety.Redact(message), requestedSchema = new { type = "object", properties = new { allow = new { type = "boolean", title = "Approve this one operation", @default = false } }, required = new[] { "allow" } } } }).ConfigureAwait(false);
            var reply = await completion.Task.WaitAsync(TimeSpan.FromSeconds(90)).ConfigureAwait(false);
            return reply.Text("action") == "accept" && reply.Property("content")?.Property("allow") is { ValueKind: JsonValueKind.True };
        }
        finally { pending.TryRemove(id, out _); }
    }
    private async Task<object> Call(JsonElement parameters)
    {
        var name = parameters.Text("name"); var args = parameters.Property("arguments") ?? JsonSerializer.SerializeToElement(new { });
        switch (name)
        {
            case "list_project_files": return Result(JsonSerializer.Serialize(WorkspaceFiles.List(root, args.Text("relativeDirectory"))));
            case "read_project_text": return Result(WorkspaceFiles.Read(root, args.Text("relativePath")));
            case "error_demo": return Result("HERMES_MCP_EXPECTED_ERROR", true);
            case "request_confirmation":
                var message = args.Text("message"); if (message.Length is < 1 or > 2000) throw new IOException("Confirmation message length");
                return Result(await Confirm(message).ConfigureAwait(false) ? "HERMES_MCP_APPROVED" : "HERMES_MCP_DECLINED");
            case "write_project_note":
                if (!writable) throw new IOException("Read-only MCP session");
                var relative = args.Text("relativePath"); var text = args.Text("text");
                if (text.Length > 10000) throw new IOException("Note exceeds 10000 characters");
                var path = Safety.Resolve(root, relative);
                if (File.Exists(path) || Directory.Exists(path) || !Directory.Exists(Path.GetDirectoryName(path))) throw new IOException("New file in an existing project directory required; existing data preserved");
                if (!await Confirm("Create NEW project file " + relative + "\n\n" + Safety.Redact(text)).ConfigureAwait(false)) return Result("HERMES_MCP_DECLINED: no file created");
                path = Safety.Resolve(root, relative);
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { var bytes = Encoding.UTF8.GetBytes(Safety.Redact(text)); await file.WriteAsync(bytes).ConfigureAwait(false); }
                return Result("Created " + relative);
            default: throw new IOException("Unknown or disabled local MCP tool");
        }
    }
}
