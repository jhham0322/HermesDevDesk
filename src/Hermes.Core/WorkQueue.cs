using System.Security.Cryptography;
using System.Text.Json;
namespace Hermes.Core;
public sealed class WorkRequirement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Planned { get; set; }
    public List<ProjectImage> PendingImages { get; set; } = [];
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
    public DateTime? Started { get; set; }
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
    public bool ResultReceived { get; set; }
    public int? ExitCode { get; set; }
    public bool RecordingPending { get; set; }
    public string RecordingError { get; set; } = "";
    public OperationResolution? Resolution { get; set; }
    public bool NeedsReview => State is "started" or "unknown" || RecordingPending;
    public override string ToString() => $"[{State}{(RecordingPending ? " / 저장 확인 필요" : "")}] {Key} · 항목 {ItemId}";
}
public sealed record OperationResolution(string PreviousState, string PreviousResult, DateTime At, string Source);
public enum RequirementChange { Append, CancelPending, ReplacePending }
public static class WorkQueue
{
    public static QueuedWork? Next(TaskProcess process) => process.Items.Where(i => i.State is "pending" or "running")
        .Where(i => i.DependsOn.All(d => process.Items.Single(x => x.Id == d).State == "verified"))
        .OrderBy(i => i.State == "running" ? 0 : 1).ThenBy(i => i.Priority).ThenBy(i => process.Items.IndexOf(i)).FirstOrDefault();
    public static void Validate(IReadOnlyList<QueuedWork> items)
    {
        var byId = new Dictionary<string, QueuedWork>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var label = string.IsNullOrWhiteSpace(item.Id) ? item.Title : item.Id;
            if (string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 80)
                throw new HermesException($"작업 항목 '{label}': id는 1~80자여야 합니다.");
            if (!byId.TryAdd(item.Id, item))
                throw new HermesException($"작업 항목 '{item.Id}': id가 중복되었습니다.");
            if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 1000)
                throw new HermesException($"작업 항목 '{item.Id}': title은 1~1000자여야 합니다.");
            if (string.IsNullOrWhiteSpace(item.Acceptance) || item.Acceptance.Length > 4000)
                throw new HermesException($"작업 항목 '{item.Id}': acceptance에 완료 조건을 입력하세요.");
            if (item.Kind is not ("artifact" or "evidence" or "answer"))
                throw new HermesException($"작업 항목 '{item.Id}': kind는 artifact, evidence, answer 중 하나여야 합니다.");
            if (item.Kind == "artifact" && (item.Artifacts.Count == 0 || item.Artifacts.Any(a => string.IsNullOrWhiteSpace(a.Path))))
                throw new HermesException($"작업 항목 '{item.Id}': artifact에는 artifacts에 산출물 path가 필요합니다.");
            if (item.Kind == "evidence" && string.IsNullOrWhiteSpace(item.ExpectedOutput))
                throw new HermesException($"작업 항목 '{item.Id}': evidence에는 실제 검증 출력에서 확인할 expectedOutput이 필요합니다.");
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Visit(QueuedWork item)
        {
            if (visited.Contains(item.Id)) return;
            if (!visiting.Add(item.Id))
                throw new HermesException($"작업 항목 '{item.Id}': dependsOn에 순환이 있습니다.");
            foreach (var dependency in item.DependsOn)
            {
                if (!byId.TryGetValue(dependency, out var previous))
                    throw new HermesException($"작업 항목 '{item.Id}': dependsOn의 '{dependency}' 항목을 찾을 수 없습니다.");
                if (previous.State is "cancelled" or "superseded" && !item.Terminal)
                    throw new HermesException($"작업 항목 '{item.Id}': dependsOn의 '{dependency}' 항목이 취소되었거나 대체되었습니다.");
                Visit(previous);
            }
            visiting.Remove(item.Id);
            visited.Add(item.Id);
        }
        foreach (var item in items) Visit(item);
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
    internal string QueueRequirement(WorkItem task, string text, RequirementChange change = RequirementChange.Append, IReadOnlyList<ProjectImage>? images = null)
    {
        var p = ActiveProcess(task) ?? State.Processes.LastOrDefault(x => x.TaskId == task.Id && x.ContinuationEnabled && x.State != "완료") ?? throw new HermesException("이어갈 작업이 없습니다.");
        if (!Enum.IsDefined(change)) throw new HermesException("요구 변경 모드를 확인하세요.");
        text = ExecutionMemory.Clean(text, 100000); if (text.Length == 0) throw new HermesException("요구 내용을 입력하세요.");
        if (change != RequirementChange.Append)
            foreach (var item in p.Items.Where(i => !i.Terminal)) item.State = change == RequirementChange.CancelPending ? "cancelled" : "superseded";
        p.Decision = null; p.DecisionTurnId = ""; p.StagnantRounds = 0;
        if (change != RequirementChange.Append) foreach (var r in p.Requirements) { r.Planned = true; r.PendingImages.Clear(); }
        if (change == RequirementChange.CancelPending) { p.NeedsPlan = false; p.Checkpoint = "사용자가 미완료 항목을 취소했습니다. 완료된 산출물은 보존합니다."; SaveCheckpoint(p); return "cancelled"; }
        var requirement = new WorkRequirement { Text = text, Planned = task.ThreadLearningVersion == 1, PendingImages = images?.ToList() ?? [] }; p.Requirements.Add(requirement); p.NeedsPlan = task.ThreadLearningVersion != 1; p.Updated = DateTime.UtcNow; SaveCheckpoint(p); return requirement.Id;
    }
    private string ManageWorkQueue(WorkItem task, TaskProcess p, JsonElement args)
    {
        var action = args.Text("action");
        if (action == "status") return WorkQueue.Summary(p, Math.Max(0, args.Property("offset")?.GetInt32() ?? 0));
        if (action == "read_request") { var r = p.Requirements.SingleOrDefault(r => r.Id == args.Text("requestId")) ?? throw new HermesException("요구 ID를 확인하세요."); var offset = Math.Clamp(args.Property("offset")?.GetInt32() ?? 0, 0, r.Text.Length); return r.Text.Substring(offset, Math.Min(8000, r.Text.Length - offset)); }
        if (action == "plan")
        {
            if (args.Property("items") is not { ValueKind: JsonValueKind.Array } plan || plan.GetArrayLength() is < 1 or > 200)
                throw new HermesException("plan의 items에 1~200개 작업 항목을 입력하세요.");
            List<QueuedWork> incoming;
            try { incoming = plan.Deserialize<List<QueuedWork>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!; }
            catch (JsonException ex) { throw new HermesException($"작업 계획 JSON 형식을 확인하세요. 오류 위치: {ex.Path}", ex); }
            if (incoming.Any(item => item == null))
                throw new HermesException("plan의 items에는 빈 항목을 넣을 수 없습니다.");
            var candidate = JsonSerializer.Deserialize<List<QueuedWork>>(JsonSerializer.Serialize(p.Items))!;
            var incomingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in incoming)
            {
                item.Id = ExecutionMemory.Clean(item.Id ?? "", 80).Trim(); item.Title = ExecutionMemory.Clean(item.Title ?? "", 1000); item.Acceptance = ExecutionMemory.Clean(item.Acceptance ?? "", 4000); item.ExpectedOutput = ExecutionMemory.Clean(item.ExpectedOutput ?? "", 4000);
                if (!incomingIds.Add(item.Id))
                    throw new HermesException($"작업 항목 '{item.Id}': 현재 계획 요청에서 id가 중복되었습니다.");
                item.RequestId = ExecutionMemory.Clean(item.RequestId ?? "initial", 80).Trim();
                item.Kind = (item.Kind ?? "").Trim().ToLowerInvariant();
                item.DependsOn = (item.DependsOn ?? []).Select(id => ExecutionMemory.Clean(id ?? "", 80).Trim()).Distinct(StringComparer.Ordinal).ToList();
                item.Artifacts ??= [];
                if (item.Artifacts.Any(artifact => artifact == null))
                    throw new HermesException($"작업 항목 '{item.Id}': artifacts에는 빈 항목을 넣을 수 없습니다.");
                foreach (var artifact in item.Artifacts) { artifact.Path = ExecutionMemory.Clean(artifact.Path ?? "", 1000); artifact.Contains = ExecutionMemory.Clean(artifact.Contains ?? "", 4000); }
                if (!p.Requirements.Any(r => r.Id == item.RequestId)) throw new HermesException("원문 요구 ID를 확인하세요.");
                var existing = candidate.SingleOrDefault(i => i.Id == item.Id);
                if (existing != null) { if (existing.RequestId != item.RequestId || existing.Title != item.Title || existing.Acceptance != item.Acceptance || existing.Kind != item.Kind || existing.ExpectedOutput != item.ExpectedOutput || JsonSerializer.Serialize(existing.Artifacts) != JsonSerializer.Serialize(item.Artifacts) || !existing.DependsOn.SequenceEqual(item.DependsOn)) throw new HermesException("기존 작업의 완료 조건을 덮어쓸 수 없습니다. 명시적 요구 변경은 새 ID로 추가하세요."); if (!existing.Terminal) existing.Priority = item.Priority; }
                else { item.State = "pending"; item.EvidenceId = item.EvidenceStart = 0; item.Started = null; item.TranscriptStart = 0; item.Verification = item.ArtifactDigest = ""; candidate.Add(item); }
            }
            WorkQueue.Validate(candidate); p.Items = candidate;
            foreach (var request in p.Requirements.Where(r => candidate.Any(i => i.RequestId == r.Id && i.State != "superseded"))) request.Planned = true;
            p.NeedsPlan = p.Requirements.Any(r => !r.Planned); p.Decision = null;
            if (p.RequirePlan && !p.NeedsPlan && WorkQueue.Next(p) is { } first) StartQueueItem(task, p, first);
            SaveCheckpoint(p);
            return WorkQueue.Summary(p);
        }
        var selected = p.Items.SingleOrDefault(i => i.Id == args.Text("itemId")) ?? throw new HermesException("작업 항목 ID를 확인하세요.");
        if (selected.State == "verified") return "이미 검증 완료된 항목입니다. 부작용을 다시 실행하지 마세요. " + selected.Verification;
        if (selected.Terminal) throw new HermesException("취소·대체된 항목은 실행할 수 없습니다.");
        if (action == "start_item")
        {
            if (WorkQueue.Next(p)?.Id != selected.Id) throw new HermesException("현재 실행 가능한 우선 항목·의존관계를 확인하세요.");
            if (selected.State != "running")
            {
                StartQueueItem(task, p, selected);
                SaveCheckpoint(p);
            }
            return "항목 실행 중. 완료 조건: " + selected.Acceptance;
        }
        if (action != "verify_item" || selected.State != "running") throw new HermesException("진행 중인 항목의 실제 검증만 완료 처리할 수 있습니다.");
        var verification = ExecutionMemory.Clean(args.Text("verification"), 4000); if (verification.Length < 10) throw new HermesException("관측한 검증 근거를 입력하세요.");
        if (selected.Kind == "answer")
        {
            var delivered = selected.Started is { } started
                ? task.Transcript.LastOrDefault(e => e.At >= started && e.Kind == "에이전트" && !string.IsNullOrWhiteSpace(e.Text))?.Text
                : task.Transcript.Skip(selected.TranscriptStart).LastOrDefault(e => e.Kind == "에이전트" && e.Text.Length >= 10)?.Text;
            if (delivered == null) throw new HermesException("실제 전달된 답변이 없습니다.");
            selected.ArtifactDigest = ExecutionBudget.Digest([delivered]);
        }
        else if (p.RequirePlan && selected.Kind == "artifact")
        {
            selected.ArtifactDigest = WorkQueue.CheckArtifacts(Project(task).Root, selected);
        }
        else
        {
            var id = args.Property("evidenceId")?.GetInt64() ?? 0;
            var proof = Memory.Recent(MemoryScope(task), task.Id).SingleOrDefault(e => e.Id == id && e.Id > selected.EvidenceStart && e.Succeeded && e.Tool is not ("workflow.report" or "provider" or "memory"));
            if (proof == null) throw new HermesException("항목 시작 후 성공한 실제 검증 evidenceId가 필요합니다.");
            if (selected.Kind == "evidence" && !Memory.OutcomeContains(MemoryScope(task), task.Id, proof.Id, selected.ExpectedOutput)) throw new HermesException("실행 출력이 항목의 완료 조건과 일치하지 않습니다.");
            if (selected.Kind == "artifact") selected.ArtifactDigest = WorkQueue.CheckArtifacts(Project(task).Root, selected);
            selected.EvidenceId = id;
        }
        RecordItemCompletion(p, selected, verification);
        var next = WorkQueue.Next(p);
        var startNext = !p.NeedsPlan && next != null && args.Property("startNext") is { ValueKind: JsonValueKind.True };
        if (startNext) StartQueueItem(task, p, next!);
        SaveCheckpoint(p);
        return "완료: " + selected.Id + (startNext
            ? "\n다음 항목 실행 시작: " + JsonSerializer.Serialize(next)
            : ". 다음: " + (next?.Id ?? "최종 결과 전달"));
    }

    private void StartQueueItem(WorkItem task, TaskProcess process, QueuedWork item)
    {
        if (item.State == "running") return;
        item.Started = DateTime.UtcNow;
        item.State = "running";
        item.TranscriptStart = task.Transcript.Count;
        if (item.Kind == "evidence" || !process.RequirePlan)
            item.EvidenceStart = Memory.Recent(MemoryScope(task), task.Id).Select(e => e.Id).DefaultIfEmpty().Max();
    }

    private static void RecordItemCompletion(TaskProcess process, QueuedWork item, string verification)
    {
        item.State = "verified";
        item.Verification = verification;
        process.Decision = null;
        process.Checkpoint = "완료: " + item.Id;
    }
}
