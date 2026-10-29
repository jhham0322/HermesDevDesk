using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Hermes.Core;
public sealed class ExecutionBudget
{
    public int MaxRounds { get; set; } = 3;
    public int MaxMinutes { get; set; } = 10;
    public int MaxTools { get; set; } = 20;
    public int MaxStagnantRounds { get; set; } = 2;
    public bool Extended { get; set; }
    public void Validate()
    {
        if (MaxRounds is < 1 or > 100 || MaxMinutes is < 1 or > 1440 || MaxTools is < 1 or > 1000 || MaxStagnantRounds is < 1 or > 20) throw new HermesException("실행 예산 범위: 라운드 1~100, 분 1~1440, 도구 1~1000, 무진전 1~20");
    }
    public ExecutionBudget Copy() => new() { MaxRounds = MaxRounds, MaxMinutes = MaxMinutes, MaxTools = MaxTools, MaxStagnantRounds = MaxStagnantRounds, Extended = Extended };
    public string Reason(TaskProcess p, bool nextRound, DateTime now)
    {
        if (p.BudgetReason.Length > 0) return p.BudgetReason;
        if ((now - p.Started).TotalMinutes >= MaxMinutes) return "단계 시간 예산 도달";
        if (nextRound && p.Round >= MaxRounds) return "단계 라운드 예산 도달";
        if (p.ToolCalls >= MaxTools) return "단계 도구 예산 도달";
        if (p.StagnantRounds >= MaxStagnantRounds) return "새 실행 근거·변경 없이 반복됨";
        return "";
    }
    public static string Digest(IEnumerable<string> facts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", facts.Distinct().Order(StringComparer.Ordinal)))));
}
public sealed partial class DeskController
{
    internal string ProgressDigest(WorkItem task) => ExecutionBudget.Digest(Memory.Recent(MemoryScope(task), task.Id)
        .Where(e => e.Id > task.ProcessEvidenceStart && e.Succeeded && e.Tool is not ("workflow.report" or "provider" or "memory"))
        .Select(e => e.Tool + "\n" + e.Method + "\n" + e.Outcome)
        .Concat(task.ChangedFiles.Select(f => f.Path + "\n" + f.SavedDiff)));
    private bool PauseAtBoundary(WorkItem task, TaskProcess process)
    {
        var reason = process.Budget.Reason(process, true, DateTime.UtcNow);
        if (reason.Length == 0) return false;
        process.PausedForBudget = true; process.BudgetReason = reason;
        process.Checkpoint = "완료한 일: " + process.ToolCalls + "회 도구 시작, " + task.ChangedFiles.Count + "개 파일 변경 관측.\n남은 일: 목표 달성·검증은 아직 확인되지 않음.\n다음 행동: 현재 기록과 변경 파일을 검토하고 ‘예산 갱신 후 계속’을 명시적으로 선택. 기존 대화에서 미완료 작업을 이어갑니다.";
        EndProcess(task, "예산 검토 필요", reason); task.Status = "예산 검토 필요";
        task.Transcript.Add(TranscriptPresentation.Host("중간보고", reason + "\n" + process.Checkpoint, task.TurnId));
        Save(); return true;
    }
    private void PublishToolReport(WorkItem task)
    {
        if (ActiveProcess(task) is not { } process) return;
        process.ObservedToolResults++;
        var key = "host-progress:" + process.Id + ":" + process.Round;
        var entry = TranscriptPresentation.Host("중간보고", $"완료한 일: 도구 결과 {process.ObservedToolResults}건 수신(성공 여부와 목표 달성은 별도 확인).\n남은 일: 목표 검증 및 완료 보고.\n다음 행동: 관측 결과를 확인하고 필요한 실행·검증만 진행.", task.TurnId) with { ItemId = key };
        var index = task.Transcript.FindLastIndex(e => e.ItemId == key);
        if (index >= 0) task.Transcript[index] = entry; else task.Transcript.Add(entry);
    }
    private void ReserveManagedTool(WorkItem task)
    {
        if (ActiveProcess(task) is not { } process) return;
        var reason = process.Budget.Reason(process, false, DateTime.UtcNow);
        if (reason.Length > 0) { process.BudgetReason = reason; throw new HermesException("예산 도달: 새 도구 실행을 보류했습니다. 완료가 아닙니다. 현재 결과와 재개 지점을 보고하세요. " + reason); }
        process.ToolCalls++;
    }
    public async Task ResumeBudgetAsync(string id)
    {
        var previous = State.Processes.Single(p => p.Id == id);
        if (!previous.PausedForBudget || previous.Active) throw new HermesException("예산으로 중지된 작업만 명시적으로 재개할 수 있습니다.");
        var task = State.Tasks.Single(t => t.Id == previous.TaskId);
        if (IsRunning(task)) throw new HermesException("이미 실행 중입니다.");
        // A new bounded stage preserves transcript, files and model/provider checks.
        if (Engine == null || !SubscriptionReady || task.ProviderFingerprint != connectedFingerprint || connectedFingerprint != State.Provider.Fingerprint || task.ThreadId == null) throw new HermesException("같은 모델 연결을 확인한 뒤 재개하세요.");
        company?.BeginTask(task.Id, task.Model);
        if (!loadedThreads.Contains(task.ThreadId))
        {
            var root = Safety.ProjectRoot(Project(task).Root);
            var mcp = new Dictionary<string, object>(disabledMcp);
            if (task.ThreadMcpEnabled == true) foreach (var server in McpConfiguration.BuiltIn(Environment.ProcessPath!, root, !task.IsReview, Path.GetDirectoryName(Memory.DatabasePath), task.Id)) mcp[server.Key] = server.Value;
            var resumed = await Engine.RequestAsync("thread/resume", new { threadId = task.ThreadId, cwd = root, model = task.Model, modelProvider = company == null ? "openai" : "hermes_company", approvalPolicy = task.IsReview ? "on-request" : "never", approvalsReviewer = "user", sandbox = "danger-full-access", config = new Dictionary<string, object> { ["mcp_servers"] = mcp, ["features.shell_tool"] = false, ["features.unified_exec"] = false, ["model_providers.hermes_company.base_url"] = company?.TaskBaseUri(task.Id).AbsoluteUri ?? "http://127.0.0.1" } });
            if (resumed.GetProperty("thread").Text("id") != task.ThreadId || !string.Equals(Path.GetFullPath(resumed.Text("cwd")), root, StringComparison.OrdinalIgnoreCase) || resumed.Text("modelProvider") != (company == null ? "openai" : "hermes_company") || resumed.GetProperty("sandbox").Text("type") != "dangerFullAccess") throw new HermesException("재개된 스레드의 경로·모델 출처·실행 정책이 일치하지 않습니다.");
            loadedThreads.Add(task.ThreadId);
        }
        var process = BeginProcess(task, previous.Goal); process.Round = 0;
        task.ProcessEvidenceStart = Memory.Recent(MemoryScope(task), task.Id).Select(e => e.Id).DefaultIfEmpty().Max();
        process.LastProgressDigest = ProgressDigest(task);
        process.State = "다음 실행 대기"; task.Status = "계속 실행 대기";
        await ContinueLoopAsync(task, process, Engine);
    }
    internal void ObserveCompanyUsage(UsageEntry usage)
    {
        UsageLedger.Upsert(State, usage);
        var task = State.Tasks.FirstOrDefault(t => t.Id == usage.TaskId); if (task == null) return;
        if (usage.At >= task.ActiveRequestAt) { task.ActiveRequestAt = usage.At; task.ActiveRequestId = usage.Id; task.ResponseModel = usage.Model; }
        for (var i = 0; i < task.Transcript.Count; i++)
            if (task.Transcript[i].Origin == "model" && task.Transcript[i].RequestId == usage.Id)
                task.Transcript[i] = task.Transcript[i] with { RequestedModel = usage.Deployment, ResponseModel = usage.Model };
    }
}
