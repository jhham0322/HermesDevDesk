# HermesDevDesk 0.7.1 — Windows x64 (experimental)

Adds a browser-independent selected-window Computer Use path with local change detection.

- `hermes_computer.wait_for_change`: 300ms local captures, previous/last-delivered comparison, cursor coalescing, timeout/cancellation. Only a changed latest frame is returned; intermediate frames are not accumulated in files or model context.
- Explicit session-scoped automatic mode for general interactions in selected allowed windows; revocation/emergency stop and sensitive/unknown-target review remain.
- Multi-process browser UI Automation descendants are scoped to the selected top-level window. No browser profile/cookie extraction or forced new browser session.
- Protected/black captures fail closed and revoke automatic capture, without DRM bypass or alternative capture fallback.
- Metadata-only audit and capture counters; cached/uncached input and reasoning/output accounting are distinct. No measured token-saving ratio.

Validation: build 0 warnings/0 errors; 41 policy/change/coordinate logic checks; 6 actual owned-WPF native capture/change-wait checks; 9 programmatic WPF panel checks with a synthetic task/delivery acknowledgement. New tests saved zero screenshot files and made zero real model calls.

Important limits: actual native input success was 0 in this audit. The low-integrity AppContainer background launcher was denied foreground activation. Real-model + approval UI + native input end-to-end, external browser input, and enterprise DRM environments remain unverified. Native capture tests are not a successful real-model benchmark. Windows foreground/UAC/secure desktop/enterprise restrictions are not bypassed. A normal foreground-eligible user host is an external prerequisite, not a guarantee of success.

Download Setup.exe to install, or extract Portable.zip and run HermesDevDesk.exe. Existing 0.7.0 app/data are preserved by version-specific paths. Includes the .NET 10 Windows desktop runtime and the unmodified official Codex CLI 0.162.1. WebView2 Runtime is required only for the in-app DOM browser. No installed browser is added or reconfigured.
