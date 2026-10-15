# HermesDevDesk 0.7.2 source

Explicit HTTP/HTTPS company gateways are supported. HTTP is unencrypted; scheme-less addresses require an explicit choice. See [0.7.2 changes](CHANGELOG-0.7.2.md). Existing public downloads below are v0.7.1 until a new binary release is published.

Independent Windows development desktop with selected-window Computer Use and an in-app WebView2 DOM browser.

**Experimental release: native input success and real-model end-to-end were not validated in the audit environment.**

See [release notes](RELEASE-NOTES.md), [validation](VALIDATION.md), [token analysis](TOKEN-ANALYSIS.md), and [local acceptance test guide](LOCAL-VALIDATION.md).

Download the Windows Setup or Portable ZIP from [v0.7.1 releases](https://github.com/jhham0322/HermesDevDesk/releases/tag/v0.7.1). Source ZIP is not the runnable package. The previous v0.7.0 release is preserved.

Use the Computer panel to select allowed open windows, choose reviewed or session-scoped automatic mode, and arm the session. Ctrl+Alt+F12 or the stop button revokes the session. Auto mode is not blanket authorization for authentication, deletion, payments, external sending or protected resources. Screen content is sent only through the explicitly connected model/tool path; protected/blank captures fail closed.

Build on Windows with the .NET 10 SDK: `scripts/fetch-engine.ps1`, then `scripts/build.ps1 -DotnetPath <dotnet.exe>`. No user settings, credentials, screenshots or browser profiles are part of the source or packages.
