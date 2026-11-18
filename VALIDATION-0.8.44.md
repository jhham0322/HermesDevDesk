# 0.8.44 validation

Baseline: clean local and remote main `2918a7d764726ab4e447215d810e96c21fc6a606` (0.8.43). Existing GitHub releases remain unchanged.

## Changes
- Empty Chat length-limited responses normalize to Responses incomplete/max_output_tokens; empty stop responses still fail. Incomplete tool calls never become executable completed output. Private reasoning is not displayed. Non-stream Chat telemetry now records actual incomplete status.
- Persistent next-day 09:30 KST scheduling for explicit failed daily-token quota only. Existing continuous tasks, provider/session identity, approvals and operation review are checked. Durable exclusive claim files survive process restart and avoid ambiguous duplicate admission. Stop cancels pending work; three automatic attempts maximum.

## Local results (2026-11-18)
- .NET 10 Core/Tests Release build: 0 warnings, 0 errors.
- 50 daily-quota checks passed, including KST boundaries, preserved checkpoint/work receipts, persistence, cancellation, no-engine deferral, concurrent claims (one winner), duplicate completion, repeat exhaustion and attempt ceiling.
- Full regression: 603 checks passed in a fresh non-repository/non-sensitive fixture directory. No real company endpoint or model key used.
- Desktop self-contained Windows package built. Installer /verify: 474 embedded files SHA256 verified. Public package scan: 475 payload files and 318 source files checked, issues empty. Local EXE SHA256: 73495312BA3D83D54103F01FFD586F3763762FF412E4BA80B6CF1655E802B1CC (CI rebuild will have its own hash/commit metadata).
- git diff --check passed.

Earlier test attempts were not passes: ordinary build was network-policy blocked; the approved build route succeeded. Running the full suite inside the repository failed its parent-Git guard; running it under AppData failed its sensitive-path guard. Both protections were kept unchanged and the final suite ran in a new non-sensitive fixture directory.

## Limits
No real daily provider reset, next-day production task completion, user account login or user desktop input was exercised. App and same valid connection must be available; no OS wake/service is installed. Explicit daily error identification deliberately excludes generic 429, billing quota, weekly usage, response-length, auth, permission and uncertain side effects. Existing manual/review-only tasks are not silently converted to continuous work.

Previously disclosed company 404 UI, expanded-default shell assertion and long-running continuation integration limitations remain; they are not newly verified by these local checks. Release CI retains prior diagnostics and will separately verify its own artifact. Local package success is not proof of GitHub publication.

Final admission review additionally checks Stop, session/model/provider/engine changes and pending approvals after asynchronous thread resume, before starting another turn. Correlated typed turn/start RPC rejections now preserve redacted code/type/message and can schedule explicit daily exhaustion. Generic exceptions, thread/resume rejection and first-request rejection with not-yet-admitted attachments/skills remain manual recovery cases. Actual RPC fixture reader and scheduling are included in tests.
