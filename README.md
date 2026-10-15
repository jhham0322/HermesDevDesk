# Hermes DevDesk 0.8.0

[Windows Setup 및 Portable 다운로드](https://github.com/jhham0322/HermesDevDesk/releases/tag/v0.8.0)

공식 Codex CLI를 독립 Windows UI에서 실행하며 회사 API는 로컬 프로토콜 변환기로 연결합니다.

## 회사 API 설정

설정 화면에서 예시를 선택하고 ‘선택 예시 채우기’를 누른 뒤 실제 회사 값으로 바꾸세요. 예시 채우기는 저장하거나 서버에 접속하지 않습니다.

| 서버 | 프로토콜 | URL 예시 | 인증 | 모델 |
|---|---|---|---|---|
| Azure OpenAI v1 | AzureV1Responses | https://YOUR-RESOURCE.openai.azure.com/openai/v1 | ApiKeyHeader | 실제 deployment 이름 |
| Azure v1 Chat | AzureV1ChatCompletions | 위와 동일 | ApiKeyHeader | 실제 deployment 이름 |
| Azure 구버전 Chat | AzureLegacyChatCompletions | https://YOUR-RESOURCE.openai.azure.com | ApiKeyHeader + 회사 api-version | 실제 deployment 이름 |
| Claude Code 호환 게이트웨이 | AnthropicMessages | ANTHROPIC_BASE_URL 값 | BearerKey | ANTHROPIC_DEFAULT_OPUS_MODEL 등의 실제 값 |

Anthropic AUTH_TOKEN은 API 키 입력란에 직접 넣습니다. ANTHROPIC_API_KEY 방식이면 ApiKeyHeader를 선택하며 x-api-key로 전달합니다. 환경변수 이름이 아니라 변수에 지정된 값을 입력하세요. api-version은 비우고 최대 출력 토큰을 모델 한도 이내로 설정합니다. /v1/messages를 자동 구성하며 이미 /v1 또는 /v1/messages가 있으면 중복하지 않습니다.

Anthropic 연결 검사는 선택 모델에 최대 출력 1토큰을 요청합니다(입력 토큰 등 사용량 발생 가능). Azure/Gateway 검사는 GET models를 조회합니다. 목록 API의 404는 실제 추론 실패를 확정하지 않습니다. 모델명은 실패/재시작 후에도 유지되고 연결 상태는 따로 표시합니다. 서버가 반환한 모델 목록을 선택할 수 있습니다. Azure 일반 모델 ID와 실제 deployment 이름이 다를 수 있으므로 회사가 제공한 deployment를 우선 사용하세요.

엔진 연결 버튼에서도 회사 API 검사와 모델 목록 조회를 수행합니다. HTTP/HTTPS를 지원하며 HTTP는 암호화되지 않습니다. 외부 fallback, 인증서 검증 해제, 자동 리다이렉트는 없습니다.

## 지원 범위

Anthropic Messages의 텍스트, system/developer 지시, 함수/이름공간/custom 도구와 결과, 이미지 및 컴퓨터 도구 이미지 결과, 실시간 SSE를 Responses 형식으로 변환합니다. 서버 usage와 중단/미완료 상태를 기록합니다. provider 고유 encrypted reasoning, hosted/server tools, PDF/파일 블록은 변환하지 않습니다.

컴퓨터 제어는 기존 실험 기능입니다. 실제 회사 URL·인증키·모델로 검증하지 않았으며 로컬 합성 서버와 공식 CLI로 변환 경로를 확인했습니다. [검증](VALIDATION.md) 참조.

## 실행 및 빌드

Setup.exe로 설치하거나 Portable.zip을 풀고 HermesDevDesk.exe를 실행하세요. .NET 런타임과 공식 CLI가 포함됩니다. WebView2 Runtime은 브라우저 패널에 필요합니다. 이 버전 설정은 0.8.0 데이터 폴더에 저장하며 이전 버전 데이터는 유지합니다. 회사 키는 Windows 사용자 DPAPI로 보호됩니다.

소스 빌드: .NET 10 SDK에서 scripts/fetch-engine.ps1 실행 후 scripts/build.ps1 -DotnetPath <dotnet.exe>. 사용자 설정/키/브라우저 프로필/화면 캡처를 패키지에 포함하지 않습니다.
