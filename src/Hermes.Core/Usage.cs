using System.Text;
using System.Text.Json;

namespace Hermes.Core;
public sealed record TokenCounts(long? Input, long? Output, long? Cached, long? Reasoning, long? Total)
{
    public static TokenCounts Missing => new(null, null, null, null, null);
    private static long? N(JsonElement e, string name) => e.Property(name) is { ValueKind: JsonValueKind.Number } n && n.TryGetInt64(out var value) && value is >= 0 and <= 1_000_000_000_000 ? value : null;
    public static TokenCounts FromResponse(JsonElement response)
    {
        var u = response.Property("usage"); if (u is not { ValueKind: JsonValueKind.Object }) return Missing;
        return new(N(u.Value, "input_tokens") ?? N(u.Value, "prompt_tokens"), N(u.Value, "output_tokens") ?? N(u.Value, "completion_tokens"),
            u.Value.Property("input_tokens_details") is { } input ? N(input, "cached_tokens") : u.Value.Property("prompt_tokens_details") is { } prompt ? N(prompt, "cached_tokens") : null,
            u.Value.Property("output_tokens_details") is { } output ? N(output, "reasoning_tokens") : u.Value.Property("completion_tokens_details") is { } completion ? N(completion, "reasoning_tokens") : null,
            N(u.Value, "total_tokens"));
    }
    public static TokenCounts FromAppServer(JsonElement usage) => new(N(usage, "inputTokens"), N(usage, "outputTokens"), N(usage, "cachedInputTokens"), N(usage, "reasoningOutputTokens"), N(usage, "totalTokens"));
}
public sealed class UsageEntry
{
    public string Id { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string TurnId { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string Model { get; set; } = "";
    public string Deployment { get; set; } = "";
    public string Source { get; set; } = "";
    public string Status { get; set; } = "started";
    public int? HttpStatus { get; set; }
    public int? Requests { get; set; }
    public TokenCounts Tokens { get; set; } = TokenCounts.Missing;
    public string Note { get; set; } = "";
}
public static class UsageLedger
{
    public static void Upsert(AppState state, UsageEntry entry)
    {
        entry = System.Text.Json.JsonSerializer.Deserialize<UsageEntry>(System.Text.Json.JsonSerializer.Serialize(entry))!;
        var index = state.Usage.FindIndex(e => e.Id == entry.Id);
        if (index < 0) state.Usage.Add(entry); else state.Usage[index] = entry;
    }
    public static void RecordThreadUpdate(AppState state, WorkItem task, JsonElement parameters)
    {
        var usage = parameters.Property("tokenUsage")?.Property("total"); if (!usage.HasValue) return;
        var current = TokenCounts.FromAppServer(usage.Value); var thread = parameters.Text("threadId");
        state.ThreadUsageSnapshots.TryGetValue(thread, out var previous); if (previous == current) return;
        long? Delta(long? now, long? before) => now.HasValue && before.HasValue && now >= before ? now - before : null;
        var knownStart = previous == null && task.UsageBaselineKnown;
        var delta = knownStart ? current : previous == null ? TokenCounts.Missing : new(Delta(current.Input, previous.Input), Delta(current.Output, previous.Output), Delta(current.Cached, previous.Cached), Delta(current.Reasoning, previous.Reasoning), Delta(current.Total, previous.Total));
        state.ThreadUsageSnapshots[thread] = current;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(current))));
        Upsert(state, new() { Id = "thread-" + thread + "-" + parameters.Text("turnId") + "-" + hash, TaskId = task.Id, TurnId = parameters.Text("turnId"), At = DateTimeOffset.UtcNow, Model = task.Model, Deployment = task.Model, Source = "AppServer aggregate delta", Status = "completed", Requests = null, Tokens = delta, Note = "Thread 누적 값의 관측 증가량; 실제 개별 요청 수/과거 날짜는 제공되지 않음. 감소/reset/초기 기존 thread 값은 추정하지 않음." });
    }
    public static List<UsageEntry> Select(AppState state, DateTimeOffset start, DateTimeOffset endExclusive, string? taskId = null, string? model = null) => state.Usage.Where(e => e.At >= start && e.At < endExclusive && (taskId == null || e.TaskId == taskId) && (model == null || e.Model == model || e.Deployment == model)).OrderBy(e => e.At).ToList();
    public static string Summary(IEnumerable<UsageEntry> entries)
    {
        var list = entries.ToList(); if (list.Count == 0) return "해당 기간에 앱이 관측한 요청 없음";
        static string Count(IEnumerable<long?> values) { var v = values.ToList(); var missing = v.Count(x => !x.HasValue); return (v.Any(x => x.HasValue) ? v.Sum(x => x ?? 0).ToString("N0") : "미제공") + (missing > 0 ? $" (미제공 {missing}건)" : ""); }
        var result = new StringBuilder("앱이 관측한 사용량 · UTC 날짜 · 회사 전체 사용량 아님\n공개 가격/청구액/관리자 조회 기능 없음\nreasoning은 output의 부분집합, cache는 input의 부분집합으로 표시하며 합산하지 않습니다.\n\n");
        foreach (var group in list.GroupBy(e => new { Day = e.At.UtcDateTime.Date, e.TaskId, e.Deployment, e.Model }))
        {
            var rows = group.ToList(); result.AppendLine($"{group.Key.Day:yyyy-MM-dd} · 작업 {group.Key.TaskId} · deployment {group.Key.Deployment} · model {group.Key.Model}");
            result.AppendLine($"HTTP 시도/CLI 미제공 {Count(rows.Select(e => e.Requests.HasValue ? (long?)e.Requests.Value : null))} / 실패·중단 {rows.Count(e => e.Status is "failed" or "cancelled" or "incomplete")} / 미확인 {rows.Count(e => e.Status is "started" or "unknown-after-restart" or "unknown")}\n입력 {Count(rows.Select(e => e.Tokens.Input))} / 출력 {Count(rows.Select(e => e.Tokens.Output))}\n입력 중 캐시 {Count(rows.Select(e => e.Tokens.Cached))} / 출력 중 reasoning {Count(rows.Select(e => e.Tokens.Reasoning))}\n서버 total {Count(rows.Select(e => e.Tokens.Total))}\n");
        }
        result.AppendLine("연결 검사·재시도·실패도 관측 요청에 포함. 사용량 없는 실패를 0 토큰으로 만들지 않습니다. 로컬 명령/외부 도구 자체 사용량은 이 응답에 제공되지 않으면 확인 불가입니다.");
        return result.ToString();
    }
}
