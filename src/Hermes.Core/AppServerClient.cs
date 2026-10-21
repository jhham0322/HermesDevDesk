using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Hermes.Core;
public sealed record ServerEvent(string Method, JsonElement Params);
public sealed record ServerRequest(JsonElement Id, string Method, JsonElement Params);

public sealed class AppServerClient : IAsyncDisposable
{
    private Process? process;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    private long nextId;
    private bool closing;
    public event Action<ServerEvent>? Notification;
    public event Action<ServerRequest>? Approval;
    public event Action<string>? Diagnostic;
    public event Action? Disconnected;
    public bool Connected => process is { HasExited: false } && !closing;
    public async Task ConnectAsync(string exe, string cwd, string? isolatedHome = null, Uri? companyBridge = null, string? bridgeKey = null, string? deployment = null, Dictionary<string, object>? mcpServers = null)
    {
        if (process != null) throw new InvalidOperationException("이미 연결되었습니다.");
        exe = Path.GetFullPath(exe);
        if (!File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("공식 codex.exe 경로를 선택하세요.");
        var info = new ProcessStartInfo(exe) { WorkingDirectory = Safety.ProjectRoot(cwd), RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in new[] { "app-server", "--listen", "stdio://", "-c", "approval_policy=\"on-request\"", "-c", "approvals_reviewer=\"user\"", "-c", "sandbox_mode=\"read-only\"", "-c", "windows.sandbox=\"unelevated\"", "-c", "web_search=\"disabled\"", "-c", "analytics.enabled=false", "-c", "features.apps=false", "-c", "features.multi_agent=false", "-c", "mcp_servers={}" }) info.ArgumentList.Add(arg);
        // Existing official login is resolved by Codex itself. Never open credential files.
        // Remove alternate billing/provider credentials from the child environment.
        foreach (var key in info.Environment.Keys.Where(k => k.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) || k.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) || k.StartsWith("AWS_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        if (isolatedHome != null)
        {
            Directory.CreateDirectory(isolatedHome); Safety.RejectReparseAncestors(isolatedHome); info.Environment["CODEX_HOME"] = isolatedHome;
            var profile = Path.Combine(isolatedHome, "private-profile"); Directory.CreateDirectory(profile);
            info.Environment["HOME"] = profile; info.Environment["USERPROFILE"] = profile;
            foreach (var setting in new[] { "features.plugins=false", "features.recommended_plugins=false", "skills.include_instructions=false", "skills.bundled.enabled=false", "feedback.enabled=false", "otel.exporter=\"none\"", "otel.trace_exporter=\"none\"", "otel.metrics_exporter=\"none\"" }) { info.ArgumentList.Add("-c"); info.ArgumentList.Add(setting); }
        }
        if (companyBridge != null)
        {
            if (!companyBridge.IsLoopback || string.IsNullOrEmpty(bridgeKey) || string.IsNullOrEmpty(deployment)) throw new IOException("회사 bridge 설정 오류");
            info.Environment["HERMES_BRIDGE_KEY"] = bridgeKey; Safety.RegisterSecret(bridgeKey);
            var config = new Dictionary<string, string>
            {
                ["model_provider"] = JsonSerializer.Serialize("hermes_company"), ["model"] = JsonSerializer.Serialize(deployment),
                ["model_providers.hermes_company.name"] = JsonSerializer.Serialize("Hermes company endpoint"),
                ["model_providers.hermes_company.base_url"] = JsonSerializer.Serialize(companyBridge.AbsoluteUri),
                ["model_providers.hermes_company.wire_api"] = JsonSerializer.Serialize("responses"),
                ["model_providers.hermes_company.env_key"] = JsonSerializer.Serialize("HERMES_BRIDGE_KEY"),
                ["model_providers.hermes_company.requires_openai_auth"] = "false", ["model_providers.hermes_company.supports_websockets"] = "false",
                ["model_providers.hermes_company.request_max_retries"] = "0", ["model_providers.hermes_company.stream_max_retries"] = "0",
                ["shell_environment_policy.inherit"] = JsonSerializer.Serialize("core"), ["shell_environment_policy.exclude"] = "[\"HERMES_BRIDGE_KEY\",\"*API_KEY*\",\"*TOKEN*\"]"
            };
            foreach (var pair in config) { info.ArgumentList.Add("-c"); info.ArgumentList.Add(pair.Key + "=" + pair.Value); }
        }
        info.Environment["RUST_LOG"] = "warn";
        if (mcpServers != null) { info.ArgumentList.Add("-c"); info.ArgumentList.Add("mcp_servers=" + McpConfiguration.Toml(mcpServers)); }
        process = Process.Start(info) ?? throw new IOException("App Server 실행 실패");
        _ = ReadMessagesAsync();
        _ = ReadDiagnosticsAsync();
        try
        {
            await RequestAsync("initialize", new { clientInfo = new { name = "hermes_devdesk", title = "Hermes DevDesk (independent client)", version = "0.8.14" }, capabilities = new { experimentalApi = true } });
            await WriteAsync(new { method = "initialized", @params = new { } });
        }
        catch { await DisposeAsync(); throw; }
    }
    public async Task<JsonElement> RequestAsync(string method, object parameters, int timeoutSeconds = 45)
    {
        if (!Connected) throw new IOException("App Server 연결이 끊겼습니다.");
        var id = Interlocked.Increment(ref nextId);
        var key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[key] = completion;
        try
        {
            await WriteAsync(new { id, method, @params = parameters });
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
        }
        finally { pending.TryRemove(key, out _); }
    }
    public Task RespondAsync(ServerRequest request, object result) => WriteAsync(new { id = request.Id, result });
    public Task RejectUnsupportedAsync(ServerRequest request) => WriteAsync(new { id = request.Id, error = new { code = -32601, message = "Unsupported or denied by Hermes DevDesk safety policy" } });
    private async Task WriteAsync(object message)
    {
        await writer.WaitAsync();
        try
        {
            var stream = process?.StandardInput ?? throw new IOException("엔진 없음");
            await stream.WriteLineAsync(JsonSerializer.Serialize(message));
            await stream.FlushAsync();
        }
        finally { writer.Release(); }
    }
    private async Task ReadMessagesAsync()
    {
        try
        {
            while (process != null && await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.Length > 128 * 1024 * 1024) throw new IOException("프로토콜 메시지 크기 초과");
                using var doc = JsonDocument.Parse(line);
                var msg = doc.RootElement;
                if (msg.TryGetProperty("method", out var method))
                {
                    var parameters = msg.Property("params")?.Clone() ?? JsonSerializer.SerializeToElement(new { });
                    if (msg.TryGetProperty("id", out var reqId))
                    {
                        var request = new ServerRequest(reqId.Clone(), method.GetString()!, parameters);
                        if (Approval != null) Approval(request); else await RejectUnsupportedAsync(request);
                    }
                    else Notification?.Invoke(new(method.GetString()!, parameters));
                }
                else if (msg.TryGetProperty("id", out var id) && pending.TryRemove(id.ToString(), out var c))
                {
                    if (msg.TryGetProperty("error", out var error)) c.TrySetException(new IOException(Safety.Redact(error.Text("message"))));
                    else if (msg.TryGetProperty("result", out var result)) c.TrySetResult(result.Clone());
                    else c.TrySetException(new IOException("잘못된 프로토콜 응답"));
                }
            }
        }
        catch (Exception ex) { if (!closing) Diagnostic?.Invoke(Safety.Redact(ex.Message)); }
        finally
        {
            foreach (var c in pending.Values) c.TrySetException(new IOException("App Server 연결 종료"));
            pending.Clear();
            if (!closing) Disconnected?.Invoke();
        }
    }
    private async Task ReadDiagnosticsAsync()
    {
        try { while (process != null && await process.StandardError.ReadLineAsync() is { } line) Diagnostic?.Invoke(Safety.Redact(line)); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }
    public async ValueTask DisposeAsync()
    {
        closing = true;
        if (process is { HasExited: false })
        {
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
        process?.Dispose(); process = null;
    }
    public static string? FindInstalledCodex()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "engine", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
        if (File.Exists(bundled)) return bundled;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        { var file = Path.Combine(folder, "codex.exe"); if (File.Exists(file)) return file; }
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (!Directory.Exists(local)) return null;
        return Directory.EnumerateDirectories(local).Select(d => Path.Combine(d, "codex.exe")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }
}
