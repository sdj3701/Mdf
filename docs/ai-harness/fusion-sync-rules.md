# MDF Fusion Sync Rules

## Core rule

MDF is a Photon Fusion 2 Host/Client game. The server/State Authority must decide persistent gameplay state. Clients may request actions, but requests are not facts.

## Identity

- `PlayerRef` is the current Fusion peer/connection identity.
- `PlayerManager.playerId` is the durable gameplay field/player id.
- Reconnect and host migration must use connection token/cache data plus `playerId`, not raw `PlayerRef` equality.
- Snapshot comparisons may display `PlayerRef`, but must not require it to remain unchanged across reconnect/migration.

## Authority

State Authority owns and validates:

- `GameManagers.currentState`, `currentRound`, `phaseTimer`, `FirstAttackerPlayerId`
- battle pairing and first-attacker mapping
- player HP, gold, wall count, shop snapshot, augment choices
- unit purchase, placement, move, sell, star merge
- wall placement/removal/permanent wall sync
- monster spawn, boss data, survivor boss routing
- magic scroll use and broadcast result
- game-over/elimination state

Clients may request these actions, but must not directly finalize durable state.

## Command path

Preferred action path:

```text
UI/AI -> ICommand -> CommandProcessor.RequestCommandExecution
  -> local authority path or PlayerManager.RPC_RequestCommandToServer
  -> server validates/corrects authoritative playerId
  -> GameManagers.RPC_BroadcastCommandToClients
  -> all peers CommandProcessor.ReceiveAndEnqueueCommand
  -> ProcessCommands -> Execute
```

Do not add new gameplay actions only to `NetworkManager.RPC_RequestCommandToServer`; that path is legacy/older compared with the current PlayerManager/GameManagers route.

## Battle command guardrails

Strategic battle actions must use the State Authority validated battle command path:

- AI and HumanBot attacker monster spawn decisions emit `BattleSpawnMonsterCommand`.
- Human attacker monster spawn requests use `RPC_RequestBattleSpawnMonster` or the same server authority executor.
- `SpawnMonsterAtPositionAsync` is the low-level spawn mechanism only. AI planning, Behavior Tree, HumanBot, UI, or test policy code must not call it directly for strategic spawn decisions.
- Attack monster pool slot consumption happens after `BattleSpawnMonsterCommand` validation. Do not consume pool slots on clients before server acceptance.
- Magic scroll decisions emit `UseMagicScrollCommand` with a stable slot/reference and observed inventory revision.
- `RPC_BroadcastMagicScrollUsed` and scroll presentation helpers are presentation-only. They may spawn VFX, but must not call gameplay `CastGameplay`, `CastSkill`, `ApplyEffect`, consume scroll inventory, or change HP/status/buff/zone state.
- Manual or AI/HumanBot strategic skill decisions emit `ActivateSkillCommand`. Automatic unit skills remain State Authority simulation and must not become command spam.
- HumanBot is a real connected human peer. It must not attach or register `AIPlayerController`.

`tools/harness/precommit.py` BLOCKs clear unsafe patterns for these rules and WARNs review-only patterns such as direct low-level spawn use outside the approved command/mechanism files.

## RPC rules

- `RPC_Request*`: client/input authority to State Authority.
- `RPC_Broadcast*`: State Authority to all peers.
- `RPC_Notify*`: State Authority result notification.
- `RpcSources.All` must manually validate `RpcInfo.Source` or equivalent authority/ownership data.
- Persistent state must not exist only as an RPC side effect. Add `[Networked]`, deterministic rebuild, or state snapshot coverage.

Photon Fusion documentation describes RPCs as punctual events and `[Networked]` properties as the correct tool for continuously changing shared state.

## State snapshots

Snapshot fields should be comparable after stable waits. Include aggregate hashes/counts for large collections.

Must include:

- runner/session/scene/tick/role
- GameManagers state/round/timer
- players by `playerId`
- HP/gold/walls/AI/connection/authority summary
- fields: placed units, wall hashes, path readiness
- shop/augment summary
- monsters/alive counts
- battle pairing/first attacker
- host migration status
- command sequence and last received command if implemented

## Host migration

Host Migration PASS requires evidence of:

- `NetworkManager.OnHostMigration` callback
- `HostMigrationHandler.StartMigration`
- `HostMigrationToken` received
- old runner shutdown with `ShutdownReason.HostMigration`
- new runner `StartGame` with `HostMigrationToken`
- `HostMigrationResume`
- `GameManagers.RestoreAfterHostMigration`
- restored players/fields/walls/AI/match state
- post-migration snapshot matches expected durable state

Normal `/quit` is not Host Migration proof.

## Common warnings

Warn, do not automatically block, when static analysis sees:

- `RpcSources.All` without `RpcInfo` validation.
- `playerId` from client input used before correction.
- `Update()` mutating networked gameplay state.
- UI updated from request event instead of authority result.
- new command class without `CommandType`/serialize/deserialize branches.
- `PlayerRef` stored as long-term ownership.
- Host Migration changes touching only one of `NetworkManager`, `HostMigrationHandler`, `GameManagers.MigrationRecovery`.
- direct low-level monster spawn or scroll gameplay calls outside the battle command executor path.

## Vendor boundaries

Do not edit:

- `Mdfproject/Assets/Photon/Fusion/**`
- Firebase plugin files
- TMP packages/assets
- Toon Shader Samples
- generated csproj/sln files

Use wrappers or project scripts instead.
