# 0.8.35 command details default collapsed

Command execution, command output and command results now start collapsed; their existing source and running/completed/failed summary headers remain visible. Only command entries change their default. The global progress-report visibility preference and all non-command expansion defaults are preserved. Detail Markdown remains lazy while collapsed. No original command, result or output is deleted.

Explicit command expansion/collapse is remembered in memory per conversation and stable item identity (legacy timestamp/kind fallback), surviving streamed output, tool status changes, paging and conversation switches. It is not a new persisted global setting: a fresh application window reopening saved history starts with the requested collapsed default. Managed command result entries reuse existing source/stage fields to show the observed result status in the folded header, without modifying original text or execution behavior.

The isolated WPF command-details check covers new and legacy entries, long output, running/success/failure headers, explicit expansion, repeated error updates, stream updates, the independent report toggle, paging, cross-chat isolation, fresh-window saved-history reopen and raw data preservation. No actual commands or company model calls are made by this fixture. Existing synthetic regression, provenance/budget UI and long-conversation checks remain part of validation.

Version 0.8.35 retains the pinned SDK and actual-deps runtime license lookup proven in 0.8.34. Existing installer manifest/public-file checks, owned reinstall fixtures and tag-based GitHub release workflow are retained. Final package metadata and exact-commit CI/download results are recorded in ignored artifacts. No user app is automatically installed or force-stopped.

Codex project-list recovery is a separate unresolved task; supported metadata backups and historical findings reside in the delegated workspace. This change does not edit Codex registrations, folders, conversations or settings.

Local validation: Release build zero warnings/errors; 368 synthetic regression checks; 15 command-detail WPF checks; existing provenance/budget/report-visibility WPF checks and 7 long-conversation checks passed. The collapsed-default screenshot was visually inspected. Reports are under artifacts/release-0.8.35-ui and artifacts/release-0.8.35-regression.log.
