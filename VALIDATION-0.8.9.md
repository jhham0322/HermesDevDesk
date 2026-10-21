# 0.8.9 validation

- Release solution build: zero warnings/errors (.NET SDK 10.0.401).
- 236 synthetic regression checks passed. Includes real loopback HTTP model-name/URL/token routing A/B/A, per-model DPAPI and JSON round-trip, rename/delete/restart, malformed import preservation.
- WPF model settings: 9 checks passed, screenshots inspected.
- WPF long conversation: 5,001 messages, at most 40 message views, retained UI objects on append and stream delta, oldest/latest navigation, 1,000 paragraphs in one text control. Initial layout 332–565ms and append layout 7–8ms on this machine; this is a local measurement, not a universal latency guarantee.
- Official bundled engine: same chat A/B/A across different model endpoints/tokens completed, including full-access mode. Synthetic upstreams only; no real company endpoint or paid model invoked.
- Hosted vLLM regression: a /v1 base with Messages HTTP 400 Hosted_vllmException resolves Chat Completions; a Claude-compatible base resolves Messages first; complete inference URLs never gain duplicate suffixes; stale basic Responses settings are rechecked; nested Unicode and rejected parameter diagnostics remain readable.
- Stream persistence batches use 2 seconds; completion and normal shutdown flush state. A process crash may lose the most recent unflushed stream delta.

Run the console test executable from a directory outside the repository, because Git boundary tests intentionally reject parent repositories. UI checks use isolated --data-dir paths and synthetic tokens only.

- Installer: 465 embedded file SHA256 checks passed without installing; public payload scan checked 466 files and 177 tracked source files, with zero issues.
