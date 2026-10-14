# Hermes DevDesk 0.5.1

## 0.5.1 소스 보관 버전

0.5.0 소스를 기반으로 버전 정보를 0.5.1로 올린 소스 보관 버전입니다. 앱 기능 변경은 없습니다. 개인 계정의 비공개 저장소에 보관하며 빌드 결과물, 로컬 도구, 런타임, 공식 CLI 바이너리, 계정 인증정보는 포함하지 않습니다.

- 저장소: https://github.com/jhham0322/HermesDevDesk
- 이 버전 소스 ZIP: https://github.com/jhham0322/HermesDevDesk/archive/refs/tags/v0.5.1.zip
- 다운로드하려면 저장소 소유자의 GitHub 계정으로 로그인해야 합니다.
- 개발 빌드에는 .NET 10 SDK가 필요합니다. `dotnet build HermesDevDesk.sln -c Release`로 빌드할 수 있습니다.
- 설치파일을 만들려면 먼저 `scripts/fetch-engine.ps1`을 실행하고, `scripts/build.ps1 -DotnetPath <dotnet.exe의 절대 경로>`를 실행하세요. 로컬 7-Zip은 선택 사항입니다.
- `--live-smoke`와 `--tool-smoke`는 실제 모델 사용량을 소비합니다. 일반 소스 확인에는 사용하지 않습니다.

아래 설치파일 안내는 설치파일을 별도로 빌드한 경우에 해당합니다. 소스 ZIP에는 설치파일이 포함되지 않습니다.

독립 Windows 개발 에이전트 작업 앱입니다. OpenAI 공식 앱을 사칭하지 않으며 Codex Desktop과 전기능 동등성을 주장하지 않습니다.


## 0.5 스킬·보관함·로컬 알림
- 프로젝트 `.agents/skills` 또는 `skills` 아래 SKILL.md 목록/선택/본문 확인/공식 skills/list 확인. 전역 파일은 표시하지 않고 설치·전역 설정을 수정하지 않습니다. 선택한 지침은 작업 시작 때 공식 skill input으로 전송합니다. UTF-8/name/frontmatter/64KB/전체128KB/3개/경로·링크·민감 값·해시 변경을 검증합니다.
- 대화 보관/보관함/복원은 시작된 대화에 공식 thread/archive/unarchive를 사용합니다. 시작 전 초안은 앱 이력에서 보관합니다. 대화/파일/사용량을 삭제하지 않으며 반복 동작은 idempotent입니다. 보관 상태에서 실행할 수 없고 실행 중 보관은 거부합니다. 복원 후 같은 provider에서 thread/resume으로 재개합니다.
- 작업 초안 예약/로컬 알림은 앱 실행 중 또는 다시 열 때 due 초안을 한 번 생성합니다. 예약/취소/오류/재시작 중복 방지를 지원합니다. 모델·명령을 자동 실행하지 않고 사용자가 내용을 확인한 후 시작합니다. background service/OS 권한/방화벽 변경을 추가하지 않습니다.

## 기존 0.4 기능
- **MCP**: 프로젝트 선택 후 상단 MCP에서 앱에 포함된 로컬 파일 서버를 연결합니다. 자격증명/회사 API/유료 모델을 사용하지 않는 전용 점검 세션입니다. 목록/인수 schema/직접 호출/오류/확인 승인·거절/연결 종료를 제공하며 모델 접근은 localhost 차단 provider로 고정합니다.
- **에이전트 MCP**: MCP 화면에서 프로젝트의 새 작업에 사용을 켭니다. 읽기 도구는 비밀·링크·루트 밖을 차단합니다. 쓰기 작업에서만 새 파일 도구가 있고 각 호출에서 사용자 확인을 받습니다. 기존 파일 덮어쓰기·삭제·shell·network 기능은 없습니다. MCP process 자체는 shell sandbox가 아니므로 이 제한된 서버 구현으로 범위를 지킵니다. 임의 외부 서버/OAuth/token 설정은 추가하지 않았습니다.
- **이미지 입력**: 프로젝트 안의 PNG/JPEG를 첨부하고 작업을 시작합니다. 파일당 2MB/전체 3MB/최대 4개/16MP 제한, PNG CRC·JPEG dimension·WPF decode·해시·전송 전 변경을 검증합니다. 선택한 이미지가 현재 provider로 전송됩니다. 이력에는 경로/해시/크기만 저장합니다. 회사 배포의 실제 시각 이해는 회사 정보가 없어 미검증입니다.
- **대화 분기**: 시작된 작업의 공식 thread/fork로 새 대화를 만듭니다. 원본 대화·파일을 보존하며 분기 자체에는 모델 호출이 없습니다.
- **AI 변경 검토**: Git 루트에서 선택 provider/model로 공식 review/start를 실행합니다. 읽기 전용 새 작업이며 사용자가 시작을 확인해야 합니다. 실제 provider 사용량이 생길 수 있습니다. 이번 검사는 로컬 응답 fixture로 lifecycle을 확인했으며 실제 회사 모델의 검토 품질을 검증한 것은 아닙니다.

## 설치와 실행
1. `HermesDevDesk-0.5.1-win-x64-Setup.exe`를 직접 실행합니다. .7z로 받은 경우 전체 압축을 먼저 해제합니다.
2. 비어 있는 새 폴더를 선택하고 **설치 / 재개**를 누릅니다. 기본 대상은 `%LOCALAPPDATA%\Programs\HermesDevDesk\0.5.1`입니다. 0.1/0.2/0.3/0.4와 별도 설치입니다.
3. 설치된 `HermesDevDesk.exe`를 직접 실행합니다. 설치 시 자동 실행/자동 로그인하지 않습니다.
4. 설치 취소 후 같은 설치파일·대상으로 재개할 수 있습니다. 같은 버전 반복 설치는 동일 해시 파일을 보존합니다. 수정한 앱 파일과 충돌하면 덮어쓰지 않고 다른 경로 선택을 안내합니다.
5. 제거는 같은 설치파일의 **이 버전 제거**로 합니다. 해당 payload와 SHA256가 일치하는 앱 파일만 제거하고 사용자 파일·수정한 파일·별도 데이터 폴더·키·이력을 보존합니다. 재귀 삭제하지 않습니다.
관리자 권한, 전역 PATH, 레지스트리, 방화벽, 보안 설정, 전체 앱 재시작이 필요하지 않습니다. .NET 10.0.12와 공식 공개 Codex CLI 0.162.1 Windows x64 패키지를 포함합니다. Git worktree/diff에는 Git이 필요합니다. 설치파일은 서명되지 않았습니다. 인증서 구매나 신뢰 설정 변경은 수행하지 않았습니다.

## 회사 Azure / 사내 Gateway 설정
**API 설정**에서 회사에 확인한 값을 직접 입력하세요. 키를 채팅으로 보내지 마세요.

- 회사 Azure resource 또는 gateway **HTTPS API base URL**. Azure v1은 resource 주소 또는 `.../openai/v1`, legacy는 resource 주소, gateway는 회사가 안내한 base를 사용합니다. 최종 responses/chat 경로를 포함하지 않습니다.
- 정확한 Azure **deployment 이름** 또는 gateway model 이름. 공개 모델명으로 추정하지 않습니다.
- 프로토콜: Azure v1 Responses 권장, Azure legacy Responses, Gateway Responses, 또는 각 Chat Completions 호환 모드.
- legacy만 정확한 회사 **api-version**이 필요합니다. v1/gateway는 비웁니다.
- 인증 방식: Azure 기본 `api-key` 헤더, gateway의 BearerKey 또는 None. Entra ID·조직 SSO 자동 로그인은 아직 지원하지 않습니다.
- 사용자 본인이 앱의 PasswordBox에 입력한 키만 Windows 현재 사용자 DPAPI로 보호합니다. 기존 Codex 키·토큰·비공개 DB·기존 세션을 앱이 읽거나 추출하지 않습니다. 키 변경/삭제는 이 앱 0.4 저장소에만 적용됩니다.

**회사 연결 검사**는 명시 동의 후 해당 endpoint의 GET models를 요청합니다. 이 경로가 회사에서 막힐 수 있고 성공해도 배포별 생성/도구 지원을 보장하지 않습니다. 실제 작업 생성 요청은 회사 정책에 따른 사용량이 발생할 수 있으므로 사용자가 회사 설정을 확인한 후 시작하세요. 실제 회사 주소·배포명·키가 제공되지 않아 이번 작업에서 회사 인증·배포 지원·사내망 접근은 검증하지 못했습니다.

모델 요청은 지정한 회사 endpoint/deployment로만 전달합니다. 공개 OpenAI 모델 API로 자동 fallback하지 않습니다. redirect는 따라가지 않으며 TLS 인증서 검증을 정상 적용합니다. 인증서 설치/검증 무시 옵션이 없습니다. 회사 키는 공식 엔진이나 도구 subprocess에 넘기지 않고, 앱의 localhost capability bridge가 헤더를 붙입니다. Engine에는 일시 capability만 전달하고 도구 환경에서 제외합니다. CODEX_HOME은 앱 전용입니다. 기존 전역 Codex 설정을 덮어쓰지 않습니다.

공식 CLI는 사용자 skill 메타데이터를 발견할 수 있지만 회사/터미널 모드에서는 skill instructions·bundled skills·plugins/recommendations·analytics/telemetry를 비활성화합니다. 앱 sandbox는 OS 전체 읽기 격리를 보장하지 않습니다. 이 제한은 경로 검증이나 프롬프트 지시만으로 완전한 보안 경계를 만든다는 의미가 아닙니다.

회사 API는 응답 완료 후 표시하는 방식입니다. Chat Completions 어댑터는 텍스트, JSON function/custom-tool 호출, namespace 이름 매핑을 지원하지만 Responses와 완전히 같지 않습니다. hosted tools·이미지 생성·음성·computer use를 흉내내지 않으며 호환 불가이면 명시 실패합니다. 배포의 도구 지원 여부는 회사 서버로 별도 검증해야 합니다. 회사 작업은 usage/cancel 연결을 정확하게 유지하도록 한 번에 한 작업만 실행합니다.

## 개발 작업
프로젝트 등록 → 새 작업 → 모델 선택 → 지시 입력 → 작업 시작. 기본 읽기 전용입니다. 쓰기가 필요하면 새 작업 시작 전에 프로젝트 쓰기를 선택합니다. 이미 시작한 thread의 쓰기 모드는 바꾸지 않습니다. 실행/진행/중단/재개, 명령 결과, 대화, 계획, 승인 요청과 한 번의 승인/거절을 공식 App Server로 처리합니다. 파일·Git diff·작업 diff·변경파일 검토·작업 검색·이력이 제공됩니다.

직접 PowerShell 명령은 **명령** 탭에서 경로/명령/쓰기 모드를 확인하고 승인 후 실행합니다. API 생성 없이 별도 공식 엔진 sandbox에서 실행합니다. Windows sandbox가 command/exec 스트리밍/PTY를 지원하지 않아 결과는 완료 후 표시하며 30초 제한입니다. 명령 중단은 해당 명령 전용 App Server만 종료합니다.

Git worktree 격리는 커밋된 HEAD에서 새 branch/worktree를 만들고 별도 프로젝트로 등록합니다. 기존 파일/미커밋 변경을 복사하거나 덮어쓰지 않습니다. hooks/checkout filter를 실행하지 않는 snapshot 방식입니다. 최대 5000 파일/전체 100MB/개별 20MB. 커밋된 비밀 경로·링크·submodule·Windows 충돌·변형 export는 생성 전 차단합니다. 자동 merge/push/prune/delete가 없습니다.

## 이력과 사용량
데이터: `%LOCALAPPDATA%\HermesDevDesk\0.5.1`. 기존 버전 이력은 자동 이주/덮어쓰지 않습니다. `--data-dir ABSOLUTE_NEW_PATH`로 검증용 폴더를 분리할 수 있습니다.
사용량은 이 앱에서 실제 관측한 response usage / CLI 누적 delta를 UTC 날짜·작업·deployment/model로 조회하고 JSON으로 내보냅니다. 입력/출력/cache/reasoning/total과 HTTP 시도 수를 기록합니다. cache는 input, reasoning은 output의 부분집합으로 별도 가산하지 않습니다. 미제공/실패/중단/복구 불확실값은 unknown이며 0을 만들지 않습니다. CLI 요청 횟수나 회사 전체 사용량은 알 수 없는 경우 unknown입니다. HTTP 시도 수는 성공 청구 요청 수를 뜻하지 않습니다. 공개 가격·비용 계산·billing/admin API 기능은 없습니다.

## 검증과 남은 범위
`VALIDATION.md`와 `artifacts`의 검사 보고서를 확인하세요. 실제 회사 서버 검증과 native mouse/keyboard 클릭 검증은 완료하지 못했습니다. 컴퓨터 사용 도구의 Windows platform 경로 오류(HRESULT 0x80070003) 때문에 native 검증이 차단되었습니다. WPF/WinForms 프로그램 이벤트 검사와 실제 공식 CLI 연결 검사는 별도로 명시합니다. 사용자 운영 환경에 자동 설치하지 않고 새 격리 경로에서만 설치/제거를 검사합니다.

컴퓨터 사용·브라우저 제어·외부 MCP 설치/로그인·음성·이미지 생성·클라우드·원격·모바일·자동화·공식 앱 프로젝트 등록/동기화는 미지원입니다. API 키만으로 이 기능이 자동으로 활성화되지 않습니다.

## 빌드와 공식 출처
`scripts/build.ps1 -DotnetPath <project-local-dotnet.exe> -SevenZipPath <official-7za.exe>`. 공식 NuGet 런타임과 `vendor/codex-0.162.1` npm 패키지를 사용합니다. 원본 패키지 integrity는 `ENGINE-PROVENANCE.json`, 라이선스/NOTICE는 vendor 안에 있습니다. 설치 payload의 각 파일은 SHA256 manifest로 검증합니다.

- [공식 Codex App Server](https://developers.openai.com/codex/app-server)
- [공식 custom provider 설정](https://developers.openai.com/codex/config-reference)
- [Azure Responses](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/responses?view=foundry-classic)
- [Azure v1 API lifecycle](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle)
- [공식 Codex 0.162.1 공개 코드/라이선스](https://github.com/openai/codex/tree/rust-v0.162.1)

