using System.Text.Json;
using Hermes.Core;

internal static class TaskLoopTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        var concurrentStore = new StateStore(Path.Combine(root, "concurrent-save")); var concurrentState = new AppState();
        await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(() => concurrentStore.Save(concurrentState))));
        check(concurrentStore.SaveCount == 30 && concurrentStore.Load().Tasks.Count == 0, "concurrent loop and engine saves preserve atomic state snapshot");
        await using var desk = new DeskController(Path.Combine(root, "data"));
        var project = desk.Register(root); var task = desk.NewTask(project, "loop fixture"); task.ThreadId = "loop-thread"; task.TurnId = "t1"; task.Status = "실행 중";
        var process = desk.BeginProcess(task, "실제 산출물을 검증할 때까지 작업한다.");
        var first = desk.Memory.Record(ExecutionMemory.Scope(root), task.Id, "one", "dependency repair", "command", "broken", false, "Error: dependency missing 123");
        var second = desk.Memory.Record(ExecutionMemory.Scope(root), task.Id, "two", "dependency repair", "command", "broken", false, "Error: dependency missing 456");
        check(desk.Memory.RecoveryContext(ExecutionMemory.Scope(root), task.Id, first).Contains("자동 개선 전환"), "two recorded failures automatically retrieve alternatives");
        check(desk.Memory.Errors(ExecutionMemory.Scope(root), "dependency").Single().Occurrences == 2, "error registry normalizes repeated numeric error details");
        bool rejected = false;
        using (var invalid = JsonDocument.Parse("""{"action":"complete","verification":"결과가 완료되었다고 주장하지만 근거는 없다."}"""))
            try { desk.DecideProcess(task, invalid.RootElement, "t1"); } catch (IOException) { rejected = true; }
        check(rejected, "loop completion rejects missing successful execution evidence");
        var proof = desk.Memory.Record(ExecutionMemory.Scope(root), task.Id, "fixed", "dependency repair", "command", "changed-method", true, "verified output");
        var recovery = desk.Memory.Recoveries(ExecutionMemory.Scope(root)).Single();
        check(recovery.EvidenceId == proof.Id && recovery.ErrorKey == second.ErrorKey, "changed successful method is linked to original error");
        desk.Memory.Improve(ExecutionMemory.Scope(root), recovery.MethodId, proof.Id, "의존성 경로를 확인하고 변경한 방법을 실행한 뒤 결과를 검증한다.");
        desk.Memory.UpdateRecovery(ExecutionMemory.Scope(root), recovery.Id, "오류 원인과 변경된 해결 방법을 검증 후 개정했다.");
        var errorRecord = desk.Memory.Errors(ExecutionMemory.Scope(root)).Single();
        desk.Memory.UpdateError(ExecutionMemory.Scope(root), errorRecord.Key, errorRecord.Tool, "수정 후 출력 결과 확인", "해결 확인");
        check(desk.Memory.Errors(ExecutionMemory.Scope(root)).Single().Status == "해결 확인" && desk.Memory.BlockReason(ExecutionMemory.Scope(root), task.Id, "command", "broken") != null, "marking error resolved preserves original retry audit");
        check(desk.Memory.Recoveries(ExecutionMemory.Scope(root)).Single().Notes.Contains("검증"), "improvement notes persist with matching execution proof");
        check(desk.Memory.Context(ExecutionMemory.Scope(root), "dependency").Contains("오류 목록"), "RAG retrieval includes grouped error registry");
        check(desk.Memory.Recoveries(ExecutionMemory.Scope(root), "개정했다").Count == 1, "RAG retrieves improvements by revised notes as well as original error");
        check(desk.Memory.Errors(ExecutionMemory.Scope(root + "-other")).Count == 0, "error registry isolates project scope");
        using (var complete = JsonDocument.Parse(JsonSerializer.Serialize(new { action = "complete", verification = "실제 결과 파일을 확인하여 원래 작업의 완료를 검증했습니다.", evidenceId = proof.Id }))) desk.DecideProcess(task, complete.RootElement, "t1");
        using (var completed = JsonDocument.Parse("""{"threadId":"loop-thread","turn":{"id":"t1","status":"completed"}}""")) desk.Handle(new("turn/completed", completed.RootElement));
        check(process.State == "완료" && task.Status == "완료", "verified report ends completion loop");
        using (var duplicate = JsonDocument.Parse("""{"threadId":"loop-thread","turn":{"id":"t1","status":"completed"}}""")) desk.Handle(new("turn/completed", duplicate.RootElement));
        check(process.Round == 1 && !process.Active, "duplicate completion does not restart finished process");
        var next = desk.NewTask(project, "wait fixture"); next.ThreadId = "next-thread"; next.TurnId = "t2"; next.Status = "실행 중";
        var waiting = desk.BeginProcess(next, "미완료 작업을 계속 수행한다.");
        using (var incomplete = JsonDocument.Parse("""{"threadId":"next-thread","turn":{"id":"t2","status":"completed"}}""")) desk.Handle(new("turn/completed", incomplete.RootElement));
        check(waiting.State == "다음 실행 대기" && DeskController.IsRunning(next), "final message alone queues continuation and remains running");
        var sibling = desk.NewTask(project, "sibling"); var siblingProcess = desk.BeginProcess(sibling, "별개 작업을 계속 실행한다.");
        await desk.StopProcessAsync(waiting.Id);
        check(waiting.State == "종료됨" && next.Status == "중단됨" && siblingProcess.Active, "stop during retry delay cancels only selected process");
        await Task.Delay(1100);
        check(waiting.Round == 1 && !waiting.Active, "cancelled queued loop cannot resurrect");
        var persisted = new StateStore(desk.DataDirectory).Load();
        check(persisted.Processes.Single(p => p.Id == siblingProcess.Id).State == "앱 종료로 중단됨", "restart preserves process history without silently rerunning work");
        var blockedTask = desk.NewTask(project, "blocked"); blockedTask.ThreadId = "blocked-thread"; blockedTask.TurnId = "blocked-turn"; blockedTask.Status = "실행 중";
        var blockedProcess = desk.BeginProcess(blockedTask, "외부 자격 증명이 필요한 작업이다.");
        using (var blocked = JsonDocument.Parse("""{"action":"blocked","verification":"서버 자격 증명이 없어 사용자 입력을 기다려야 합니다."}""")) desk.DecideProcess(blockedTask, blocked.RootElement, "blocked-turn");
        using (var completed = JsonDocument.Parse("""{"threadId":"blocked-thread","turn":{"id":"blocked-turn","status":"completed"}}""")) desk.Handle(new("turn/completed", completed.RootElement));
        check(blockedProcess.State == "사용자 확인 필요" && !blockedProcess.Active, "external blocker ends automatic spin with explicit reason");
        var reopened = new ExecutionMemory(Path.Combine(desk.DataDirectory, "knowledge"));
        check(reopened.Errors(ExecutionMemory.Scope(root)).Single().Occurrences == 2 && reopened.Recoveries(ExecutionMemory.Scope(root)).Count == 1, "error and improvement registries survive reopening database");
    }
}
