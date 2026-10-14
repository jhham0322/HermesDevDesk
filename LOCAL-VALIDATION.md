# Computer Use local improvement

This release originated from an isolated source copy of v0.7.0 commit `2e5096b2df8dc4118f12e4475af3f7a512cd7dbc`, labelled `0.7.1`. It is an experimental release; it is not evidence of successful real-model/native-input end-to-end validation. Original source, packages and user settings were preserved. The default data directory is separate; test commands use an explicit isolated data directory.

## Implemented

- Native worker records foreground delegation, actual target foreground, interactive session, input desktop match, DPI, request outcomes, image dimensions/bytes and actual `SendInput` event count. No bypass through fake Alt keys, thread-input attachment, desktop switching or elevation.
- Owned native self-test has an explicit `--approve-owned-test-start` option. It inspects the actual button and requests a native click through the normal `ComputerWorkerClient`, rather than creating approval signals or relying on a separate input helper. Without that option, the original manual start gate remains.
- Coordinate mapping uses the actual returned image dimensions and client pixel dimensions, rather than guessing a scaled image's rounding. Native tests cover successful input, Unicode, shortcuts, wheel, drag, resized/wrong windows, cancellation and worker stop when Windows permits foreground. Their successful execution remains blocked in this environment.
- `hermes_computer` adds `wait_for_change(windowId, timeoutMs)`, with timeout 300–30000ms. Its dedicated local worker captures the selected identity-validated client area on a nominal 300ms cadence. Slow captures cannot overlap; capture processing may extend the cadence. No model API is called from this worker.
- Local comparison uses a fixed 64×40 brightness sample, threshold 12/255 and changed fraction 1%. This is a pixel heuristic, not semantic understanding; a small change outside the sample can be missed. Previous observed and last successfully returned delivery baselines are separate. Small per-tick changes accumulate against the last delivered frame.
- Cursor positions are sampled only inside the selected client area. Continuous motion and animation use 300ms debounce, 1000ms maximum coalescing wait and at most one change notification per second. A fixed number of current/baseline buffers are replaced in memory. Progress IPC replaces counters; it does not queue images.
- On change, one latest image plus metadata returns through the app's existing dynamic-tool response. Timeout/lifecycle results contain metadata without an image. Stop kills the capture worker, clears pending events/baselines/preview, and invalidates snapshots. New command, action completion, approvals and errors are independent lifecycle events. Disconnect and turn completion also stop capture.
- The engine can still perform inference after a timeout/tool response; “zero calls while unchanged” describes the local polling loop, not an unbounded guarantee about the connected model's later decisions.
- Session-scoped automatic mode applies to permitted general click/type/scroll and read/change-wait actions in the armed allowlist. Mode changes revoke the session. Unknown/sensitive click targets retain review; password/file/clipboard/global-shortcut restrictions remain. This does not authorize financial, destructive, external-send or authentication operations, or bypass OS/enterprise/platform approvals. The risk-label heuristic is not a complete semantic classifier.
- Existing top-level Windows-window selection is browser-independent; no Chrome/Edge-only restriction, forced profile creation or cookie extraction was added. Native screen control is separate from the in-app browser's DOM tools.
- Explicit display-affinity capture protection and all-black/blank client images fail closed, with no alternative capture API or infinite retry. Black content cannot reliably be identified as DRM; a legitimate black window also fails conservatively. No protected company document was tested.
- App audit history stores action/mode/count/size metadata, not screenshot bytes, UI text or typed content. Only changed images go to the model tool result. Underlying engine retention of delivered images has not been audited with a real model connection.

## Verification and limitations

See adjacent workspace logs and final validation reports. Logic tests are distinct from native capture tests, programmatic WPF-panel tests, synthetic delivery acknowledgement and real model reasoning.

The input test observed the same input desktop, session 1 and DPI 144, but `AllowSetForegroundWindow=false` and `SetForegroundWindow=false`. It safely sent zero input events. A foreground-eligible normal app host is still an external prerequisite; this audit did not lower Windows settings or ask the driving user to operate the PC.

Real model/API calls were not used. Existing approved model access could not be exercised through the normal app UI in this environment; credentials were not read/imported/configured. Real model+approval UI+native input end-to-end remains unverified.

Chrome, Edge and legacy IE executable paths exist locally. Their actual input behavior and Edge IE mode were not validated; the queried Edge IE-integration policy value was absent. Nothing enabled IE mode or changed company policy. No claim of universal browser/DRM support is made.

The SDK's first invocation printed a development-certificate installation notice. Subsequent builds explicitly disabled automatic development-certificate generation. The certificate store was not inspected or cleaned, to avoid reading/removing existing credentials. No model key, login or token was created.

## Reproduce without model calls

Use an existing .NET 10 SDK/runtime, and set process-local `DOTNET_ROOT` to its directory if required. Build each project individually with `MSBuildEnableWorkloadResolver=false`, `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`, telemetry disabled, and the existing NuGet package cache as source; no package install is needed.

```powershell
.\tests\Hermes.Tests\bin\Release\net10.0\Hermes.Tests.exe --computer-policy-only
.\src\Hermes.Desktop\bin\Release\net10.0-windows\HermesDevDesk.exe --computer-change-self-test '<new empty folder>'
.\src\Hermes.Desktop\bin\Release\net10.0-windows\HermesDevDesk.exe --computer-panel-smoke --data-dir '<new isolated folder>'
.\src\Hermes.Desktop\bin\Release\net10.0-windows\HermesDevDesk.exe --computer-self-test '<new empty folder>' --approve-owned-test-start
```

The last command performs actual native input only in its own fixtures when Windows permits it. The panel smoke uses programmatic control events and a synthetic running-task/delivery acknowledgement; it is not a real model/API test. Test commands close only their own fixture processes.
