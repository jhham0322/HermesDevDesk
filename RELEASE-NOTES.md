# Hermes DevDesk 0.8.58

- 재설치 시 새 파일을 검증하고 기존 App 폴더 전체를 교체합니다. 중단된 설치가 남긴 확인 가능한 임시·이전 앱 폴더와 같은 버전의 이전 설치 폴더도 정리합니다.

# Hermes DevDesk 0.8.57

- 짧은 설명 요청과 기본 첨부 설명 요청은 앱이 한 단계 계획을 저장하고 바로 실행합니다. 최종 답변을 받으면 작업내역에 완료를 기록합니다.
- 코드 수정과 복합 작업은 기존 세부 계획과 검증 흐름으로 진행합니다.

# Hermes DevDesk 0.8.56

- 화면 갱신과 WebView2 탐색 이벤트가 주소창의 입력을 덮어쓰지 않습니다. 탭으로 돌아오면 해당 탭의 주소 초안을 복원합니다.

# Hermes DevDesk 0.8.55

- 회사 API 생성 요청에 연결별 출력 토큰 설정을 적용해 로컬 엔진의 작은 제한값이 우선하는 문제를 수정했습니다.
- 간단 설정에서 기존 출력 한도를 보존하고, 프로세스 상세에 실제 전송한 상한을 표시합니다.

# Hermes DevDesk 0.8.54

- 출력 한도로 자동 재개하는 동안 엔진 오류 문구를 표시하지 않습니다. 자동 재개를 모두 사용하면 짧은 재개 안내를 표시합니다.

# Hermes DevDesk 0.8.53

- 모델 출력 한도에 도달하면 받은 답변과 작업 기록을 보존하고 남은 부분을 최대 두 번 자동으로 이어갑니다. 실제 연결 끊김과 구분하며, 한도가 반복되면 재개 지점을 남깁니다.

# Hermes DevDesk 0.8.52

- 짧은 한국어 완료 조건과 실제 검증 출력의 짧은 표식을 허용합니다.
- 계획 오류는 작업 ID와 수정할 필드를 알려 주며, 기존 계획과 완료 항목을 보존합니다.

# Hermes DevDesk 0.8.51

- 프로젝트에 설치된 DRM 스킬을 문서 첨부와 문서 경로가 포함된 작업에 자동으로 적용합니다. 작업 도중 문서를 발견할 때도 등록된 스킬을 먼저 사용합니다.
- 프로젝트의 `.agents/skills/drm/SKILL.md` 또는 `skills/drm/SKILL.md`가 기본 위치입니다. 이름에 `drm`이 포함된 프로젝트 스킬과 수동 선택도 지원합니다. 사내 DRM의 사용자 권한을 사용합니다.

# Hermes DevDesk 0.8.50

- 계획 저장과 첫 항목 시작을 한 번에 처리합니다. 항목 완료와 다음 항목 시작도 한 요청에서 처리할 수 있습니다.
- 모델 사용량과 진행 표시의 저장을 묶고, 명령 응답 한 건을 기록하는 중복 상태 저장을 줄였습니다.
- Git 변경 검토는 선택한 파일의 상태만 다시 조회하고, 변경이 없는 프로젝트는 모델 호출 없이 검토를 마칩니다.

# Hermes DevDesk 0.8.49

- 작업을 작은 항목으로 계획하고 완료한 항목을 체크합니다. 프로세스별 `작업계획.txt`와 `작업내역.txt`는 앱 데이터 폴더의 `work`에 저장됩니다.
- 각 항목의 완료 조건을 한 번 확인합니다. 모든 항목이 끝나면 작업이 자동으로 완료되며 재시작하면 저장된 미완료 항목부터 이어갑니다.
- 제목은 한 줄로 표시됩니다. 제목 펼치기와 제목 복사에서 전체 제목을 사용할 수 있습니다.
- 모델 서버 응답 전 연결 오류와 실제 HTTP 503을 구분합니다. 안전하게 재시도할 수 있는 경우만 제한적으로 재시도하며, 이후에는 받은 결과와 재개 지점을 보존합니다.
- 회사 API에서 gpt-6-luna를 선택해도 로컬 엔진이 작업 계획 도구를 전달합니다. 서버에는 선택한 모델명을 보냅니다.
- 진행 중에 추가한 이미지는 저장한 뒤 다음 실행 단계의 요청과 함께 전달합니다.
- 스트림과 개별 표시 이벤트의 화면 갱신 및 저장을 묶고, 진행 가능한 자동 단계의 대기 시간을 줄였습니다.

# Hermes DevDesk 0.8.48

- Queued commands without operationId receive stable item/method identities; saved success/failure receipts prevent replay. Unknown effects still require review. Timeout input accepts integer 1000..120000 ms and is distinct from an actual execution timeout.
- Optional cwd/inputFiles/buildTool preflight checks declared existing dependencies without guessing output paths in shell text. Retry protection distinguishes actual working directories while canonical equivalent paths remain the same context.
- Long received Java compiler output retains the first file:line error and symbol/location in bounded previews and saved receipts. No company project or real Gradle build was used for validation.
- Output-token limits, missing-terminal EOF and response deadlines retain partial text and explicit reasons; interrupted objectives pause for explicit checkpoint resume. Empty final messages no longer erase received partial answers. Completed or uncertain side effects are not automatically replayed.
- Settings connection failures display HTTP status alongside the original diagnostic; provider settings and the underlying raw-body connection contract remain intact. Shell validation now checks the existing user-requested collapsed command default.
- Validation uses synthetic fixtures and an isolated official local engine. No real company API, OAuth login or authenticated model request was made. No production performance improvement is claimed from the single synthetic phase-timing run.

# Hermes DevDesk 0.8.47

- Windows release and main CI record browser startup, manual cancellation, and shutdown stages. The browser cancellation watchdog remains 120 seconds.
- Includes the conversation save responsiveness improvements shipped in 0.8.46. Browser cancellation behavior is unchanged.

# HermesDevDesk 0.8.46 — UI save responsiveness

- Based on verified 0.8.45; preserves full-result completion verification, response-stream terminal handling, daily-quota recovery, company model settings and OAuth.
- A real synthetic WPF window measured synchronous save/redaction as an input-queue blocker. Unchanged immutable transcript entries now reuse weakly held sanitized results. New secret registrations invalidate the cache; edited entries and restarted histories are rechecked. No asynchronous/drop-on-close persistence or data pruning is introduced.
- Local five-sample warmed medians with 64 synthetic registered secrets: save plus UI-refresh input dispatch at 2,000 messages 32.3 -> 15.5 ms; at 10,000 messages 167.3 -> 37.4 ms. These are controlled local WPF dispatcher measurements, not native mouse latency or a guarantee on all machines. Conversation opening remains around 100 ms; no material improvement is claimed for opening or ordinary scrolling/stream rendering.
- Local regression: 638 checks passed, including new-secret invalidation, edited-entry masking, restart/history retention and previous completion/stream/quota tests. Long-conversation WPF checks retain 40 realized views and access to all history.
- Existing company 404 display assertion, original shell expanded-default assertion and prior continuation integration uncertainty remain disclosed. Real company servers, OAuth consent/model calls, overnight provider reset and user PC installation were not tested or changed.
# Hermes DevDesk 0.8.45

- Saves complete task results for later retrieval.
- Handles incomplete and prematurely terminated response streams without marking unfinished output as complete.
- Local handoff validation recorded 634 passing regression checks.

# Hermes DevDesk 0.8.44

Fixes misleading malformed-response errors for valid Chat length-limited empty output, preserves incomplete status, and blocks truncated tool calls from execution. Adds persistent next-day 09:30 KST recovery for explicit daily-token exhaustion only, with same-session/provider checks, Stop cancellation, durable single-claim admission and three-attempt limit.

The app must remain open and connected; when closed/offline, execution waits for the next valid connection. Provider reset time and successful real-account completion are not guaranteed or verified. The prior company 404 UI assertion, expanded-tool-card shell assertion and long-running continuation integration limitations are preserved, not reported as passed. No real company requests, account credentials, user GUI manipulation or installed settings changes were used for local verification.

See VALIDATION-0.8.44.md for exact checks and CI evidence. Existing releases and their assets are not overwritten.
