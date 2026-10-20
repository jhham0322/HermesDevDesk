using System.Text.Json;

namespace Hermes.Core;
public sealed class ProjectEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Root { get; set; } = "";
    public string? ParentProjectId { get; set; }
    public string? WorktreeBranch { get; set; }
    public string? BaseCommit { get; set; }
    public bool LocalMcpEnabled { get; set; }
    public bool FullPermissions { get; set; } = true;
    public bool BrowserEnabled { get; set; }
    public bool ComputerEnabled { get; set; }
    public override string ToString() => Name;
}
public sealed class WorkItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = "";
    public string Title { get; set; } = "새 작업";
    public string? ThreadId { get; set; }
    public string? TurnId { get; set; }
    public string Status { get; set; } = "대기";
    public DateTime Updated { get; set; } = DateTime.UtcNow;
    public bool? WritablePreference { get; set; }
    public bool Writable { get; set; }
    public bool? ThreadWritable { get; set; }
    public List<TranscriptEntry> Transcript { get; set; } = [];
    public string Diff { get; set; } = "";
    public List<ChangedFile> ChangedFiles { get; set; } = [];
    public string ProviderFingerprint { get; set; } = "";
    public string Model { get; set; } = "";
    public bool UsageBaselineKnown { get; set; }
    public bool? ThreadMcpEnabled { get; set; }
    public bool? ThreadBrowserEnabled { get; set; }
    public bool? ThreadComputerEnabled { get; set; }
    public bool IsReview { get; set; }
    public bool Archived { get; set; }
    public bool Pinned { get; set; }
    public string ReasoningEffort { get; set; } = "";
    public List<SkillRecord> Skills { get; set; } = [];
    public List<AttachmentRecord> Attachments { get; set; } = [];
    public override string ToString() => $"{Title} · {Status}";
}
public sealed record TranscriptEntry(string Kind, string Text, DateTime At, string? ItemId = null);
public sealed class AppState
{
    public string PendingModel { get; set; } = "";
    public Dictionary<string, List<string>> SavedModels { get; set; } = [];
    public string? CodexPath { get; set; }
    public List<ProjectEntry> Projects { get; set; } = [];
    public List<WorkItem> Tasks { get; set; } = [];
    public ProviderSettings Provider { get; set; } = new();
    public List<ScheduledDraft> ScheduledDrafts { get; set; } = [];
    public List<UsageEntry> Usage { get; set; } = [];
    public Dictionary<string, TokenCounts> ThreadUsageSnapshots { get; set; } = [];
    public DesktopPreferences Desktop { get; set; } = new();
}
public sealed class DesktopPreferences
{
    public SavedWindowBounds? WindowBounds { get; set; }
    public bool WindowMaximized { get; set; }
    public bool DarkTheme { get; set; }
    public bool SidebarVisible { get; set; } = true;
    public bool InspectorVisible { get; set; }
    public double InspectorWidth { get; set; } = 400;
    public bool EnterToSend { get; set; } = true;
    public string GitExecutable { get; set; } = "git";
    public Dictionary<string, string> Drafts { get; set; } = [];
}
public sealed class StateStore
{
    public int SaveCount { get; private set; }
    private readonly string path;
    public StateStore(string directory) { Directory.CreateDirectory(directory); path = Path.Combine(directory, "state.json"); }
    public AppState Load()
    {
        if (!File.Exists(path)) return new();
        var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path)) ?? throw new IOException("이력 파일 손상: 원본을 보존했습니다.");
        foreach (var entry in state.Usage.Where(e => e.Status == "started")) { entry.Status = "unknown-after-restart"; entry.Requests = null; entry.Note = "연결 종료 전 최종 usage를 받지 못함"; }
        foreach (var task in state.Tasks.Where(t => t.Status is "실행 중" or "승인 대기" or "시작 중" or "중단 중")) { task.Status = "연결 끊김 · 재개 가능"; task.TurnId = null; }
        return state;
    }
    public void Save(AppState state)
    {
        foreach (var id in state.Desktop.Drafts.Keys.ToList()) state.Desktop.Drafts[id] = Safety.Redact(state.Desktop.Drafts[id]);
        foreach (var draft in state.ScheduledDrafts) { draft.Title = Safety.Redact(draft.Title); draft.Prompt = Safety.Redact(draft.Prompt); }
        foreach (var task in state.Tasks)
        {
            task.Title = Safety.Redact(task.Title); task.Diff = Safety.Redact(task.Diff);
            task.Transcript = task.Transcript.Select(e => e with { Text = Safety.Redact(e.Text) }).ToList();
        }
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        var pending = path + ".new";
        File.WriteAllText(pending, json);
        if (File.Exists(path)) File.Copy(path, path + ".backup", true);
        File.Move(pending, path, true);
        SaveCount++;
    }
}
public static class Json
{
    public static string Text(this JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    public static JsonElement? Property(this JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) ? p : null;
}
