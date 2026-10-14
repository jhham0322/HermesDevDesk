# Hermes DevDesk 0.5.1 소스 보관 검증

2026-10-14: 0.5.0의 기능을 유지한 소스 보관 버전이며 앱/설치파일/클라이언트 버전을 0.5.1로 올렸다. 기존 0.5.0 폴더와 이전 설치파일은 보존했다.

- .NET 10 SDK로 Release 솔루션 빌드: 성공, 경고 0개/오류 0개.
- Hermes.Tests.exe 기본 회귀 검사: 합성 회사 서버/보안/사용량 포함 총 74개 PASS. 실제 회사 주소·키·유료 모델은 사용하지 않았다.
- 업로드 대상 54개 소스/문서 파일에서 알려진 GitHub/OpenAI/AWS 키와 개인 키 패턴을 검사했고 일치가 없었다. 바이너리, 로컬 도구, 빌드 출력, 사용자 인증/이력 파일은 업로드 대상에서 제외했다.
- GitHub 저장소는 개인 계정 jhham0322 소유의 private이다. 협업자는 소유자 1명이며 대기 초대가 없고, 익명 저장소 조회는 HTTP 404이다.
- 이번 작업은 소스 보관이며 설치파일 제작/새 설치/실제 엔진·화면 검사를 재수행하지 않았다.

## 기존 0.5.0의 검증 기록

아래는 0.5.0에 있던 검증 기록이며 이번 0.5.1에서 새로 실행한 결과가 아니다.


최종 숫자는 artifacts 보고서를 따른다. 기존 core/company/security/usage, MCP/image/fork/review 회귀와 신규 skill/archive/restore/repeat/error/cancel/local draft timer 검사를 수행한다. 실제 공개 CLI 0.162.1 사용; model 응답은 project-local company fixture만 사용하고 실제 회사/유료 model을 호출하지 않는다.

WPF control/event 및 WinForms PerformClick은 native mouse/keyboard 검증과 다르다. CUA runtime HRESULT 0x80070003 차단을 보존한다. 사용자 운영 위치 자동 설치/전역 설정/보안/방화벽/권한/외부 credential 변경 없음. 기존 0.4 및 다른 프로젝트 보존.

스킬: 프로젝트의 .agents/skills 또는 skills만, 전역/외부 파일 제외. 실제 공식 skills/list와 skill input 지침 포함을 검증한다. 변경/민감 값/잘못된 frontmatter/취소/반복/이력 검사.
보관/복원: 공식 RPC 실패에서 앱 상태를 보관됨으로 만들지 않는다. 같은 provider만 사용, 대화·파일·usage 유지, 반복 동작은 모델 호출 없음, 복원 후 resume. 시작 전 초안은 local history만 보관.
예약: 앱 내 due 초안/알림만 생성, model/command 자동 실행 없음. 중복/취소/오류/재시작 검사.

실제 회사 인증/vision/review 품질과 native 입력은 미검증. public 계약/외부정보/장치권한 조건에 따른 남은 항목은 PLAN.md에 구체적으로 기록한다.

확정 결과: core/company/security/usage 74개 PASS; MCP 11개 PASS; image/MCP/fork/review 실제 엔진 fixture 15개 PASS; skill/archive/restore/error/cancel/draft timer 26개 PASS; self-contained WPF control/event 36개 PASS; installer install/run/repeat/error/cancel/recover/remove 9개 PASS; payload 456개 SHA256 및 7z PASS. 실제 회사 모델 품질/인증과 native 마우스 입력은 미검증.
