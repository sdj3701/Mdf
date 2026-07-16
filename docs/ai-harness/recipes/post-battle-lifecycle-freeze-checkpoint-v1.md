## post-battle-lifecycle-freeze-checkpoint-v1: Freeze only after battle progression before reconnect, disconnect, or Host Migration assertions

Status: stale
Pinned: true
Category: host-migration, reconnect, security, battle
Created: 2026-05-06
Last used: 2026-07-14
Last verified: 2026-07-13
Use count: 19
Review after: 2026-08-04
Triggers: A post-battle lifecycle E2E must compare a progressed battle checkpoint across client disconnect, same-token reconnect, or Host Migration without hiding normal game-flow advancement as expected randomness
Applies to: `MPTestCommandLine`, `MPTestAutomationServer`, `battle_progression_common.py`, post-battle reconnect/disconnect/Host Migration runners
Verified by: see Verification section below; migrated from old Status: provisional; artifacts/mp/20260515-202032-matrix/20260515-202037-progressed-reconnect-after-battle/battle-preservation-assertions.json; artifacts/mp/20260531-022409-matrix/20260531-022414-progressed-reconnect-after-battle/result.json; artifacts/mp/20260531-030739-matrix/20260531-030744-progressed-reconnect-after-battle/result.json; artifacts/mp/20260531-030739-matrix/20260531-030744-progressed-reconnect-after-battle/result.json; artifacts/mp/20260531-032610-matrix/20260531-032615-progressed-reconnect-after-battle/result.json; artifacts/mp/20260531-040328-matrix/20260531-040334-progressed-reconnect-after-battle/result.json; artifacts/mp/20260531-044835-matrix/20260531-044847-network-budget-pending-stress/battle-checkpoint-freeze-wait-latest.json; artifacts/mp/20260531-053458-matrix/20260531-053503-network-budget-pending-stress/battle-checkpoint-freeze-wait-latest.json; artifacts/mp/20260531-061544-matrix/20260531-061549-progressed-host-migration-after-battle/battle-checkpoint-freeze-wait-latest.json; artifacts/mp/20260712-215951-progressed-host-migration-after-battle/battle-checkpoint-frozen-comparison.json
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Battle command E2E must run without `--mpFreezeGameFlow` at launch so timers can advance into `Battle1`/`Battle2` and HumanBot can emit `BattleSpawnMonsterCommand`/scroll decisions. After the battle checkpoint is captured, however, normal battle simulation can keep advancing while the harness kills a peer or waits for Host Migration. Weakening comparisons would hide real post-replication divergence.

Recipe:
- Launch post-battle lifecycle cases unfrozen and drive battle with the host HumanBot plus an observer client.
- After strict battle command evidence is captured, stop the HumanBot and call the `--mpTest` automation endpoint `/test/freezeGameFlow` on both peers.
- Re-dump a frozen host/client battle checkpoint and require `compare_state_snapshots.py` success before killing a client or host.
- For client disconnect, compare the frozen host checkpoint with the post-takeover host snapshot using semantic battle/world fingerprints, while asserting the dropped durable `playerId` remains present, `isConnected=false`, `isAI=true`, and `ai.controllerRegistered=true`.
- For same-token reconnect, use the same connection token for the replacement build client, assert the local `playerId` and token hash are reclaimed, require full host/client snapshot comparison, and compare the post-reconnect host snapshot against the frozen battle checkpoint.
- For Host Migration, kill the host process, not `/quit`, then require `OnHostMigration`, non-null token, `StartGame` with token/resume/recovery, promoted survivor host/server, a connected non-AI survivor, unique `playerId`s, and frozen battle checkpoint preservation.
- Keep event-time preservation strict. If active monster/effect timers still drift after freeze, report the exact fingerprint mismatch as a lifecycle blocker instead of relaxing the comparison.
- Permanent augment bonuses cannot remain RPC-only. Publish attack damage/speed bonus buckets as Networked player state and compute snapshots from that replicated source; otherwise same-token reconnect can reclaim the player correctly but fail active-effect comparison because the reconnected client missed the original bonus RPC.
- Attack monster pool contents cannot rely only on local lists plus one-shot RPCs during post-battle Host Migration. Publish slot names/counts/boss metadata into Networked snapshot arrays and use those arrays before local `AttackMonsterPool` when capturing migration snapshots or state hashes.

Verification:
- Dry-run coverage passed for `run_progressed_reconnect_after_battle.py --dry-run`, `run_progressed_disconnect_after_battle.py --dry-run`, `run_battle_seed_sweep.py --seeds 7101,7102,7103 --dry-run`, and `run_matrix.py --case progressed-reconnect-after-battle --dry-run`.
- `artifacts/builds/20260506-095411/MDF-MPTest.exe` passed launch smoke after the post-battle lifecycle fixes.
- `artifacts/mp/20260506-095518-progressed-host-migration-after-battle` passed with Host Migration callback/token/resume/start-game/recovery evidence and frozen battle checkpoint preservation.
- `artifacts/mp/20260506-095609-progressed-reconnect-after-battle` passed same-token reconnect with reclaimed `playerId`, `isAI=false`, `ai.controllerRegistered=false`, full snapshot comparison, and battle preservation.
- `artifacts/mp/20260506-095711-progressed-disconnect-after-battle` passed disconnect/AI takeover with the dropped `playerId` preserved as disconnected AI and frozen battle state preserved.
- `artifacts/mp/20260506-095834-battle-seed-sweep` passed seeds `7101,7102,7103`.

Lifecycle notes:
- 2026-05-16: verified artifact `artifacts/mp/20260515-202032-matrix/20260515-202037-progressed-reconnect-after-battle/battle-preservation-assertions.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-022409-matrix/20260531-022414-progressed-reconnect-after-battle/result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-030739-matrix/20260531-030744-progressed-reconnect-after-battle/result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-030739-matrix/20260531-030744-progressed-reconnect-after-battle/result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-032610-matrix/20260531-032615-progressed-reconnect-after-battle/result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-040328-matrix/20260531-040334-progressed-reconnect-after-battle/result.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-044835-matrix/20260531-044847-network-budget-pending-stress/battle-checkpoint-freeze-wait-latest.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-053458-matrix/20260531-053503-network-budget-pending-stress/battle-checkpoint-freeze-wait-latest.json`
- 2026-05-31: verified artifact `artifacts/mp/20260531-061544-matrix/20260531-061549-progressed-host-migration-after-battle/battle-checkpoint-freeze-wait-latest.json`
- 2026-07-13: Frozen pre-migration battle checkpoint compared successfully.
- 2026-07-13: verified artifact `artifacts/mp/20260712-215951-progressed-host-migration-after-battle/battle-checkpoint-frozen-comparison.json`
- 2026-07-14: Applied the same stop-then-freeze checkpoint discipline to two-HumanBot bounded-progression finalization before stable queue drain and peer comparison.
