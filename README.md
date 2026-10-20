# Hermes DevDesk 0.8.7

[Windows 설치 파일 다운로드](https://github.com/jhham0322/HermesDevDesk/releases/download/v0.8.7/HermesDevDesk-0.8.7-win-x64-Setup.exe) · [Portable ZIP 다운로드](https://github.com/jhham0322/HermesDevDesk/releases/download/v0.8.7/HermesDevDesk-0.8.7-win-x64-Portable.zip) · [릴리스 페이지](https://github.com/jhham0322/HermesDevDesk/releases/tag/v0.8.7)

캡처 이미지는 입력창에서 Ctrl+V 또는 붙여넣기 버튼으로 첨부합니다. 이미지·파일 선택 버튼과 파일 드래그도 지원합니다. PNG/JPEG/BMP/GIF/TIFF 이미지는 최대 4개, 파일당 20MB·합계 40MB입니다. 일반 파일은 최대 10개, 파일당 25MB·합계 50MB입니다. 선택한 파일은 프로젝트에 사본을 만들며 텍스트는 미리보기를, PDF·문서 등은 AI가 도구로 읽을 파일 경로를 전달합니다. 첨부만 있어도 전송할 수 있습니다.

브라우저 도구는 같은 채팅의 패널에 열린 페이지를 읽고 실제 화면 이미지와 텍스트를 모델로 보냅니다. 비활성 채팅의 화면도 읽을 수 있도록 WebView를 유지하고 비동기 스크립트 결과를 기다립니다. 응답 시간 초과나 페이지 스크립트 오류는 채팅에 표시합니다. Chat Completions 중계에서도 화면을 이미지로 전달합니다. 선택 모델은 이미지 입력을 지원해야 합니다.

로컬 API 중계는 화면·첨부·대화 이력을 포함한 96MB 이하 요청을 지원하며, 초과 시 HTTP 413과 안내를 반환합니다. 회사 서버의 HTTP 오류 내용과 만료된 로컬 작업 세션 오류를 구분해 표시합니다. 분기 대화는 자기 작업의 중계 주소를 사용합니다.

공개 진행 요약과 전체 권한 기본 설정은 유지됩니다. Windows 릴리스는 태그 소스에서 빌드하고 회귀·공식 엔진 연동·설치 파일 SHA256·공개 페이로드 검사를 통과한 뒤 게시합니다. 실제 회사 서버나 유료 모델은 검증에 사용하지 않았습니다.
