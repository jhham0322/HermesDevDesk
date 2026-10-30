using System.Security.Cryptography;
using System.Text.Json;
namespace Hermes.Core;
public sealed class WorkRequirement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Planned { get; set; }
}
public sealed class WorkArtifact
{
    public string Path { get; set; } = "";
    public string Contains { get; set; } = "";
}
public sealed class QueuedWork
{
    public string Id { get; set; } = "";
    public string RequestId { get; set; } = "initial";
    public string Title { get; set; } = "";
    public string Acceptance { get; set; } = "";
    public string Kind { get; set; } = "artifact";
    public string ExpectedOutput { get; set; } = "";
    public List<string> DependsOn { get; set; } = [];
    public List<WorkArtifact> Artifacts { get; set; } = [];
    public int Priority { get; set; }
    public string State { get; set; } = "pending";
    public long EvidenceStart { get; set; }
    public long EvidenceId { get; set; }
    public int TranscriptStart { get; set; }
    public string Verification { get; set; } = "";
    public string ArtifactDigest { get; set; } = "";
    public bool Terminal => State is "verified" or "cancelled" or "superseded";
}
public sealed class OperationReceipt
{
    public string Key { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string MethodDigest { get; set; } = "";
    public string State { get; set; } = "started";
    public string Result { get; set; } = "";
}
public enum RequirementChange { Append, CancelPending, ReplacePending }
public static class WorkQueue
{
    public static QueuedWork? Next(TaskProcess process) => process.Items.Where(i => i.State is "pending" or "running")
        .Where(i => i.DependsOn.All(d => process.Items.Single(x => x.Id == d).State == "verified"))
        .OrderBy(i => i.State == "running" ? 0 : 1).ThenBy(i => i.Priority).ThenBy(i => process.Items.IndexOf(i)).FirstOrDefault();
    public static void Validate(IReadOnlyList<QueuedWork> items)
    {
        if (items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != items.Count) throw new HermesException("작업 항목 ID 중복입니다.");
        foreach (var i in items)
            if (i.Id.Length is < 1 or > 80 || i.Title.Length is < 1 or > 1000 || i.Acceptance.Length is < 10 or > 4000 || i.Kind is not ("artifact" or "evidence" or "answer")
                || i.DependsOn.Any(d => !items.Any(x => x.Id == d)) || i.DependsOn.Contains(i.Id)
                || (i.Kind == "artifact" && i.Artifacts.Count == 0) || (i.Kind == "evidence" && i.ExpectedOutput.Length < 3)) throw new HermesException("작업 항목의 완료 조건·검증 방식·의존관계를 확인하세요.");
        var visited = new HashSet<string>(); var visiting = new HashSet<string>();
        void Visit(string id) { if (visited.Contains(id)) return; if (!visiting.Add(id)) throw new HermesException("작업 의존관계에 순환이 있습니다."); foreach (var dep in items.Single(i => i.Id == id).DependsOn) Visit(dep); visiting.Remove(id); visited.Add(id); }
        foreach (var i in items) Visit(i.Id);
    }
    public static string CheckArtifacts(string root, QueuedWork item)
    {
        var facts = new List<string>();
        foreach (var check in item.Artifacts)
        {
            var path = Safety.Resolve(root, check.Path);
            if (!File.Exists(path)) throw new HermesException("완료 산출물이 없습니다: " + check.Path);
            if (check.Contains.Length > 0 && (new FileInfo(path).Length > 10000000 || !File.ReadAllText(path).Contains(check.Contains, StringComparison.Ordinal))) throw new HermesException("산출물 완료 조건이 충족되지 않았습니다: " + check.Path);
            using var input = File.OpenRead(path); facts.Add(check.Path + ":" + Convert.ToHexString(SHA256.HashData(input)));
        }
        return ExecutionBudget.Digest(facts);
    }
    public static string Summary(TaskProcess p, int offset = 0)
    {
        var next = Next(p);
        return JsonSerializer.Serialize(new { offset, itemCount = p.Items.Count, requestCount = p.Requirements.Count, goal = p.Goal[..Math.Min(p.Goal.Length, 8000)], next, completed = p.Items.Where(i => i.State == "verified").Skip(offset).Take(30).Select(i => new { i.Id, i.Verification, i.ArtifactDigest }),
            remaining = p.Items.Where(i => !i.Terminal).Skip(offset).Take(30).Select(i => new { i.Id, i.Title, i.State, i.DependsOn }),
            requests = p.Requirements.Skip(offset).Take(20).Select(r => new { r.Id, r.Planned, text = r.Text[..Math.Min(r.Text.Length, 1200)] }),
            operations = p.Operations.Select(o => new { o.Key, o.ItemId, o.State, result = o.Result[..Math.Min(o.Result.Length, 1000)] }).TakeLast(12), p.BlockKind, p.Checkpoint });
    }
}
public sealed partial class DeskController
{
    internal string QueueRequirement(WorkItem task, string text, RequirementChange change = RequirementChange.Append)
    {
        var p = ActiveProcess(task) ?? State.Processes.LastOrDefault(x => x.TaskId == task.Id && x.ContinuationEnabled && x.State != "완료") ?? throw new HermesException("이어갈 작업이 없습니다.");
        if (!Enum.IsDefined(change)) throw new HermesException("요구 변경 모드를 확인하세요.");
        text = ExecutionMemory.Clean(text, 100000); if (text.Length == 0) throw new HermesException("요구 내용을 입력하세요.");
        if (change != RequirementChange.Append)
            foreach (var item in p.Items.Where(i => !i.Terminal)) item.State = change == RequirementChange.CancelPending ? "cancelled" : "superseded";
        p.Decision = null; p.DecisionTurnId = "";
        if (change != RequirementChange.Append) foreach (var r in p.Requirements) r.Planned = true;
        if (change == RequirementChange.CancelPending) { p.NeedsPlan = false; p.Checkpoint = "사용자가 미완료 항목을 취소했습니다. 완료된 산출물은 보존합니다."; Save(); return "cancelled"; }
        var requirement = new WorkRequirement { Text = text, Planned = task.ThreadLearningVersion == 1 }; p.Requirements.Add(requirement); p.NeedsPlan = task.ThreadLearningVersion != 1; p.Updated = DateTime.UtcNow; Save(); return requirement.Id;
    }
    private string ManageWorkQueue(WorkItem task, TaskProcess p, JsonElement args)
    {
        var action = args.Text("action");
        if (action == "status") return WorkQueue.Summary(p, Math.Max(0, args.Property("offset")?.GetInt32() ?? 0));
        if (action == "read_request") { var r = p.Requirements.SingleOrDefault(r => r.Id == args.Text("requestId")) ?? throw new HermesException("요구 ID를 확인하세요."); var offset = Math.Clamp(args.Property("offset")?.GetInt32() ?? 0, 0, r.Text.Length); return r.Text.Substring(offset, Math.Min(8000, r.Text.Length - offset)); }
        if (action == "plan")
        {
            var incoming = JsonSerializer.Deserialize<List<QueuedWork>>(args.GetProperty("items").GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            if (incoming.Count == 0) throw new HermesException("실행할 작업 항목을 입력하세요.");
            var candidate = JsonSerializer.Deserialize<List<QueuedWork>>(JsonSerializer.Serialize(p.Items))!;
            foreach (var item in incoming)
            {
                item.Id = ExecutionMemory.Clean(item.Id, 80); item.Title = ExecutionMemory.Clean(item.Title, 1000); item.Acceptance = ExecutionMemory.Clean(item.Acceptance, 4000); item.ExpectedOutput = ExecutionMemory.Clean(item.ExpectedOutput, 4000);
                foreach (var artifact in item.Artifacts) { artifact.Path = ExecutionMemory.Clean(artifact.Path, 1000); artifact.Contains = ExecutionMemory.Clean(artifact.Contains, 4000); }
                if (!p.Requirements.Any(r => r.Id == item.RequestId)) throw new HermesException("원문 요구 ID를 확인하세요.");
                var existing = candidate.SingleOrDefault(i => i.Id == item.Id);
                if (existing != null) { if (existing.Title != item.Title || existing.Acceptance != item.Acceptance || existing.Kind != item.Kind || existing.ExpectedOutput != item.ExpectedOutput || JsonSerializer.Serialize(existing.Artifacts) != JsonSerializer.Serialize(item.Artifacts) || !existing.DependsOn.SequenceEqual(item.DependsOn)) throw new HermesException("기존 작업의 완료 조건을 덮어쓸 수 없습니다. 명시적 요구 변경은 새 ID로 추가하세요."); if (!existing.Terminal) existing.Priority = item.Priority; }
                else { item.State = "pending"; item.EvidenceId = 0; item.Verification = item.ArtifactDigest = ""; candidate.Add(item); }
            }
            WorkQueue.Validate(candidate); p.Items = candidate;
            foreach (var request in p.Requirements.Where(r => candidate.Any(i => i.RequestId == r.Id && i.State != "superseded"))) request.Planned = true;
            p.NeedsPlan = false; p.Decision = null; Save(); return WorkQueue.Summary(p);
        }
        var selected = p.Items.SingleOrDefault(i => i.Id == args.Text("itemId")) ?? throw new HermesException("작업 항목 ID를 확인하세요.");
        if (selected.State == "verified") return "이미 검증 완료된 항목입니다. 부작용을 다시 실행하지 마세요. " + selected.Verification;
        if (selected.Terminal) throw new HermesException("취소·대체된 항목은 실행할 수 없습니다.");
        if (action == "start_item")
        {
            if (WorkQueue.Next(p)?.Id != selected.Id) throw new HermesException("현재 실행 가능한 우선 항목·의존관계를 확인하세요.");
            if (selected.State != "running") { selected.State = "running"; selected.EvidenceStart = Memory.Recent(MemoryScope(task), task.Id).Select(e => e.Id).DefaultIfEmpty().Max(); selected.TranscriptStart = task.Transcript.Count; }
            Save(); return "항목 실행 시작. 완료 조건: " + selected.Acceptance;
        }
        if (action != "verify_item" || selected.State != "running") throw new HermesException("진행 중인 항목의 실제 검증만 완료 처리할 수 있습니다.");
        var verification = ExecutionMemory.Clean(args.Text("verification"), 4000); if (verification.Length < 10) throw new HermesException("관측한 검증 근거를 입력하세요.");
        if (selected.Kind == "answer")
        {
            var delivered = task.Transcript.Skip(selected.TranscriptStart).LastOrDefault(e => e.Kind == "에이전트" && e.Text.Length >= 10)?.Text;
            if (delivered == null) throw new HermesException("실제 전달된 답변이 없습니다.");
            selected.ArtifactDigest = ExecutionBudget.Digest([delivered]);
        }
        else
        {
            var id = args.Property("evidenceId")?.GetInt64() ?? 0;
            var proof = Memory.Recent(MemoryScope(task), task.Id).SingleOrDefault(e => e.Id == id && e.Id > selected.EvidenceStart && e.Succeeded && e.Tool is not ("workflow.report" or "provider" or "memory"));
            if (proof == null) throw new HermesException("항목 시작 후 성공한 실제 검증 evidenceId가 필요합니다.");
            if (selected.Kind == "evidence" && !proof.Outcome.Contains(selected.ExpectedOutput, StringComparison.Ordinal)) throw new HermesException("실행 출력이 항목의 완료 조건과 일치하지 않습니다.");
            if (selected.Kind == "artifact") selected.ArtifactDigest = WorkQueue.CheckArtifacts(Project(task).Root, selected);
            selected.EvidenceId = id;
        }
        selected.State = "verified"; selected.Verification = verification; p.Decision = null; p.Checkpoint = "검증 완료: " + selected.Id + ". 다음: " + (WorkQueue.Next(p)?.Id ?? "남은 요구 확인"); Save(); return WorkQueue.Summary(p);
    }
}
