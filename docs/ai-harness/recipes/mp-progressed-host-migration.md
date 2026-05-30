## mp-progressed-host-migration: Compare full durable fingerprints after host kill

Status: active
Pinned: true
Category: host-migration, security
Created: 2026-05-06
Last used: 2026-05-31
Last verified: 2026-05-31
Use count: 4
Review after: 2026-08-04
Triggers: progressed Host Migration, HumanBot checkpoint, process.kill, migration token, durable random state
Applies to: `tools/harness/mp/run_progressed_host_migration_e2e.py`, Phase 23
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/mp/20260530-203814-matrix/20260530-203831-status-effect-host-migration
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Basic Host Migration callback/token/resume proof does not prove that randomized HumanBot-created state survives migration. A progressed-state test must kill the host only after a synchronized client-bot checkpoint, then compare the new host's durable state against that exact checkpoint. Random mismatches after migration are failures, not expected randomness.

Recipe:
- Launch build host and one build client through `MatchingLobby`; enable `--mpFreezeGameFlow` on both peers so the progressed checkpoint does not drift while the host is killed and recovery completes.
- Enable `--mpHumanBot` only on the survivor client. Pause it before Game load, then start it after a synchronized `before-bot` checkpoint.
- Use at least two bounded HumanBot commands when practical. Seed `4001` with the balanced persona produced `SelectAugment` plus `PlaceWall`, proving both selected augment hash and field wall hash changed before migration.
- Fail fast unless the pre-migration host/client progressed checkpoint comparison succeeds. Save `human-bot-progressed-assertions.json`, `progressed-checkpoint-comparison.json`, and `progressed-random-evidence.json`.
- Kill the host process with `process.kill`, not `/quit`.
- Require the survivor snapshot to prove `OnHostMigration`, non-null migration token, `StartGame` success with the token, `HostMigrationResume`, `completeCount > 0`, `failureCount == 0`, and `recoverySucceeded == true`.
- Compare a full durable fingerprint from the survivor client's progressed checkpoint to the post-migration survivor snapshot: game state/round, battle hashes, HP, gold, wall count, shop revision/hash, presented and selected augment hashes, field grid/wall/unit hashes, and monster hashes.
- Assert the survivor is promoted to Host/server, at least one connected non-AI human remains, player IDs stay unique, and the HumanBot is paused rather than silently resumed.

Verification:
- `python tools/harness/mp/run_progressed_host_migration_e2e.py --seed 4001 --player-path artifacts/builds/20260505-183018/MDF-MPTest.exe` passed with artifact `artifacts/mp/20260505-185323-progressed-host-migration-e2e`.
- `host-migration-proof.json` reported callback/token/resume/startGame/complete success with `failureCount=0`.
- `human-bot-progressed-assertions.json` reported `commandsIssued=2`, `lastCommandType=PlaceWall`, and durable deltas for `wallCount`, selected augment hash, and `field.wallHash`.
- `progressed-host-migration-assertions.json` and `durable-state-report.json` reported `success=true` with `mismatches=[]`.
- `result.json` reported `success=true` and `failures=[]`.

Pitfalls:
- Do not reuse the basic Host Migration durable report alone; it does not compare augment snapshots or all progressed random-state fields.
- If game flow advances during migration, do not loosen comparisons. Use the test-only freeze or add a more explicit test-only migration freeze point.
- A successful migration callback sequence is insufficient if the pre/post durable fingerprint diverges.
- During `HostMigrationResume`, do not read Networked `GameManagers` properties immediately after resume-spawn just to log status. Use the cached migration snapshot label until the object is fully safe to access; the bad pattern caused `Error when accessing GameManagers.currentRound. Networked properties can only be accessed when Spawned() has been called` followed by Fusion cleanup noise in raw player logs.

Lifecycle notes:
- 2026-05-31: verified artifact `artifacts/mp/20260530-203814-matrix/20260530-203831-status-effect-host-migration`
