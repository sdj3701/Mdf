# MDF Battle Command Model

## Purpose

Battle actions that create persistent gameplay state must become State Authority validated battle commands. RPCs may request actions or play presentation events, but must not be the source of truth for monster pool consumption, monster spawn, scroll inventory, damage, buff, status, zone, HP, gold, or battle state.

Target commands:

- `BattleSpawnMonsterCommand`
- `UseMagicScrollCommand`
- existing `ActivateSkillCommand`, hardened for manual/strategic skill use

## Current Battle Risks

### Monster spawn

Current human attacker path:

```text
AttackSequenceManager.SpawnMonsterAsync
  host: MonsterSpawner.SpawnMonsterAtPositionAsync + TryConsumeMonsterFromPool
  client: TryConsumeMonsterFromPool locally + GameManagers.RPC_RequestSpawnMonster
```

Current AI attacker path:

```text
GameManagers.StartBattleForPlayers
  -> MonsterSpawner.SpawnAllMonstersToTargetField(isAI=true)
  -> StartAutoSpawnFromPool
  -> AIAttackStrategy.BuildSpawnPlan
  -> ExecuteSpawnPlanAsync
  -> SpawnMonsterAtPositionAsync + MonsterPoolEntry.TryConsume
```

Risks:

- Client attacker durably consumes the local pool before server acceptance.
- AI attacker bypasses the command path for strategic spawn decisions.
- Legacy `RPC_RequestSpawnMonster` validates some authority facts, but it accepts monster data name and boss/origin fields from the request shape and does not produce structured accept/reject artifacts.

### Magic scroll

Current path:

```text
AttackSequenceManager.UseMagicScrollAsync
  host: TryConsumeMagicScroll + RPC_BroadcastMagicScrollUsed
  client: RPC_RequestUseMagicScroll
GameManagers.RPC_BroadcastMagicScrollUsed
  -> CreateScrollCasterLocal on all peers
  -> ScrollCaster.CastSkill
  -> SkillEffect.ApplyEffect on all peers
```

Risk:

- The broadcast RPC is not presentation-only today. It creates a caster and applies damage/buff/status/zone effects on every peer.

## Command Execution Scopes

### `ClientRequest`

Used when a real client asks State Authority to perform a battle action.

Examples:

- Human attacker spawn request.
- HumanBot scroll request from a connected build client.
- Manual skill button request.

Rules:

- Must route through the owned `PlayerManager` or another object with validated input authority.
- Must validate `RpcInfo.Source == Object.InputAuthority`.
- Must correct or reject mismatched `playerId`.
- Must not apply durable effects on the requester before acceptance.

### `ServerAuthorityOnly`

Used when State Authority originates a strategic action.

Examples:

- Server AI slot emits `BattleSpawnMonsterCommand`.
- Test automation running on the host emits a battle command under `--mpTest`.

Rules:

- Only valid when `GameManagers.Object.HasStateAuthority`.
- May call an authority executor directly.
- Must still run the same command-specific validation and produce the same logs/result structure as client requests.

### `PresentationOnly`

Used for VFX/UI/audio events after authoritative gameplay execution.

Examples:

- Scroll VFX.
- Skill cast presentation.
- Spawn accepted UI feedback.

Rules:

- Must not change HP, monster pool, scroll inventory, buffs, statuses, zones, gold, or battle state.
- May instantiate non-networked VFX.
- May fire UI events such as `GameEvents.TriggerMagicScrollUsed`.

## Common Battle Validation Helpers

Create a reusable validation surface before adding concrete commands.

Required helpers:

- `IsBattlePhase(GameManagers gm)`
- `ResolveActorPlayer(GameManagers gm, int playerId)`
- `ResolveOpponent(GameManagers gm, PlayerManager actor, int expectedOpponentId)`
- `IsCurrentBattleAttacker(PlayerManager actor)`
- `IsCurrentBattleDefender(PlayerManager actor)`
- `IsServerAiOrTestAuthority(PlayerManager actor, CommandExecutionScope scope)`
- `IsFiniteTargetPosition(Vector3 position)`
- `IsAuthorizedClientSource(PlayerManager actor, RpcInfo info)`
- `IsInsideBattleSpawnZone(FieldManager defenderField, Vector3 position)`
- `IsInsideScrollTargetDomain(MagicScrollData scroll, FieldManager defenderField, Vector3 position)`

Use existing support where practical:

- `GameManagers.TryGetBattleOpponentSnapshot`
- `GameManagers.GetBattleOpponent`
- `GameManagers.TryGetBattleDefenderField`
- `GameManagers.IsWithinFieldOuterBounds`
- `PlayerManager.ValidateBattlePhase`
- `PlayerManager.RPC_RequestCommandToServer` source ownership pattern

## Rejection Result Shape

All battle command validation should return a structured result:

```text
success
errorCode
message
commandType
playerId
opponentPlayerId
scope
source
sequence
```

Suggested type:

```csharp
public readonly struct BattleCommandResult
{
    public readonly bool Success;
    public readonly string ErrorCode;
    public readonly string Message;
    public readonly CommandType CommandType;
    public readonly int PlayerId;
}
```

The result must be logged for accepted and rejected commands. Rejections should be precise enough to debug authority mistakes without dumping tokens or secrets.

## MPTEST Logs

Common command logs:

- `battle_command_request`
- `battle_command_accepted`
- `battle_command_rejected`
- `battle_command_executed`

Spawn-specific logs:

- `battle_spawn_request`
- `battle_spawn_accepted`
- `battle_spawn_rejected`
- `battle_spawn_executed`

Scroll-specific logs:

- `scroll_request`
- `scroll_accepted`
- `scroll_rejected`
- `scroll_effect_applied`
- `scroll_presentation`

Skill-specific logs:

- `skill_command_request`
- `skill_command_accepted`
- `skill_command_rejected`
- `skill_command_executed`

## `BattleSpawnMonsterCommand`

Parameters:

- `attackerPlayerId`
- `defenderPlayerId`
- `poolSlotIndex`
- `spawnWorldPosition`
- `count`, default `1`
- optional `sourceReason` for logs only

Validation:

- current state is `Battle1` or `Battle2`
- attacker exists and is current battle attacker
- defender exists and is the attacker's assigned opponent
- source is attacker input authority, server AI authority, or test authority
- pool slot is in range and non-empty
- pool entry is still available
- target position is finite
- target position is inside defender outer spawn zone or explicitly allowed battle spawn zone
- client request does not provide trusted boss unique id or origin player id

Execution:

- State Authority resolves `MonsterPoolEntry` from `poolSlotIndex`.
- State Authority derives `MonsterData`, boss identity, and origin from authoritative pool state.
- State Authority calls `MonsterSpawner.SpawnMonsterAtPositionAsync`.
- State Authority consumes the pool only after spawn succeeds.
- Clients never call local `Instantiate` or `Runner.Spawn` for persistent battle monsters.

Legacy RPC:

- `RPC_RequestSpawnMonster` should become a wrapper around the command executor or be marked deprecated with no direct execution path.

## `UseMagicScrollCommand`

Parameters:

- `casterPlayerId`
- `scrollSlotIndex` or another stable inventory reference
- `targetWorldPosition`
- optional `targetDomain`/`reason` for logs only

Validation:

- current state is `Battle1` or `Battle2`
- caster exists
- source is caster input authority, server AI authority, or test authority
- caster owns the scroll at the stable slot/reference
- target position is finite
- target position is inside the allowed battle target domain
- `MagicScrollData.skillData` exists

Execution:

- State Authority consumes scroll after validation.
- State Authority applies gameplay effects.
- State Authority emits presentation-only RPC after gameplay execution.
- Clients must not apply damage, heal, buff, status, zone, inventory, or HP changes from the presentation RPC.

## `ActivateSkillCommand`

Current command exists and UI manual skills already use it.

Required hardening:

- battle phase check
- unit belongs to requesting player
- unit NetworkId is valid
- skill is manual or AI-allowed strategic skill
- mana is full and cost can be paid
- cooldown/casting readiness valid
- unit is alive and not disabled/silenced
- target requirements are satisfied

Automatic skills remain inside State Authority simulation and must not become command spam.

## Snapshot Fields Needed

Battle:

- current state, round, battle phase
- battle opponent hash
- match first attacker hash
- battle active role hash

Commands:

- `acceptedBattleCommandSeq`
- `spawnMonsterSeq`
- `useMagicScrollSeq`
- `activateSkillSeq`
- rejected battle command counts by type/reason, if practical

Monster:

- alive count
- type/count/HP-bucket hash
- owner origin hash
- target player/field hash
- boss/pool identity hash when available

Player battle inventory:

- `attackMonsterPoolHash`
- `ownedScrollsHash`

Effects:

- `activeBuffHash`
- `activeStatusHash`
- `zoneHash` if practical

## E2E Proof

Required tests:

- `run_battle_spawn_monster_command.py --seed 6102`
- `run_magic_scroll_command.py --seed 6201`
- `run_human_bot_battle_progression.py --seed 6101`
- progressed reconnect after battle
- progressed Host Migration after battle
- battle seed sweep

Do not assert fixed random outcomes. Assert that the same player's replicated outcomes and battle command results match across peers.
