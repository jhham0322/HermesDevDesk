# 0.7.1 validation scope

Local acceptance: 41 logic checks, 6 actual owned-WPF capture/change-wait checks, and 9 WPF panel checks passed. Panel tests use programmatic UI events and a synthetic running task/delivery acknowledgement. All are separate from a real-model/provider test.

Native input: one start-button click request was refused before input; successful actual input events = 0. The test launcher had low integrity/AppContainer status, was not foreground, and was denied foreground delegation/activation. Session/input-desktop matching and DPI 144 were observed. No security restriction was bypassed.

No real model calls or paid benchmarks; provider token usage is unavailable. No actual external-browser input or company-protected document test. Black-frame owned-fixture tests passed conservatively without claiming DRM identification.

Release CI builds the same committed source, runs the isolated logic checks and verifies every embedded installer file by SHA256. It does not claim GUI/native-input/model validation on GitHub-hosted runners. Package assembly strips build PDB files and excludes settings, screenshots, browser profiles and credentials.
