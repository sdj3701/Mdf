# MDF Skill Command Policy

## Purpose

MDF has two skill categories:

- automatic unit skills that are part of State Authority combat simulation
- manual or strategic skills that are player/AI decisions and must use `ActivateSkillCommand`

Do not turn automatic attack, mana, tick, or auto-skill behavior into command spam.

## Current State

Automatic path:

- `Unit.HandleManaFull` checks `SkillActivationType.Automatic`.
- State Authority calls `Unit.ActivateSkill` when mana and target conditions are met.
- `Unit.ActivateSkill` checks combat phase, State Authority, casting, silence/stun through `BuffManager.CanUseSkill`, loaded skill data, and mana.

Manual UI path:

- `StatusBarUI.InitializeSkillButton` shows a button for local-player-owned manual skills.
- `StatusBarUI.RequestSkillActivation` creates `ActivateSkillCommand(owner.Owner.playerId, owner.Object.Id.Raw)`.
- `CommandProcessor.RequestCommandExecution` submits it.
- `PlayerManager.ValidateActivateSkillRequest` currently checks battle phase, unit NetworkId, and ownership.
- `ActivateSkillCommand.Execute` finds the `NetworkObject` and calls `Unit.ActivateSkill`.

## Policy Boundary

Automatic skills:

- remain in `Unit` State Authority simulation
- no HumanBot/AI command decision
- no client prediction of gameplay effects

Manual/strategic skills:

- real player UI uses `ActivateSkillCommand`
- HumanBot uses shared defender policy and `HumanClientCommandEmitter`
- server AI uses shared defender policy and `ServerAiCommandEmitter`
- test automation uses `TestAutomationCommandEmitter` under `--mpTest`

## ActivateSkillCommand Hardening

Validation should include:

- current state is `Battle1` or `Battle2`
- requesting player exists and is the owner of the unit
- RPC source owns the requesting `PlayerManager` when the request came from a client
- unit `NetworkId` exists and resolves to `Unit`
- unit belongs to the player field/owner
- unit is alive
- unit is in combat phase
- skill exists and `SkillData` is loaded or loadable
- skill is `Manual`, or explicitly marked AI-allowed strategic skill
- mana is full and `manaCost` can be paid
- unit is not already casting
- `BuffManager.CanUseSkill` is true
- target requirements are currently satisfiable

Execution should happen on State Authority. Presentation VFX may be broadcast separately later if needed, but durable damage/heal/buff/status effects must not be client-side RPC side effects.

## DefenderSkillPolicy

Purpose:

- identify high-impact manual skill opportunities for defenders
- emit `ActivateSkillCommand` only when the command is likely valid

Inputs:

- defending player
- allied units on field
- attacking monsters on the defender field
- unit mana/readiness
- skill range/effects/targeting strategy
- battle heatmap

Candidate filters:

- unit is alive and owned by defender
- `currentSkillActivationType == SkillActivationType.Manual`
- unit has a valid `NetworkObject`
- mana full
- not already casting
- can use skill according to `BuffManager`
- at least one target or useful zone target exists

Scoring:

```text
score =
  targetsInRange * 100
  + bossThreatBonus
  + nearGoalThreatBonus
  + lowHpAllySaveBonus
  + highValueTargetBonus
  - overkillOrEmptyPenalty
```

Result:

- `MdfDecision` with `CommandType.ActivateSkill`
- `unitNetworkId`
- score/reason
- journal fields describing target opportunity

## AI/HumanBot Use

```text
BattleDecisionPolicy
  defender branch
    -> DefenderSkillPolicy.TryChoose
    -> ActivateSkillCommand
```

Emitters:

- HumanBot: `HumanClientCommandEmitter` through real client request path
- AI slot: `ServerAiCommandEmitter` on State Authority
- test automation: `TestAutomationCommandEmitter` only under `--mpTest`

## MPTEST Logs

Add logs:

- `skill_command_request`
- `skill_command_accepted`
- `skill_command_rejected`
- `skill_command_executed`
- `defender_skill_evaluated`
- `defender_skill_selected`

Each rejection should include:

- player id
- unit network id
- error code
- command type
- source/scope, without secrets

## Snapshot Fields

Useful fields:

- `activateSkillSeq`
- `manualSkillReadyHash`
- `activeSkillEffectHash`
- `activeBuffHash`
- `activeStatusHash`

The snapshot should not require exact VFX object equality.

## Non-Goals

- Do not commandize normal basic attacks.
- Do not commandize automatic mana-full skills.
- Do not use AI to directly call `Unit.ActivateSkill`.
- Do not let clients apply durable skill effects from presentation RPCs.
