# HermesDevDesk 0.7.3 (experimental)

Includes explicit HTTP company gateway support from commit 64b4e84 and a separate maximized-window taskbar fix.

- HTTP or HTTPS must be explicit, or selected explicitly when entering a scheme-less URL. Port and path are preserved. HTTP displays an unencrypted transport warning. HTTPS certificate validation and no-downgrade behavior remain enabled.
- Maximized borderless windows now use the current monitor's physical work area, including negative monitor origins and DPI changes. Auto-hide activation strips are reserved without changing taskbar settings. Restore bounds and normal z-order are retained.
- Validation: HTTP localhost fake-key tests 30 plus existing company checks 31; settings UI 6; maximize geometry 144; actual owned WPF window 24 checks on four monitors (96/144 DPI), including before/after bounds and restore. No OS settings changed.
- Computer Use remains experimental. Historical native capture/change tests passed, but real native input and a real model-to-tool execution loop remain unverified in this environment. No real company gateway, API key, or model was used in these tests. Auto-hide and other taskbar edges were tested in geometry logic, not by changing the user's taskbar.
- Setup payload verification does not install the app. Existing versions and v0.7.1 remain available. Packages contain no user settings, credentials, company endpoints, or test screenshots.
