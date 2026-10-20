# Hermes DevDesk 0.8.8

[Windows 설치 파일 다운로드](https://github.com/jhham0322/HermesDevDesk/releases/download/v0.8.8/HermesDevDesk-0.8.8-win-x64-Setup.exe) · [Portable ZIP 다운로드](https://github.com/jhham0322/HermesDevDesk/releases/download/v0.8.8/HermesDevDesk-0.8.8-win-x64-Portable.zip) · [릴리스 페이지](https://github.com/jhham0322/HermesDevDesk/releases/tag/v0.8.8)

오른쪽 패널을 드래그해 조정한 폭을 저장합니다. 창 크기 변경·새로고침·도구 패널 재열기 중 기본 폭으로 되돌리지 않으며, 앱 재실행 후 폭과 선택 탭을 복원합니다. 기존의 700px 상한도 제거했습니다.

내장 브라우저의 **전체 캡처 · AI 첨부** 버튼으로 현재 로드된 페이지를 스크롤 아래까지 PNG로 저장하고 AI 입력에 첨부할 수 있습니다. 긴 페이지는 글자가 지나치게 작아지지 않도록 위에서 아래 순서의 이미지로 나눕니다. 전송 버튼을 누르면 선택한 모델에 이미지로 전달됩니다. 원본 PNG는 프로젝트의 `.hermes-captures`에 보관합니다.

AI도 `hermes_browser`의 `screenshot` 동작으로 페이지 전체를 직접 캡처하고 이미지와 원본 경로를 받아 사용할 수 있습니다. `fullPage=false`는 현재 보이는 영역만 캡처합니다. 전체 캡처는 현재 로드된 문서에 대해 한 변 32767px, 합계 64MP까지 지원합니다. 지연 로딩되어 아직 생성되지 않은 내용이나 별도 스크롤 영역 내부의 전체 내용까지 자동 수집한다고 주장하지 않습니다.

이전 버전의 이미지·파일 첨부, 공개 진행 요약, 전체 권한 기본값과 로컬 API 오류 처리 개선을 포함합니다. Windows 릴리스는 태그 소스에서 빌드하고 회귀·공식 엔진 연동·설치 파일 SHA256·공개 페이로드 검사를 통과한 뒤 게시합니다. 실제 회사 서버나 유료 모델은 검증에 사용하지 않았습니다.
