# 0.8.32 source and execution validation

## Scope and behavior

Public progress and answer provenance is stored separately from original message text. Labels show the response `model` observed from the selected company/local API; this is server-reported metadata, not independent proof of its internal backend. Requested aliases are shown separately. Missing metadata remains unknown, including legacy messages. Request identities prevent a delayed old response or a model switch from relabeling later messages. Host reports use Hermes; tool events retain their tool origin. Text tags do not grant completion or change application state. No private reasoning is extracted.

The saved, default-on progress toggle hides public summaries and host reports without deleting transcript entries or answers. Streaming preserves the existing text control. Reports contain observed work, unverified remaining work and next action; within-turn tool-result reports are coalesced. A completed tool operation is distinct from overall objective verification. A model completion report with a valid execution evidence reference is labeled as a verification/report receipt; the host checks evidence provenance, not the business result independently. Observed errors are marked with unknown cause unless independently established.

Automatic completion loops now take a settings snapshot for each stage: default 3 rounds, 10 minutes, 20 host-tool starts and 2 consecutive rounds without a new successful evidence/file-change digest. The explicit long-work preset is 12 rounds, 60 minutes and 100 starts; limits are editable within validated ranges. Budgets are checked before new managed command/browser/computer calls and automatic turns. Already-started work is not terminated on a timer. MCP starts are observed and counted for subsequent boundaries, rather than claimed as pre-execution admission control. This is bounded continuation and admission control, not a strict wall-clock limit on a silent server or one active engine turn.

Identical successful evidence and heartbeat/prose-only activity do not reset the stagnation digest. This conservative signal cannot prove semantic progress. Budget pauses preserve transcript, changed files and an explicit checkpoint; they never report completion. The process panel supports explicit stage-budget renewal and resume. Reconnecting restores the original engine thread and task-scoped bridge route before resuming. Existing provider/root/policy checks remain in force. Restart never automatically resumes a process.

Usage metrics store header latency, first useful public output/tool arguments, total elapsed milliseconds and buffered-response status. Process metrics aggregate managed tool queue/processing and automatic-turn delay. No request body, token, prompt or output is added to these metrics. Heartbeats do not count as useful output. User cancellation now reaches the company request before waiting for engine interruption.

## Local checks

- Release solution build: zero warnings/errors; `artifacts/execution-build.log`.
- Synthetic regression: 368 checks; `artifacts/execution-regression.log`. Includes missing model metadata, alias mismatch, delayed response, model switch, immutable completion metadata, untrusted stage tags, tool/host origin, state migration, time/round/tool budgets, stagnation, checkpoint persistence and buffered/stream timing.
- Official bundled engine with local synthetic company API: existing completion/recovery/RAG, queued and active stop and external blocker tests; `artifacts/execution-engine.log`.
- Separate official engine processes: budget pause followed by explicit resume preserves original thread, per-stage round limit, partial result and observed model origin; `artifacts/budget-resume-engine.log`.
- Actual isolated WPF controls: 13 report-visibility assertions plus 8 source/budget assertions; `artifacts/execution-ui-reviewed/execution-ui.json` and `progress-visibility.json`. Includes source label, request alias, live text-control retention, budget checkpoint, report hide/show, settings dialog persistence and inspector details. Screenshot `progress-execution-sources.png` was visually inspected.
- Additional WPF runs: separate-process preference restart, existing 5,001-message bounded paging (7 checks) and process/error-RAG panel (5 checks); `artifacts/execution-ui-verified/`.
- `git diff --check` is clean. Source and existing toggle changes were checkpointed before implementation under ignored artifacts.

All model/API checks above use owned loopback fixtures with zero actual company model calls. No performance improvement percentage is claimed. No user application was force-stopped or automatically installed. Final packaging, payload hashes and remote publication are recorded separately after producing the tested source commit.

## Release route

Version manifests, installer and visible product heading are 0.8.32. The existing destination is origin main and tag v0.8.32 in jhham0322/HermesDevDesk. The versioned workflow rebuilds the exact tag, reruns regression and engine/UI checks, verifies installer hashes/public payload, runs owned reinstall fixtures, and publishes the EXE and portable ZIP. Existing package integrity and licenses are preserved. No new host or authentication method is introduced.
