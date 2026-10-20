namespace Hermes.Core;

public static class ConnectionErrors
{
    public static string Describe(Exception exception, CancellationToken cancellation = default)
    {
        var text = exception switch
        {
            HttpRequestException http => http.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => "서버 이름을 찾지 못했습니다. URL의 호스트와 사내 DNS/VPN을 확인하세요.",
                HttpRequestError.SecureConnectionError => "HTTPS 인증서/TLS 연결에 실패했습니다. 회사 인증서 설정을 확인하세요. 인증서 검증을 끄지는 않습니다.",
                HttpRequestError.ConnectionError => "서버와 TCP 연결하지 못했습니다. 주소·포트·사내망/VPN과 서버 실행 상태를 확인하세요.",
                _ => "HTTP 통신 실패: " + http.HttpRequestError + ". 서버 주소·포트와 네트워크 상태를 확인하세요."
            },
            OperationCanceledException => cancellation.IsCancellationRequested ? "연결 확인을 취소했습니다." : "서버 응답 시간이 초과됐습니다.",
            System.Text.Json.JsonException => "서버에 도달했지만 예상한 JSON 응답이 아닙니다. API 경로가 로그인/웹 페이지인지 확인하세요.",
            IOException io => io.Message,
            KeyNotFoundException or FormatException or NotSupportedException or ArgumentException or InvalidOperationException => "서버 응답 형식이 선택한 API 방식과 다릅니다. 자동 연결 또는 회사의 API 경로를 확인하세요.",
            _ => "연결 처리 실패: " + exception.GetType().Name
        };
        return Safety.Redact(text);
    }
}
