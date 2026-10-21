# 0.8.13 verification

- Full synthetic regression suite: 266 checks. SQLite tests cover persistent repeat-error detection, changed-method execution, Korean similar-query retrieval, project isolation, valid execution evidence for revisions, failed-guide reliability, automatic improvement, deduplication, deletion and credential masking.
- Official engine with a local Chat fixture: two failed PowerShell commands execute; the third identical call is blocked before execution; the changed command succeeds; the agent searches and updates the guide with a real evidence ID; a separate chat automatically receives the improved guide. A read-only command cannot create a file.
- Official engine tool fixture: browser and computer callbacks each reject two repeated calls; third calls are blocked; a changed computer observation succeeds. Memory stores semantic targets without historical coordinates/window IDs/snapshot IDs. Business procedure and the model final report are collected automatically with an explicit unverified-goal label.
- WPF learning panel checks: project method listing, detail display, persistent improvement and actual SQLite retrieval (4 checks).
- Retrieval is local lexical RAG (FTS5/BM25 plus Korean bigrams), injected on chat start and steering, with on-demand model tools. No external embedding/model service is required. Historical data does not grant execution permissions.
- Command/browser/computer host operations serialize per task. MCP host has its own serialized repeat-error check and SQLite audit. Native shell tools are disabled for learning-enabled task threads so shell commands use the checked execution path. Existing task engine sessions migrate once, retain app transcripts, and supply recent user/assistant context.
- Retry state persists per task and resets naturally in a new task; prior failed methods remain retrievable. Tool host success and completed model turns are not proof of the real business outcome. Computer and browser outcomes beyond the owned fixture are not claimed.

- Real local MCP subprocess test: two failed calls recorded, third repeated call denied, changed successful operation indexed in the shared RAG (3 checks).

- Browser smoke: 12 checks, including actual backward/forward navigation and enabled native WebView2 developer tools with a visible toolbar entry. Actual password login/save remains untested.
- Initial CI command fixture used a 30-second command budget and 90-second total budget. Clean Windows Server PowerShell launches took about 22-30 seconds, producing a timeout instead of the intended first exit-7 result and making the repeated-error assertion inconsistent. Fixture commands now explicitly allow 90 seconds and the whole scenario 180 seconds; application retry criteria are unchanged.
