## mp-automation-server-gates: Keep build control loopback-only

Status: active
Pinned: true
Category: security
Created: 2026-05-05
Last used: 2026-05-05
Last verified: 2026-05-05
Use count: 1
Review after: 2026-08-03
Triggers: automation server, HttpListener, --mpTest, --mpAutomationToken
Applies to: `MPTestAutomationServer`, Development Build harness control
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
The build-side automation server is useful for E2E control but must not become a production surface.

Recipe:
- Compile the server and dispatcher only under `UNITY_EDITOR || DEVELOPMENT_BUILD`.
- Start the server only when `--mpTest`, `--mpAutomationPort`, and `--mpAutomationToken` are present.
- Bind `HttpListener` to `IPAddress.Loopback`, not wildcard prefixes.
- Require `Authorization: Bearer`, `X-MPTest-Token`, or fallback `?token=` on every endpoint.
- Dispatch Unity API work through `MPTestMainThreadDispatcher`.

Verification:
- `python tools\harness\precommit.py --all` returned `0 errors`.
- `python tools\harness\precommit.py --self-test` returned all PASS.
- `unity-cli --project Mdfproject editor refresh --compile` and console error check passed after adding the missing `GameCore.Enums` import.

Pitfalls:
- Do not log token values. Log only `AutomationTokenHash`/`ConnectionTokenHash`.
