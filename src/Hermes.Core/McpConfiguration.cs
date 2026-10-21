using System.Collections;
using System.Text.Json;
namespace Hermes.Core;
public static class McpConfiguration
{
    public const string Name = "hermes_project";
    public static Dictionary<string, object> BuiltIn(string serverExe, string root, bool writable, string? memoryDirectory = null, string? taskId = null)
    {
        root = Safety.ProjectRoot(root); serverExe = Path.GetFullPath(serverExe); Safety.RejectReparseAncestors(serverExe);
        if (!File.Exists(serverExe) || !serverExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Local MCP host executable missing");
        var args = new List<string> { "--mcp-local-stdio", "--root", root }; if (writable) args.Add("--allow-write");
        var environment = new Dictionary<string, string> { ["HERMES_BRIDGE_KEY"] = "", ["OPENAI_API_KEY"] = "" };
        if (memoryDirectory != null && taskId != null) { environment["HERMES_MEMORY_DIR"] = memoryDirectory; environment["HERMES_MEMORY_TASK"] = taskId; }
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnet) environment["DOTNET_ROOT"] = dotnet;
        return new() { [Name] = new { command = serverExe, args, cwd = root, enabled = true, required = true, startup_timeout_sec = 15, tool_timeout_sec = 120, default_tools_approval_mode = "auto", env = environment } };
    }
    public static string Toml(object? value)
    {
        var element = JsonSerializer.SerializeToElement(value);
        string Convert(JsonElement e) => e.ValueKind switch {
            JsonValueKind.Object => "{ " + string.Join(", ", e.EnumerateObject().Select(p => JsonSerializer.Serialize(p.Name) + " = " + Convert(p.Value))) + " }",
            JsonValueKind.Array => "[" + string.Join(", ", e.EnumerateArray().Select(Convert)) + "]",
            JsonValueKind.String => JsonSerializer.Serialize(e.GetString()),
            JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Number => e.GetRawText(),
            _ => throw new IOException("Unsupported ephemeral MCP config value")
        };
        return Convert(element);
    }
}
