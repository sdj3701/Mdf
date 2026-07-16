# MDF Project Structure for Harness Agents

## Current checkout baseline

- Unity project root: `Mdfproject`
- Unity version: `2021.3.45f1`
- Test Framework: `1.1.33`
- Addressables: `1.19.19`
- URP: `12.1.15`
- Input System: `1.7.0`
- UniTask via Git package
- unity-cli connector installed in `Packages/manifest.json`
- Fusion SDK under `Mdfproject/Assets/Photon/Fusion`
- Fusion build info: `2.0.9 Stable 1566`
- Build scenes: `00_Title`, `01_MatchingLobby`, `02_JoinLobby`, `03_Game`; CLI aliases remain `Title`, `MatchingLobby`, `TestMatching`, `JoinLobby`, `Game`
- Measured 2026-07-15 after cleanup/migration: `Assets/Scripts` contains 382 C# files and about 129,215 physical lines
- Project-owned runtime assemblies: Foundation, Grid, Pooling, RuntimeAssets, RuntimeUI
- Those assemblies currently contain 22 C# files and about 2,689 physical lines; most gameplay still compiles in `Assembly-CSharp`

## Top-level script map

```text
Assets/Scripts/
  AI/                         BehaviorTree, UtilitySystem, MazePlanner
  Button/                     UI button components
  Commands/                   Command pattern: Core, PlayerActions, Sync, AI
  ComponentRegistrySystem/    dynamic runtime NetworkObject/component lookup only
  DB/                         addressable key helper, MiniJSON
  Editor/                     data importer/exporter utilities
  Enums/                      GameEnums, CommandType, stats, monster enums
  Foundation/                 Unity/Fusion-free identity, snapshot, lifecycle and flow policies
  Game/                       Units, Monsters, Skills, Battle, Augments, Targeting, Rules
  Grid/                       field geometry and typed occupancy index
  Interfaces/                 ICommand, IHealth, IEnemy, IMana, IPlacementHandler
  MainLobby/                  character/lobby selection
  Managers/                   GameManagers, PlayerManager, FieldManager, Shop, Combat, Load
  Network/                    NetworkManager, HostMigrationHandler, NetworkPlayer, lobby UI
  Pooling/                    bounded Unity object pooling primitive
  RuntimeAssets/              ref-counted Addressables cache, owners, leases and prewarmer
  RuntimeUI/                  UI Toolkit pointer routing and per-frame dispatch gates
  UI/                         HUD, ranking, shop, augment, attack sequence UI
  VFX/                        projectile and VFX pooling
```

## Highest-risk files

Large/high-risk files that should be edited narrowly:

```text
FieldManager partials                  ~8,928 lines (main file ~6,498)
PlayerManager partials                 ~9,477 lines (main file ~6,115)
CombatScheduler partials               ~6,184 lines
GameManagers partials                  ~5,675 lines (main file ~2,685)
HostMigrationHandler.cs                ~4,998 lines
Unit.cs                                ~4,426 lines
Monster.cs                             ~3,262 lines
GamePrepareUIToolkitController.cs      ~3,010 lines
MonsterSpawner.cs                      ~2,205 lines
NetworkManager.cs                      ~1,945 lines
```

Codex should avoid broad rewrites of these files. A partial file is an organization aid, not a dependency boundary. Prefer a small hook into a typed service or a pure policy with executable tests.

## Runtime module boundaries

The safe dependency direction is from `Assembly-CSharp` gameplay shells into the small project-owned assemblies below. Runtime modules never reference `Assembly-CSharp`.

| Assembly | Engine dependency | Responsibility | Current gameplay integration |
| --- | --- | --- | --- |
| `MDF.Runtime.Foundation` | none | stable keys, snapshot packing, lobby/match policies, lifecycle generations, ordered collection invariants | `NetworkPlayer`, `JoinLobbyUI`, `GameManagers`, `PlayerManager` inventory facade, pooled `Unit`/`Monster` |
| `MDF.Runtime.Grid` | UnityEngine | immutable field geometry and cell occupancy | `FieldManager` unit, destructible-wall and permanent-wall maps |
| `MDF.Runtime.Pooling` | UnityEngine | bounded local object pool | runtime VFX/UI consumers |
| `MDF.Runtime.Assets` | UnityEngine, Addressables, UniTask | coalesced/ref-counted loads, leases, lifecycle owners and first-spawn presentation prewarm | game/bootstrap asset loading |
| `MDF.Runtime.UI` | UnityEngine UI Toolkit | primary pointer routing and duplicate-frame dispatch gates | game HUD and wall action panels |

Responsibility seams currently extracted from the largest classes:

- `FieldManager` delegates cell storage to `GridOccupancyIndex<T>` and coordinate math to `FieldGridGeometry`.
- `PlayerManager` keeps Fusion authority/revisions while `PlayerMagicScrollInventory` owns scroll rules and delegates ordered storage to `OrderedInventory<T>`.
- `GameManagers` keeps replicated state/timers while `MatchFlowPolicy` owns presentation and transition-gate decisions.
- `Unit` and `Monster` keep simulation state while `LifecycleGeneration` rejects stale async completion from an earlier pooled lifetime.

Continue this facade-first pattern. Moving a Unity/Fusion class wholesale behind an asmdef is unsafe while it still depends on `Assembly-CSharp` types. First extract a dependency-free policy or an interface-backed service, wire it, add behavior coverage, then move only that seam into an assembly.

## Network entry points

### `NetworkManager`

Responsibilities:

- Maintains singleton and `NetworkRunner`.
- Joins Fusion lobby.
- Starts game through `StartGame(GameMode mode, string sessionName, string sceneName)`.
- Spawns `_playerPrefab` on `OnPlayerJoined` when server.
- Handles disconnect, reconnect cache, and host migration callback.
- Contains older command RPC path; current player action path is primarily `PlayerManager` + `GameManagers` + `CommandProcessor`.

Important methods/areas:

- `StartGame(...)`
- `OnPlayerJoined(...)`
- `OnPlayerLeft(...)`
- `OnHostMigration(...)`
- `TryGetConnectionTokenString(...)`
- `CacheDisconnectedPlayerData(...)`
- `TryReassociateDisconnectedPlayer(...)`

### `HostMigrationHandler`

Responsibilities:

- Caches migration data.
- Starts migration using `HostMigrationToken`.
- Creates new runner and calls `StartGame` with `HostMigrationToken` and `HostMigrationResume`.
- Restores `GameManagers` and `PlayerManager` objects.
- Runs migration recovery and AI reconciliation.

Harness requirement:

Host Migration PASS must show actual callback/resume artifacts, not only normal `/quit`.

### `GameSceneInitializer`

Responsibilities:

- Direct `Game` scene Play starts Single mode if no active `NetworkManager` runner.
- Multiplayer flow waits for `NetworkManager` runner and only host spawns `GameManagers`.

Harness relevance:

- Editor-side custom tools can use existing scene flow, but must avoid accidentally starting Single mode when a multiplayer runner is expected.

## Core gameplay systems

### `GameManagers`

NetworkBehaviour partial class. Important state:

- `currentState`: `Setup`, `DataLoading`, `Prepare`, `Battle1`, `Battle2`, `GameOver`
- `currentRound`
- `phaseTimer`
- `NetworkPlayers` array
- `singlePlayerModeCount`
- `FirstAttackerPlayerId`
- `_battleOpponents`
- `_matchFirstAttacker`

Important partials:

- `GameManagers.cs`: lifecycle, networked state, timers, command broadcast, battle flow.
- `GameManagers.StateTransition.cs`: state transition wrappers and migration snapshot push.
- `GameManagers.PlayerRegistry.cs`: `AllPlayers`, local player relink, battle pair reconstruction.
- `GameManagers.UIFlow.cs`: UI/state flow.
- `GameManagers.MigrationRecovery.cs`: migration recovery, timer/UI/shop/battle/AI recovery.

### `PlayerManager`

NetworkBehaviour. Important networked state:

- `playerId`
- `health`
- `gold`
- `wallCount`
- shop snapshot fields
- `IsActivelyFighting`
- `IsAttackerInCurrentBattle`

Important managers/references:

- `FieldManager`
- `ShopManager`
- `MonsterSpawner`
- `AttackSequenceManager`
- `AIPlayerController`

Important command path:

- `RPC_RequestCommandToServer(...)` corrects mismatched `playerId` to authoritative player id before broadcast.

### `FieldManager`

Handles grid, unit placement, wall placement/removal, permanent wall sync, pathfinding/A* integration, migration rebuild of placed units/walls.

Harness snapshot should summarize field state rather than serialize every Unity object:

- grid size
- placed units count/hash
- destructible walls count/hash
- permanent walls count/hash
- path/goal readiness
- owner `playerId`

### `CommandProcessor`

The core deterministic command queue.

Rules for new commands:

- Add a command class.
- Add `CommandType` enum value.
- Add serialize and deserialize branches.
- Route UI/AI request through `RequestCommandExecution`.
- Add snapshot/assertion coverage for durable state changes.

### AI

`AIPlayerController` registers in `ComponentRegistry` by `playerId` and runs BehaviorTrees in `Prepare`, `Battle1`, and `Battle2`.

Important action files:

- `BuildMazeAction`
- `BuyBestUnitAction`
- `PlaceBestUnitAction`
- `RerollShopAction`
- `ChooseBestAugmentAction`
- `AIAttackStrategy`

## Data and assets

- `GameData/Units`
- `GameData/Monsters`
- `GameData/MonsterWave`
- `GameData/Skills`
- `GameData/Scrolls`
- `GameData/Augments`
- Addressables config under `AddressableAssetsData`
- Addressables server data under `ServerData/StandaloneWindows64`

Harness should prefer stable names/keys over Unity instance IDs in snapshots.

## Existing tests

`Assets/Tests/PlayMode/MDF.PlayModeTests.asmdef` is the dedicated PlayMode integration assembly. It currently covers the Addressables cache plus runtime lifecycle, lobby load ACK/retry, boss-card quantity, wall-action duplicate dispatch, grid occupancy, and a real UI Toolkit primary-pointer event.

The multiplayer EditMode/harness suite remains under `Assets/Scripts/Testing/MP`. Prefer executable policy, reflection, PlayMode, and matrix assertions over source-string contracts. Source inspection is still acceptable for release compile gates and forbidden dependency checks, but it must not be the only proof of gameplay behavior.

Architecture coverage in `ManagerResponsibilityExtractionEditModeTests` verifies both assembly ownership and the typed service fields wired into the large runtime shells. When adding a new boundary, test the policy behavior and the actual compiled type relationship; do not assert only that a class or method name appears in source.
