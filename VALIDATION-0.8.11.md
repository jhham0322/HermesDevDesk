# 0.8.11 verification

- Reproduced strict text-only gateway failure through the official engine with the previous adapter: HTTP 400 for array-form text content. After the request normalization the same fixture completed.
- Official engine Chat streaming test: function-call arguments split over SSE chunks, correlated browser tool result, a visible first answer chunk before releasing the final chunk, and completed final answer all passed.
- Regression tests cover CRLF/[DONE], hidden reasoning exclusion, truncated-stream rejection, basic one-token reasoning-only probe, human-readable nested JSON errors, parallel tool-call grouping and request field compatibility.
- CompanyBridge no longer uses a four-minute HttpClient timeout for buffered Chat completion. Streaming Chat content is forwarded live; generation cancellation is bounded at ten minutes and Chat stream idle reads at two minutes.
- Installer uses a stable App directory. In-place reinstallation removes stale/modified files from the owned installation while preserving history outside the installation directory. New payload is validated before replacing the old folder. Ownership and reparse-point guards remain.
- Server outage was not established. The user later observed successful execution without this update; transient gateway latency/queues remain possible. No real company endpoint or paid model was called in tests.

- Browser library regression checks cover deduplication, bookmark toggling, callback credential exclusion and bounded history. Full regression suite: 248 checks passed.
- Owned WebView2 page fixture checks persisted global/per-chat last URL, bookmark title, history, DOM selection, form-value exclusion and explicit source attachment (9 checks). Native password autosave/autofill profile flags are verified; actual login/password save was not tested. No passwords are read by application code.

- Final local installer SHA256: C8C9DF137ABB3D5DC05517E031C091D7492E0EC6D045D789D1924A7213367DA1. Installed validation passed: fixed target, legacy version cleanup, clean replacement of modified/stale installed files, external data preserved, executable/DLL versions, 16 model UI checks, long-conversation check, 9 browser checks including disposal/new-view last-page restoration.
- Reinstall fixture exposed the legacy .NET Framework path limit in the longer staging path. Installer now targets Framework 4.8 and enables long-path handling with an OS manifest; the same long-path fixture passed after the fix.
