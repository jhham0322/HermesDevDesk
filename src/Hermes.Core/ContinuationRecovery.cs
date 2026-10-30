using System.Text.Json;
using System.Text.RegularExpressions;
namespace Hermes.Core;
public sealed partial class DeskController
{
    private static string ExternalStopReason(JsonElement turn)
    {
        if (turn.Property("error") is not { ValueKind: JsonValueKind.Object } error) return "";
        var text = Regex.Replace(error.GetRawText(), @"([a-z])([A-Z])", "$1_$2", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Replace(' ', '_');
        if (Regex.IsMatch(text, "insufficient_quota|usage_limit|rate_limit|rate.limit|quota|billing|payment|unauthorized|forbidden|permission.denied|authentication|login.required|HTTP.?[ :]*(401|402|403|429)|too.many.requests|context.length.exceeded|token.limit", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
            return "공급자/인증/권한/문맥의 실제 제한: " + ProviderErrors.ReadableMessage(error.Text("message"));
        return "";
    }
    private void ObserveOperation(WorkItem task, JsonElement item, bool completed)
    {
        if (ActiveProcess(task) is not { } process) return;
        var type = item.Text("type"); var tool = item.Text("tool");
        if (type is not ("mcpToolCall" or "fileChange" or "commandExecution" or "webSearch") && !(type == "dynamicToolCall" && tool is "hermes_browser" or "hermes_computer")) return;
        var id = item.Text("id"); if (id.Length == 0) return;
        var key = "observed:" + id; var receipt = process.Operations.SingleOrDefault(o => o.Key == key);
        if (!completed && receipt == null) { receipt = new OperationReceipt { Key = key, ItemId = process.Items.FirstOrDefault(i => i.State == "running")?.Id ?? "", MethodDigest = ExecutionBudget.Digest([type, item.Text("server"), tool]) }; process.Operations.Add(receipt); }
        if (completed && receipt != null) { receipt.State = item.Text("status") == "failed" || item.Property("success") is { ValueKind: JsonValueKind.False } ? "failed" : "completed"; receipt.Result = ExecutionMemory.Clean(type + " / " + tool + " 결과 수신. 목표 달성은 별도 검증."); }
        Save();
    }
    private OperationReceipt? BeginOperation(WorkItem task, JsonElement args, string eventId)
    {
        var process = ActiveProcess(task); if (process == null) return null;
        if (process.Operations.Any(o => o.State is "started" or "unknown")) throw new HermesException("결과가 미확인인 실행이 있습니다. 부작용을 다시 실행하지 말고 실제 상태를 확인하세요.");
        var operation = ExecutionMemory.Clean(args.Text("operationId"), 120);
        if (process.Items.Count > 0 && operation.Length == 0) throw new HermesException("작업 큐의 명령에는 항목별 안정적인 operationId가 필요합니다. 완료된 부작용은 반복하지 마세요.");
        if (operation.StartsWith("observed:", StringComparison.Ordinal) || operation.Length > 120 || operation.Any(char.IsControl)) throw new HermesException("operationId 형식을 확인하세요.");
        var key = operation.Length == 0 ? "event:" + eventId : operation;
        var digest = ExecutionBudget.Digest([args.Text("command"), args.Text("purpose")]);
        var previous = process.Operations.SingleOrDefault(o => o.Key == key);
        if (previous != null) { if (previous.MethodDigest != digest) throw new HermesException("같은 operationId를 다른 실행에 사용할 수 없습니다."); return previous; }
        var receipt = new OperationReceipt { Key = key, ItemId = process.Items.FirstOrDefault(i => i.State == "running")?.Id ?? "", MethodDigest = digest };
        process.Operations.Add(receipt); Save(); return receipt;
    }
    private async Task RestorePendingLoopsAsync()
    {
        foreach (var p in State.Processes.Where(p => p.ContinuationEnabled && !p.UserStopped && p.State == "앱 종료로 중단됨").ToList())
        {
            if (p.BlockKind.Length > 0 || p.Operations.Any(o => o.State is "started" or "unknown"))
            { p.State = "실행 결과 확인 필요"; p.Detail = "승인/외부 차단 또는 결과 미확인 실행을 보존했습니다. 자동 재실행하지 않습니다."; continue; }
            try { await ResumeSavedProcessAsync(p.Id); }
            catch (Exception ex) { p.State = "연결 복구 필요"; p.Detail = ConnectionErrors.Describe(ex); }
        }
        Save();
    }
    public async Task ResumeSavedProcessAsync(string id, bool userConfirmed = false)
    {
        var p = State.Processes.Single(x => x.Id == id);
        var task = State.Tasks.Single(t => t.Id == p.TaskId);
        if (p.ThreadId.Length > 0 && p.ThreadId != task.ThreadId) throw new HermesException("현재 대화가 다른 요청으로 바뀌었습니다. 이전 체크포인트를 새 요청의 실행 상태에 덮어쓰지 않습니다.");
        if (!p.ContinuationEnabled || p.Active || p.State == "완료" || IsRunning(task)) throw new HermesException("재개할 미완료 체크포인트를 선택하세요.");
        if (p.Operations.Any(o => o.State is "started" or "unknown")) throw new HermesException("결과 미확인 실행이 있어 자동 재실행을 차단했습니다. 실제 부작용 상태 확인이 필요합니다.");
        if ((p.UserStopped || p.BlockKind.Length > 0) && !userConfirmed) throw new HermesException("사용자 정지·외부 제한·승인 조건이 해결됐는지 확인한 뒤 명시적으로 재개하세요.");
        if (Engine == null || !SubscriptionReady || task.ThreadId == null || connectedFingerprint != State.Provider.Fingerprint || task.ProviderFingerprint != connectedFingerprint) throw new HermesException("같은 모델·공급자의 유효한 연결을 확인하세요.");
        var root = Safety.ProjectRoot(Project(task).Root); company?.BeginTask(task.Id, task.Model);
        if (!loadedThreads.Contains(task.ThreadId))
        {
            var mcp = new Dictionary<string, object>(disabledMcp);
            if (task.ThreadMcpEnabled == true) foreach (var server in McpConfiguration.BuiltIn(Environment.ProcessPath!, root, !task.IsReview, Path.GetDirectoryName(Memory.DatabasePath), task.Id)) mcp[server.Key] = server.Value;
            var result = await Engine.RequestAsync("thread/resume", new { threadId = task.ThreadId, cwd = root, model = task.Model, modelProvider = company == null ? "openai" : "hermes_company", approvalPolicy = task.IsReview ? "on-request" : "never", approvalsReviewer = "user", sandbox = "danger-full-access", config = new Dictionary<string, object> { ["mcp_servers"] = mcp, ["features.shell_tool"] = false, ["features.unified_exec"] = false, ["model_providers.hermes_company.base_url"] = company?.TaskBaseUri(task.Id).AbsoluteUri ?? "http://127.0.0.1" } });
            if (result.GetProperty("thread").Text("id") != task.ThreadId || !string.Equals(Path.GetFullPath(result.Text("cwd")), root, StringComparison.OrdinalIgnoreCase) || result.Text("modelProvider") != (company == null ? "openai" : "hermes_company") || result.GetProperty("sandbox").Text("type") != "dangerFullAccess") throw new HermesException("복원 스레드의 경로·출처·정책이 일치하지 않습니다.");
            loadedThreads.Add(task.ThreadId);
        }
        p.UserStopped = false; p.BlockKind = ""; p.PausedForBudget = false; p.BudgetReason = ""; p.Decision = null; p.DecisionTurnId = ""; p.LastCompletedTurnId = "";
        p.Budget = State.Desktop.ExecutionBudget.Copy();
        p.StageStarted = DateTime.UtcNow; p.StageRoundOffset = p.Round; p.StagnantRounds = 0; p.ConsecutiveFailures = 0;
        p.State = "다음 실행 대기"; task.Status = "계속 실행 대기"; task.TurnId = null; Save();
        await ContinueLoopAsync(task, p, Engine);
    }
}
