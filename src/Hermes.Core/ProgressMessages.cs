namespace Hermes.Core;

// Only public provider summaries are rendered; raw reasoning content is never consumed.
public static class ProgressMessages
{
    public static bool IsSummary(TranscriptEntry entry) => entry.Kind is "진행 요약" or "호스트 보고" || entry.ItemId?.StartsWith("progress:", StringComparison.Ordinal) == true;
    // Attention and answer entries stay visible regardless of tool-card preference.
    public static bool IsHiddenWhenProgressOff(TranscriptEntry entry)
    {
        if (entry.Kind is "사용자" or "에이전트" or "에이전트 스트림" || entry.Kind.Contains("오류", StringComparison.Ordinal)
            || entry.Kind.StartsWith("승인", StringComparison.Ordinal) || entry.Stage is "에러원인" or "입력 필요" || HasLegacyToolFailure(entry)) return false;
        return IsSummary(entry) || entry.Origin.StartsWith("도구:", StringComparison.Ordinal)
            || entry.ItemId?.StartsWith("tool-progress:", StringComparison.Ordinal) == true
            || entry.Kind is "작업 진행" or "명령 결과" or "명령 출력" or "MCP 도구" or "도구 결과";
    }
    private static bool HasLegacyToolFailure(TranscriptEntry entry)
    {
        if (entry.Kind == "명령 결과" && entry.Stage.Length == 0)
        {
            var match = System.Text.RegularExpressions.Regex.Match(entry.Text, @"(?:^|\n)exit:\s*(-?\d+)\s*$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
            if (match.Success && int.TryParse(match.Groups[1].Value, out var exit) && exit != 0) return true;
        }
        if (entry.Kind != "MCP 도구") return false;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(entry.Text);
            var item = document.RootElement;
            if (item.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            return item.Text("status") == "failed" || item.Property("error") is { ValueKind: not (System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined) }
                || item.Property("isError") is { ValueKind: System.Text.Json.JsonValueKind.True }
                || item.Property("result")?.Property("isError") is { ValueKind: System.Text.Json.JsonValueKind.True };
        }
        catch (System.Text.Json.JsonException) { return false; }
    }
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
