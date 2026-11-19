using System.Text.Json;
namespace Hermes.Core;
internal static class ResponseInterruption
{
    internal static void Terminal(UsageEntry entry,JsonElement response)
    {
        if(response.Text("status")!="incomplete")return;
        entry.IncompleteReason=ExecutionMemory.Clean(response.Property("incomplete_details")?.Text("reason")??"",200);
        entry.FailureKind=entry.IncompleteReason=="max_output_tokens"?"response_output_limit":"response_incomplete";
        entry.Note=Message(entry.FailureKind);
    }
    internal static string Message(string kind)=>kind switch
    {
        "response_output_limit"=>"[MODEL_OUTPUT_LIMIT] 모델 출력 한도(max_output_tokens)로 응답이 미완료입니다. 받은 텍스트와 실행 근거를 보존했습니다. 한도·문맥을 확인한 뒤 명시적으로 남은 작업만 재개하세요.",
        "response_upstream_transport"=>"[MODEL_CONNECTION] 모델 서버와의 연결이 끊겼습니다. 저장된 작업 지점에서 재개할 수 있습니다.",
        "response_upstream_unavailable"=>"[MODEL_UNAVAILABLE] 모델 서버가 재시도 후에도 일시적으로 요청을 처리하지 못했습니다. 저장된 작업 지점에서 재개할 수 있습니다.",
        "response_early_eof"=>"[RESPONSE_EARLY_EOF] 종료 이벤트 전에 응답 스트림이 끊겼습니다. 받은 텍스트와 실행 근거를 보존했으며 완료로 처리하거나 자동 재실행하지 않습니다.",
        "response_timeout"=>"[RESPONSE_TIMEOUT] 사용자 취소가 아닌 응답 대기 시간 초과가 관측됐습니다. 부분 결과를 보존했습니다. 서버·연결 상태를 확인한 뒤 명시적으로 남은 작업만 재개하세요.",
        _=>"[RESPONSE_INCOMPLETE] 공급자가 응답 미완료를 알렸습니다. 부분 결과를 보존했습니다. 미완료 원인을 확인한 뒤 명시적으로 재개하세요."
    };
}
public sealed partial class DeskController
{
    private void ObserveResponseInterruption(WorkItem task,UsageEntry usage)
    {
        if(!usage.FailureKind.StartsWith("response_",StringComparison.Ordinal))return;
        // Late events from an older turn cannot pause a newer request.
        if(usage.TurnId.Length==0||usage.TurnId!=task.TurnId)return;
        var id="response-interruption-"+usage.Id;
        if(!task.Transcript.Any(e=>e.ItemId==id))
            task.Transcript.Add(TranscriptPresentation.Host(usage.FailureKind=="response_output_limit"?"중간보고":"에러원인",usage.Note,usage.TurnId) with{ItemId=id});
        if(ActiveProcess(task) is not {} process||process.UserStopped||process.BlockKind.Length>0&&!process.BlockKind.StartsWith("response_",StringComparison.Ordinal))return;
        process.BlockKind=usage.FailureKind;process.Detail=usage.Note;process.Checkpoint=WorkQueue.Summary(process);
    }
}
