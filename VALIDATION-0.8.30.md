# 0.8.30 Hermes 추가 OS 샌드박스 제거

엔진 시작 기본값 danger-full-access, Windows unelevated 샌드박스 지정 제거. thread/start/resume/fork, review, MCP inspection, turn/start, command/exec 모두 danger-full-access/dangerFullAccess를 명시합니다. 일반 작업은 과거 writable/FullPermissions 값으로 OS 제한이 재활성화되지 않게 정규화하며 기존 선택 UI를 숨깁니다. 회사 모델 서버 설정은 수정하지 않습니다. 검토 전용 지시와 도구별 범위 검사, 중단, RAG, 반복 오류 차단은 유지합니다.

검증:
- Release 빌드 경고 0, 오류 0.
- 기존 회귀 335개 통과. 자식 엔진 시작 인자에서 danger-full-access와 Windows sandbox 옵션 부재 검사 포함.
- 전체 WPF UI 100개 통과. 과거 읽기 전용 UI 선택으로 샌드박스가 다시 켜지지 않는지 검사.
- 공식 엔진 + 합성 회사 서버의 자동 루프에서 프로젝트 밖 테스트 파일 쓰기와 로컬 HTTP 요청 성공. 과거 writable=false/FullPermissions=false도 제한 재활성화 없음. 수동 명령 창에도 동일한 프로젝트 밖 쓰기/HTTP 검사를 수행. 대화 분기의 반환 sandbox.type도 dangerFullAccess 검사. 반복 오류 차단, 다른 방법 성공, 완료 보고, 대기·실행 중 중단 검사 포함.
- Learning 엔진 검사에서 과거 writable=false 명령의 실제 실행 성공과 RAG 명시 검색 확인.
- 테스트는 소유한 격리 폴더와 로컬 HTTP 합성 서버를 사용하며 실제 회사 모델 호출 없음.

배포 태그 v0.8.30에서 회귀·공식 엔진·브라우저·프로세스·공개 파일·강제 재설치 검사를 다시 통과해야 설치 파일과 포터블 ZIP을 공개합니다.
