## mp-e2e-mvp-start-order: Reproduce real room flow before Game load

Status: active
Pinned: false
Category: harness
Created: 2026-05-05
Last used: 2026-05-09
Last verified: 2026-05-09
Use count: 2
Review after: 2026-08-03
Triggers: Editor/Build E2E, build/build E2E, Game scene, FieldManager wall snapshots
Applies to: Phase 10/11 E2E matrix, `compare_state_snapshots.py`, `FieldManager`
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Older E2E scripts launched the host with `--mpAutoStart --mpLoadGame --mpScene Game` before the client was in the room. This let `GameManagers.SetupPlayersAndGrids()` fill missing slots as AI/late-join state, which produced client-side permanent wall gaps and `wallHash` mismatches. A second edge case allowed client wall-map rebuilds to include a stray permanent wall candidate unless filtered by the server's authoritative permanent-wall cell list.

Evidence:
- `artifacts/mp/20260504-204857-editor-host-build-client/comparison.json` failed on `player.0.field.wallHash`.
- `artifacts/mp/20260504-204948-build-host-editor-client/comparison.json` failed on `player.0.field.wallHash`.
- `artifacts/mp/20260504-205150-build-host-build-client/comparison.json` failed on `player.0.field.wallHash`, so the blocker is not Editor-only.
- `artifacts/mp/20260504-212154-build-host-build-client/comparison.json` on the latest build still failed on `player.0.field.wallHash` and also exposed a transient `game.currentState` split (`Prepare` vs `Setup`) at snapshot time.

Recipe:
- Launch all peers with `--mpTest` and automation enabled, but without `--mpAutoStart` and without `--mpLoadGame`.
- Start host/client into `MatchingLobby` first, wait until every peer reports `runner.activePlayerCount == expectedPlayers`, then have the host call `/loadGame` or `mp_load_game` for `Game`.
- Normalize scene aliases in harness readiness checks. Runtime can resolve legacy aliases such as `MatchingLobby` and `Game` to numbered scenes like `01_MatchingLobby` and `03_Game`, but snapshot readiness must compare the resolved scene name or the harness will wait until timeout and start bots after automation has already failed.
- Wait for field readiness and stable full snapshot agreement before PASS.
- Keep wall hashes in comparisons; do not suppress them to make MVP pass.
- In `FieldManager`, client peers must wait for server permanent-wall sync. Rebuilds should filter permanent wall candidates through the authoritative cell list received from `RPC_ApplyPermanentWalls`.

Verification:
- `artifacts/mp/20260504-221902-build-host-build-client/comparison.json` passed with `errors=[]`.
- `artifacts/mp/20260504-222012-editor-host-build-client/comparison.json` passed with `errors=[]`.
- `artifacts/mp/20260504-222048-build-host-editor-client/comparison.json` passed with `errors=[]`.
- `artifacts/mp/20260509-042900-human-bot-3round-progression/result.json` passed after scene alias normalization, with host/client visible HumanBots reaching `R4:Prepare`, 10/10 checkpoint comparisons passing, and `cleanupStatus=PASS`.

Pitfalls:
- `unity-cli editor play --wait` can return while the connector reports `reloading` or `playing`; harness scripts should poll `unity-cli status` and accept `playing` as controllable.
