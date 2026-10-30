# 0.8.36 full conversation titles

Baseline: clean main at fe352e056420f50ebe73aafabe3bd2bd49ff2262. No repository AGENTS.md or .agents skill directory was found in the inspected applicable repository/parent paths. Existing build, release and verification scripts retained.

The opened conversation header wraps its stored full title; a bounded preview expands into a scrollable full title. A separate copy button reads the current task's stored title, including line breaks. Conversation switching resets expansion; ordinary refresh preserves it. Sidebar and tab trimming remains unchanged. New initial titles retain the complete safety-redacted request rather than permanently truncating to 45 characters. Existing saved truncated titles are not reconstructed or migrated.

Validation: Release solution build succeeded with zero warnings/errors. 368 existing synthetic regression checks passed using the project SDK and an ordinary workspace outside the Git repository. Earlier environment-only attempts failed because the system apphost lacked the required runtime, nested Git fixtures inherited the repository, or AppData temp paths were rejected by the existing project safety guard; no guards or permissions were loosened.

45 isolated WPF title checks passed for long Korean, unbroken English, multiline titles, 900px window layout, repeated expansion/collapse, refresh, scrolling to the final line, full clipboard equality, saved-state reload, chat switching and rename. Clipboard contents were preserved and restored without logging. Screenshots were visually inspected. Existing command-details, execution UI, progress visibility/restart and long-conversation WPF checks also passed against the final self-contained test build. No real company model/API requests were made.

Version 0.8.36 uses the existing pinned SDK, runtime notices, installer manifest/public-file checks and version-specific tag workflow. Installer extraction and reinstallation checks target owned fixtures only. No user application is installed or force-stopped. Publication and download evidence are recorded separately under ignored artifacts after execution.
