# 0.8.0 검증

2026-10-15 KST, Windows, .NET SDK 10.0.401 / 런타임 10.0.12 / 공식 CLI 0.162.1.

- 일반 회귀 136 checks: Anthropic URL/인증/메시지/지시/도구/이미지/SSE/usage/미완료/잘못된 도구/404/리다이렉트 차단/모델 보존/선택 모델 실제 전송 포함.
- 공식 CLI + 합성 Anthropic 서버: 실제 도구 카탈로그 변환, 컴퓨터 도구 및 이미지 결과 왕복, 대기 요청 interrupt 통과. 실제 PC 입력이나 실제 모델 추론을 의미하지 않습니다.
- UI와 패키지 최종 결과는 릴리스 RELEASE-VALIDATION.json 참조.

실제 회사 서버/키/모델 사용량은 사용하지 않았습니다. 기존 컴퓨터 제어는 실험 기능이며 실장치 성공률을 보장하지 않습니다.

최종 패키지: WPF 53 checks, 설정 UI 9 checks, 설치 파일 검증 및 격리 경로 추출 후 설정 UI 9 checks PASS. Setup/Portable 각각 465개 파일 SHA256 일치. 공개 payload 466 files 검사, 사용자 데이터/키/프로필/화면/PDB 및 민감 패턴 이슈 0.
