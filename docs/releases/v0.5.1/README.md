# Hermes DevDesk 0.5.1 deployment status

Checked 2026-10-14 06:07 UTC: main had the source commit `5448396b9e48323fda96414bcd90d34ce9c2defc` and 54 source/document files; GitHub Releases was empty. The runnable installer and portable ZIP were built and tested locally, but have **not** been uploaded to GitHub. These documentation files do not contain the binary downloads. Do not interpret the source archive as an installable app.

## Prepared binary assets

| File | Bytes | Contents |
|---|---:|---|
| HermesDevDesk-0.5.1-win-x64-Setup.exe | 232097280 | Per-user unsigned Windows x64 installer, app + .NET/WPF + official engine |
| HermesDevDesk-0.5.1-win-x64-Portable.zip | 232072784 | Entire runnable folder, all 457 files; extract all and run HermesDevDesk.exe |

SHA256 values are in [SHA256SUMS.txt](SHA256SUMS.txt). [Release notes and usage](RELEASE-NOTES.md) describe install/data paths, limitations, verification and removal. [Verification report](RELEASE-VALIDATION.json) records the pinned source, licenses and tests. These are the exact prepared release documents, not a claim that the binaries are already attached here.

## Build from the source tag

Use an approved Windows x64 development environment with .NET 10 SDK. The Git repository intentionally excludes local tools, runtime caches, credentials and build output. Clone this repository and select existing tag `v0.5.1` (the source commit above). From that folder, run:

```powershell
dotnet build HermesDevDesk.sln -c Release
.\scripts\fetch-engine.ps1
.\scripts\build.ps1 -DotnetPath 'ABSOLUTE_PATH_TO_APPROVED_DOTNET_SDK\dotnet.exe'
```

The engine fetcher uses the official npm registry with a pinned SHA512 integrity check and retains license/NOTICE files. The standard script produces a stored-ZIP installer. The prepared download packages use Deflate compression, omit local PDBs, map build source paths and include release provenance/read-first text; identical byte hashes are not promised for a fresh standard-script build. Runtime functionality is pinned to the same source. Do not run `--live-smoke` / `--tool-smoke` without separate approval for actual model usage.

## What is excluded and why

User/company keys, DPAPI files, account profiles, existing Codex auth/sessions, company endpoints, test state, raw logs/screenshots, SDK/NuGet caches, local PDBs and files from other projects are excluded. The runtime and official engine required to run the app are in the prepared packages. Test executables and development caches are not runtime prerequisites. Git remains a separate prerequisite for Git features.

## Completing binary publication

The current connection can write source documents but has no GitHub Release create/asset upload action. `gh` is unavailable, and the normal browser tool cannot start (`0x80070003`). No tokens/cookies were extracted, new credentials/workflows created, security settings changed or control restrictions bypassed.

An existing approved authenticated GitHub UI/CLI can publish a release on tag `v0.5.1`, attaching Setup.exe, Portable.zip, RELEASE-NOTES.md, RELEASE-VALIDATION.json and SHA256SUMS.txt. Preserve any existing release/assets and compare their hashes before adding anything. Do not push the large packages into source history. Verify the actual published release and downloads before calling binary delivery complete.

Actual company Azure authentication/API/proxy/TLS and native mouse/keyboard interaction are unverified. No external model fallback or paid model request was used in the local tests. A separate owner request to restore private visibility at 19:00 KST (10:00 UTC) today remains in effect; this documentation update does not change visibility or schedules.
