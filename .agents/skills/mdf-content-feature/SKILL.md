---
name: mdf-content-feature
description: "Use when the user asks to add, change, implement, improve, balance, or fix MDF gameplay/content/AI/UI/network features, including short Korean requests like 새 유닛, 새 스크롤, AI 개선, 증강 추가, 몬스터 추가, 전투 개선, 3라운드 진행, 게임 끝까지 테스트."
---

# MDF Content Feature

Use this skill to expand short MDF feature requests into the full content development workflow. Do not ask the user to paste the long template.

## Procedure

1. Classify the request.

Categories:

- `code-only/internal`
- `Unity asset/content`
- `multiplayer-visible`
- `command/authority`
- `battle-visible`
- `AI/HumanBot`
- `prepare-phase`
- `random outcome`
- `persistent state`
- `reconnect/Host Migration sensitive`
- `long-progression sensitive`
- `endurance/game-to-end sensitive`

2. Read the appropriate docs.

Always:

- `AGENTS.md`
- `.agent/rules/projectrull.md`
- `docs/ai-harness/content-development-routine.md`
- `docs/ai-harness/feature-implementation-loop.md`
- `docs/ai-harness/fusion-sync-rules.md`
- `docs/ai-harness/state-snapshot-schema.md`
- `docs/ai-harness/learned-recipes.md`

AI/Battle:

- `docs/ai-design/behavior-tree-v2.md`
- `docs/ai-design/battle-command-model.md`
- `docs/ai-design/scroll-targeting-ai.md`
- `docs/ai-design/skill-command-policy.md`

Random/progression:

- `docs/ai-harness/randomized-progression-test-plan.md`
- `docs/ai-harness/human-bot-driver-design.md`

Long/endurance:

- `docs/ai-harness/long-progression-test-plan.md` if present.

3. Plan before editing.

- Map the code/content path.
- List files to change.
- Identify the State Authority owner.
- Identify the command path if the action is strategic or multiplayer-visible.
- Identify snapshot fields and comparison rules.
- Identify Unity asset changes and reserialize needs.
- Identify HumanBot or AI usage.
- Identify the verification profile. Use `python tools/harness/mp/select_verification_profile.py` when the mapping is not obvious.

4. Implement minimally.

5. Verify.

- `python tools/harness/precommit.py --all`
- `unity-cli --project Mdfproject status`
- `unity-cli --project Mdfproject editor refresh --compile`
- `unity-cli --project Mdfproject console --type error --stacktrace user`
- `unity-cli --project Mdfproject test --mode EditMode`
- Relevant E2E profile or targeted case.

6. Select E2E coverage.

- `multiplayer-visible`: `smoke`.
- `battle-visible`: `battle`.
- `AI/Prepare/Battle`: targeted HumanBot plus `battle` if relevant.
- `random content`: `random-aware`.
- `persistent/reconnect/migration`: `lifecycle`.
- 3-round or midgame: `long`.
- 3-round lifecycle: `long-lifecycle`.
- Game-to-end: `endurance` only when explicitly requested.
- Merge or large change: `full-regression`.
- `nightly` is not the default and must follow `run_matrix.py`.

7. Require strict E2E PASS evidence.

- Artifact path.
- `cleanupStatus=PASS`.
- `orphanedPids=[]`.
- No `[MPTEST] phase=error`.
- Snapshot comparison success.

8. Final report.

- Classification.
- Files changed.
- Commands run.
- Artifacts.
- Skipped tests with exact reason.
- `cleanupStatus`.
- Remaining risk.
- Learned recipe touched/updated, or no new reusable recipe discovered.
