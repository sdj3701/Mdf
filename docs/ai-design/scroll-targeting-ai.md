# MDF Scroll Targeting AI

## Purpose

AI and HumanBot battle policies need a shared evaluator for magic scroll targets. The evaluator chooses target positions only. It does not consume scroll inventory, apply effects, spawn VFX, or mutate gameplay state.

`UseMagicScrollCommand` remains the authority gate for validation and execution.

## Current Scroll Model

`MagicScrollData` currently contains:

- `scrollName`
- `description`
- `icon`
- `tier`
- `skillData`

`SkillData` contains:

- `range`
- `targetingStrategy`
- `effects`
- `vfxPrefab`

Legacy scroll execution created a `ScrollCaster` and ran `SkillEffect.ApplyEffect` through `RPC_BroadcastMagicScrollUsed` on all peers. The current command architecture splits this into:

- server-only gameplay application through `UseMagicScrollCommand` and `ScrollCaster.CastGameplay`
- presentation-only client VFX/event broadcast through `RPC_BroadcastMagicScrollUsed` and presentation helpers

Do not reintroduce gameplay effects, inventory consumption, HP/status/buff/zone mutation, or `CastGameplay` calls into presentation RPC/helpers.

## Required Scroll Metadata

Add metadata to `MagicScrollData` or a sidecar catalog:

```csharp
public enum MagicScrollTacticalRole
{
    Damage,
    Debuff,
    Buff,
    Heal,
    Utility
}

public enum MagicScrollTargetDomain
{
    EnemyUnits,
    AlliedMonsters,
    EnemyUnitsNearAlliedMonsters,
    GroundPoint
}
```

Fields:

- `tacticalRole`
- `targetDomain`
- `canAiUse`
- `aiMinValue`

These fields classify AI use and validation domains. They are not trusted when submitted by a client command; State Authority reads them from authoritative asset data.

## BattleHeatmap

`BattleHeatmap` is a read-only query object created from an attacker, defender, and field state.

Inputs:

- attacker `PlayerManager`
- defender `PlayerManager`
- defender `FieldManager`
- attacking monsters visible on defender field
- defender units on defender field
- optional goal/choke/path data from `FieldManager` and `AstarGrid`

Queries:

- allied attacking monster clusters
- opponent defender unit clusters
- engagement clusters where attacking monsters and defender units overlap or approach
- boss/destroyer/tank clusters
- low-HP or high-value defender unit candidates
- near-goal clusters
- valid ground target samples

Rules:

- Use stable semantic data: unit data name/type, monster data key/type/traits, player id, HP buckets, grid/world centers.
- Do not use raw Unity instance IDs for equality or scoring output.
- Do not mutate field, unit, monster, HP, buff, status, or inventory state.

## ScrollTargetEvaluator

Suggested API:

```csharp
public readonly struct ScrollTargetResult
{
    public readonly bool Success;
    public readonly Vector3 GameplayPosition;
    public readonly Vector3 VisualPosition;
    public readonly float Score;
    public readonly string Reason;
    public readonly IReadOnlyDictionary<string, object> JournalFields;
}

public sealed class ScrollTargetEvaluator
{
    public bool TryFindBestTarget(
        MdfDecisionContext context,
        MagicScrollData scroll,
        BattleHeatmap heatmap,
        out ScrollTargetResult result);

    public ScrollTargetResult EvaluateDamageOrDebuffTarget(...);
    public ScrollTargetResult EvaluateBuffTarget(...);
}
```

The gameplay target position should be the field/collider center used for physics/targeting. The visual position may be offset upward for VFX only.

## Offensive And Debuff Targeting

For `Damage` and `Debuff` roles:

Primary score:

- maximize defender units inside the skill radius

Secondary score:

- prefer target points near allied monster clusters or active engagements

Tertiary score:

- prefer high-value defender units
- prefer low-HP units when the effect is finishing damage
- prefer clustered ranged units if available

Example score components:

```text
score =
  defenderUnitCount * 100
  + defenderUnitValue * 15
  + engagementOverlapCount * 35
  + alliedMonsterNearbyCount * 10
  + lowHpFinishBonus
  - emptyAreaPenalty
```

Allowed target domains:

- `EnemyUnits`
- `EnemyUnitsNearAlliedMonsters`
- `GroundPoint` only if the scroll is intentionally area-based and validation allows it

## Buff And Heal Targeting

For `Buff` and monster-side `Heal` roles:

Primary score:

- maximize allied attacking monster count/value inside radius

Secondary score:

- prefer boss, destroyer, tank, or engaged monster clusters

Tertiary score:

- prefer near-goal or high-impact groups
- prefer injured allied monsters for heal effects

Example score components:

```text
score =
  alliedMonsterCount * 100
  + bossCount * 80
  + destroyerCount * 50
  + tankCount * 30
  + engagedMonsterCount * 25
  + nearGoalBonus
  + injuredMonsterBonus
```

Allowed target domains:

- `AlliedMonsters`
- `GroundPoint` only when the scroll creates an allied zone or aura

## Utility Targeting

Utility scrolls need explicit metadata before AI uses them. If a utility scroll cannot be valued safely, set `canAiUse=false`.

Examples:

- zone control: target engagement cluster
- slow/root: target defender cluster near allied monsters
- displacement: require a later explicit policy

## MPTEST And Journal Output

Evaluator output should include:

- scroll asset key/name
- role and target domain
- selected slot index
- gameplay position
- visual position
- radius
- score
- reason
- counted defender units
- counted allied monsters
- engagement count
- high-value/low-HP counts when used

Logs:

- `scroll_target_evaluated`
- `scroll_target_selected`
- `scroll_request`
- `scroll_accepted`
- `scroll_effect_applied`

## Snapshot Support

Validation needs:

- `ownedScrollsHash`
- `scrollUseSeq`
- `activeStatusHash`
- `activeBuffHash`
- `zoneHash` if practical
- monster type/target hashes
- defender placed-unit hash

## Limitations

- Exact target positions should not be compared with high precision across peers.
- Snapshot comparison should use semantic hashes and coarse buckets.
- The evaluator should tolerate no valid target and return observe/noop.
- AI should not use a scroll when score is below `aiMinValue`.
