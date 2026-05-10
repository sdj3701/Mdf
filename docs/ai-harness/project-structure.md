# MDF Project Structure for Harness Agents

## Baseline detected from ZIP

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
- Project code count under `Assets/Scripts`: about 196 C# files

## Top-level script map

```text
Assets/Scripts/
  AI/                         BehaviorTree, UtilitySystem, MazePlanner
  Button/                     UI button components
  Commands/                   Command pattern: Core, PlayerActions, Sync, AI
  ComponentRegistrySystem/    runtime registry and asset registry
  DB/                         addressable key helper, MiniJSON
  Editor/                     data importer/exporter utilities
  Enums/                      GameEnums, CommandType, stats, monster enums
  Game/                       Units, Monsters, Skills, Battle, Augments, Targeting, Rules
  Interfaces/                 ICommand, IHealth, IEnemy, IMana, IPlacementHandler
  MainLobby/                  character/lobby selection
  Managers/                   GameManagers, PlayerManager, FieldManager, Shop, Combat, Load
  Network/                    NetworkManager, HostMigrationHandler, NetworkPlayer, lobby UI
  UI/                         HUD, ranking, shop, augment, attack sequence UI
  VFX/                        projectile and VFX pooling
```

## Highest-risk files

Large/high-risk files that should be edited narrowly:

```text
FieldManager.cs                       ~4000 lines
GameManagers.cs                       ~2100 lines
Unit.cs                               ~1950 lines
Monster.cs                            ~1680 lines
PlayerManager.cs                      ~1660 lines
HostMigrationHandler.cs               ~1530 lines
MonsterSpawner.cs                     ~1080 lines
NetworkManager.cs                     ~1000 lines
GameManagers.MigrationRecovery.cs      ~880 lines
```

Codex should avoid broad rewrites of these files. Prefer small hooks, extension helpers, or test-only wrapper files.

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

No dedicated test assemblies were detected in `Assets/Tests` in the ZIP. There are test scenes under `Assets/Scenes/Test*`, but the automation harness should create focused EditMode/PlayMode tests rather than depend on manual scenes only.
