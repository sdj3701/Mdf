# MDF Battle Command / Behavior Tree v2 Implementation Order

## Gate Rule

Proceed one phase at a time. Do not advance until the current phase has the required command output and artifact-backed PASS, or an exact `FAIL`, `BLOCKED`, `NEEDS_IMPLEMENTATION`, or `NEEDS_ENVIRONMENT` reason.

`python tools/harness/precommit.py --all` is required after every code/doc phase in this plan. Unity compile/tests are required after C# or Unity asset changes.

## Phase 1: Design Docs

Files:

- `docs/ai-design/behavior-tree-v2.md`
- `docs/ai-design/battle-command-model.md`
- `docs/ai-design/scroll-targeting-ai.md`
- `docs/ai-design/skill-command-policy.md`
- `docs/ai-design/implementation-order.md`

Verification:

- `python tools/harness/precommit.py --all`

Stop after docs.

## Phase 2: Battle Command Foundation

Implement shared infrastructure only:

- `CommandExecutionScope`
- battle validation helpers
- server-authority battle command executor skeleton
- `BattleCommandResult`
- MPTEST command logs

Do not implement actual monster spawn or scroll effects yet.

Verification:

- precommit
- Unity status/compile/console
- EditMode tests
- Fusion review

## Phase 3: BattleSpawnMonsterCommand

Implement:

- `CommandType.BattleSpawnMonster`
- `BattleSpawnMonsterCommand`
- serialization/deserialization or server-only routing
- authority validation
- State Authority-only spawn execution
- AI spawn plan emission through commands
- human attack sequence emission through commands
- snapshot fields for pool and spawned monster state

Smallest E2E:

- battle/HumanBot progression case if available
- otherwise report `NEEDS_IMPLEMENTATION` for E2E and do not claim final battle PASS

## Phase 4: Spawn Audit

Audit and harden:

- no strategic AI direct calls to `SpawnMonsterAtPositionAsync`
- human and AI use command/executor
- no client durable pool consumption before acceptance
- legacy RPC is wrapper or deprecated
- spawn logs and snapshots are sufficient

Run:

- `run_battle_spawn_monster_command.py --seed 6102` if available

## Phase 5: UseMagicScrollCommand

Implement:

- `CommandType.UseMagicScroll`
- `UseMagicScrollCommand`
- stable scroll slot/reference
- authority validation
- server-only scroll inventory consumption
- server-only gameplay effects
- presentation-only scroll RPC
- tactical metadata if needed
- snapshot fields for scroll/effect state

Run battle scroll or HumanBot battle progression E2E if available.

## Phase 6: Scroll Asset And Effect Audit

Audit:

- all `MagicScrollData` assets
- tactical metadata
- `ScrollCaster`
- `SkillEffect` implementations
- targeting strategies
- VFX/presentation path

If asset YAML changes:

- reserialize changed assets
- compile and console check
- asset guard review

## Phase 7: ScrollTargetEvaluator And BattleHeatmap

Implement read-only targeting:

- allied monster clusters
- defender unit clusters
- engagement clusters
- offensive/debuff rules
- buff/heal rules
- score/reason journal fields

Evaluator must not mutate gameplay state.

## Phase 8: Defender Skill Policy

Harden `ActivateSkillCommand` and add:

- `DefenderSkillPolicy`
- manual/strategic skill scoring
- logs
- snapshot fields if practical

Automatic skills stay State Authority simulation.

## Phase 9: Behavior Tree v2

Implement shared decision/emitter architecture:

- `MdfDecisionContext`
- `MdfDecision`
- `IMdfDecisionPolicy`
- `MdfCommandEmitter`
- `HumanClientCommandEmitter`
- `ServerAiCommandEmitter`
- `TestAutomationCommandEmitter` if useful
- `PrepareDecisionPolicy`
- `BattleDecisionPolicy`

Convert:

- `AIPlayerController`
- `MPTestHumanBotDriver`

Preserve current prepare behavior as much as possible.

## Phase 10: Battle Snapshot Coverage

Add/verify:

- battle command sequences
- spawn sequence
- scroll sequence
- activate skill sequence
- attack monster pool hash
- owned scrolls hash
- monster type/origin/target hashes
- active buff/status/zone hashes where practical

Update comparison without weakening existing random-aware checks.

## Phase 11: Battle E2E Harness

Add/update:

- `run_human_bot_battle_progression.py`
- `run_battle_spawn_monster_command.py`
- `run_magic_scroll_command.py`
- `run_progressed_host_migration_after_battle.py`
- `run_matrix.py`

Artifacts:

- command transcript
- snapshots
- comparison JSON
- `[MPTEST]` timeline
- screenshots if available
- failure summary

## Phase 12: Post-Battle Reconnect / Disconnect / Host Migration / Seed Sweep

Add/update:

- progressed reconnect after battle
- progressed disconnect after battle
- progressed Host Migration after battle
- battle seed sweep

Rules:

- kill/drop host for Host Migration proof
- do not use graceful `/quit` as migration proof
- do not hide post-checkpoint divergence as randomness
- add `--mpTest` freeze only when preserving checkpoint state is the scenario purpose

## Phase 13: Guardrails

Update:

- `fusion-sync-rules.md`
- `feature-implementation-loop.md`
- `learned-recipes.md`
- `tools/harness/precommit.py`
- relevant `.agents/skills`

Guard against:

- AI direct strategic spawn bypass
- scroll gameplay in presentation RPC
- scroll use without command
- battle monster spawn without command
- strategic manual skill bypass
- HumanBot registering as AI
- missing State Authority validation

Use BLOCK only for clear unsafe patterns; WARN for heuristic checks.

## Phase 14: Final Audit

Run all final audit commands from the phase prompt.

Overall PASS requires:

- zero precommit BLOCK errors
- Unity compile/console/EditMode proof
- battle spawn command E2E artifact
- magic scroll command E2E artifact
- HumanBot battle progression artifact
- progressed Host Migration after battle PASS or exact blocker
- reconnect after battle PASS or exact blocker
- battle seed sweep report or exact environment blocker

Do not claim final PASS when any required command or artifact is missing.

## First Safe Implementation Slice

Phase 2 is the first safe code slice:

- add battle command scope/result/helper types
- add MPTEST logging helpers
- no gameplay behavior change
- no new spawn or scroll execution

This gives later phases a shared validation and artifact vocabulary before changing persistent battle behavior.
