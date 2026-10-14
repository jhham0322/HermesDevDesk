# Hermes DevDesk 0.6.1

Windows x64용 독립 개발 에이전트 클라이언트입니다. 현재 Codex 데스크톱 화면을 참고해 밝은 사이드바, 프로젝트별 채팅, 넓은 대화 영역, 하단 입력창과 접이식 오른쪽 패널로 구성했습니다. 전체 Codex 기능과 서비스가 동일한 제품은 아닙니다.

## Windows 다운로드 및 실행

[0.6.1 릴리스](https://github.com/jhham0322/HermesDevDesk/releases/tag/v0.6.1)에서 **Setup.exe** 또는 **Portable.zip**을 받으세요. GitHub의 자동 생성 Source code ZIP은 소스이며 실행 패키지가 아닙니다.

- 설치: `HermesDevDesk-0.6.1-win-x64-Setup.exe` 실행.
- 설치 없이 실행: `HermesDevDesk-0.6.1-win-x64-Portable.zip` 전체 압축 해제 후 `HermesDevDesk.exe` 실행. EXE만 따로 복사하면 실행되지 않습니다.
- Windows 10/11 x64. .NET 10.0.12 Desktop 런타임과 공식 Codex CLI 0.162.1을 포함합니다. 브라우저 패널은 Microsoft Edge WebView2 Runtime이 필요합니다. 자동 설치하지 않습니다.
- 설치본은 코드 서명되지 않았습니다. 배포 파일의 SHA-256은 같은 릴리스의 `SHA256SUMS.txt`에서 확인할 수 있습니다.

## 시작

1. 프로젝트 추가로 실제 작업 폴더를 선택합니다.
2. 설정에서 회사 API 주소/배포명/인증을 입력하거나 기존 ChatGPT CLI 연결을 명시적으로 선택합니다. 회사 모드에서 공개 OpenAI 모델 API로 자동 전환하지 않습니다.
3. 파일 메뉴의 엔진 연결을 실행합니다. 새 채팅에서 모델과 권한을 선택하고 지시를 보냅니다.
4. 실행 중 추가 지시와 중단을 사용할 수 있습니다. 승인 요청은 현재 요청의 명령과 경로를 검토한 뒤 처리합니다.

## Git

모든 Git 작업은 **PC의 로컬 git.exe**를 사용합니다. GitHub API가 필요하지 않습니다.

- 파일 → 로컬 Git 및 원격 서버에서 PATH의 `git` 또는 설치된 `git.exe` 경로를 지정합니다.
- Git 변경 패널에서 선택 파일 스테이징/취소와 로컬 커밋을 실행할 수 있습니다.
- `origin` 등 원격 이름과 `https://server/repo.git`, `git@server:repo.git`, `ssh://git@server/repo.git` 또는 로컬 bare 저장소의 절대 폴더를 설정할 수 있습니다.
- Fetch와 현재 branch Push를 제공합니다. 원격 인증은 사용자의 Git Credential Manager/SSH 설정을 사용합니다. URL에 토큰이나 비밀번호를 넣지 않습니다. 자동 Pull/force push는 하지 않습니다.
- 프로젝트 밖 경로, 비밀 파일, 링크 경로는 UI의 스테이징과 커밋에서 제외합니다.

## 대화와 작업

- 프로젝트별 채팅 목록, 고정, 이름 변경, 제목/대화 검색, 보관/복원, 분기.
- 사용자/에이전트 메시지, Markdown 기본 서식과 코드 블록 복사, 활동 펼치기, 파일 링크 미리보기, 대화 Markdown 내보내기.
- 채팅별 입력 초안 보존, 앞/뒤 탐색, 밝은/어두운 테마, 패널 접기, 명령 팔레트.
- 공식 `model/list`가 제공한 모델과 추론 수준 선택. 실행 중 `turn/steer`, 중단/재개, 요청별 승인.
- 회사 Responses SSE는 실시간으로 전달합니다. Chat Completions 호환 응답은 완료 후 표시합니다.
- 파일/실제 Git diff/작업 변경/worktree/AI 검토, PNG/JPEG 입력, 프로젝트 스킬, 프로젝트 로컬 MCP, 관측한 사용량, Windows sandbox 명령.
- 예약은 앱 실행 중 초안/알림입니다. 자동 AI 실행으로 표시하지 않습니다.

## 브라우저

왼쪽 브라우저 버튼 또는 명령 팔레트에서 브라우저 패널을 엽니다. 주소를 입력하면 실제 WebView2 페이지를 표시합니다. 앱 전용 프로필이며 기존 Chrome/Edge의 로그인 정보를 가져오지 않습니다.

프로젝트에서 **새 채팅에서 브라우저 도구 사용**을 켜면 새 thread에 `hermes_browser`를 연결합니다. 모델의 탐색/페이지 읽기/클릭/텍스트 입력 요청은 동작을 검토한 뒤 실행합니다. 비밀번호/파일 입력, 자동 다운로드, 카메라/마이크/위치 권한과 임의 JavaScript 도구는 제공하지 않습니다. 실제 웹 사용과 데이터 전송은 사용자가 검토합니다.

## 성능

스트리밍 화면 갱신을 최대 초당 10회로 묶고, 기존 메시지와 사이드바를 재사용합니다. 진행 중 텍스트는 한 컨트롤에서 갱신하고 완료 후 서식을 적용합니다. 스트림 이력은 250ms 단위로 묶어 저장하고 상태 전환/종료 시 최종 저장합니다.

격리된 테스트에서 2,000개 delta를 1회 스냅샷 저장으로 처리했고 20,000자 모두 보존했습니다. 이 수치는 테스트 장비의 결과이며 모든 PC에서 같은 응답 시간을 보장하지 않습니다.

## 단축키

| 키 | 동작 |
|---|---|
| Ctrl+N | 새 채팅 |
| Ctrl+K | 명령 팔레트 |
| Ctrl+F | 채팅 검색 |
| Ctrl+B | 사이드바 |
| Ctrl+J | 오른쪽 패널 |
| Enter / Shift+Enter | 전송 / 줄바꿈 (보기 메뉴에서 Enter 전송 변경) |

## 데이터 및 현재 범위

앱 데이터는 `%LOCALAPPDATA%\HermesDevDesk\0.6.1`에 저장합니다. 회사 키는 현재 Windows 사용자 DPAPI로 보호합니다. 기존 Codex 인증파일/비공개 DB/세션을 직접 가져오지 않습니다. 이력과 초안은 평문 JSON이므로 임의 비밀 문자열을 모두 판별할 수는 없습니다. 키는 설정 화면에서 입력하세요.

아직 연결되지 않은 기능: 전체 데스크톱 컴퓨터 제어, 음성/Realtime, 이미지 생성, 외부 MCP/OAuth 통합, 플러그인 스토어, 공식 클라우드/원격 모바일/프로젝트 동기화, Space/문서 아티팩트/Pets, 무인 자동 AI 실행. 디자인도 공식 앱의 모든 화면을 1:1 복제한 것은 아닙니다. 앱의 도움말 → 기능 및 연결 상태 및 `PLAN.md`에서 상태를 확인할 수 있습니다.

## 개발 및 빌드

.NET 10 SDK, Windows, 로컬 Git이 필요합니다.

```powershell
dotnet build HermesDevDesk.sln -c Release
dotnet run --project tests/Hermes.Tests -c Release
powershell -File scripts/fetch-engine.ps1
pwsh -File scripts/build.ps1 -DotnetPath 'C:\path\dotnet.exe'
```

빌드 스크립트는 Setup.exe, Portable.zip, app 실행 폴더를 함께 생성합니다. 기존 산출물을 덮어쓰지 않습니다. 다시 빌드하려면 `-OutputDirectory artifacts/build-새이름`을 지정하세요. 선택적인 7z 추가 압축은 `-CompressInstaller`로 실행합니다.

일반 테스트는 로컬 synthetic fixture만 사용합니다. `--desktop-engine-smoke`는 실제 공개 엔진과 로컬 가짜 provider로 브라우저 도구/추가 지시 계약을 검증합니다. `--live-smoke`/`--tool-smoke`는 실제 모델 사용량이 발생하므로 별도 의도 없이 실행하지 마세요.

자체 코드 MIT, 공식 CLI Apache-2.0, .NET/WebView2/Fluent 아이콘의 라이선스와 notices를 배포 패키지에 포함합니다. 공식 CLI 바이너리는 수정하지 않았습니다.