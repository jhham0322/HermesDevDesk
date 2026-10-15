# Hermes DevDesk 0.8.0 — Windows x64

- AnthropicMessages 연결: 텍스트, 지시, 함수/custom 도구, 이미지 및 컴퓨터 도구 결과, 실시간 SSE를 Codex Responses 형식으로 변환합니다.
- Azure v1/구버전 및 Anthropic 연결 예시를 설정 화면에 추가했습니다. 예시 채우기는 저장/접속하지 않습니다.
- 실패해도 설정 모델은 표시하고 연결 상태를 분리했습니다. 서버가 반환한 모델 목록에서 선택한 모델을 실제 요청에 사용합니다.
- Anthropic 연결 검사는 최대 출력 1토큰 생성 요청으로 사용량이 발생할 수 있습니다. Azure/Gateway는 GET models 검사이며 404만으로 추론 실패를 확정하지 않습니다. 엔진 연결 버튼에서도 검사를 수행합니다.
- Anthropic AUTH_TOKEN 방식은 BearerKey, API key 방식은 ApiKeyHeader(x-api-key)를 사용합니다. 모델에는 환경변수 이름 대신 실제 값을 넣으세요.
- 고유 encrypted reasoning, hosted/server tools, PDF/파일 블록은 지원하지 않습니다. 컴퓨터 제어는 실험 기능입니다. 실제 회사 서버/키/모델로 검증하지 않았습니다.
- Windows Setup/Portable은 .NET 10.0.12 및 공식 CLI 0.162.1을 포함합니다. 이전 버전과 데이터는 유지합니다.
