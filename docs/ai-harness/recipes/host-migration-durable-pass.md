## host-migration-durable-pass: Snapshot MDF state beyond Fusion token resume

Status: active
Pinned: true
Category: host-migration, security
Created: 2026-05-05
Last used: 2026-05-31
Last verified: 2026-05-31
Use count: 15
Review after: 2026-08-03
Triggers: Phase 16, Host Migration durable E2E, wallHash/gold/shop/player identity drift
Applies to: `HostMigrationHandler`, `PlayerManager`, `FieldManager`, `run_host_migration_e2e.py`
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/mp/20260530-175140-matrix/20260530-175147-progressed-host-migration-after-battle; artifacts/mp/20260530-185259-matrix/20260530-185304-progressed-host-migration-after-battle; artifacts/mp/20260530-185847-matrix/20260530-185854-progressed-host-migration-after-battle; artifacts/mp/20260530-203814-matrix/20260530-203831-status-effect-host-migration; artifacts/mp/20260530-212402-matrix/20260530-212408-stat-buff-host-migration; artifacts/mp/20260530-220028-matrix/20260530-220041-status-effect-host-migration; artifacts/mp/20260530-220233-matrix/20260530-220238-stat-buff-host-migration; artifacts/mp/20260530-220427-matrix/20260530-220433-zone-host-migration; artifacts/mp/20260531-015519-matrix/20260531-015524-status-effect-host-migration/result.json
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Fusion Host Migration callback/token/resume can succeed while MDF gameplay state still drifts. In failing artifacts, `GameManagers.Instance` was null at migration start, so cached game state fell back to Setup/R0 and post-migration gold/shop/wall state changed. A later failure showed the survivor object could resume with the wrong durable `playerId` and no `InputAuthority`.

Recipe:
- Capture a durable MDF snapshot at `StartMigration` from the active runner, not only `GameManagers.Instance`.
- Include `currentRound/currentState`, all `PlayerManager` HP/gold/wallCount/shop snapshot, permanent wall flat cells, wall hash, local input authority, and AI/connected observations.
- Apply this snapshot on the new host before `RestoreAfterHostMigration`, matching players by local input authority first, wall hash second, then current `playerId`.
- Reassign the survivor's transient `InputAuthority` to the new runner local player, but keep durable identity as MDF `playerId`.
- Do not reset player HP/gold/wallCount in `PlayerManager.Spawned()` during Host Migration.
- Restore permanent wall cells from the snapshot and rebuild wall maps before post-migration assertions.

Verification:
- `artifacts/mp/20260505-115931-host-migration-e2e/host-migration-e2e-result.json` passed with `failures=[]` after identity/control and durable state restore.
- `artifacts/mp/20260505-123330-host-migration-e2e/host-migration-e2e-result.json` passed on the latest build after wall-sync hardening.
- `durable-state-report.json` for the passing run reported no per-player mismatches.

Lifecycle notes:
- 2026-05-31: Verified CombatScheduler pending fire/hit network snapshot hardening with progressed Host Migration after battle.
- 2026-05-31: verified artifact `artifacts/mp/20260530-175140-matrix/20260530-175147-progressed-host-migration-after-battle`
- 2026-05-31: verified artifact `artifacts/mp/20260530-185259-matrix/20260530-185304-progressed-host-migration-after-battle`
- 2026-05-31: verified artifact `artifacts/mp/20260530-185847-matrix/20260530-185854-progressed-host-migration-after-battle`
- 2026-05-31: verified artifact `artifacts/mp/20260530-203814-matrix/20260530-203831-status-effect-host-migration`
- 2026-05-31: verified artifact `artifacts/mp/20260530-212402-matrix/20260530-212408-stat-buff-host-migration`
- 2026-05-31: verified artifact `artifacts/mp/20260530-220028-matrix/20260530-220041-status-effect-host-migration`
- 2026-05-31: verified artifact `artifacts/mp/20260530-220233-matrix/20260530-220238-stat-buff-host-migration`
- 2026-05-31: verified artifact `artifacts/mp/20260530-220427-matrix/20260530-220433-zone-host-migration`
- 2026-05-31: verified artifact `artifacts/mp/20260531-015519-matrix/20260531-015524-status-effect-host-migration/result.json`
