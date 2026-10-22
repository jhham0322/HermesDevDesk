# 0.8.24 작업 기록 도구 진행 표시 수정

원인: item/started가 dynamicToolCall과 hermes_memory라는 내부 유형/이름을 그대로 표시했고, item/completed에는 동적 도구의 완료/실패 표시 처리가 없었습니다. 이 문구 자체는 오류가 아닙니다. 기록 검색은 LearningInstructions에서 낯선 작업 전과 실패 후 사용하도록 안내합니다. 사용자가 본 개별 호출의 실제 실패 여부는 해당 결과 없이 단정하지 않습니다.

수정: 한국어 도구 설명, 항목 ID별 진행/완료 갱신, success=false 또는 failed 상태에서 반환 텍스트 원인 표시. 기존 검색/개선 실행 동작 유지. 앱/코어/설치 버전 0.8.24.

검증: Release 빌드 및 로컬 설치 교체 검사 통과. 회귀 316개 통과 (artifacts/tool-progress-0.8.24-verified-tests.log). 정상 검색 표시, 완료 시 중복 제거, completed이지만 success=false인 실제 실패 원인 표시 포함. 실제 회사 API 호출 없음.

검사 환경: dotnet run은 자체 프로세스 fixture와 맞지 않으며 저장소 내부 실행은 상위 Git 감지 검사에 걸립니다. 저장소 외부에서 빌드된 Hermes.Tests.exe로 실행하여 316개 모두 통과했습니다. 기존 빌드 스크립트와 같이 NuGetAudit=false로 빌드했습니다. 기본 감사는 기존 SQLitePCLRaw.lib.e_sqlite3 2.1.11 취약성 경고 NU1903로 막혔으며 이 변경에서 의존성은 수정하지 않았습니다.

산출물: artifacts/release-0.8.24-local/HermesDevDesk-0.8.24-win-x64-Setup.exe. 운영 설치는 변경하지 않았습니다.
