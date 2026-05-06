# MDF Random Authority Audit

Status: Phase 25 verified-local
Last updated: 2026-05-05

## Purpose

This audit classifies RNG and time-derived randomness under `Mdfproject/Assets/Scripts` so HumanBot progression tests can distinguish authoritative gameplay outcomes from client visual noise, AI pacing, and test-only labels.

Phase 25 does not make MDF deterministic. It hardens obvious authority boundaries and documents remaining random-state risks for later battle/progression work.

## Classification Rules

- Authoritative gameplay random: changes durable game state and must be decided by State Authority, synced or reconstructed, received on reconnect, preserved by Host Migration, and visible in snapshots through a hash/revision.
- Client visual/random delay only: affects UI, debug labels, or non-durable presentation.
- AI/bot decision pacing: changes when an AI or test bot decides to request an action; accepted gameplay effects must still go through authoritative command paths.
- Test-only: guarded by `--mpTest`, Editor tooling, automation server, journals, or artifact naming.

## Authoritative Gameplay RNG

| Area | Source | Authority and sync evidence | Phase 25 hardening | Remaining risk |
| --- | --- | --- | --- | --- |
| Shop reroll | `ShopManager.Reroll`: unit slot and star rolls | `RerollShopCommand` and round start execute on server/State Authority. `PlayerManager.PublishShopSnapshot` writes Networked shop names/stars/sold/revision. Snapshots include `shop.itemsHash`, count, round, revision. Host Migration pushes shop snapshots. | `ShopManager.Reroll` now returns early on a running non-server client peer. | Keep using HumanBot and seed sweep to catch any path that bypasses `RerollShopCommand` or round-start authority. |
| Augment presentation | `AugmentManager.PresentAugments`: tier roll and choice shuffle | `GameManagers.StartNextRound` runs under authority and publishes presented names through `PlayerManager.PublishPresentedAugmentSnapshot` plus sync command/RPC. Snapshots include `augment.presentedHash`. | `PresentAugments` now returns early on a running non-server client peer. | Presented choices are not deterministic across runs; tests must compare same-player hashes across peers only. |
| Augment selection target fallback | `AugmentManager.SelectAndApplyAugment`: fallback random target when no opponent is assigned | `SelectAugmentCommand` runs on server. Selection publishes selected augment snapshot and clears presented snapshot. Snapshots include `augment.selectedHash`, `augment.activeEffectHash`, and `augment.activeTargetHash`. | `SelectAndApplyAugment` now returns early on a running non-server client peer. Battle Snapshot Coverage v1 adds active effect/target hashes. | Fallback target choice is still not a standalone random outcome journal entry. Use the active target hash as sync evidence and add journaling if a future bug needs exact target-choice history. |
| Permanent initial walls | `FieldManager.GeneratePermanentWallsIfNeeded` / `ShuffleList` | Existing guards skip running client peers and migration restore. Server chooses cells, records authoritative permanent wall cells, broadcasts `RPC_ApplyPermanentWalls`, and snapshots compare `field.wallHash`. Host Migration restores wall cells. | No code change needed; existing authority guard and rebroadcast/fallback paths are the preferred shape. | Continue checking `wallHash` in every progression, reconnect, and Host Migration scenario. |
| Monster outer spawn position | `FieldManager.GetRandomOuterSpawnWorldPosition` via `MonsterSpawner` | `MonsterSpawner.SpawnMonsterAtPositionAsync` spawns only when the player object has State Authority; Fusion replicates spawned network objects. Snapshots include monster alive count, legacy living hash, `monsters.typeCountHpHash`, and `monsters.targetPlayerHash`. Boss metadata plus owner player id, monster data key, type, and traits are mirrored through Networked fields. Client/reconnect instances can rebind to the owner's monster parent from those fields, and snapshot collection falls back to live `Monster` NetworkObjects filtered by owner id. | Battle Snapshot Coverage v1 adds stable type/count/HP-bucket and target/player hashes without transform assertions. | Snapshot still does not require exact interpolated position equality. If late battle tests find path/combat divergence, add coarse path/progress buckets after stabilization. |
| Battle pairing and first attacker | `GameManagers.AssignBattleOpponents` shuffle and first-attacker roll | `StartBattle1Phase` exits unless `Object.HasStateAuthority`. State Authority publishes compact Networked battle-opponent and first-attacker arrays; battle-start RPCs also record the same read-only map on every peer. `CaptureBattleSnapshotForMigration` resolves from local map or Networked fallback. Non-authority migration/readiness paths only cache the Networked map locally and do not write `FirstAttackerPlayerId`. Snapshots include `battleOpponentsHash`, `matchFirstAttackerHash`, and `battleActiveHash`. | Battle Snapshot Coverage v1 adds replicated battle-map coverage, active role/fighting hash, and active flag comparisons. | The current replicated battle map is sized for the existing four-player cap. Late-game HumanBot tests should assert these hashes in battle states and after Host Migration. |
| Survivor boss target assignment | `SurvivorBossManager.AssignTargetsToSurvivors` and obsolete `GetPendingBossesWithTargets` | Called from battle start under `GameManagers` State Authority in the normal path. Snapshots include `survivorBossPendingCount`, `survivorBossAssignmentCount`, `survivorBossPendingHash`, and `survivorBossAssignmentHash` when local manager state is available. | `RegisterSurvivorBoss`, `AssignTargetsToSurvivors`, and the obsolete target helper now return early on a running non-server client peer. Battle Snapshot Coverage v1 adds pending/assignment counts and hashes; non-zero state on only one peer fails comparison. | Boss pending/assignment is still private manager state rather than a live replicated map; compare authority pre/post Host Migration snapshots when these hashes are known. |

## AI And Bot Decision RNG

| Area | Source | Classification | Notes |
| --- | --- | --- | --- |
| AI maze planning | `MazePlanner`: `Environment.TickCount`, `System.Random`, shuffles, `Stopwatch` search limits | AI/bot decision planning | Maze randomness chooses candidate wall commands. Durable effects must still pass through authority command validation and wall snapshots. Do not use it as deterministic replay evidence. |
| AI prepare cadence | `AIPacer` and `BuildMazeAction` `Random.Range` delays | AI decision pacing | Timing should not be asserted directly. Harness waits on state gates and accepted durable outcomes. |
| AI attack spawn pacing | `AIAttackStrategy` random phase delays | AI combat pacing | Authoritative spawn requests still need attacker/defender validation and battle-state assertions. |
| HumanBot policy | `MPTestHumanBotPolicy` `System.Random(seed)` | Test-only bot decision tie-breaks | Seed is a diagnostic label unless every gameplay RNG source is separately controlled. |

## Client Visual Or Identity Randomness

| Area | Source | Classification | Notes |
| --- | --- | --- | --- |
| Local nickname fallback | `NextScenes`: `Random.Range(1000, 9999)` | Client visual/identity label | Not durable gameplay identity. Durable reconnect identity is connection token plus MDF `playerId`. |
| Fallback player UUID | `NetworkManager`, `HostMigrationHandler`: `Guid.NewGuid` / PlayerPrefs UUID | Identity fallback, not gameplay RNG | Must not replace connection-token based durable identity in harness assertions. |
| Debug and UI timing | `BuildDebugGUI`, migration elapsed logs, wait loops | Client visual/debug timing | Allowed snapshot differences. |

## Test-Only Time And Seed Usage

| Area | Source | Classification | Notes |
| --- | --- | --- | --- |
| MPTest seed init | `MPTestBootstrap`: `UnityEngine.Random.InitState(_options.Seed)` | Test-only | Useful for diagnostic sweeps, not a replay guarantee. |
| MPTest snapshots/logs/journals | `DateTime.UtcNow`, artifact filenames, screenshot names | Test-only | Allowed to differ between peers/runs. |
| HumanBot cadence | `MPTestHumanBotDriver`: `Time.realtimeSinceStartup` | Test-only | Bounded by max duration, max commands, stop round, and explicit `/bot/stop`. |

## Required Assertions After Phase 25

- Same `playerId` shop hash/revision/count must match across every peer.
- Same `playerId` presented and selected augment hashes must match across every peer when available.
- Same `playerId` active augment effect/target hashes must match across every peer when available.
- Same `playerId` field wall/unit/grid hashes must match across every peer.
- Battle mapping and active battle hashes must match in battle states.
- Monster type/count/HP-bucket and target/player hashes must match when living monsters exist.
- Survivor boss pending/assignment counts must match, and hashes must match across comparable authority snapshots when known.
- Host Migration may allow random state before the kill; any random-derived divergence after migration is a failure.
- Seeds are run labels unless the specific source is proven controlled.

## Remaining Risks

1. Survivor boss pending and target assignment state now has snapshot hashes, but it is still not a live replicated map; progressed Host Migration must compare authority snapshots when those hashes are known.
2. Monster spawn positions are replicated through network objects, but snapshot comparison intentionally uses type/count/HP buckets and target/player hashes rather than exact interpolated transforms.
3. Augment fallback target choice is authoritative and snapshotted through active target hashes, but target choice is not exposed as a separate random outcome journal entry.
4. AI maze planning uses uncontrolled `System.Random` from `Environment.TickCount`; this is acceptable for adaptive HumanBot/AI progression, but not for command replay.

## Phase 25 Verification

- `python tools/harness/precommit.py --all` reported `0 errors, 12 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed compilation.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed `8/8`.
- Rebuilt Development player with launch smoke at `artifacts/builds/20260505-192828/MDF-MPTest.exe`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --player-path artifacts/builds/20260505-192828/MDF-MPTest.exe` passed with artifact `artifacts/mp/20260505-192927-human-bot-prepare`.
- `python tools/harness/mp/run_progressed_host_migration_e2e.py --seed 4001 --player-path artifacts/builds/20260505-192828/MDF-MPTest.exe` passed with artifact `artifacts/mp/20260505-193030-progressed-host-migration-e2e`.
- `python tools/harness/mp/run_human_bot_seed_sweep.py --seeds 5101,5102,5103 --player-path artifacts/builds/20260505-192828/MDF-MPTest.exe` passed with artifact `artifacts/mp/20260505-193157-human-bot-seed-sweep`.

## Verification Recipe

After changing any authoritative RNG path:

```powershell
python tools/harness/precommit.py --all
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
unity-cli --project Mdfproject test --mode EditMode
python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --player-path <Development player>
python tools/harness/mp/run_progressed_host_migration_e2e.py --seed 4001 --player-path <Development player>
```

Run the seed sweep when the change can alter shop, augment, wall, or battle random outcomes:

```powershell
python tools/harness/mp/run_human_bot_seed_sweep.py --seeds 5101,5102,5103 --player-path <Development player>
```
