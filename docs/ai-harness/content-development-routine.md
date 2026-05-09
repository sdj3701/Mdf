# MDF Content Development Routine

## Purpose

Short gameplay, content, AI, UI, and network feature requests must automatically expand into the MDF content feature workflow. Use `.agents/skills/mdf-content-feature/SKILL.md` when available. The user does not need to paste the long feature-loop template.

Examples that trigger this routine include:

- "새 유닛 추가해줘"
- "화염 스크롤 만들어줘"
- "AI가 보스를 아껴 쓰게 해줘"
- "새 증강 추가해줘"
- "상점 밸런스 바꿔줘"
- "몬스터 하나 추가해줘"
- "3라운드까지 버티는 AI 만들어줘"
- "add a unit"
- "make a scroll"
- "improve AI"
- "balance the shop"

Codex must expand the short request internally, choose the smallest verification profile that proves the change, and report the completion contract before claiming PASS.

Do not ask the user to paste the long template.

## Feature Classification

Classify every request before editing. Multiple categories may apply.

| Category | Meaning |
| --- | --- |
| `code-only/internal` | Internal code, tooling, or docs with no Unity asset or multiplayer-visible runtime behavior. |
| `Unity asset/content` | Prefab, scene, ScriptableObject, material, controller, Addressables, or serialized content changes. |
| `multiplayer-visible` | Any runtime behavior visible to more than one peer or captured in replicated snapshots. |
| `command/authority` | Client requests, commands, RPCs, State Authority validation, command serialization, or durable gameplay writes. |
| `battle-visible` | Battle entry, monster spawn, scrolls, skills, effects, target selection, battle pairing, or battle snapshots. |
| `AI/HumanBot` | AI behavior, shared decision policy, HumanBot, bot personas, or test-only bot command emission. |
| `prepare-phase` | Shop, augment, unit purchase, placement, walls, reroll, maze, or Prepare flow. |
| `random outcome` | Shop, augment, wall generation, battle pairing, spawn choices, rewards, or stochastic AI decisions. |
| `persistent state` | `[Networked]` fields, deterministic rebuild state, snapshots, save-like state, or durable round/player state. |
| `reconnect/Host Migration sensitive` | Player identity, reconnect, disconnect/AI takeover, Host Migration, runner recovery, or migration snapshots. |
| `long-progression sensitive` | Requests that need confidence through multiple rounds or midgame state. |
| `endurance/game-to-end sensitive` | Explicit game-to-end, GameOver, soak, or bounded endurance requests. |

## Automatic Read Set

Always read:

- `AGENTS.md`
- `.agent/rules/projectrull.md`
- `docs/ai-harness/content-development-routine.md`
- `docs/ai-harness/feature-implementation-loop.md`
- `docs/ai-harness/fusion-sync-rules.md`
- `docs/ai-harness/state-snapshot-schema.md`
- `docs/ai-harness/learned-recipes.md`
- `docs/ai-harness/recipe-lifecycle.md`

For AI or battle work, also read:

- `docs/ai-design/behavior-tree-v2.md`
- `docs/ai-design/battle-command-model.md`
- `docs/ai-design/scroll-targeting-ai.md`
- `docs/ai-design/skill-command-policy.md`

For random or progression work, also read:

- `docs/ai-harness/randomized-progression-test-plan.md`
- `docs/ai-harness/human-bot-driver-design.md`

For Host Migration or reconnect work, also read:

- `docs/ai-harness/host-migration-test-plan.md`
- `docs/ai-harness/mp-test-protocol.md`

For long or endurance work, also read when present:

- `docs/ai-harness/long-progression-test-plan.md`
- `tools/harness/mp/run_human_bot_3round_progression.py`
- `tools/harness/mp/run_human_bot_game_to_end.py`

## Workflow

1. Classify the request using the categories above.
2. Read the automatic docs for the selected categories.
3. Map the real code/content path before editing.
4. Identify the State Authority owner for durable gameplay state.
5. Identify command and RPC paths for strategic actions.
6. Identify snapshot fields and comparison rules needed to prove sync.
7. Identify Unity asset changes and reserialize needs.
8. Identify HumanBot or AI policy usage.
9. Choose the smallest relevant verification profile; use `python tools/harness/mp/select_verification_profile.py` when the mapping is not obvious.
10. Implement minimally and keep vendor files untouched.
11. Verify, inspect artifacts, and touch learned recipe lifecycle metadata when a recipe was used or verified.

## Verification Profiles

Use `docs/ai-harness/verification-profile-selector.md` and the current `tools/harness/mp/run_matrix.py` profile definitions.

| Profile | Current cases in `run_matrix.py` | Use when |
| --- | --- | --- |
| `smoke` | `editor-host-build-client`, `build-host-editor-client`, `build-host-build-client` | Cheap multiplayer-visible launch/session/Game-scene proof. |
| `regression` | `smoke`, `ai-fill-smoke`, `disconnect-ai-takeover`, `same-token-reconnect`, `four-player-smoke`, `human-bot-prepare` | Broader non-long regression. |
| `battle` | `battle-spawn-monster-command`, `magic-scroll-command`, `human-bot-battle-progression` | Battle, monster, scroll, skill, effect, or battle command changes. |
| `lifecycle` | `progressed-reconnect-after-battle`, `progressed-disconnect-after-battle`, `progressed-host-migration-after-battle` | Persistent state, reconnect, disconnect/AI takeover, or Host Migration-sensitive changes. |
| `random-aware` | `human-bot-prepare`, `human-bot-4p-progression`, progressed lifecycle cases, `human-bot-seed-sweep` | Random content, Prepare/AI progression, or replicated random outcome changes. |
| `long` | `human-bot-3round-progression` | 3-round or midgame confidence. |
| `long-lifecycle` | `3round-reconnect`, `3round-disconnect-ai-takeover`, `3round-host-migration` | Reconnect/disconnect/Host Migration after a 3-round checkpoint. |
| `full-regression` | `regression`, `battle`, `lifecycle`, `long` | Merge or large changes that must include long progression. |
| `nightly` | `regression`, `battle`, `lifecycle`, `battle-seed-sweep`, `human-bot-seed-sweep` | Current pre-long nightly set. Do not confuse with `full-regression`; it excludes `long` and `endurance`. |
| `endurance` | `human-bot-game-to-end` | Explicit game-to-end or endurance requests only. |

Selection guidance:

- `code-only/internal`: `python tools/harness/precommit.py --all`, Unity status/compile/console, and relevant Unity tests.
- `multiplayer-visible`: `smoke` or a targeted `--case`.
- `AI/HumanBot` or `prepare-phase`: targeted HumanBot prepare case; add `random-aware` when significant.
- `battle-visible`, monster, scroll, or skill: `battle`.
- `persistent state`, reconnect, or Host Migration: `lifecycle`.
- `random outcome`: `random-aware`.
- 3-round or midgame confidence: `long`.
- 3-round lifecycle: `long-lifecycle`.
- Merge or large change: `full-regression`.
- Game-to-end: `endurance` only when explicitly requested.
- `nightly` must follow the actual `run_matrix.py` definition and is not a synonym for `full-regression`.

## Cleanup Rules

- Use `--headless-player` for build E2E unless screenshot or visual artifacts are required.
- Job Object cleanup proof should show build players were contained and cleanup reports were written.
- E2E PASS requires `cleanupStatus=PASS` and `orphanedPids=[]`.
- Do not run heavy E2E when the orphan pressure gate blocks; report `NEEDS_ENVIRONMENT` with the exact reason.
- A screenshot skipped in headless mode is not a failure when screenshots are not assertions.
- Endurance TIMEOUT can be `NEEDS_TUNING`, `TIMEOUT`, or `STALLED`; it is not GameToEnd PASS unless `GameOver` was reached and final comparisons passed.

## Feature Completion Contract

A feature PASS report must include:

- Files changed.
- Commands run.
- PASS/FAIL result per command.
- Compile and console result.
- Test results, or skipped tests with exact reason.
- E2E profile or case run, or skipped E2E with exact reason.
- Artifact paths for E2E.
- `cleanupStatus` for E2E.
- `orphanedPids` for E2E.
- Any `phase=error` or snapshot comparison failures, or confirmation none were found.
- Remaining risk.
- Learned recipe touched/updated, or a statement that no reusable method was found.

## Internal Expansion Template

The following is reference only.

The user does not need to paste this.

```text
Implement this feature using the MDF content feature workflow:

<short user request expanded internally>

Classify the request, read the automatic read set, map the real code/content path, identify State Authority and command/snapshot impact, implement minimally, choose the smallest relevant verification profile, and report the feature completion contract.

Use mdf_* subagents explicitly when needed.
Do not claim PASS without command output, artifact paths, cleanupStatus=PASS, and orphanedPids=[] for E2E runs.
```
