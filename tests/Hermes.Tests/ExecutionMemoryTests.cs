using Hermes.Core;
internal static class ExecutionMemoryTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var memory = new ExecutionMemory(Path.Combine(root, "learning")); var scope = ExecutionMemory.Scope(root); var other = ExecutionMemory.Scope(root + "-other");
        memory.Record(scope, "task", "1", "설치 경로 오류 수정", "command", "bad-command", false, "Error: missing dependency 123");
        check(memory.BlockReason(scope, "task", "command", "bad-command") == null, "one failed method permits diagnosis/retry");
        memory.Record(scope, "task", "2", "설치 경로 오류 수정", "command", "bad-command", false, "Error: missing dependency 456");
        check(memory.BlockReason(scope, "task", "command", "bad-command") != null, "same normalized error twice blocks third execution");
        check(memory.BlockReason(scope, "task", "command", "fixed-command") == null, "changed method remains executable");
        check(memory.BlockReason(scope, "another-task", "command", "bad-command") == null, "retry guard is scoped to persistent task");
        var proof = memory.Record(scope, "task", "3", "설치 경로 오류 수정", "command", "fixed-command", true, "validated");
        var found = memory.Search(scope, "설치경로 수정");
        check(found.Count == 1 && found[0].Method == "fixed-command", "Korean bigram RAG finds similar purpose");
        memory.Improve(scope, found[0].Id, proof.Id, "의존성을 먼저 확인한 후 수정 명령을 실행하고 결과를 검증한다.");
        var reopened = new ExecutionMemory(Path.Combine(root, "learning")); var updated = reopened.Search(scope, "설치").Single();
        check(updated.Revision == 2 && updated.Notes.Contains("의존성"), "improved method revision survives restart");
        check(reopened.BlockReason(scope, "task", "command", "bad-command") != null, "repeat failure guard survives restart");
        check(reopened.Search(other, "설치").Count == 0 && reopened.Recent(other).Count == 0, "RAG never retrieves other projects");
        bool rejected = false; try { memory.Improve(other, found[0].Id, proof.Id, "cross project notes"); } catch (IOException) { rejected = true; }
        check(rejected, "cross project method edits rejected");
        rejected = false; try { memory.Improve(scope, found[0].Id, proof.Id + 1000, "invented evidence"); } catch (IOException) { rejected = true; }
        check(rejected, "invented execution proof rejected");
        memory.Record(scope, "task", "4", "설치 경로 오류 수정", "command", "fixed-command", false, "Error: environment changed");
        check(memory.Search(scope, "설치").Single().Failures == 1, "a failing learned method loses reliability");
        memory.Record(scope, "task", "5", "설치 경로 오류 수정", "command", "better-command", true, "verified");
        updated = memory.Search(scope, "설치").Single();
        check(updated.Method == "better-command" && updated.Revision == 3 && updated.Failures == 0, "better executed method automatically replaces old guide with new revision");
        var duplicate = memory.Record(scope, "task", "5", "설치 경로 오류 수정", "command", "better-command", true, "verified");
        check(memory.Recent(scope).Count == 5 && duplicate.Id > 0, "duplicate execution events do not inflate history");
        check(ExecutionMemory.Clean("password=unregistered-example-secret").Contains("[redacted]"), "memory strips credential assignments before storage");
        memory.Delete(scope, updated.Id);
        check(memory.Search(scope, "설치").Count == 0 && memory.BlockReason(scope, "task", "command", "bad-command") != null, "deleting guide removes FTS entry and preserves failure audit");
    }
}
