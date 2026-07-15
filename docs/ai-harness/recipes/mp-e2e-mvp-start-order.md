## mp-e2e-mvp-start-order: Reproduce real room flow before Game load

Status: active
Pinned: false
Category: harness
Created: 2026-05-05
Last used: 2026-07-15
Last verified: 2026-07-15
Use count: 25
Review after: 2026-08-03
Triggers: Editor/Build E2E, build/build E2E, Game scene, FieldManager wall snapshots
Applies to: Phase 10/11 E2E matrix, `compare_state_snapshots.py`, `FieldManager`
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/mp/20260509-093437-2human-aifill-visible-3round-stable; artifacts/mp/20260510-101534-matrix; artifacts/mp/20260510-103350-two-humanbot-two-ai-upgrade-check; artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless; artifacts/mp/20260601-033513-two-hbot-two-ai-monster-spawn/result.json; artifacts/mp/20260519-040220-matrix/matrix-summary.json; artifacts/mp/20260519-055718-matrix/matrix-summary.json; artifacts/mp/20260713-055637-editor-host-build-client/result.json; artifacts/mp/20260713-063902-editor-host-build-client/result.json; artifacts/mp/20260713-123134-editor-host-build-client/result.json; artifacts/mp/20260713-144951-matrix/matrix-summary.json; artifacts/mp/20260713-145138-matrix/matrix-summary.json; artifacts/mp/map-theme-personal/20260715-022416-editor-host-build-client/result.json; artifacts/mp/map-theme-personal/20260715-022738-build-host-editor-client/result.json; artifacts/mp/map-theme-personal/20260715-022416-editor-host-build-client/result.json; artifacts/mp/map-theme-personal/20260715-022738-build-host-editor-client/result.json
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

Lifecycle notes:
- 2026-05-09: Verified visible 2-human + 2-AI fill run must repeat /startHost and /join until NetworkRunner is running; a single automation_start_peer call may leave the build on 00_Title while Photon is only connecting.
- 2026-05-09: verified artifact `artifacts/mp/20260509-093437-2human-aifill-visible-3round-stable`
- 2026-05-10: Verified smoke matrix E2E headless on 2026-05-10; success=true cleanupStatus=PASS orphanedPids=[].
- 2026-05-10: verified artifact `artifacts/mp/20260510-101534-matrix`
- 2026-05-10: Verified real room flow with 2 HumanBot peers, maxPlayers=4, Game load after lobby, and headless snapshot comparison on 2026-05-10.
- 2026-05-10: verified artifact `artifacts/mp/20260510-103350-two-humanbot-two-ai-upgrade-check`
- 2026-05-10: Applied host start, client join, load Game, then bot start order for 2 HumanBot + 2 AI headless verification.
- 2026-05-10: verified artifact `artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless`
- 2026-06-01: verified artifact `artifacts/mp/20260601-033513-two-hbot-two-ai-monster-spawn/result.json`
- 2026-05-13: Reused real room flow for 2 HumanBot peers with maxPlayers=4, then loaded Game to allow 2 AI fill players.
- 2026-05-13: verified artifact `artifacts/mp/20260513-131556-two-humanbot-two-ai-smoke`
- 2026-05-19: Reset Editor active scene to 00_Title before smoke matrix; all smoke cases passed with cleanupStatus=PASS orphanedPids=[].
- 2026-05-19: verified artifact `artifacts/mp/20260519-040220-matrix/matrix-summary.json`
- 2026-05-19: Smoke matrix after ranking overlay input passthrough passed all selected cases; cleanupStatus=PASS orphanedPids=[].
- 2026-05-19: verified artifact `artifacts/mp/20260519-055718-matrix/matrix-summary.json`
- 2026-07-13: verified artifact `artifacts/mp/20260713-055637-editor-host-build-client/result.json`
- 2026-07-13: verified artifact `artifacts/mp/20260713-063902-editor-host-build-client/result.json`
- 2026-07-13: verified artifact `artifacts/mp/20260713-123134-editor-host-build-client/result.json`
- 2026-07-13: verified artifact `artifacts/mp/20260713-144951-matrix/matrix-summary.json`
- 2026-07-13: verified artifact `artifacts/mp/20260713-145138-matrix/matrix-summary.json`
- 2026-07-15: verified artifact `artifacts/mp/map-theme-personal/20260715-022416-editor-host-build-client/result.json`
- 2026-07-15: verified artifact `artifacts/mp/map-theme-personal/20260715-022738-build-host-editor-client/result.json`
- 2026-07-15: verified artifact `artifacts/mp/map-theme-personal/20260715-022416-editor-host-build-client/result.json`
- 2026-07-15: verified artifact `artifacts/mp/map-theme-personal/20260715-022738-build-host-editor-client/result.json`
