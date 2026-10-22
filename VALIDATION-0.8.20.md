# 0.8.20 검증
- 0.8.19 오류 복구 수정 포함: 과거 도구 기록 변환 실패 수정, HTTP 429/503/529 제한적 재시도.
- 브라우저 도구 숨기기/표시 버튼 추가. 탭·주소·탐색·부가기능·상태 표시를 함께 접어 페이지 영역 확장. 복원 버튼 유지 및 숨김 설정 저장. WebView와 페이지는 유지.
- 오류 복구 포함 코어 회귀 300개 통과 (artifacts/recovery-0.8.19-regression-final.txt).
- 0.8.20 실제 WebView2 브라우저 검사 32개 통과 (artifacts/browser-controls-0.8.20-verified/browser-experience-report.json).
- 실제 브라우저 검사: 접기/복원, 페이지 높이 증가, WebView 유지, 설정 재로드, 탭 10개/닫기/팝업/채팅 격리/북마크/탐색.
- 기존 UI smoke의 컨테이너 위치 고정 탐색을 새 browserControls 참조로 갱신.
- 회사 서버 및 유료 모델 호출 없음. 당시 high demand 오류의 원격 서버 원인은 로그 부재로 확정하지 않음.
- NU1903 기존 SQLite 의존성 경고 및 최초 테스트 실행 환경 문제는 VALIDATION-0.8.19.md 참조.
- 최종 설치 파일 /verify: 내장 파일 471개 SHA256 모두 통과. 운영 앱 설치/재시작은 실행하지 않음.
