# HermesDevDesk 0.8.39 validation scope

Baseline: main 53167afa9a2a205f90a5cd1a0c6902187442338b. The v0.8.38 annotated tag is preserved. Incorporates verified diagnostic commits through 6533ec4c3d798921201287a1a2eec2f958def2aa; its separate CI 41066846866 passed.

Only test observation/deadline/receipt diagnostics and required version/release metadata change. Production command/RPC limits, execution policies, approvals, cancellation and provider limits are unchanged. No new feature or user installation is included.

Continuation uses distinct completed operation identities for progress, a 90-second idle guard and a 10-minute whole-fixture ceiling across restart. The ceiling was based on observed command intervals up to about 20 seconds for 27 commands plus headroom; the successful diagnostic CI subsequently observed intervals up to 29.757 seconds and completed the fixture in about 8m27s. Failed/unknown receipts stop immediately. Numeric exit codes and fixed error markers are logged without raw command/output/message data.

Retain 431 existing regression assertions, 35 deterministic virtual-time policy assertions, 8 diagnostic/privacy assertions, real official-engine/synthetic-provider continuation, budget-resume and task-loop checks. Preserve all 25 unique effects, zero replayed effects, additional requirement and same-thread automatic restart with three verified items. Release workflow retains payload/license/installer, isolated reinstall, browser, Office and existing WPF regression checks before publication.

Exact final-source and package/remote/download evidence is collected under ignored artifacts/release-0.8.39* and GitHub CI for the release tag. Earlier intermittent command failure did not recur in the successful diagnostic run; its actual exit code remains unknown. No claim of complete remediation or real company model validation is made.
