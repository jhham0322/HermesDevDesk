using System.Text.Json;

namespace Hermes.Core;

public sealed partial class DeskController
{
    private readonly Dictionary<string, CancellationTokenSource> loopWaits = [];
    private const string LegacyLoopInstructions = " Continue the authorized whole objective using this existing thread tool contract. Internal tool call counts are unlimited. Preserve every added user requirement and do not stop merely after a partial answer. Report complete only with concrete verification and successful evidenceId for tool work, or a real external blocker; never replay completed or uncertain effects. User stop, approval, provider limits and repeated failures remain binding.";
    private const string LoopInstructions = " Internal tool call counts are unlimited. Normal internal stages automatically continue with real progress unless the user selected a confirmation limit; never ask the user to resume just because a normal internal stage ended.  For multiple requirements use hermes_task plan/start_item/verify_item. Preserve completed items; merge additional request IDs, dependencies and priorities. Read full long requests with read_request; status is a compact checkpoint, not a replacement for the original requirements. Each queued command must use a stable operationId; never replay a completed operation or an uncertain side effect. A successful evidence ID alone is not full verification: use planned artifact checks or expected output, then verify_item. Do not report internal stage boundaries or unfinished items as an external blocker.  This request runs in a host-managed completion loop. Continue taking useful actions until the entire user objective is fulfilled and verified. A final message alone does not stop the loop. Use hermes_task with action=complete and concrete verification only when all requested work is finished; cite a successful evidenceId for tool-based work. For an answer-only task, verification may describe the delivered answer without evidenceId. If a real missing credential, user decision, or unavailable external dependency prevents further work, use action=blocked with the exact reason. Do not claim success from a tool exit code alone. After repeated errors, use automatically retrieved RAG to diagnose and execute a materially different approach, then verify. Never expand user authorization. Work in short stages: report completed work, remaining work and next action; avoid repeated status messages. At a host budget boundary preserve partial results and a restart point, and never report budget exhaustion as completion. Distinguish observed errors from hypotheses. Use [분석], [중간보고], [에러원인], [검증] for readable public summaries; text tags do not change host state.";
    private static readonly object[] LoopTools = [new
    {
        type = "function", name = "hermes_task", description = "Manage a persistent verified work queue. For multiple requirements first plan explicit items with acceptance criteria, artifact checks or expected evidence output and dependencies. requestId defaults to initial; cover every pending request. start_item selects the next eligible item; execute and verify_item with a successful evidenceId. Completed items cannot be rerun. status/read_request retrieve saved requirements. complete is accepted only after every requirement/item is verified. blocked is only for a real external blocker, never an internal stage boundary or remaining work.",
        inputSchema = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{"action":{"type":"string","enum":["complete","blocked","plan","start_item","verify_item","status","read_request"]},"verification":{"type":"string","maxLength":4000},"evidenceId":{"type":"integer"},"itemId":{"type":"string"},"requestId":{"type":"string"},"offset":{"type":"integer","minimum":0},"blockerType":{"type":"string","enum":["credential","approval","provider_limit","external_dependency","user_decision"]},"items":{"type":"array","minItems":1,"maxItems":200,"items":{"type":"object","properties":{"id":{"type":"string"},"requestId":{"type":"string"},"title":{"type":"string"},"acceptance":{"type":"string","minLength":10},"kind":{"type":"string","enum":["artifact","evidence","answer"]},"expectedOutput":{"type":"string"},"dependsOn":{"type":"array","items":{"type":"string"}},"priority":{"type":"integer"},"artifacts":{"type":"array","items":{"type":"object","properties":{"path":{"type":"string"},"contains":{"type":"string"}},"required":["path"],"additionalProperties":false}}},"required":["id","title","acceptance","kind"],"additionalProperties":false}}},"required":["action"],"additionalProperties":false}""")
    }];
    public IReadOnlyList<TaskProcess> Processes => State.Processes.OrderByDescending(p => p.Started).ToList();
    private TaskProcess? ActiveProcess(WorkItem task) => State.Processes.LastOrDefault(p => p.TaskId == task.Id && p.Active);
    internal TaskProcess BeginProcess(WorkItem task, string goal)
    {
        if (ActiveProcess(task) != null) throw new HermesException("이 작업에는 이미 실행 중인 프로세스가 있습니다.");
        State.Desktop.ExecutionBudget.Validate();
        var process = new TaskProcess { TaskId = task.Id, ThreadId = task.ThreadId ?? "", Goal = ExecutionMemory.Clean(goal, 100000), Budget = State.Desktop.ExecutionBudget.Copy(), ContinuationEnabled = true, Requirements = [new WorkRequirement { Id = "initial", Text = ExecutionMemory.Clean(goal, 100000) }], NeedsPlan = task.ThreadLearningVersion != 1 && System.Text.RegularExpressions.Regex.Matches(goal, @"(?m)^\s*(?:[-*]|[A-Z][.)]|\d+[.)])\s+\S", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)).Count >= 2 };
        process.LastProgressDigest = ProgressDigest(task); process.StageProgressDigest = process.LastProgressDigest;
        task.Transcript.Add(TranscriptPresentation.Host("분석", $"단계 실행 시작 · {process.Budget.MaxRounds}라운드 / {process.Budget.MaxMinutes}분 · 도구 호출 횟수 상한 없음. 정상 진척이 있으면 내부 단계를 자동으로 이어가며 사용자 지정 정지와 외부 제한은 유지합니다."));
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
        process.UserStopped = true;
        await StopAsync(task);
    }
    internal string DecideProcess(WorkItem task, JsonElement args, string turnId)
    {
        var process = ActiveProcess(task) ?? throw new HermesException("활성 완료 루프가 없습니다.");
        var action = args.Text("action");
        if (action is "plan" or "start_item" or "verify_item" or "status" or "read_request") return ManageWorkQueue(task, process, args);
        var verification = ExecutionMemory.Clean(args.Text("verification"), 4000);
        if (action is not ("complete" or "blocked") || verification.Length < 10) throw new HermesException("완료 검증 결과 또는 외부 차단 원인을 10자 이상 입력하세요.");
        if (action == "blocked")
        {
            var reason = args.Text("blockerType");
            if (System.Text.RegularExpressions.Regex.IsMatch(verification, "(internal|stage|내부|단계).*(budget|limit|예산|횟수)", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)) && !System.Text.RegularExpressions.Regex.IsMatch(verification, "provider|quota|billing|공급자|과금|인증", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) throw new HermesException("내부 단계·도구 횟수는 외부 차단 사유가 아닙니다. 저장된 다음 미완료 항목을 계속하세요.");
            if (reason.Length == 0 && !System.Text.RegularExpressions.Regex.IsMatch(verification, "credential|authentication|login|approval|permission|unavailable|quota|rate.limit|user.decision|자격|인증|로그인|승인|권한|사용자.*입력|외부.*불가", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) throw new HermesException("미완료 작업이나 내부 단계 경계는 외부 차단이 아닙니다. 남은 항목을 계속하세요.");
            process.BlockKind = reason.Length == 0 ? "external_dependency" : reason;
        }
        if (action == "complete")
        {
            if (process.NeedsPlan || process.Requirements.Any(r => !r.Planned && r.Id != "initial") || process.Items.Any(i => !i.Terminal) || process.Operations.Any(o => o.State is "started" or "unknown")) throw new HermesException("미완료·미계획 요구 또는 결과 미확인 실행이 남아 있습니다. 다음 작업을 이어가세요.");
            if (process.Items.Count > 0)
            {
                if (process.Requirements.Any(r => !r.Planned)) throw new HermesException("모든 원문 요구를 작업 항목에 연결하세요.");
                foreach (var item in process.Items.Where(i => i.State == "verified" && i.Kind == "artifact")) _ = WorkQueue.CheckArtifacts(Project(task).Root, item);
                process.Decision = action; process.DecisionTurnId = turnId; process.Detail = verification; Save(); return "모든 작업 항목의 기록·검증 근거를 확인했습니다. 최종 결과를 전달하세요.";
            }

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
        if (process.LastCompletedTurnId == turn.Text("id") || !process.CompletedTurnIds.Add(turn.Text("id"))) return;
        process.LastCompletedTurnId = turn.Text("id");
        if (process.UserStopped || process.State == "종료 중" || status == "interrupted") { EndProcess(task, "종료됨", "사용자 또는 엔진이 실행을 중단했습니다."); task.Status = "중단됨"; return; }
        var external = ExternalStopReason(turn);
        if (external.Length > 0) { process.BlockKind = "provider_limit_or_denial"; process.Checkpoint = WorkQueue.Summary(process); EndProcess(task, "외부 제한 확인 필요", external); task.Status = "외부 제한 확인 필요"; task.Transcript.Add(TranscriptPresentation.Host("입력 필요", external + "\n상태를 보존했습니다. 공급자 한도·인증·권한 문제가 해결된 뒤 명시적으로 재개하세요.")); Save(); return; }
        if (process.BlockKind == "permission_denied") { EndProcess(task, "승인 거절", "사용자가 거절한 동작은 자동 재시도하지 않습니다."); task.Status = "승인 거절"; Save(); return; }
        if (Approvals.Any(a => a.Params.Text("threadId") == task.ThreadId) || process.BlockKind == "approval") { process.State = "승인 대기"; task.Status = "승인 대기"; Save(); return; }
        if (process.Operations.Any(o => o.State is "started" or "unknown")) { process.BlockKind = "uncertain_operation"; EndProcess(task, "실행 결과 확인 필요", "완료 여부를 확인하지 못한 실행이 있어 중복 실행을 막았습니다."); task.Status = "실행 결과 확인 필요"; Save(); return; }

        if (process.State == "종료 중") { EndProcess(task, "종료됨", "사용자가 프로세스를 종료했습니다."); task.Status = "중단됨"; return; }
        if (status == "interrupted") { EndProcess(task, "종료됨", "사용자 또는 엔진이 실행을 중단했습니다."); return; }
        if (status == "completed" && process.Items.Count > 0 && process.Items.All(i => i.Terminal) && process.Items.Any(i => i.State == "verified") && process.Requirements.All(r => r.Planned))
        {
            try
            {
                foreach (var item in process.Items.Where(i => i.State == "verified" && i.Kind == "artifact")) _ = WorkQueue.CheckArtifacts(Project(task).Root, item);
                process.Decision = "complete"; process.DecisionTurnId = turn.Text("id"); process.Detail = "저장된 모든 요구의 작업 항목을 실행하고 선언된 산출물·출력·답변 조건을 검증했습니다.";
            }
            catch (IOException ex) { process.Decision = null; task.Transcript.Add(TranscriptPresentation.Host("에러원인", "최종 산출물 재검증 실패: " + ExecutionMemory.Clean(ex.Message))); }
        }
        if (status == "completed" && process.DecisionTurnId == turn.Text("id") && process.Decision != null)
        {
            var complete = process.Decision == "complete";
            if (complete && (process.Items.Any(i => !i.Terminal) || process.Requirements.Any(r => !r.Planned && r.Id != "initial"))) { process.Decision = null; }
            else
            {
            task.Transcript.Add(TranscriptPresentation.Host(complete ? "검증" : "입력 필요", complete ? (process.Items.Count > 0 ? "작업 항목의 선언된 산출물·출력·전달 조건을 확인했습니다. 원문 요구와 검증 기록을 보존합니다.\n" : "모델 완료 보고와 실행 근거 연결을 확인했습니다. 목표 달성 자체는 모델 보고이며 앱의 독립 검증은 아닙니다.\n") + process.Detail : process.Detail, turn.Text("id")));
            EndProcess(task, complete ? "완료" : "사용자 확인 필요", process.Detail);
            task.Status = complete ? "완료" : "사용자 확인 필요"; return;
            }
        }
        if (process.Items.Any(i => !i.Terminal) && WorkQueue.Next(process) == null && process.Requirements.All(r => r.Planned))
        { process.BlockKind = "dependency"; EndProcess(task, "의존관계 확인 필요", "취소·미완료 선행 항목 때문에 다음 항목을 실행할 수 없습니다. 요구 변경과 의존관계를 확인하세요."); task.Status = "의존관계 확인 필요"; Save(); return; }
        var digest = ProgressDigest(task);
        process.StagnantRounds = digest == process.LastProgressDigest ? process.StagnantRounds + 1 : 0;
        process.LastProgressDigest = digest;
        if (PauseAtBoundary(task, process)) return;
        task.Transcript.Add(TranscriptPresentation.Host("중간보고", $"완료한 일: {process.ToolCalls}회 도구 시작, {task.ChangedFiles.Count}개 파일 변경 관측.\n남은 일: 완료 검증 보고 미접수.\n다음 행동: 미완료 범위만 이어서 실행. 라운드 {process.Round}/{process.Budget.MaxRounds}", turn.Text("id")));
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
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(15, 1 + process.ConsecutiveFailures * 3)), ct);
            process.AutomaticWaitMs += clock.ElapsedMilliseconds;
            dispatch(() => { if (!ct.IsCancellationRequested) _ = ContinueLoopAsync(task, process, engine); });
        }
        catch (OperationCanceledException) { }
    }
    private async Task ContinueLoopAsync(WorkItem task, TaskProcess process, AppServerClient? engine)
    {
        if (disposed || engine == null || engine != Engine || ActiveProcess(task) != process || process.State != "다음 실행 대기") return;
        if (PauseAtBoundary(task, process)) return;
        if (loopWaits.Remove(task.Id, out var wait)) wait.Dispose();
        try
        {
            if (!SubscriptionReady || task.ProviderFingerprint != connectedFingerprint || connectedFingerprint != State.Provider.Fingerprint || task.ThreadId == null)
                throw new HermesException("연결 상태가 바뀌었습니다. 같은 모델에 다시 연결한 뒤 실행하세요.");
            task.ActiveRequestId = ""; task.ResponseModel = "";
            process.State = "실행 중"; process.Round++; process.Updated = DateTime.UtcNow; task.Status = "시작 중";
            var root = Safety.ProjectRoot(Project(task).Root);
            var automatic = !task.IsReview;
            var policy = new { type = "dangerFullAccess" };
            var prompt = "Continue the original authorized objective. Do the remaining work and verify its outcome; do not merely repeat a status message. " + (task.ThreadLearningVersion == 1 ? LegacyLoopInstructions : LoopInstructions) + "\nOriginal objective and saved checkpoint:\n" + (task.ThreadLearningVersion == 1 ? string.Join("\n\nAdditional authorized request:\n", process.Requirements.Select(r => r.Text)) : WorkQueue.Summary(process)) + "\n" + Memory.Context(MemoryScope(task), WorkQueue.Next(process)?.Title ?? process.Goal);
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
