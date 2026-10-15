# 0.7.2 HTTP gateway support

- Explicit http:// and https:// company API base URLs are supported. HTTP is unencrypted; the settings screen warns that keys, requests and responses can be observed on the network.
- A URL without a scheme requires an explicit HTTP/HTTPS selection. Ports and base paths are preserved. A fully specified HTTPS URL is never rewritten to HTTP.
- No automatic HTTPS-to-HTTP downgrade, redirect following, public-provider fallback or TLS certificate-validation bypass was added.
- Local validation: 30 HTTP gateway checks, 31 existing company contract checks and 6 programmatic settings UI checks passed. Saved/reopened settings, owned fake-key persistence, request paths/model and api-key/Bearer/None authentication were checked using localhost mocks only. Real company addresses/keys were not read or logged; no actual company gateway/model success is claimed.
- App and installer paths use 0.7.2; existing 0.7.1 files/data are preserved. Source is prepared for publication; no 0.7.2 public binary release is implied by this commit.
