using System.Text.Json;
namespace Hermes.Core;

public sealed class OperationReviewRequiredException(string processId, string message) : HermesException(message)
{
    public string ProcessId { get; } = processId;
}

internal sealed record CommandResponseReceipt(string TaskId, string ProcessId, string Key, string MethodDigest, int ExitCode, string Result);

public sealed partial class DeskController
{
    private string CommandResponsePath(TaskProcess process, OperationReceipt receipt)
    {
        var directory = Path.Combine(DataDirectory, "command-results");
        Safety.RejectReparseAncestors(directory);
        return Path.Combine(directory, ExecutionBudget.Digest([process.TaskId, process.Id, receipt.Key, receipt.MethodDigest]) + ".json");
    }
    private void PersistCommandResponse(TaskProcess process, OperationReceipt receipt)
    {
        if (!receipt.ResultReceived || receipt.ExitCode == null) throw new HermesException("호스트가 수신한 종료 코드가 없어 결과를 확정할 수 없습니다.");
        var response = new CommandResponseReceipt(process.TaskId, process.Id, receipt.Key, receipt.MethodDigest, receipt.ExitCode.Value, receipt.Result);
        var path = CommandResponsePath(process, receipt);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); Safety.RejectReparseAncestors(path);
        if (File.Exists(path))
        {
            if (ReadCommandResponse(process, receipt) != response) throw new HermesException("보존된 응답과 현재 응답이 다릅니다. 기존 기록을 덮어쓰지 않았습니다.");
            return;
        }
        var pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, response); stream.Flush(true); }
        File.Move(pending, path); // Immutable, app-owned response. Never overwrite an older response.
    }
    private CommandResponseReceipt? ReadCommandResponse(TaskProcess process, OperationReceipt receipt)
    {
        var path = CommandResponsePath(process, receipt); Safety.RejectReparseAncestors(path);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 150000) throw new HermesException("응답 기록 크기가 유효하지 않습니다.");
        var result = JsonSerializer.Deserialize<CommandResponseReceipt>(File.ReadAllText(path));
        if (result == null || result.TaskId != process.TaskId || result.ProcessId != process.Id || result.Key != receipt.Key || result.MethodDigest != receipt.MethodDigest)
            throw new HermesException("응답 기록의 작업·프로세스·실행 식별자가 일치하지 않습니다.");
        return result;
    }
    private CommandResponseReceipt? VerifiedCommandResponse(TaskProcess process, OperationReceipt receipt)
    {
        var saved = ReadCommandResponse(process, receipt); if (saved != null) return saved;
        // Legacy event receipts can use an exact same-task successful execution record.
        // Stable operation IDs without a stored event link are deliberately not guessed.
        if (!receipt.Key.StartsWith("event:", StringComparison.Ordinal)) return null;
        var task = State.Tasks.Single(t => t.Id == process.TaskId);
        var proof = Memory.ExactCommandSuccess(MemoryScope(task), task.Id, receipt.Key[6..]);
        if (proof == null || ExecutionBudget.Digest([proof.Method, proof.Purpose]) != receipt.MethodDigest) return null;
        return new(process.TaskId, process.Id, receipt.Key, receipt.MethodDigest, 0, "동일 이벤트의 성공 실행 근거 evidenceId=" + proof.Id + " / exit=0\n" + proof.Outcome);
    }
    internal void RecordCommandResponse(WorkItem task, OperationReceipt? receipt, JsonElement result)
    {
        if (receipt == null) return;
        if (result.Property("exitCode") is not { ValueKind: JsonValueKind.Number } code || !code.TryGetInt32(out var exit))
        { receipt.State = "unknown"; receipt.Result = "응답에 종료 코드가 없습니다. 실행 상태를 확인해야 합니다."; Save(); throw new OperationReviewRequiredException(ActiveProcess(task)?.Id ?? "", receipt.Result); }
        receipt.ResultReceived = true; receipt.ExitCode = exit;
        receipt.State = exit == 0 ? "completed" : "failed";
        receipt.Result = "exit=" + exit + "\n" + ExecutionMemory.Clean(result.Text("stdout") + result.Text("stderr"), 20000);
        var process = State.Processes.Single(p => p.TaskId == task.Id && p.Operations.Contains(receipt));
        try { PersistCommandResponse(process, receipt); receipt.RecordingPending = false; receipt.RecordingError = ""; Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            receipt.RecordingPending = true; receipt.RecordingError = ExecutionMemory.Clean(ex.Message, 1000);
            if (process.BlockKind.Length == 0) process.BlockKind = "result_recording";
            throw new OperationReviewRequiredException(process.Id, "명령 응답과 종료 코드는 수신했지만 기록 저장을 확인하지 못했습니다. 명령은 재실행하지 않습니다. 프로세스의 ‘실행 기록 확인’에서 " + receipt.Key + " 항목을 확인하세요.");
        }
    }
    public static string OperationReviewSummary(TaskProcess process)
    {
        var pending = process.Operations.Where(o => o.NeedsReview).ToArray();
        return $"실행 기록 확인이 필요한 항목 {pending.Length}개가 있어 재개하지 않았습니다.\n" +
            string.Join("\n", pending.Take(8).Select(o => $"• {o.Key} · 항목 {o.ItemId} · {(o.ResultReceived ? "응답 수신 / 기록 저장 확인 필요" : "실제 실행 결과 미확인")}")) +
            "\n프로세스 → 실행 기록 확인에서 보존된 응답을 대조하세요. 대응하는 호스트 응답이 없으면 실제 부작용 확인이 필요하며 재실행하지 않습니다.";
    }
    public string DescribeOperation(string processId, string key)
    {
        var process = State.Processes.Single(p => p.Id == processId); var receipt = process.Operations.Single(o => o.Key == key);
        var saved = VerifiedCommandResponse(process, receipt);
        return $"실행: {receipt.Key}\n항목: {receipt.ItemId}\n상태: {receipt.State}\n응답 수신: {receipt.ResultReceived}\n종료 코드: {receipt.ExitCode}\n기록 저장 확인 필요: {receipt.RecordingPending}\n기록 오류: {receipt.RecordingError}\n\n보존된 결과\n{receipt.Result}\n\n" +
            (saved == null ? "대응하는 로컬 호스트 응답 기록이 없습니다. 사용자 확인만으로 완료·실패 처리하거나 다시 실행할 수 없습니다." : $"동일 작업·프로세스·실행의 호스트 응답이 있습니다. exit={saved.ExitCode}\n{saved.Result}\n‘수신 응답과 대조’는 실행 결과 기록만 복원합니다. 업무 목표 완료를 판정하거나 작업을 재개하지 않습니다.") +
            (receipt.Resolution == null ? "" : $"\n대조 이력: {receipt.Resolution.At:u} / {receipt.Resolution.Source}\n이전 상태: {receipt.Resolution.PreviousState}\n이전 기록: {receipt.Resolution.PreviousResult}");
    }
    public void RecoverKnownOperation(string processId, string key)
    {
        var process = State.Processes.Single(p => p.Id == processId); var task = State.Tasks.Single(t => t.Id == process.TaskId);
        if (process.Active || IsRunning(task)) throw new HermesException("실행 중에는 기록을 확정하지 않습니다. 중지 상태에서 확인하세요.");
        var receipt = process.Operations.Single(o => o.Key == key);
        if (!receipt.NeedsReview) throw new HermesException("이미 확정된 실행 기록입니다. 재실행하거나 덮어쓰지 않습니다.");
        var saved = VerifiedCommandResponse(process, receipt);
        // Only a response actually received by this host qualifies, never a user/model assertion.
        if (saved == null && receipt.ResultReceived && receipt.ExitCode != null) { PersistCommandResponse(process, receipt); saved = ReadCommandResponse(process, receipt); }
        if (saved == null) throw new HermesException("대응하는 호스트 응답이 없어 안전하게 확정할 수 없습니다. 실제 산출물·외부 처리 상태를 확인해야 합니다. 재실행하지 않았습니다.");
        var before = JsonSerializer.Serialize(receipt); var block = process.BlockKind;
        receipt.Resolution = new(receipt.State, receipt.Result, DateTime.UtcNow, "동일 실행의 로컬 호스트 응답/성공 근거 대조");
        receipt.ResultReceived = true; receipt.ExitCode = saved.ExitCode; receipt.Result = saved.Result;
        receipt.State = saved.ExitCode == 0 ? "completed" : "failed"; receipt.RecordingPending = false; receipt.RecordingError = "";
        if (!process.Operations.Any(o => o.NeedsReview) && process.BlockKind is "uncertain_operation" or "result_recording") process.BlockKind = "";
        try { Save(); }
        catch
        {
            var old = JsonSerializer.Deserialize<OperationReceipt>(before)!;
            receipt.State=old.State; receipt.Result=old.Result; receipt.ResultReceived=old.ResultReceived; receipt.ExitCode=old.ExitCode;
            receipt.RecordingPending=old.RecordingPending; receipt.RecordingError=old.RecordingError; receipt.Resolution=old.Resolution; process.BlockKind=block;
            throw;
        }
        // Deliberately no Resume/Start. Stop, approvals and remaining work stay untouched.
    }
}
