## ai-behavior-metrics-summary-v1: Summarize HumanBot behavior from artifacts without changing gameplay

Status: active
Pinned: false
Category: AI/HumanBot, battle
Created: 2026-05-07
Last used: 2026-07-15
Last verified: 2026-07-15
Use count: 3
Review after: 2026-08-05
Triggers: AI behavior tuning, HumanBot journal review, behavior baseline, seed sweep metrics
Applies to: `tools/harness/mp/summarize_bot_metrics.py`, HumanBot battle progression, battle seed sweep
Verified by: see Verification section below; migrated from old Status: verified-from-existing-artifact; artifacts/mp/20260715-105036-matrix/20260715-110318-human-bot-battle-progression/bot-metrics-summary.json
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Before changing AI decision quality, agents need a lightweight baseline that counts what the bot actually did. That baseline should come from existing artifacts and `[MPTEST]` timelines, not from extra gameplay state mutations or policy changes.

Recipe:
- Run or reuse a HumanBot battle artifact with `build-host-bot.jsonl`, `mptest.timeline.jsonl`, snapshots, and `battle-command-evidence.json`.
- Generate metrics with:
  - `python tools/harness/mp/summarize_bot_metrics.py <artifact-dir>`
- The summarizer writes `<artifact-dir>/bot-metrics-summary.json` with command counts by type, rejection reasons, observed gold spend, rerolls, buys, walls, monster spawns, scroll uses, scroll target counts, manual skill uses, round reached, and battle reached.
- `run_human_bot_battle_progression.py` writes this artifact through the shared battle progression runner after timeline collection.
- `run_battle_seed_sweep.py` writes each child artifact's metrics and aggregates them into `bot-metrics-seed-sweep-summary.json`.

Verification:
- `python tools/harness/mp/summarize_bot_metrics.py artifacts/mp/20260506-102324-human-bot-battle-progression` produced `bot-metrics-summary.json`.
- The summary counted `SelectAugment=1`, `BattleSpawnMonster=2`, `monsterSpawns=2`, `battleReached=true`, and no rejected decisions for that existing artifact.
- `python -m py_compile tools/harness/mp/summarize_bot_metrics.py tools/harness/mp/battle_progression_common.py tools/harness/mp/run_battle_seed_sweep.py tools/harness/mp/run_human_bot_battle_progression.py` passed.

Pitfalls:
- `commandsIssued` is bot-issued command intent from `human_bot_decision` timeline lines. It can be ahead of final snapshot command counters when the bot emits a command just after the last strict comparison sample.
- `goldSpent` is an observed estimate from journal gold drops and known command cost fields; if a run gains gold after spending before the next journal entry, it can undercount.
- Use dedicated scroll artifacts to judge scroll quality. A seed sweep may still pass while only some seeds actually use a scroll.

Lifecycle notes:
- 2026-07-15: verified artifact `artifacts/mp/20260715-105036-matrix/20260715-110318-human-bot-battle-progression/bot-metrics-summary.json`
