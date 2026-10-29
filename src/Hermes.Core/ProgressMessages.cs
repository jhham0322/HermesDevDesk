namespace Hermes.Core;

// Only public provider summaries are rendered; raw reasoning content is never consumed.
public static class ProgressMessages
{
    public static bool IsSummary(TranscriptEntry entry) => entry.Kind is "진행 요약" or "호스트 보고" || entry.ItemId?.StartsWith("progress:", StringComparison.Ordinal) == true;
    public static void Delta(WorkItem task, string itemId, string part, string delta)
    {
        var key = "progress:" + itemId + ":" + part;
        var index = task.Transcript.FindLastIndex(e => e.Kind == "진행 요약" && e.ItemId == key);
        var text = (index >= 0 ? task.Transcript[index].Text : "") + delta;
        Put(task, key, text, index);
    }
    public static void Complete(WorkItem task, string itemId, string part, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var key = "progress:" + itemId + ":" + part;
        Put(task, key, text, task.Transcript.FindLastIndex(e => e.Kind == "진행 요약" && e.ItemId == key));
    }
    private static void Put(WorkItem task, string key, string text, int index)
    {
        var entry = index >= 0 ? task.Transcript[index] with { Text = Safety.Redact(text[..Math.Min(200_000, text.Length)]) } : TranscriptPresentation.Model(task, new TranscriptEntry("진행 요약", Safety.Redact(text[..Math.Min(200_000, text.Length)]), DateTime.UtcNow, key), "분석");
        if (index >= 0) task.Transcript[index] = entry;
        else task.Transcript.Add(entry);
    }
}
