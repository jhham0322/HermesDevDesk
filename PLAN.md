# Hermes DevDesk 0.5.1 최종 핵심 기능표

| 기능 | 구현 및 검증 상태 |
|---|---|
| 프로젝트 등록/선택, 작업·대화·이력·검색 | 구현/회귀 검사 |
| 공식 엔진 실행/진행/중단/재개, command 결과/로그/승인 | 구현/실제 공개 CLI+로컬 company fixture. 기존 구독 실제 호출 검사는 이전 버전 증거 보존 |
| 파일/비밀 제외/경로, Git 및 작업 diff, worktree | 구현/실제 Git·격리·보존 검사 |
| 회사 Azure v1/legacy/Gateway Responses, Chat 호환, DPAPI 키, usage | 구현/합성 키·정상 TLS·redirect 차단·unknown·dedup·restart 검사. 실제 회사 정보 없음 |
| 프로젝트 로컬 MCP 목록/실행/오류/승인·거절 | 구현/실제 엔진 및 실제 파일 I/O. 앱 내 무자격증명 서버만 사용 |
| PNG/JPEG 입력·이미지 metadata, conversation fork, AI review | 구현/실제 engine 계약·모델 fixture. 회사 vision/review 품질은 미검증 |
| 프로젝트 스킬 목록/선택/공식 확인/호출 | 0.5 구현. 실제 skills/list, skill 지침 전송, 반복·변경·민감 값·오류·취소·이력 보존 검사 |
| 대화 보관함/보관/복원 | 0.5 구현. 실제 thread/archive/unarchive/resume, 반복·취소·진행 중 거부·provider mismatch·RPC 오류·보존 검사 |
| 로컬 작업초안 예약/알림 | 0.5 구현. 앱 실행/재시작에 due draft 한 번 생성·취소·오류·중복 방지. 모델/명령 자동 실행 없음 |
| Windows 설치/실행/취소/복구/반복/제거 | self-contained per-user 설치본. modified/user/history 보존. 격리 event/payload/hash 검사 |

이 범위의 공개 로컬 핵심 기능은 후속 구현 목록으로 남기지 않았다. 범위를 늘리는 새 기능을 추가하지 않는다.

남은 항목의 구체적 조건:
- 실제 회사 인증·vision/review 품질: 회사 endpoint/deployment/api-version/auth/사용자 키 및 실제 사내망 조건 없음. 키는 앱에 직접 입력.
- native mouse/keyboard 검증: CUA Windows runtime 경로 HRESULT 0x80070003. WPF/WinForms event 검사를 실제 native click으로 주장하지 않음.
- 외부 MCP/OAuth/통합: 실제 서버 command/url, 코드 접근 범위, 허용 도구와 자격증명 대상 미지정. 외부 설치/권한/토큰 승인 없음. 공개 config를 비공개 API로 오분류하지 않음.
- voice/realtime/remote/mobile: 공개 실험적 methods 존재. 회사 realtime endpoint/deployment 및 microphone/remote pairing 등 장치·접근 권한이 없음. 자동 활성화 없음.
- 이미지 생성: 실제 provider imageGeneration 지원/배포·인증 조건 없음. 이미지 입력과 생성은 구분.
- 공식 Desktop cloud job/project sync/plugin store: 독립 제품용 공개 계약 확인 안 됨. 비공개 DB/session/token/API 복제 안 함.
- unattended background AI 자동 실행: 실제 provider 조건 및 작업별 미래 자동 모델 실행 승인이 없음. 현재 예약은 명확히 초안/알림 전용이고 자동 AI 실행으로 가장하지 않음. OS service/권한 변경 없음.

근거: 공식 CLI 0.162.1 generated stable/experimental App Server schema. skills/list + UserInput.skill, thread/archive/unarchive/resume/fork/review/start는 실제 시험. MCP와 model provider 설정은 공식 버전 config.schema.json. source/SDK/npm licenses 및 integrity는 ENGINE-PROVENANCE.json/vendor.
