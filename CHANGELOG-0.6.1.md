# Hermes DevDesk 0.6.1 — Windows x64

- 현재 Codex 작업 화면을 참고한 밝은 사이드바, 프로젝트별 채팅, 넓은 대화 영역, 하단 입력창과 접이식 패널.
- 상단 빈 영역 드래그로 창 이동, 더블클릭 최대화/복원 수정. 실제 Windows 마우스로 확인했습니다.
- PC의 로컬 git.exe 사용. 선택 Git 실행 경로 및 HTTPS/SSH/로컬 bare 원격 설정, stage/unstage/commit/fetch/push.
- 스트리밍 렌더링 최대 10Hz, 메시지와 사이드바 재사용, 이력 250ms 묶음 저장. 2,000 deltas / 20,000자 fixture에서 저장 1회, 손실 없음.
- 고정/이름 변경/검색/채팅별 초안/테마/단축키/팔레트/내보내기, 추론 수준 선택, 실행 중 추가 지시.
- 실제 WebView2 브라우저 패널과 요청별 검토를 거치는 모델 탐색/읽기/클릭/입력 도구.
- 회사 Responses SSE 실시간 전달과 최종 메시지 중복 제거.

검증: 소스/Windows publish 경고 0·오류 0, 90 일반 checks, 53 WPF checks, 공식 engine + synthetic provider 계약, 9 설치/실행/보존/복구 시나리오, 패키지 467개 파일의 SHA-256 검증.

**실행하려면 아래 Setup.exe 또는 Portable.zip을 받으세요.** Source code ZIP은 실행 파일이 아닙니다. Portable은 전체 압축 해제 후 HermesDevDesk.exe를 실행하세요.

Windows 10/11 x64, 포함 .NET 10 Desktop 런타임 및 공식 CLI 0.162.1. 브라우저는 Microsoft Edge WebView2 Runtime이 필요합니다. 패키지는 아직 코드 서명되지 않았습니다.

전체 Codex 디자인/기능 1:1 동등성은 달성하지 않았습니다. 전체 컴퓨터 제어·음성·이미지 생성·외부 MCP/OAuth·플러그인 스토어·공식 클라우드/모바일/Space/Pets 등은 아직 미연결/미구현이며 PLAN.md에 명시합니다.