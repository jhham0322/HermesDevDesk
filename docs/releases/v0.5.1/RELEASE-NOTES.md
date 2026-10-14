# Hermes DevDesk 0.5.1 — Windows x64

Runnable binaries built from commit `5448396b9e48323fda96414bcd90d34ce9c2defc`, matching existing tag `v0.5.1`. This version archives the existing 0.5 functionality; it does not claim parity with the official Codex desktop app. Hermes is an independent app.

## Choose one download

- **HermesDevDesk-0.5.1-win-x64-Setup.exe** — 232,097,280 bytes, about 221.35 MiB. Per-user installer. Select a new empty approved local folder, install, then run the installed `HermesDevDesk.exe`. Unsigned and asInvoker. No automatic login or model request.
- **HermesDevDesk-0.5.1-win-x64-Portable.zip** — 232,072,784 bytes, about 221.32 MiB. Extract the WHOLE archive to a new approved local folder, then run `HermesDevDesk.exe`. Do not copy only the EXE. Includes all 457 files: app, .NET/WPF 10.0.12 runtime, full official Codex CLI 0.162.1 vendor resources and licenses. Git is separate for Git features. Neither .NET SDK nor npm is required to run the app.
- **SHA256SUMS.txt** — verify downloads using an approved checksum tool before running.
- **RELEASE-VALIDATION.json** — build provenance, test coverage and verification limits.

Installer and portable contain the same 456 manifest-covered files plus the manifest. Compression, omitted local debug symbols and path mapping reduce delivery size without deleting engine/runtime dependencies. Installed payload is about 576 MiB; allow extra space for extraction and tests.

Portable describes delivery, not a no-footprint credential store. App data defaults to `%LOCALAPPDATA%/HermesDevDesk/0.5.1` in both modes. Use `--data-dir ABSOLUTE_NEW_APPROVED_PATH` for explicit isolated test data. Installer defaults to `%LOCALAPPDATA%/Programs/HermesDevDesk/0.5.1`. Use this same installer to remove hash-matching app files; modified files, history and keys are preserved. No recursive data deletion or automatic migration.

## Verified locally

Build from the pinned public source; 74 core/security/usage checks; 11 local MCP checks; 15 official-engine image/MCP/fork/review fixture checks; 26 skill/archive/draft lifecycle fixture checks; 9 installer install/start/repeat/conflict/cancel/recover/remove checks; 36 portable WPF programmatic control/event checks; all 456 embedded file hashes and all 457 extracted portable files. Official engine fixtures use local synthetic responses, not a company model. The installed WPF app was also launched by the installer self-test.

The complete license/NOTICE files are retained. No account credentials, company profiles/endpoints/keys, sessions, test data, local PDBs or raw build logs are in the packages. All published files were checked for known local username/build-path byte patterns, with no hits; this is not a universal secret detector.

## Not verified / limitations

Actual company Azure authentication, proxy/TLS and model/tool capability have NOT been tested. Native mouse/keyboard verification is blocked by the UI runtime (`0x80070003`); programmatic event tests are not native click tests. Configure only your approved CompanyApi endpoint/deployment/auth mode. Only the user enters an approved API key in the app. No automatic fallback to an external model API. Entra/SSO automatic login, remote computer control and external MCP installation/OAuth are not implemented. Follow your organization's Windows/download/execution policies; do not disable security controls to run an unsigned package.

The source README retains historical private-source wording; this release provides runnable binaries. Repository visibility can change independently. Making a repository private cannot revoke copies already downloaded while it was public.

This document and the assets are release-ready. Presence of these local files is not evidence that a GitHub Release has been published.
