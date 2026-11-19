# 0.8.46 UI responsiveness validation

Baseline: remote main 164f600bd3d6a6cd4160a91649fa78f0387902db / release 0.8.45 88e726f1c306afe5b57be6a3da2deeaf01ee0310. An isolated worktree preserves the original dirty development tree.

Actual Windows WPF window, synthetic profiles, 64 synthetic registered secret strings, 20/2,000/10,000-message histories. Five warmed samples per phase. Before/after sources differ only in save-redaction caching. Input dispatch is measured by a DispatcherPriority.Input probe queued behind each normal-priority UI action; this is not physical keyboard/mouse timing. Both sides exercise open, text entry/scroll, stream update, save-only and save+UI refresh with identical fixtures.

| Messages | Save+UI input median before | After |
|---|---:|---:|
|20|3.29 ms|2.28 ms|
|2,000|32.28 ms|15.50 ms|
|10,000|167.34 ms|37.39 ms|

Long-chat opening: 109.57 -> 101.66 ms (no material improvement claimed). Ordinary input/scroll and stream refresh remained around 1–2 ms. Full-history JSON serialization and disk snapshot remain synchronous; this change reduces repeated redaction and allocations rather than claiming all UI latency eliminated.

Regression: 638 PASS. WPF existing conversation regression: 7 PASS, 40 realized views, retained live control, paging to oldest/latest and all saved history preserved. Real company/OAuth/model calls=0. No actual user installation changed. Existing known 404/shell assertion failures and continuation limitation are not hidden or fixed by this change.

Raw local evidence: artifacts/ui-response/before/ui-response-benchmark.json, after/ui-response-benchmark.json, regression.log, baseline-build.log, after-build.log. Independent source review should note Safety secret-registry revision is synchronized and the weak cache never survives restart; records are re-sanitized on edits/new registrations. Existing conversation lists are copied only when a redacted replacement is needed.