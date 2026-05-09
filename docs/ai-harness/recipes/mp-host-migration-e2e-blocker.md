## mp-host-migration-e2e-blocker: Callback succeeds but durable E2E still fails

Status: stale
Pinned: true
Category: host-migration
Created: 2026-05-05
Last used: 2026-05-05
Last verified: 2026-05-05
Use count: 1
Review after: 2026-08-03
Triggers: Host Migration E2E, durable state comparison
Applies to: `tools/harness/mp/run_host_migration_e2e.py`, Host Migration Phase 16
Verified by: see preserved recipe notes below; migrated from old Status: observed-blocker; blocker note preserved in recipe body
Replacement: host-migration-durable-pass
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
The surviving client can become the new Host through Fusion Host Migration, but strict durable state checks currently fail.

Evidence:
- `artifacts/mp/20260504-211233-host-migration-e2e/host-migration-e2e-result.json` showed callback/token/resume/recovery success and `failureCount=0`.
- The same artifact failed E2E on `no_connected_human_survivor_after_migration`, `player_0_gold_changed`, `player_0_field_wallHash_changed`, `player_1_gold_changed`, `player_1_wallCount_changed`, and `player_1_field_wallHash_changed`.
- `artifacts/mp/20260505-113106-host-migration-e2e/host-migration-e2e-result.json` used the real `MatchingLobby -> Game` flow and preserved the active scene after migration, but still failed durable state: `GameManagers` restored as `Setup/R0`, both player gold values reset to `20`, shop snapshots were empty, and player 1 permanent wall hash changed.
- Waiting an extra 3 seconds before killing the host (`--migration-settle-seconds 3`) produced `artifacts/mp/20260505-112939-host-migration-e2e`, which was worse: the token resumed only one player and lost the connected human survivor. Keep settle as an opt-in diagnostic, not the default.

Recipe:
- Treat Phase 15 as feasibility only.
- Treat Phase 16 as blocked until durable human identity/control and pre/post gameplay state are preserved or explicitly paused with documented semantics.
- Keep the host kill path; do not replace it with graceful `/quit`.
- Host Migration E2E must follow the real room flow: start both peers in `MatchingLobby`, wait for both to join, then load `Game`.
- `NetworkManager` on clients must remember the last Fusion scene from `OnSceneLoadDone`; otherwise migration restart can resume into `MatchingLobby`.
- The next repair needs a real MDF durable migration snapshot or equivalent production recovery path for `GameManagers`, all `PlayerManager` state, shop snapshots, and permanent wall cells. Do not mark Phase 16 PASS from callback/token/resume alone.
