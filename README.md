# Hermes DevDesk 0.8.2

[Windows Setup 및 Portable 다운로드](https://github.com/jhham0322/HermesDevDesk/releases/tag/v0.8.2)

공식 Codex CLI를 독립 Windows UI에서 실행하며 회사 API는 로컬 프로토콜 변환기로 연결합니다.

## 회사 서버 연결

기본 화면에 서버 URL, API 키, 모델명 세 값만 입력하고 **저장하고 연결 확인**을 누르세요. 특별한 인증 헤더나 CLI 구독 연결이 필요한 경우에만 고급 설정을 사용합니다.

모델명은 회사가 준 문자열을 그대로 사용합니다. 입력 중에도 메인 모델 선택 상자에 표시하며, 저장 검증 실패·연결 오류·재실행 후에도 보존합니다. 저장 전 모델은 미저장 상태이며 연결 성공을 의미하지 않습니다. API 키는 이 PC의 보호 저장소에 저장하며 설정 창을 다시 열어도 키 내용을 표시하지 않습니다.

자동 연결은 입력한 서버와 경로 범위 안에서 Responses, Chat Completions, Anthropic Messages API를 확인합니다. HTTP/HTTPS, 포트, 기본 경로를 유지하며 /v1/chat/completions 같은 완전한 호출 URL도 지원합니다. API 키가 Bearer 또는 표준 API-key 헤더로 전달되는 호환 게이트웨이를 대상으로 합니다. 사내 고유 요청/인증 규격이면 회사의 규격에 맞춘 추가 지원이 필요합니다.

연결 확인은 최대 출력 1토큰의 짧은 생성 요청을 보내므로 사용량이 발생할 수 있습니다. 지원되지 않는 경로만 다음 경로로 확인하고, 명시적인 모델 오류·서버 오류·리다이렉트는 중단합니다. 다른 서버, 다른 모델, HTTPS에서 HTTP로 전환하지 않습니다. HTTP는 암호화되지 않습니다. 저장만으로는 네트워크에 요청하지 않습니다.

이전 앱의 프로필을 재사용하므로 업데이트 후에도 설정을 이어 사용합니다. 새 설치는 공통 data 프로필을 사용합니다. 기존 키를 복사/추출하지 않습니다. 새 대화가 없어도 전송하면 대화를 생성하고 필요한 엔진 연결을 자동 수행합니다.

오류에는 실제 키 저장, DNS, TCP, TLS, 응답 JSON 형식 또는 HTTP 요청 경로를 표시합니다. 연결 실패를 모델명 미설정이나 사용량 미제공으로 바꿔 표시하지 않습니다.

고급 설정에는 사내 중계서버 자동 연결·Responses·Chat Completions·Anthropic Messages 예시가 있습니다. 예시 채우기는 저장/접속하지 않습니다. 이전 직접 공급자 모드는 중계서버 자동 연결로 전환합니다.

## 지원 범위

Anthropic Messages의 텍스트, system/developer 지시, 함수/이름공간/custom 도구와 결과, 이미지 및 컴퓨터 도구 이미지 결과, 실시간 SSE를 Responses 형식으로 변환합니다. 서버 usage와 중단/미완료 상태를 기록합니다. provider 고유 encrypted reasoning, hosted/server tools, PDF/파일 블록은 변환하지 않습니다.

컴퓨터 제어는 기존 실험 기능입니다. 실제 회사 URL·인증키·모델로 검증하지 않았으며 로컬 합성 서버와 공식 CLI로 변환 경로를 확인했습니다. [검증](VALIDATION.md) 참조.

## 실행 및 빌드

Setup.exe로 설치하거나 Portable.zip을 풀고 HermesDevDesk.exe를 실행하세요. .NET 런타임과 공식 CLI가 포함됩니다. WebView2 Runtime은 브라우저 패널에 필요합니다. 이전 앱 프로필 또는 공통 data 폴더에 설정을 보존합니다. 회사 키는 Windows 사용자 DPAPI로 보호됩니다.

소스 빌드: .NET 10 SDK에서 scripts/fetch-engine.ps1 실행 후 scripts/build.ps1 -DotnetPath <dotnet.exe>. 사용자 설정/키/브라우저 프로필/화면 캡처를 패키지에 포함하지 않습니다.
