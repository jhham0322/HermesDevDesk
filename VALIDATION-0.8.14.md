# 0.8.14 verification

- Six core URL checks cover bare localhost and IPv4/IPv6 loopback normalization, explicit HTTPS preservation, exact-port/path/query HTTP alternative, exclusion of external/LAN/hostname suffixes, and credential rejection. Full regression target: 272 checks.
- Native WebView2 test uses two app-owned loopback listeners: plaintext HTTP and TLS with a generated self-signed certificate. Actual HTTP navigation, HTTPS-to-HTTP protocol-error recovery, remembered working address, development certificate acceptance, cache clearing when disabled and preference persistence passed (8 checks, including agent navigation awaiting recovery).
- No root certificates are installed. No public network endpoints, real SSO accounts, credentials or policy changes are used by the new tests.
- Local certificate handling is limited to literal localhost/loopback IP URLs. Revoked certificate errors are excluded. Native certificate validation outside this scope is unchanged.
- Local HTTP retry runs once per navigation attempt, preserves port/path/query, excludes certificate errors and cancellation, and is controlled by the saved development-mode preference. Explicit HTTP action is available in the browser UI.
- SSO authorization bypass was requested and declined. The user confirmed the same account is denied in external browsers too. No SSO access-control bypass is implemented.

- Agent browser navigation waits for local HTTP retry or accepted certificate to finish; initial TLS error events are not incorrectly delivered as final tool failure. Automatic retry applies to non-default local HTTPS developer ports.
