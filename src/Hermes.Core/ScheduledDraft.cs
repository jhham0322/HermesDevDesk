namespace Hermes.Core;
public sealed class ScheduledDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Prompt { get; set; } = "";
    public DateTimeOffset DueAtUtc { get; set; }
    public bool Enabled { get; set; } = true;
    public string? CreatedTaskId { get; set; }
    public override string ToString() => $"{Title} · {DueAtUtc:yyyy-MM-dd HH:mm} UTC · {(CreatedTaskId != null ? "초안 생성됨" : Enabled ? "예약" : "취소됨")}";
}
