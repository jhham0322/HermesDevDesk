using System.Text.Json;
namespace Hermes.Core;
public sealed class ProjectMcpSession : IAsyncDisposable
{
    private readonly string root, home, serverExe;
    private readonly bool writable;
    private readonly Action<Action> dispatch;
    private AppServerClient? engine;
    private LoopbackHttp? offline;
    public int RejectedModelRequests { get; private set; }
    private string threadId = "";
    private int calling;
    public bool Connected => engine?.Connected == true && threadId.Length > 0;
    public List<ServerRequest> Approvals { get; } = [];
    public List<McpToolView> Tools { get; } = [];
    public string Status { get; private set; } = "연결 전";
    public event Action? Changed;
    public event Action<string>? Log;
    public ProjectMcpSession(string root, string home, string serverExe, bool writable, Action<Action>? dispatch = null)
    { this.root = Safety.ProjectRoot(root); this.home = home; this.serverExe = serverExe; this.writable = writable; this.dispatch = dispatch ?? (a => a()); }
    public async Task ConnectAsync(string cli)
    {
        if (engine != null) throw new HermesException("새 MCP 세션을 사용하세요.");
        engine = new(); engine.Diagnostic += line => dispatch(() => Log?.Invoke(line));
        engine.Approval += r => dispatch(() => Receive(r));
        engine.Notification += e => dispatch(() => { if (e.Method == "serverRequest/resolved") { Approvals.RemoveAll(r => r.Id.ToString() == e.Params.Property("requestId")?.ToString()); Changed?.Invoke(); } });
        engine.Disconnected += () => dispatch(() => { Status = "연결 끊김"; Approvals.Clear(); Changed?.Invoke(); });
        try
        {
            var config = McpConfiguration.BuiltIn(serverExe, root, writable);
            offline = new(async (_, stream, ct) => { RejectedModelRequests++; await LoopbackHttp.Reply(stream, 403, "application/json", "{\"error\":{\"message\":\"MCP inspection: model access disabled\"}}", ct); });
            await engine.ConnectAsync(cli, root, home, new Uri(offline.BaseUri, "v1"), Guid.NewGuid().ToString("N"), "hermes-offline-inspection", config);
            var thread = await engine.RequestAsync("thread/start", new { cwd = root, model = "hermes-offline-inspection", modelProvider = "hermes_company", approvalPolicy = "on-request", approvalsReviewer = "user", sandbox = "read-only", config = new { mcp_servers = config, web_search = "disabled" }, developerInstructions = "Inspection thread only. No model turn is authorized. The configured local MCP server is scoped to this project." });
            threadId = thread.GetProperty("thread").Text("id");
            if (Path.GetFullPath(thread.Text("cwd")) != root) throw new HermesException("MCP 프로젝트 경로 불일치");
            await RefreshAsync();
        }
        catch { Status = "연결 실패"; await engine.DisposeAsync(); Changed?.Invoke(); throw; }
    }
    public async Task RefreshAsync()
    {
        var client = engine ?? throw new HermesException("MCP 연결 필요");
        var result = await client.RequestAsync("mcpServerStatus/list", new { threadId, serverName = McpConfiguration.Name, limit = 100, detail = "full" });
        Tools.Clear(); var server = result.GetProperty("data").EnumerateArray().SingleOrDefault(s => s.Text("name") == McpConfiguration.Name);
        if (server.ValueKind != JsonValueKind.Object) throw new HermesException("로컬 MCP 목록에 서버가 없음");
        Status = server.Text("runtimeStatus") + " / auth=" + server.Text("authStatus");
        if (server.Property("toolsError") is { ValueKind: JsonValueKind.String } error) Status += " / " + Safety.Redact(error.GetString()!);
        if (server.Property("tools") is { ValueKind: JsonValueKind.Object } tools) foreach (var t in tools.EnumerateObject())
            Tools.Add(new(t.Value.Text("name") is { Length: > 0 } name ? name : t.Name, Safety.Redact(t.Value.Text("description")), t.Value.Property("inputSchema")?.Clone() ?? JsonSerializer.SerializeToElement(new { })));
        Changed?.Invoke();
    }
    public async Task<JsonElement> CallAsync(string tool, JsonElement arguments)
    {
        if (!Connected || !Tools.Any(t => t.Name == tool)) throw new HermesException("현재 로컬 MCP 목록에 없는 도구");
        if (arguments.ValueKind != JsonValueKind.Object || arguments.GetRawText().Length > 15000) throw new HermesException("MCP 인수는 15KB 이하 JSON object");
        if (Interlocked.CompareExchange(ref calling, 1, 0) != 0) throw new HermesException("MCP 도구가 이미 실행 중입니다.");
        try
        {
            var result = await engine!.RequestAsync("mcpServer/tool/call", new { threadId, server = McpConfiguration.Name, tool, arguments }, 110);
            Log?.Invoke((result.Property("isError") is { ValueKind: JsonValueKind.True } ? "MCP tool error: " : "MCP result: ") + Safety.Redact(result.GetRawText()));
            return result;
        }
        catch { await DisposeAsync(); throw; }
        finally
        {
            if (engine?.Connected == true) foreach (var r in Approvals.ToList()) { try { await engine.RespondAsync(r, new { action = "cancel" }); } catch (IOException) { } }
            Approvals.Clear(); Interlocked.Exchange(ref calling, 0); Changed?.Invoke();
        }
    }
    private void Receive(ServerRequest r)
    {
        if (r.Method != "mcpServer/elicitation/request" || r.Params.Text("threadId") != threadId || r.Params.Text("serverName") != McpConfiguration.Name || Volatile.Read(ref calling) != 1 || r.Params.Text("mode") != "form")
        { _ = Reject(r); return; }
        Approvals.Add(r); Changed?.Invoke();
    }
    private Task Reject(ServerRequest r) => r.Method == "mcpServer/elicitation/request" ? engine!.RespondAsync(r, new { action = "cancel" }) : engine!.RejectUnsupportedAsync(r);
    public async Task AnswerAsync(ServerRequest r, bool accept)
    {
        if (!Connected || !Approvals.Contains(r) || Volatile.Read(ref calling) != 1) throw new HermesException("만료된 MCP 확인 요청");
        if (r.Params.Text("mode") != "form" || r.Params.Property("requestedSchema")?.Property("properties")?.Property("allow")?.Text("type") != "boolean") throw new HermesException("미지원 확인 form 또는 민감 입력 요청");
        await engine!.RespondAsync(r, accept ? (object)new { action = "accept", content = new { allow = true } } : new { action = "decline" });
        Approvals.Remove(r); Changed?.Invoke();
    }
    public async ValueTask DisposeAsync()
    {
        if (engine != null)
        {
            foreach (var r in Approvals.ToList()) { try { await engine.RespondAsync(r, new { action = "cancel" }); } catch (IOException) { } }
            Approvals.Clear(); await engine.DisposeAsync();
        }
        Status = "연결 종료"; Changed?.Invoke();
        if (offline != null) { await offline.DisposeAsync(); offline = null; }
    }
}
public sealed record McpToolView(string Name, string Description, JsonElement InputSchema) { public override string ToString() => Name; }
