## unity-cli-editor-availability-v1: Wait for an open Editor connector

Status: active
Pinned: false
Category: unity-cli
Created: 2026-05-07
Last used: 2026-06-11
Last verified: 2026-05-21
Use count: 6
Review after: 2026-08-05
Triggers: no Unity instances running, not responding, manual Editor launch, status polling
Applies to: unity-cli connector, Windows PowerShell, Unity 2021.3.45f1
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/mp/20260521-031558-matrix/matrix-summary.json
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
`unity-cli` only talks to an already open Unity Editor with the connector loaded. If no Editor is open it fails with `Error: no Unity instances running`; immediately after launch it may report `not responding` while Unity imports, compiles, or shows `Hold on...`.

Recipe:
- Open `Mdfproject` in Unity 2021.3.45f1, then poll `unity-cli --project Mdfproject status` until it reports `ready`.
- Treat transient `not responding` with a recent heartbeat as startup/import work, not a verification failure.
- Once `ready`, run the normal sequence: `editor refresh --compile`, `console --type error --stacktrace user`, then `test --mode EditMode`.

Verification:
- On 2026-05-07, `unity-cli --project Mdfproject status` first failed with `no Unity instances running`, then reported `not responding`, then became `ready` on port 8090 after the Editor was manually opened.
- `unity-cli --project Mdfproject editor refresh --compile` completed, console errors returned `[]`, and EditMode passed `32/32`.

Pitfalls:
- v0.3.15 can print update notices to stderr even when the status command exits successfully; use the exit code and ready line, not the update notice, as the connector readiness signal.

Lifecycle notes:
- 2026-05-15: Opened Unity 2021.3.45f1 via Start-Process, polled unity-cli --project Mdfproject status through no-instance startup until ready on port 8090.
- 2026-05-21: verified artifact `artifacts/mp/20260521-031558-matrix/matrix-summary.json`
