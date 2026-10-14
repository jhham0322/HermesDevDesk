using Hermes.Core;
namespace Hermes.Core;
public sealed class LocalCommandSession : IAsyncDisposable
{
    private AppServerClient? client;
    private int active;
    public bool Running => Volatile.Read(ref active) == 1;
    public event Action<string>? Output;
    private readonly string home;
    public LocalCommandSession(string home) => this.home = home;
    public async Task<string> RunAsync(string exe, string root, string command, bool writable)
    {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0) throw new IOException("명령이 이미 실행 중입니다.");
        try
        {
            root = Safety.ProjectRoot(root);
            if (string.IsNullOrWhiteSpace(command) || command.Length > 20000) throw new IOException("명령 내용을 확인하세요.");
            if (client != null) await client.DisposeAsync();
            client = new(); client.Approval += request => _ = client.RejectUnsupportedAsync(request);
            await client.ConnectAsync(exe, root, home);
            var policy = writable ? (object)new { type = "workspaceWrite", writableRoots = new[] { root }, networkAccess = false, excludeTmpdirEnvVar = true, excludeSlashTmp = true } : new { type = "readOnly", networkAccess = false };
            // Official Windows sandbox supports buffered command/exec, not streaming or custom outputBytesCap.
            var result = await client.RequestAsync("command/exec", new { command = new[] { "powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command }, cwd = root, streamStdoutStderr = false, streamStdin = false, tty = false, timeoutMs = 30000, sandboxPolicy = policy }, 40);
            var output = Safety.Redact(result.Text("stdout") + result.Text("stderr"));
            if (output.Length > 200000) output = output[..200000] + "\n[display truncated]";
            var text = $"exit={result.Property("exitCode")?.ToString() ?? "unknown"}\n{output}";
            Output?.Invoke(text); return text;
        }
        finally { Interlocked.Exchange(ref active, 0); }
    }
    // This client belongs exclusively to the current one-off command, never the task engine or another app.
    public async Task StopAsync() { if (client != null && Running) await client.DisposeAsync(); }
    public async ValueTask DisposeAsync() { if (client != null) await client.DisposeAsync(); }
}

