## long-lifecycle-target-round-freeze-v1: Stop HumanBot before inserting long lifecycle events

Status: active
Pinned: true
Category: host-migration, reconnect
Created: 2026-05-08
Last used: 2026-05-08
Last verified: 2026-05-08
Use count: 1
Review after: 2026-08-06
Triggers: lifecycle insertion happens immediately after `round-complete`, especially R4 Prepare after a 3-round run
Applies to: `tools/harness/mp/long_lifecycle_common.py`, `run_3round_*`, Host Migration/reconnect/disconnect insertion after long progression
Verified by: see Verification section below; migrated from old Status: verified
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Recipe:
- For `round-complete` long lifecycle cases, start HumanBot with `stopAtRound = targetRound + 1`.
- Still call `/test/bot/stop` and `/test/freezeGameFlow` on both peers before taking the pre-event checkpoint; this gives explicit stop/freeze artifacts.
- Require stable host/client pre-event snapshots after bot stop and freeze before killing a peer or host.
- Use the frozen client snapshot as the Host Migration preservation baseline, and reject the run if post-event state moved to lobby, lost players, or missed Host Migration proof counters.
- Host Migration PASS must also reject duplicate runtime `PlayerManager` state. Gate on `objects.playerManagerCount == expectedPlayers` and scan full player logs for `[HM-SMOKE] FAIL` or `handler_migration_complete_fail`.
- Do not record `handler_migration_complete_success` until AI takeover reconciliation has completed and `aiTakeoverReady=true`.
- If Fusion resumes duplicate `PlayerManager` objects for the same durable `playerId`, rebuild `GameManagers.NetworkPlayers` from the best canonical candidate and despawn stale duplicates before AI takeover and smoke checks.
- Keep this behavior scoped to long lifecycle insertion; long progression and endurance modes should continue using `--mpBotStopAtRound 0` unless their own pass condition requires a stop.

Verification:
- `python -m py_compile tools/harness/mp/run_human_bot_3round_progression.py tools/harness/mp/long_lifecycle_common.py tools/harness/mp/run_3round_host_migration.py tools/harness/mp/run_matrix.py` PASS.
- `python tools/harness/mp/run_matrix.py --profile long-lifecycle --dry-run` PASS.
- `python tools/harness/mp/build_player.py --launch-smoke --exit-after-seconds 5 --headless-player --orphan-threshold 0 --cleanup-timeout-seconds 20` built `artifacts/builds/20260508-034700/MDF-MPTest.exe`; launch smoke cleanup PASS.
- `python tools/harness/mp/run_3round_host_migration.py --seed 8201 --headless-player --player-path artifacts/builds/20260508-034700/MDF-MPTest.exe` PASS at `artifacts/mp/20260508-034748-3round-host-migration`: `target_round_complete`, `maxRoundReached=4`, `host-migration-proof.success=true`, `onHostMigrationCount=1`, `recoverySucceeded=true`, `aiTakeoverReady=true`, post snapshot `playerManagerCount=2`, `[HM-SMOKE] PASS`, preservation success, cleanup PASS.
- `python tools/harness/mp/run_3round_reconnect.py --seed 8201 --headless-player --player-path artifacts/builds/20260508-034700/MDF-MPTest.exe` PASS at `artifacts/mp/20260508-035119-3round-reconnect`: reconnected client reclaimed the same playerId, full comparison/preservation passed, cleanup PASS.
- `python tools/harness/mp/run_3round_disconnect_ai_takeover.py --seed 8201 --headless-player --player-path artifacts/builds/20260508-034700/MDF-MPTest.exe` PASS at `artifacts/mp/20260508-035459-3round-disconnect-ai-takeover`: disconnected player became AI-controlled, preservation passed, cleanup PASS.

Pitfalls:
- If the bot keeps running into R4 Prepare, it can select a new augment between the completion snapshot and the lifecycle kill. That race can make Host Migration either restore a different baseline or fall back to lobby before proof counters advance.
- `GameManagers.AllPlayers` can hide duplicate runtime `PlayerManager` objects because it reads the canonical network array. Always check object counts as well as logical player snapshots after migration.
- A clean C# compile does not update an already-built player; rebuild and pass the fresh `--player-path` after Host Migration or PlayerManager changes.
