# Progress summary visibility — local validation

Source: versions/0.8.11, main, product project version 0.8.31. Work tree was clean before this change. No applicable AGENTS.md, project .agents/.codex instruction folder, Git lock, or running Hermes/dotnet process was found during preflight. No install, release, push, authentication, user-app shutdown or Office monitor startup was performed.

## Behavior

The composer now has a `진행 요약 표시` checkbox. It defaults to checked for new and older saved settings. DesktopPreferences.ShowProgressSummaries persists the global user choice. Changing it redraws the current conversation immediately and resets pagination to the latest page. Public progress summaries are excluded before paging while hidden. Raw transcript entries and assistant answers are preserved, including summaries arriving or completing while hidden. New and existing conversations use the same preference; restarting restores it. Default-visible rendering retains the existing transcript-list path.

## Evidence

- Release solution build: exit 0, zero warnings/errors. `artifacts/progress-visibility-build.log`.
- Existing regression: 341 total checks, exit 0. `artifacts/progress-visibility-regression-isolated.log`. Run the built Hermes.Tests.exe from a folder outside any Git repository and outside AppData. Initial DLL invocation could not self-launch the fixture; running under this repository passed 295 checks before the existing nested-repository fixture rejected its location. A temporary AppData location was also rejected by existing project safety rules. The final successful run used the delegated workspace; no production code was changed for those runner constraints.
- Actual WPF controls/event/rendering: 13 assertions, exit 0. `artifacts/progress-visibility-ui-verified/progress-visibility.json`. Includes streaming toggle, completion, old/new conversations, filtered paging, persistence of both choices, and intact answers/transcript.
- Separate application process restart: exit 0. `artifacts/progress-visibility-ui-verified/progress-restart.json`: hidden checkbox restored, 1 visible answer, all 87 original transcript entries retained.
- Existing conversation WPF performance: 7 assertions, exit 0. 5,001 messages, 40 realized controls, initial rendering 316ms, append 7ms on this machine. `artifacts/progress-visibility-ui-verified/performance/conversation-performance.json`.
- Both rendered screenshots were visually inspected: `artifacts/progress-visibility-ui-verified/progress-visible.png` and `progress-hidden.png`. Checkbox label is legible; hiding removes summaries and preserves the answer.
- `git diff --check`: clean.

These are isolated local tests using synthetic public summary events, with zero real model calls. No live upstream streaming session or installed production-app update was tested. Existing displayed version text was left unchanged. No remaining implementation blocker; changes remain uncommitted for review.