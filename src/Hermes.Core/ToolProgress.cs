using System.Text.Json;

namespace Hermes.Core;

internal static class ToolProgress
{
    private static string Label(JsonElement item) => item.Text("tool") switch
    {
        "hermes_memory" => "작업 기록 검색·관리",
        "hermes_command" => "명령 실행",
        "hermes_browser" => "브라우저 작업",
        "hermes_computer" => "컴퓨터 작업",
        _ => "도구 실행 · " + item.Text("tool")
    };
    internal static void Start(WorkItem task, JsonElement item) => Put(task, item, "작업 진행", Label(item) + " 중…");
    internal static void Complete(WorkItem task, JsonElement item)
    {
        var failed = item.Property("success") is { ValueKind: JsonValueKind.False } || item.Text("status") == "failed";
        var detail = "";
        if (failed && item.Property("contentItems") is { ValueKind: JsonValueKind.Array } contents)
            detail = string.Join("\n", contents.EnumerateArray().Select(c => c.Text("text")).Where(t => t.Length > 0));
        Put(task, item, failed ? "도구 오류" : "작업 진행", Label(item) + (failed ? " 실패" + (detail.Length > 0 ? "\n" + detail : " · 상세 결과를 확인하지 못했습니다.") : " 완료"));
    }
    private static void Put(WorkItem task, JsonElement item, string kind, string text)
    {
        var key = "tool-progress:" + item.Text("id");
        var index = task.Transcript.FindLastIndex(t => t.ItemId == key);
        var entry = new TranscriptEntry(kind, Safety.Redact(text), DateTime.UtcNow, key);
        if (index >= 0) task.Transcript[index] = entry;
        else task.Transcript.Add(entry);
    }
}
