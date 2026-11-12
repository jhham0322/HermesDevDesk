# ChatGPT plan OAuth — local review

This feature is prepared for the 0.8.43 OAuth preview release. Actual account login and model inference remain unverified. Historical local-review results below retain their original scope; publication is not evidence of real-account E2E success.

## Entry point

Open **File → ChatGPT 계정 (OAuth)**. Choose **Continue with ChatGPT** for a new account/workspace, complete the official browser authorization, refresh the account's server model list, select a model and connect. Existing CLI path and a registered project are required. The existing company **모델 추가** settings remain the API-key path; OAuth does not replace them.

OAuth replaces the API-key credential, not the inference transport: inference uses the public Responses API at `https://api.openai.com/v1`. Account policy, model availability, regional eligibility, app-specific plan allowance and subscription limits still apply. There is no automatic billing fallback, credit purchase or provider switch. ChatGPT conversations and personal memory are not imported.

## Implemented boundaries

- Dynamic public-client registration uses `dynamic_agent_client`; subsequent authorization reuses the issued client and validated account identity. A stable opaque host ID is retained in the app profile.
- Loopback callback uses `127.0.0.1` and `/auth/callback`, fresh state/nonce, PKCE S256 and the identical redirect URI during exchange. Duplicate parameters, mismatched state/client/identity, denied consent and incomplete registration are rejected.
- RS256 ID tokens are verified against the issuer-discovered HTTPS JWKS, including issuer, audience, authorized party when required, expiration, nonce and subject. Unsupported algorithms fail closed.
- Each `(issued client, verified subject)` has an independent registration. Tokens are atomically stored under current-user Windows DPAPI, outside source and company-key export. Refresh is serialized across processes and rotating tokens are saved together. Sign-out attempts refresh-token revocation and clearly reports unconfirmed remote revocation while clearing local tokens. Account/client mapping and host ID remain for re-login.
- Model names come from the selected account's live server catalog. A catalog or connected engine is not proof of successful inference. Only a completed turn is successful; failed/interrupted turns remain separate under the existing execution state machine.
- App-server receives the token through its own environment, never command-line arguments. The provider uses Responses HTTP with WebSockets disabled, no OpenAI-auth fallback and no inference retries. Shell-child credential exclusions remain enforced.
- OAuth profiles and thread-provider fingerprints are account specific. Existing company fingerprints are unchanged. Reconnection preserves checkpoints; it does not automatically replay saved work. Near-expiry sessions block new rounds and require stopping active work and reconnecting to refresh/resume safely.
- Existing company settings, registration/import/export implementation and secrets are not converted. OAuth never adds a row to the company-model registration list. Existing company model selection is available through its unchanged settings path.

## Validation and limits

Tests use synthetic tokens, a generated RSA key, mocked official HTTPS responses, a real local callback listener and a fake app-server child. They do not sign into a real account or perform model inference. WPF tests use programmatic events on the real window, not native mouse automation.

The ordinary company settings smoke passed 22 checks. The broader `--provider-settings-smoke` fails the assertion `404 keeps configured model and separately displays failure` both on this tree and on the preserved pre-OAuth 0.8.42 app (source commit `34fb1a51b80bd03a3b9128bd3aba4fca72e56239`). It is recorded as a pre-existing failing check, not fixed under this OAuth request.

Actual dynamic registration, current-account eligibility, live account switching, real model catalog, official-engine end-to-end request bodies, streaming completion/usage-limit errors and renewal during real use remain unverified until the user explicitly completes browser login and consents to plan usage. No authenticated state or entitlement is claimed by the mock tests. Real model requests may consume plan allowance and require a separate explicit test action.

Unsupported hosted capabilities include image generation, file search, Code Interpreter, native Responses computer use, hosted MCP/connectors, `tool_search`, audio/video and Files upload API. Local custom tools remain subject to existing user authorization. A server rejection is surfaced; it is not routed to a different API or billing path.

## Official references

- [Overview](https://developers.openai.com/siwc/token-sharing-open-source)
- [Registration and sign-in](https://developers.openai.com/siwc/token-sharing-open-source/sign-in)
- [Accounts, refresh and sign-out](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions)
- [Models and inference](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference)
- [Codex app-server](https://developers.openai.com/siwc/token-sharing-open-source/codex-app-server)
- [Preview limits](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations)
- [Errors and recovery](https://developers.openai.com/siwc/token-sharing-open-source/errors-and-recovery)

## 인증 전 실행 안내와 최소 실제 E2E 절차

### 설치하지 않고 새 프로필에서 실행

이 개발 빌드는 기존 앱의 `--data-dir` 옵션을 지원합니다 (`App.xaml.cs`의 시작 인수 처리). 이는 HermesDevDesk의 기존 옵션이며 OpenAI CLI 인수가 아닙니다. 아래 명령은 아직 실행하지 않았습니다. 기존 앱 프로필이나 회사 설정을 복사하지 않습니다.

개발 EXE:
`E:\work2026\hermes\HermesDevDesk\versions\0.8.11\src\Hermes.Desktop\bin\Release\net10.0-windows10.0.17763.0\HermesDevDesk.exe`

프레임워크 의존 개발 빌드이므로 EXE 단독 배포용 파일이 아닙니다. 같은 빌드 폴더의 DLL/리소스와 기존 로컬 .NET 런타임이 필요합니다. 설치 없이 실행하는 PowerShell 예시는 다음과 같습니다. `DOTNET_ROOT`는 자식 프로세스에만 상속하고 현재 셸의 원래 값은 복원합니다. 시스템 환경 변수는 변경하지 않습니다.

```powershell
$trial = Join-Path $env:TEMP ('HermesOAuthE2E-' + [guid]::NewGuid().ToString('N'))
$profile = Join-Path $trial 'profile'
$project = Join-Path $trial 'project'
New-Item -ItemType Directory -Path $project -Force | Out-Null
$previousDotnetRoot = $env:DOTNET_ROOT
try {
    $env:DOTNET_ROOT = 'E:\work2026\hermes\HermesDevDesk\.tools\dotnet10\sdk'
    Start-Process -FilePath 'E:\work2026\hermes\HermesDevDesk\versions\0.8.11\src\Hermes.Desktop\bin\Release\net10.0-windows10.0.17763.0\HermesDevDesk.exe' -ArgumentList @('--data-dir', ('"' + $profile + '"'))
} finally {
    $env:DOTNET_ROOT = $previousDotnetRoot
}
Write-Output "시험 프로필: $profile"
Write-Output "빈 시험 프로젝트: $project"
```

이 프로필에는 기존 회사 모델 목록이 없습니다. 기존 회사 설정/키 파일을 복사하거나 가져오지 마세요. `$project`만 앱에 등록하고, **파일 → CLI 선택**에서 다음 보존된 공식 엔진을 선택합니다.

`E:\work2026\hermes\HermesDevDesk\versions\0.8.11\vendor\codex-0.162.1\vendor\x86_64-pc-windows-msvc\bin\codex.exe`

### 사용자가 직접 해야 하는 인증

1. **파일 → ChatGPT 계정 (OAuth) → Continue with ChatGPT**를 누릅니다.
2. 시스템 브라우저의 `auth.openai.com`에서 본인 계정과 워크스페이스를 확인하고 등록 앱 이름을 확인합니다. 비밀번호나 브라우저 인증 코드를 담당자에게 전달하지 않습니다.
3. 표시되는 신원·ChatGPT 플랜 사용 권한을 읽고 원할 때만 동의합니다. 플랜 사용을 거부해도 이를 우회하거나 API 키로 자동 전환하지 않습니다.
4. 앱으로 돌아와 등록된 계정을 선택하고 **선택 계정 모델 목록 새로 확인**을 누릅니다. 목록에 있는 모델을 선택해 연결합니다. 이 단계까지는 모델 추론 요청을 보내지 않습니다.
5. 여기서 멈추고 인증 성공/계정 모델 목록 확인 여부만 알려 주세요. 로그인 URL·토큰·암호화 자격 증명 파일은 전달하지 않습니다. 첫 추론 시험 1회의 플랜 사용량 소비에 동의한 다음 아래 시험을 진행합니다.

저장 정보: 검증된 계정 subject·email, 발급 client ID, access/refresh/ID 토큰, 동의한 scope와 만료 정보는 이 시험 프로필의 `chatgpt-plan/accounts.dpapi`에 현재 Windows 사용자 DPAPI로 암호화 저장됩니다. 불투명 host ID, 선택한 계정/모델 설정, 계정별 로컬 엔진 대화·작업 기록도 이 프로필 아래에 남습니다. 계정·등록(client)별로 구분하며 기존 회사 키 저장소와 섞지 않습니다.

요청 scope는 `openid profile email offline_access resource.invoke chatgpt.tokens.use.direct`입니다. 신원 확인, 재로그인 없이 갱신 가능한 세션, 동의한 ChatGPT 플랜의 지원되는 추론 사용을 요청합니다. ChatGPT 대화·개인 기억을 읽는 권한은 아닙니다. 이 OAuth 동의가 로컬 파일/도구의 별도 사용자 승인을 대신하지도 않습니다. 구독 변경·추가 크레딧 구매는 하지 않습니다. 앱별 플랜 한도/해제는 ChatGPT 설정 → Usage에서 사용자가 관리합니다.

### 인증 후 첫 시험: 실제 추론 1회만

- 빈 시험 프로젝트에서 새 대화를 만들고 **요청 완료까지 작업 계속 진행**은 끕니다. 회사 자료·파일을 첨부하지 않습니다. 브라우저/컴퓨터/MCP 작업은 요청하지 않습니다.
- 프롬프트: `도구를 사용하거나 파일을 읽고 쓰지 말고, 정확히 OAUTH_OK 라고만 답하세요.`
- 전송은 한 번만 누릅니다. 앱의 선택 계정·서버 모델 slug, 응답 문자열과 최종 상태를 확인합니다. 모델 표시명/목록 조회/텍스트 일부 수신만으로 성공이라고 기록하지 않습니다.
- 정상 판정: 대응하는 app-server `turn/completed`의 `turn.status=completed`를 처리한 앱 상태가 **완료**이고 기대 문자열을 받았습니다. 직접 Responses 연결의 대응 종료 신호는 `response.completed`입니다. 현재 앱은 app-server 경로를 사용하므로 직접 SSE 이벤트를 별도로 수집했다고 주장하지 않습니다. 실패/중단/완료 신호 없는 종료는 성공이 아닙니다.
- 이 1회는 플랜 사용량을 소비할 수 있습니다. 오류가 나면 상태·안전한 오류 코드/요청 ID만 기록하고 자동 재전송하지 않습니다. 한도·자격·지역·권한 오류를 우회하지 않습니다.

### 나머지 검증은 첫 시험과 분리

| 항목 | 언제/최소 범위 |
| --- | --- |
| 실제 다른 계정/워크스페이스 | 두 번째 계정의 별도 로그인·동의가 있을 때만 모델 목록과 계정 라벨 분리를 확인합니다. 새 계정 사용 승인이 없으면 미실행으로 남깁니다. |
| 로그인 취소·재진입 | 추론 없이 공식 로그인 도중 앱의 취소를 누르고 원래 계정이 바뀌지 않는지 확인할 수 있습니다. |
| 스트림 중 사용자 Stop | 완료 시험과 동시에 증명할 수 없습니다. 별도 승인된 두 번째 요청 및 추가 사용량이 필요합니다. 첫 요청 1회 범위에는 포함하지 않습니다. |
| 실제 토큰 만료·갱신·재연결 | 실제 인증 후 정상 만료 경계까지 기다리거나 자연 만료가 발생했을 때 중지/체크포인트 보존·재연결을 확인합니다. PC 시간·토큰·저장 파일을 조작하지 않습니다. 갱신만으로 추론을 다시 보내지 않습니다. |
| 로그아웃·원격 해제 | 사용자가 선택한 시험 계정만 로그아웃합니다. 원격 해제 미확인 메시지가 나오면 이를 성공으로 바꾸지 않고 ChatGPT 설정의 해제 경로를 안내합니다. |
| 회사 모델로 돌아가기 | 이 새 프로필의 시험 앱을 종료한 뒤 기존 설치 앱을 원래 방식으로 열어 기존 목록 표시만 확인합니다. 실제 회사 추론 요청·설정 변경은 하지 않습니다. 같은 프로필 내 회사 모델 재선택은 이미 합성 회귀로 확인했으며, 실제 회사 키를 시험 프로필로 옮겨 추가 검증하지 않습니다. |

현재 위 실제 인증/E2E 절차는 모두 미실행입니다. 기존 404 공급자 UI 시험 실패는 여전히 알려진 기존 실패로 남아 있으며 OAuth 성공으로 숨기지 않습니다.

## 2026-11-12 authentication-preflight follow-up

Added an OAuth-only cancellation check immediately before launching the system browser. Preserved the previous three source files in artifacts/oauth-preauth-before-4b3093777bc647f1b0feec4ba6c959a6. A deterministic boundary test confirms a cancelled attempt does not open a browser; programmatic WPF tests additionally cover browser-launch failure, retry guard recovery and cancellation after retry. Build: zero warnings/errors. Full synthetic regression: 549 checks passed. OAuth WPF: 8 checks passed; no native mouse or real browser login. No company-model production code or existing 404 test was changed. The known 404 display assertion remains an unresolved baseline failure; this preflight improvement does not resolve it. Actual runtime model identity is not exposed by the available tools. Actual OAuth login, permission consent, model requests, installation and publication remain unperformed.
