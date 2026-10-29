namespace Hermes.Core;

public static class ChatMessages
{
    public static void Delta(WorkItem task, string itemId, string delta)
    {
        var index = task.Transcript.FindLastIndex(e => e.Kind == "에이전트 스트림" && e.ItemId == itemId);
        if (index >= 0)
        {
            var entry = task.Transcript[index];
            task.Transcript[index] = entry with { Text = Safety.Redact((entry.Text + delta)[..Math.Min(200_000, entry.Text.Length + delta.Length)]) };
        }
        else task.Transcript.Add(TranscriptPresentation.Model(task, new("에이전트 스트림", Safety.Redact(delta), DateTime.UtcNow, itemId), "중간보고"));
    }
    public static void Complete(WorkItem task, string itemId, string text)
    {
        var index = task.Transcript.FindLastIndex(e => e.ItemId == itemId && e.Kind is "에이전트 스트림" or "에이전트");
        var entry = index >= 0 ? task.Transcript[index] with { Kind = "에이전트", Text = Safety.Redact(text) } : TranscriptPresentation.Model(task, new TranscriptEntry("에이전트", Safety.Redact(text), DateTime.UtcNow, itemId), "중간보고");
        if (index >= 0) task.Transcript[index] = entry;
        else task.Transcript.Add(entry);
    }
    public static void Rename(AppState state, WorkItem task, string title)
    {
        if (!state.Tasks.Contains(task)) throw new HermesException("등록한 대화가 아닙니다.");
        title = title.Trim(); if (title.Length is 0 or > 200) throw new HermesException("대화 이름은 1~200자입니다.");
        task.Title = Safety.Redact(title);
    }
}
