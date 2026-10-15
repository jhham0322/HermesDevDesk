# Hermes 0.8.1 기능 현황

기준: 설치된 Codex 데스크톱 화면 캡처 및 공개 App Server 0.162.1 stable/experimental schema. 사용자 요청은 전체 디자인/기능 일치이며 아래 미구현 항목 때문에 전체 동등성은 달성하지 않았다.

| 기능 | 상태 | 검증 / 조건 |
|---|---|---|
| 밝은 사이드바 / 프로젝트 채팅 / 하단 입력 / 접이식 패널 | 구현 | 실제 화면 비교 및 WPF / native UI 검사 |
| 어두운 테마 / 고정 / 이름 변경 / 검색 / 초안 / 단축키 / 팔레트 | 구현 | 영속성, 컨트롤, 재사용 검사 |
| 채팅 스트리밍 / 완료 중복 제거 / 추가 지시 / 중단 / 재개 | 구현 | 공식 엔진 + synthetic provider round trip |
| 모델 / 추론 수준 | 구현 | 설정 모델 실패 후 보존, 회사 models 목록 선택, 추론 수준은 서버 제공 값만 |
| Anthropic Messages | 구현 | 텍스트/도구/이미지/SSE/usage 변환, 로컬 합성 서버 및 공식 CLI 검사 |
| 사내 중계서버 연결 예시 | 구현 | 자동/Responses/Chat Completions/Anthropic, 자동 저장/요청 없음 |
| 회사 Responses SSE | 구현 | 최종 응답 전 첫 chunk 도착, 사용량/취소/실패 검사 |
| 로컬 Git / 지정 git.exe / 원격 서버 / stage / commit / fetch / push | 구현 | 실제 격리 Git / bare 원격 Push/Fetch / working file 보존 |
| 파일 / 실제 Git diff / 저장된 작업 diff / worktree / AI 검토 | 기존 구현 유지 | 회귀 검사; 실제 AI 품질은 provider 조건 필요 |
| 이미지 입력 / 스킬 / 로컬 MCP / 분기 / 보관 | 기존 구현 유지 | 공개 engine 계약 / 로컬 fixture |
| 앱 브라우저 / 모델 탐색·읽기·클릭·입력 도구 | 구현 | 실제 WebView2 fixture + 공개 dynamic tool 계약; WebView2 Runtime 필요 |
| 사용량 / DPAPI 키 / sandbox 명령 | 기존 구현 유지 | synthetic key / usage / Windows sandbox |
| 예약 초안 / 알림 | 구현 | 앱 실행 중 처리; 자동 AI 실행 아님 |
| 렌더링 / 이력 저장 튜닝 | 구현 | 2000 deltas, 1 snapshot, 텍스트 보존; 메시지와 사이드바 재사용 |
| Windows 선택 창 컴퓨터 제어 | 구현 | 화면 읽기/클릭/입력/스크롤/드래그, 창 허용 및 요청 승인, 전역 중단 |
| 음성 / 이미지 생성 | 미구현 | provider 연결 추가 개발 필요 |
| 외부 MCP/OAuth / 플러그인 스토어 | 미구현 | 실제 서비스 설정·인증과 UI/호스트 추가 구현 필요 |
| 공식 클라우드 / 원격 모바일 / 공식 프로젝트 동기화 | 미연결 | 독립 앱용 서비스 계약 및 접근 미확보 |
| Space / 문서 아티팩트 / Pets | 미구현 | 별도 서비스 구현과 연결 필요 |
| 무인 자동 AI 실행 | 미구현 | 현재 예약은 초안/알림만 생성 |
| 공식 앱 전체 화면 / 모든 메뉴 1:1 일치 | 미달성 | 주요 작업 화면 구조를 맞췄으며 앱별 기능과 브랜드 차이 유지 |

공식 문서: https://learn.chatgpt.com/docs/features , https://learn.chatgpt.com/docs/app-server

Windows 실행 패키지는 GitHub Release에 첨부하며 source Git history에는 대형 runtime/CLI/EXE, 계정 데이터, 브라우저 프로필, 개인 화면 캡처를 넣지 않는다.
0.8.1: 3필드 게이트웨이 자동 연결, 저장 실패 전 모델 입력 보존, 공통/기존 프로필 재사용, 실패 단계별 진단을 추가했습니다.
