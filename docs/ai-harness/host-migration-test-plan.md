# MDF Host Migration Test Plan

## Principle

Host Migration is not proven by calling `/quit` or by seeing migration-related code exist. PASS requires evidence that the original host dropped, Fusion delivered a `HostMigrationToken`, a new runner resumed, state was rebuilt, and MDF durable state remained valid.

## Known code areas

- `Mdfproject/Assets/Scripts/Network/NetworkManager.cs`
- `Mdfproject/Assets/Scripts/Network/HostMigrationHandler.cs`
- `Mdfproject/Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs`
- `Mdfproject/Assets/Scripts/Managers/GameManagers.PlayerRegistry.cs`
- `Mdfproject/Assets/Scripts/Managers/PlayerManager.cs`
- `Mdfproject/Assets/Scripts/Managers/FieldManager.cs`
- `Mdfproject/Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`

## Feasibility gate

Before implementing a PASS E2E, Codex must prove:

- Fusion Host Migration is enabled/configured in `NetworkProjectConfig.fusion` or runtime settings.
- `OnHostMigration` fires when the host process is killed/dropped.
- The callback receives a non-null `HostMigrationToken`.
- The old runner is shut down and a new runner is started from the token.
- MDF logs show `HostMigrationHandler.StartMigration`, resume/rebuild, and `GameManagers.RestoreAfterHostMigration` or equivalent.
- The post-migration peer can reach a stable scene and runner state.

If any item is missing, report `HOST_MIGRATION_NOT_PROVEN` or `NEEDS_PROJECT_SUPPORT`; do not fake PASS.

## E2E proof gate

After feasibility passes, an E2E PASS requires:

1. Build host + at least one build client, or Editor host + build client if the first implementation cannot yet do build-build.
2. Start session and reach `Game` scene.
3. Dump pre-migration snapshots.
4. Kill/drop only the host process. Do not call normal `/quit` as proof.
5. Wait for surviving peer callback/resume logs.
6. Dump post-migration snapshots.
7. Assert:
   - exactly one active runner is new host/authority peer;
   - `GameManagers` exists and is bound to the active runner;
   - no duplicate `playerId`;
   - player HP/gold/wall/shop/field state restored;
   - destructible wall cell/HP/revision hashes match before and after migration;
   - survivor-boss pending/assignment payload (identity, HP, target, invaded flag, next ID) matches, not only count/hash summaries;
   - selected and persistent runtime augment identities match;
   - every connected human has the same valid 64-character SHA-256 connection-token hash before and after migration;
   - field-unit restoration has reached a terminal success state rather than merely starting asynchronous spawns;
   - AI takeover/reconciliation completed or is explicitly unsupported;
   - stale `PlayerRef` is not used as durable identity;
   - battle/prepare state can continue or is safely paused with documented reason.
8. Collect artifacts.

## Artifacts

- host stdout/stderr before kill;
- surviving peer stdout/stderr;
- `[MPTEST]` timeline;
- Fusion migration logs;
- pre/post snapshots;
- screenshots;
- command transcript;
- failure summary JSON/MD.

## Non-PASS states

- `OnHostMigration` method exists but was never called.
- `/quit` worked.
- New host starts a fresh match instead of resuming.
- Snapshot lacks player/field/shop/game state assertions.
- Raw `PlayerRef` equality is required after migration.
- Recovery is marked successful before `FlowResumed`, AI reconciliation, and asynchronous field-unit restoration are terminal.
- A timed-out or failed migration leaves a late `DontDestroyOnLoad` runner alive.
- Survivor-boss or wall-health payload capacity overflow is silently truncated.
