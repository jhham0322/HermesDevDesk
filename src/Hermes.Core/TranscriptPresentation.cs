namespace Hermes.Core;
public static class TranscriptPresentation
{
    public static TranscriptEntry Model(WorkItem task, TranscriptEntry entry, string stage) => entry with { Origin = "model", RequestedModel = task.Model, ResponseModel = task.ResponseModel, RequestId = task.ActiveRequestId, TurnId = task.TurnId ?? "", Stage = stage };
    public static TranscriptEntry Host(string stage, string text, string? turn = null) => new("호스트 보고", text, DateTime.UtcNow) { Origin = "Hermes", Stage = stage, TurnId = turn ?? "" };
    public static string Label(TranscriptEntry entry)
    {
        if (entry.Kind == "사용자") return "";
        var source = entry.Origin;
        if (source == "model" || entry.Kind is "에이전트" or "에이전트 스트림" or "진행 요약") source = Name(entry.ResponseModel, "미확인");
        else if (source.Length == 0) source = entry.Kind switch { "명령 결과" or "실행 결과" => "hermes_command", "MCP 결과" => "MCP", "브라우저 결과" => "hermes_browser", "컴퓨터 결과" => "hermes_computer", _ => "Hermes" };
        var stage = entry.Stage.Length > 0 ? entry.Stage : entry.Kind switch { "오류" => "에러원인", "진행 요약" => "분석", "계획" => "분석", _ => "중간보고" };
        var requested = entry.Origin == "model" && entry.RequestedModel.Length > 0 && entry.RequestedModel != entry.ResponseModel ? " · 요청 " + Name(entry.RequestedModel, "미확인") : "";
        return "[" + Name(source, "미확인") + "][" + stage + "]" + requested + (entry.Kind is "오류" or "도구 오류" ? " · 관측 오류 / 원인 미확정" : "");
    }
    public static string Name(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : Safety.Redact(new string(value.Where(c => !char.IsControl(c) && c is not ('[' or ']')).Take(160).ToArray()));
}
