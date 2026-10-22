using System.Text.Json;

namespace Hermes.Core;

public sealed partial class DeskController
{
    private readonly Dictionary<string, CancellationTokenSource> loopWaits = [];
    private const string LoopInstructions = " This request runs in a host-managed completion loop. Continue taking useful actions until the entire user objective is fulfilled and verified. A final message alone does not stop the loop. Use hermes_task with action=complete and concrete verification only when all requested work is finished; cite a successful evidenceId for tool-based work. For an answer-only task, verification may describe the delivered answer without evidenceId. If a real missing credential, user decision, or unavailable external dependency prevents further work, use action=blocked with the exact reason. Do not claim success from a tool exit code alone. After repeated errors, use automatically retrieved RAG to diagnose and execute a materially different approach, then verify. Never expand user authorization.";
    private static readonly object[] LoopTools = [new
    {
        type = "function", name = "hermes_task", description = "Report verified completion or a concrete external blocker to the host-managed task loop. Only complete after fulfilling and verifying the full objective; verification explains observed results. evidenceId must refer to a successful tool execution in this process for tool-based work. The host verifies evidence provenance, not the business result itself.",
        inputSchema = new { type = "object", properties = new { action = new { type = "string", @enum = new[] { "complete", "blocked" } }, verification = new { type = "string", minLength = 10, maxLength = 4000 }, evidenceId = new { type = "integer" } }, required = new[] { "action", "verification" }, additionalProperties = false }
    }];
    public IReadOnlyList<TaskProcess> Processes => State.Processes.OrderByDescending(p => p.Started).ToList();
    private TaskProcess? ActiveProcess(WorkItem task) => State.Processes.LastOrDefault(p => p.TaskId == task.Id && p.Active);
    internal TaskProcess BeginProcess(WorkItem task, string goal)
    {
        if (ActiveProcess(task) != null) throw new HermesException("이 작업에는 이미 실행 중인 프로세스가 있습니다.");
        var process = new TaskProcess { TaskId = task.Id, Goal = ExecutionMemory.Clean(goal, 100000) };
        State.Processes.Add(process); return process;
    }
    private void EndProcess(WorkItem task, string state, string detail)
    {
        if (loopWaits.Remove(task.Id, out var wait)) { wait.Cancel(); wait.Dispose(); }
        if (ActiveProcess(task) is not { } process) return;
        process.State = state; process.Detail = ExecutionMemory.Clean(detail, 4000); process.Updated = DateTime.UtcNow;
    }
    private async Task InterruptStoppedTurnAsync(WorkItem task, string turnId)
    {
        try { if (Engine != null) await Engine.RequestAsync("turn/interrupt", new { threadId = task.ThreadId, turnId }); }
        catch (Exception ex) { AppLog.Write(ex, "loop-stop-late-turn"); }
    }
    public async Task StopProcessAsync(string id)
    {
        var process = State.Processes.SingleOrDefault(p => p.Id == id) ?? throw new HermesException("프로세스를 찾을 수 없습니다.");
        if (!process.Active) return;
        var task = State.Tasks.SingleOrDefault(t => t.Id == process.TaskId);
        if (task == null) { process.State = "종료됨"; Save(); return; }
        await StopAsync(task);
    }
    internal string DecideProcess(WorkItem task, JsonElement args, string turnId)
    {
        var process = ActiveProcess(task) ?? throw new HermesException("활성 완료 루프가 없습니다.");
        var action = args.Text("action"); var verification = ExecutionMemory.Clean(args.Text("verification"), 4000);
        if (action is not ("complete" or "blocked") || verification.Length < 10) throw new HermesException("완료 검증 결과 또는 외부 차단 원인을 10자 이상 입력하세요.");
        if (action == "complete")
        {
            var attempts = Memory.Recent(MemoryScope(task), task.Id).Where(e => e.Id > task.ProcessEvidenceStart && e.Tool is not ("workflow.report" or "provider" or "memory")).ToList();
            var proof = args.Property("evidenceId");
            if (attempts.Count > 0 && (proof is not { ValueKind: JsonValueKind.Number } || !proof.Value.TryGetInt64(out var id) || !attempts.Any(e => e.Id == id && e.Succeeded)))
                throw new HermesException("이번 프로세스에서 성공한 실제 도구 실행 evidenceId와 목표 검증 결과가 필요합니다. 실패 근거나 이전 실행 근거는 사용할 수 없습니다.");
            if (proof is { ValueKind: JsonValueKind.Number } && (!proof.Value.TryGetInt64(out var cited) || !attempts.Any(e => e.Id == cited && e.Succeeded)))
                throw new HermesException("현재 프로세스의 성공 실행 근거가 아닙니다.");
        }
        process.Decision = action; process.DecisionTurnId = turnId; process.Detail = verification; process.Updated = DateTime.UtcNow; Save();
        return "호스트에 " + (action == "complete" ? "완료 검증 보고" : "외부 차단 사유") + "를 등록했습니다. 사용자에게 결과를 설명하세요.";
    }
    private void FinishLoopTurn(WorkItem task, JsonElement turn)
    {
        if (ActiveProcess(task) is not { } process) return;
        var status = turn.Text("status");
        if (process.LastCompletedTurnId == turn.Text("id")) return;
        process.LastCompletedTurnId = turn.Text("id");
        if (process.State == "종료 중") { EndProcess(task, "종료됨", "사용자가 프로세스를 종료했습니다."); task.Status = "중단됨"; return; }
        if (status == "interrupted") { EndProcess(task, "종료됨", "사용자 또는 엔진이 실행을 중단했습니다."); return; }
        if (status == "completed" && process.DecisionTurnId == turn.Text("id") && process.Decision != null)
        {
            var complete = process.Decision == "complete";
            EndProcess(task, complete ? "완료" : "사용자 확인 필요", process.Detail);
            task.Status = complete ? "완료" : "사용자 확인 필요"; return;
        }
        process.Decision = null;
        process.ConsecutiveFailures = status == "completed" ? 0 : process.ConsecutiveFailures + 1;
        if (process.ConsecutiveFailures >= 3)
        {
            EndProcess(task, "연결 복구 필요", "엔진 요청이 3회 연속 실패했습니다. 인증·서버·모델 연결을 확인한 후 다시 실행하세요."); task.Status = "연결 복구 필요"; return;
        }
        process.State = "다음 실행 대기"; process.Updated = DateTime.UtcNow;
        task.Status = "계속 실행 대기";
        var cancellation = new CancellationTokenSource(); loopWaits[task.Id] = cancellation;
        _ = QueueLoopAsync(task, process, Engine, cancellation.Token);
    }
    private async Task QueueLoopAsync(WorkItem task, TaskProcess process, AppServerClient? engine, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(15, 1 + process.ConsecutiveFailures * 3)), ct);
            dispatch(() => { if (!ct.IsCancellationRequested) _ = ContinueLoopAsync(task, process, engine); });
        }
        catch (OperationCanceledException) { }
    }
    private async Task ContinueLoopAsync(WorkItem task, TaskProcess process, AppServerClient? engine)
    {
        if (disposed || engine == null || engine != Engine || ActiveProcess(task) != process || process.State != "다음 실행 대기") return;
        if (loopWaits.Remove(task.Id, out var wait)) wait.Dispose();
        try
        {
            if (!SubscriptionReady || task.ProviderFingerprint != connectedFingerprint || connectedFingerprint != State.Provider.Fingerprint || task.ThreadId == null)
                throw new HermesException("연결 상태가 바뀌었습니다. 같은 모델에 다시 연결한 뒤 실행하세요.");
            process.State = "실행 중"; process.Round++; process.Updated = DateTime.UtcNow; task.Status = "시작 중";
            var root = Safety.ProjectRoot(Project(task).Root);
            var automatic = task.Writable && Project(task).FullPermissions;
            var policy = automatic ? (object)new { type = "dangerFullAccess" } : task.Writable ? (object)new { type = "workspaceWrite", writableRoots = new[] { root }, networkAccess = false, excludeTmpdirEnvVar = true, excludeSlashTmp = true } : new { type = "readOnly", networkAccess = false };
            var prompt = "Continue the original authorized objective. Do the remaining work and verify its outcome; do not merely repeat a status message. " + LoopInstructions + "\nOriginal objective:\n" + process.Goal + "\n" + Memory.Context(MemoryScope(task), process.Goal);
            task.Transcript.Add(new("자동 계속", $"{process.Round}회차 · 미완료 작업을 이어서 수행합니다.", DateTime.UtcNow)); Save();
            var result = await engine.RequestAsync("turn/start", new { threadId = task.ThreadId, cwd = root, model = task.Model, effort = task.ReasoningEffort.Length == 0 ? null : task.ReasoningEffort, summary = "auto", approvalPolicy = automatic ? "never" : "on-request", approvalsReviewer = "user", sandboxPolicy = policy, input = new[] { new { type = "text", text = prompt } } });
            if (ActiveProcess(task) != process || process.State == "종료 중")
            {
                await engine.RequestAsync("turn/interrupt", new { threadId = task.ThreadId, turnId = result.GetProperty("turn").Text("id") }); return;
            }
            if (task.Status is "시작 중" or "실행 중") { task.TurnId = result.GetProperty("turn").Text("id"); company?.SetTurn(task.Id, task.TurnId); task.Status = "실행 중"; }
            Save();
        }
        catch (Exception ex)
        {
            if (ActiveProcess(task) != process) return;
            EndProcess(task, "연결 복구 필요", ConnectionErrors.Describe(ex)); task.Status = "연결 복구 필요";
            task.Transcript.Add(new("자동 실행 오류", ConnectionErrors.Describe(ex), DateTime.UtcNow)); Save();
        }
    }
}
