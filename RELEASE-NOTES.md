# Hermes DevDesk 0.8.45

- Saves complete task results for later retrieval.
- Handles incomplete and prematurely terminated response streams without marking unfinished output as complete.
- Local handoff validation recorded 634 passing regression checks.

# Hermes DevDesk 0.8.44

Fixes misleading malformed-response errors for valid Chat length-limited empty output, preserves incomplete status, and blocks truncated tool calls from execution. Adds persistent next-day 09:30 KST recovery for explicit daily-token exhaustion only, with same-session/provider checks, Stop cancellation, durable single-claim admission and three-attempt limit.

The app must remain open and connected; when closed/offline, execution waits for the next valid connection. Provider reset time and successful real-account completion are not guaranteed or verified. The prior company 404 UI assertion, expanded-tool-card shell assertion and long-running continuation integration limitations are preserved, not reported as passed. No real company requests, account credentials, user GUI manipulation or installed settings changes were used for local verification.

See VALIDATION-0.8.44.md for exact checks and CI evidence. Existing releases and their assets are not overwritten.
