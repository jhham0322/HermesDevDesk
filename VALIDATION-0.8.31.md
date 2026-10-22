# 0.8.31 validation

## Change

Office startup detection and automatic loading of registered COM/VSTO and application-native add-ins. A current-user login entry starts a separate monitor, which also continues after the Hermes window closes. An Office process is matched by PID and start time before attachment. NativeOM, active COM object and ROT discovery support multiple instances. No new Office process is created.

Excel uses Installed plus IsOpen, RegisterXLL and Workbooks.Open where needed: an installed-but-unopened add-in is not reported as loaded merely because it is registered. Word uses Installed; PowerPoint uses Loaded; COM/VSTO uses Connect. Office trust settings, signatures and organizational policies are not modified. Web task-pane add-ins and unregistered DLLs are outside this loader's API scope.

The Office tab displays module results, persistent searchable error RAG and start/stop controls. One failed module does not block later modules. Failed module calls are not repeatedly replayed within the same Office process. Startup attachment is retried with backoff. An isolated helper has a 30-second response limit. Stopping persists across login and does not unload modules already in use.

## Local checks

- Release solution build: zero warnings/errors (`artifacts/office-build.log`).
- Regression: 341 checks passed (`artifacts/office-regression.log`).
- WPF control/event checks: 100 passed (`artifacts/office-ui/ui-smoke.txt`).
- Office adapters and monitor: 28 checks (`artifacts/office-addin-report.json`). Includes independent module failure handling, actual Windows callback marshalling, method/property COM collection compatibility, installed-but-unopened XLL/XLAM and rejected-Installed fallbacks, separate monitor process, singleton, stop, and persisted stop. These checks use owned fake Office object models and an isolated monitor with Office scanning disabled.
- Local installer: 474 embedded files verified by SHA256. Public payload/source scanner: no issues.
- Actual forced clean reinstall: app/file holders stopped, unrelated process preserved, read-only/stale files removed, ownership repair and external history preservation passed (`artifacts/office-reinstall.log`).

Real Office tests created and closed owned empty Word and Excel instances, without accessing user documents. Word: native attachment and all 3 registered COM add-ins passed (`artifacts/office-live-word-2.json`). Excel: native attachment, 7 loaded/already-connected modules including XLL and XLAM; 2 system-installed COM add-ins were rejected by Office because they require administrator rights (`artifacts/office-live-excel-alternate.json`). EUROTOOL's Installed setter failed, and the direct load fallback succeeded. These environment restrictions are reported, not claimed as successful loads. An existing PowerPoint process was detected, so a live PowerPoint test was skipped; its object-model adapter is covered synthetically. Login registry installation is implemented in the production installer; fixture tests deliberately avoid altering the user's live startup settings.

## Release

Version manifests, installer, README download links and release notes are 0.8.31. The tag workflow rebuilds tagged source, reruns regression/engine checks, Office adapter/monitor checks, process/browser UI checks, installer integrity/public-file checks and forced reinstall before publishing the versioned EXE and portable ZIP. Published assets are verified separately after the workflow completes.
