# MDF Behavior Tree v2 Design

## Purpose

Behavior Tree v2 separates decision policy from command emission. AI slots and the test-only HumanBot can share policy code, but they must submit decisions through different emitters:

- Server AI slots may run on State Authority and emit server-authority commands.
- HumanBot remains a real connected human peer, keeps `isAI=false`, and emits through the normal client request path.
- Test automation can emit bounded commands only under `--mpTest` gates.

Status note, 2026-05-08:

- `AIPlayerController` and `MPTestHumanBotDriver` now use shared `MdfBotProfile`, `PrepareDecisionPolicy`, and `BattleDecisionPolicy`.
- The only intended runtime difference is the emitter: server AI uses `ServerAiCommandEmitter`; HumanBot uses `HumanClientCommandEmitter`.
- The obsolete `MPTestHumanBotPolicy` adapter has been removed; HumanBot work uses the shared policies directly.
- Strategic AI battle spawn plans are converted into `BattleSpawnMonsterCommand` submissions by `BattleDecisionPolicy`; `SpawnMonsterAtPositionAsync` remains a low-level `MonsterSpawner` mechanism and command executor detail only.
- Real AI battle auto-spawn bootstrap from `GameManagers.StartBattleForPlayers` / migration rebootstrap is disabled; actual AI must not bypass `BattleDecisionPolicy`.
- Magic scroll gameplay goes through `UseMagicScrollCommand`; presentation RPCs must remain presentation-only.

The historical repo had a useful prepare-phase AI path:

```text
AIPlayerController
  -> prepare BehaviorTree
  -> ChooseBestAugmentAction / BuildMazeAction / BuyBestUnitAction / RearrangeAllUnitsAction / RerollShopAction
  -> CommandProcessor.RequestCommandExecution
```

The older combat BehaviorTree was empty, and strategic battle behavior entered through `MonsterSpawner.StartAutoSpawnFromPool`. V2 keeps the planning value from `AIAttackStrategy`, but the plan is now expected to become a sequence of battle commands.

## Current Paths

- `Mdfproject/Assets/Scripts/Commands/AI/AIPlayerController.cs` registers the player id in `ComponentRegistry`, creates a default `MdfBotProfile`, and ticks shared prepare/battle policies.
- `Mdfproject/Assets/Scripts/Testing/MP/MPTestHumanBotDriver.cs` drives a local input-authority player through shared prepare/battle policies and `HumanClientCommandEmitter`.
- No HumanBot compatibility adapter remains between the driver and the shared decision policies.
- `Mdfproject/Assets/Scripts/AI/BehaviorTree/Nodes/Actions/AIAttackStrategy.cs` builds `AISpawnPlan`/`AISpawnPhase`/`AISpawnOrder` from the attack monster pool and defender field.
- `MonsterSpawner.ExecuteSpawnPlanAsync` converts `AISpawnOrder` entries into `BattleSpawnMonsterCommand`; pool consumption is owned by the command.

## New Shared Decision Model

### `MdfDecisionContext`

Read-only context passed to policies.

Required fields:

- `GameManagers Game`
- `PlayerManager Actor`
- `PlayerManager Opponent`
- `FieldManager OwnField`
- `FieldManager OpponentField`
- `CommandExecutionScope PreferredScope`
- `bool IsHumanBot`
- `bool IsServerAi`
- `bool IsTestAutomation`
- `int PlayerId`
- `int Round`
- `GameManagers.GameState GameState`
- `bool IsBattlePhase`
- `bool IsCurrentBattleAttacker`
- `bool IsCurrentBattleDefender`
- `IReadOnlyList<MonsterPoolEntry> AttackMonsterPool`
- `IReadOnlyList<MagicScrollData> OwnedScrolls`
- observed hashes for journal output: shop, augment, wall, unit, battle map, attack pool, owned scrolls

Rules:

- It is a snapshot of observations, not a mutable gameplay object bag.
- It may expose managers so evaluators can query current state, but policies must not mutate gameplay state directly.
- It must never use `PlayerRef` as durable identity. Use `playerId` and connection token hashes for journal/snapshot identity.

### `MdfDecision`

A policy result.

Required fields:

- `string DecisionType`
- `CommandType? CommandType`
- `ICommand Command`
- `int ActorPlayerId`
- `int? OpponentPlayerId`
- `float Score`
- `string Reason`
- `string TargetSummary`
- `Vector3? TargetWorldPosition`
- `int? PoolSlotIndex`
- `int? ScrollSlotIndex`
- `uint? UnitNetworkId`
- `CommandExecutionScope Scope`
- `Dictionary<string, object> JournalFields`

Rules:

- A decision describes intent and contains a command when an action is chosen.
- Observe/noop decisions have no command and must still be journalable.
- Random tie-breaks are allowed for pacing and policy choice, but persistent random outcomes remain State Authority owned.

### `IMdfDecisionPolicy`

```csharp
public interface IMdfDecisionPolicy
{
    bool TryChoose(MdfDecisionContext context, out MdfDecision decision);
}
```

Policies:

- `PrepareDecisionPolicy`
- `BattleDecisionPolicy`
- `DefenderSkillPolicy`
- `ScrollTargetEvaluator` used by `BattleDecisionPolicy`

### `MdfCommandEmitter`

Base emitter contract:

```csharp
public interface MdfCommandEmitter
{
    bool TryEmit(MdfDecision decision, out BattleCommandResult result);
}
```

Concrete emitters:

- `HumanClientCommandEmitter`
  - Used by HumanBot and real client UI adapters.
  - Calls `CommandProcessor.RequestCommandExecution`.
  - Must resolve the local input-authority `PlayerManager`.
  - Must not attach or register `AIPlayerController`.
- `ServerAiCommandEmitter`
  - Used by server-owned AI slots.
  - Emits only from State Authority.
  - Can route server-authority commands directly through the authoritative executor or through `CommandProcessor` when the command is broadcast-backed.
- `TestAutomationCommandEmitter`
  - Used only under `UNITY_EDITOR || DEVELOPMENT_BUILD` and `--mpTest`.
  - Must require loopback/token-gated automation entry points where called from the build automation server.

## PrepareDecisionPolicy

Initial adapter can wrap existing behavior:

1. Select augment if presented.
2. Build maze using `MazePlanner`.
3. Buy best affordable unit from the current shop snapshot.
4. Move/rearrange units.
5. Reroll shop when no better purchase is available.
6. Observe.

Existing AI action classes may remain as adapters, but the current target shape is one shared policy used by both:

```text
AIPlayerController -> MdfBotProfile -> PrepareDecisionPolicy -> ServerAiCommandEmitter
MPTestHumanBotDriver -> MdfBotProfile -> PrepareDecisionPolicy -> HumanClientCommandEmitter
```

HumanBot must remain test-only and human:

- no `AIPlayerController` component
- no `ComponentRegistry.Register<AIPlayerController>`
- snapshot `isAI == false`

## BattleDecisionPolicy

Attacker behavior:

1. If a valuable scroll opportunity exists, produce `UseMagicScrollCommand`.
2. Else if there is a monster spawn order available, produce `BattleSpawnMonsterCommand`.
3. Else observe.

Defender behavior:

1. If a manual skill opportunity exists, produce `ActivateSkillCommand`.
2. Else observe.

The policy must not call:

- `MonsterSpawner.SpawnMonsterAtPositionAsync`
- `PlayerManager.TryConsumeMonsterFromPool`
- `PlayerManager.TryConsumeMagicScroll`
- `ScrollCaster.CastSkill`
- `Unit.ActivateSkill`

Those happen in State Authority validated command execution or existing State Authority simulation.

## Decision Journal

Every bot/AI/test decision should be journalable with:

- `persona`
- `gameState`
- `round`
- `playerId`
- `opponentPlayerId`
- observed hashes: shop, augment, field wall, placed units, attack pool, owned scrolls, battle map
- `decisionType`
- `commandType`
- `score`
- `reason`
- command target summary
- rejection reason if applicable

Do not use raw Unity instance IDs in journals meant for cross-peer equality. Network ids may be diagnostic only unless the snapshot comparison explicitly supports them.

## Migration Strategy

1. Keep the current prepare BT behavior as the baseline.
2. Introduce the shared decision model and emitters behind adapters.
3. Convert battle monster spawn to commands first.
4. Convert scroll use to commands and separate presentation.
5. Add scroll targeting and defender skill policy.
6. Switch `AIPlayerController` and `MPTestHumanBotDriver` to shared policies.

## E2E Requirements

Behavior Tree v2 is not complete until artifacts prove:

- HumanBot remains human and can still progress Prepare through the real client path.
- AI attacker and human attacker both use `BattleSpawnMonsterCommand`.
- Scroll decisions use `UseMagicScrollCommand`.
- Manual defender skills use `ActivateSkillCommand`; automatic unit skills remain State Authority simulation.
- Host/client/build/editor snapshots agree under random-aware comparison.
