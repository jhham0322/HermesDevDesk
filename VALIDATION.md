# Hermes DevDesk 0.7.0 검증

검증 날짜: 2026-10-15 KST. Windows에서 .NET 10.0.401 SDK / 10.0.12 self-contained runtime / 공식 CLI 0.162.1을 사용했습니다.

- 소스와 Windows publish: 경고 0, 오류 0.
- 일반 회귀 및 추가 기능: 112 checks PASS. 실제 로컬 Git stage/unstage/commit과 선택한 로컬 bare 원격으로 Push/Fetch를 확인했습니다.
- WPF 컨트롤/이벤트: 53 checks PASS. 메시지/사이드바 재사용, 테마, 채팅 초안, 고정, 이름 변경, 실제 WebView2 fixture 탐색/DOM 읽기/클릭 포함. native UI 검사를 대체하지 않습니다.
- 공식 engine + synthetic company provider: 브라우저 dynamic tool 광고/호출/응답, 최종 메시지 중복 제거, 실제 turn/steer가 통과했습니다. 실제 회사 서버/키나 실제 모델 사용량은 사용하지 않았습니다.
- 스트리밍 성능 fixture: 2,000 deltas, 20,000자 보존, 디스크 스냅샷 1회, 테스트 시간 402ms (대기 300ms 포함). UI 렌더링은 100ms 갱신, 이력은 250ms 묶음 저장. 실제 서버/PC 전체 성능 보장은 아닙니다.
- 실제 Windows mouse drag: 상단 빈 영역을 잡아 드래그했습니다. 화면 원점 (4800,354)에서 (5025,474)로 이동했습니다. 더블클릭 최대화 시 2560x1440 화면, 다시 더블클릭 복원 확인. 메뉴 클릭과 파일 메뉴의 로컬 Git / 원격 서버 설정 항목 접근도 확인했습니다.
- 설치 패키지: 467개 embedded file SHA-256 일치. Portable ZIP 전체 manifest 검증.
- 설치/실행/반복/충돌 보존/제거 보존/취소/복구/재설치/unowned 경로는 installer 격리 테스트로 검증합니다. 검증 결과는 릴리스 RELEASE-VALIDATION.json 참조.

디자인: 원본과 구현을 1280x1392 상태로 함께 비교했습니다. 주요 작업 공간의 사이드바, 대화 시작 위치, 색상, 입력창의 위치와 간격을 조정했습니다. 브랜드/데이터/실제 연결 기능은 Hermes에 맞게 유지합니다. 전체 Codex 제품의 모든 화면과 기능 1:1 일치를 완료했다고 주장하지 않습니다.

미검증/미구현 범위는 PLAN.md에 명시합니다. 실제 회사 endpoint의 품질/호환성, 외부 브라우저 사이트별 입력·폼 동작, 모든 Windows PC, 실제 모델 기반 리뷰/vision 품질은 이번 fixture 통과 범위 밖입니다.

0.7.0 추가 검증:
- 별도 소유 WPF 창 두 개를 대상으로 실제 Windows 제어 15 checks PASS: JPEG 캡처/UI Automation/한글/단축키/클릭/휠/드래그/창 전환, 비밀번호·잘못된 대상·좌표·닫힌 창 차단. 응답 없는 창의 작업 프로세스 취소 2초 이내.
- 실제 공식 CLI와 합성 로컬 provider에서 컴퓨터 dynamic tool 광고, 호출 상관관계, 텍스트+이미지 응답 전달 및 대기 요청 turn interrupt PASS. 실제 모델/회사 endpoint는 사용하지 않았습니다.
- 최종 패키지 WPF 회귀 53 checks PASS. 첫 실행은 테스트 입력 경로에 슬래시를 사용하여 문자열 경로 비교가 실패했고, Windows 정규 경로로 재실행하여 통과했습니다.
- 설치 프로그램 embedded 467 files SHA256 PASS.

최종 설치 시나리오 9개 PASS: 설치/실행/재설치/충돌 보존/제거 보존/취소/복구/제거 후 재설치/미소유 경로 보존. Portable 전체 467개 파일 해시 PASS.
