## human-bot-battle-after-prepare-v2-v1: Full prepare can feed battle command progression

Status: active
Pinned: false
Category: AI/HumanBot, battle
Created: 2026-05-08
Last used: 2026-07-15
Last verified: 2026-07-15
Use count: 36
Review after: 2026-08-06
Triggers: Prepare v2 recheck, HumanBot battle progression, battle command sync
Applies to: `run_human_bot_battle_progression.py`, `battle_progression_common.py`, `PrepareDecisionPolicy`, `BattleDecisionPolicy`
Verified by: see Verification section below; migrated from old Status: verified with cleanup environment blocker; blocker note preserved in recipe body; artifacts/mp/20260508-221828-human-bot-3round-progression; artifacts/mp/20260509-004943-human-bot-3round-progression; artifacts/mp/20260509-005651-ai-fill-2p-smoke; artifacts/mp/20260509-005809-ai-fill-2p-battle-smoke; artifacts/mp/20260509-023108-2human-aifill-visible-3round; artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless; artifacts/mp/20260510-170014-monster-healthbar-battle2-2hbot-2ai-headless; artifacts/mp/20260515-201909-matrix/20260515-201914-human-bot-battle-progression/result.json; artifacts/mp/20260516-032200-matrix/20260516-032206-human-bot-battle-progression; artifacts/mp/20260516-043338-human-bot-battle-progression; artifacts/mp/20260516-050511-human-bot-battle-progression; artifacts/mp/20260516-053255-human-bot-battle-progression; artifacts/mp/20260516-071704-human-bot-battle-progression; artifacts/mp/20260516-083543-human-bot-battle-progression; artifacts/mp/20260516-090318-human-bot-battle-progression; artifacts/mp/20260530-053015-matrix battle profile PASS cleanupStatus=PASS orphanedPids=[]; artifacts/mp/20260530-171651-matrix/20260530-171657-human-bot-battle-progression; artifacts/mp/20260531-065212-matrix/20260531-065218-human-bot-battle-progression/result.json; artifacts/mp/20260531-224905-matrix/20260531-224911-human-bot-battle-progression/result.json; artifacts/mp/20260513-122315-matrix/20260513-122324-human-bot-battle-progression; artifacts/mp/20260713-055128-human-bot-battle-progression/result.json; artifacts/mp/20260713-063037-human-bot-battle-progression/result.json; artifacts/mp/20260714-043422-human-bot-3round-progression/result.json; E:/UnityProjects/mdf/artifacts/mp/20260714-151104-final-lobby-cadence/20260714-061104-human-bot-3round-progression; artifacts/mp/20260715-044948-matrix/20260715-045616-human-bot-battle-progression/result.json; artifacts/mp/20260715-053152-matrix/20260715-053751-human-bot-battle-progression/result.json
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
After changing composition-aware prepare buy/reroll behavior, battle progression must prove that full prepare still reaches battle and that battle command counters/snapshots stay synchronized. The battle command-specific scripts can keep `augment-only`, but `run_human_bot_battle_progression.py` should default to full prepare for this recheck.

Recipe:
- Use a Development player built after the current C# changes. Stale builds can miss prepare/battle policy or unit lifecycle fixes.
- `run_human_bot_battle_progression.py` accepts `--bot-persona` as a compatibility alias for `--host-bot-persona`.
- Keep `run_human_bot_battle_progression.py` default `--bot-prepare-mode full` for Prepare v2 rechecks. Use `--bot-prepare-mode augment-only` only when isolating battle command sync.
- For PASS, check both `battle-command-evidence.json` and `bot-metrics-summary.json`:
  - `battle-command-evidence.json.success=true`, empty errors/warnings, host/client command counters match.
  - `battle-comparison-latest.json.success=true`.
  - `bot-metrics-summary.json.summary.battleReached=true`.
  - Buy/reroll metrics preserve Prepare v2 invariants: `BuyUnit > 0`, no `rerollBeforeThreeSold`, and first reroll sold slot count is `>=3` when reroll exists.
- When `BattleDecisionPolicy` emits one `BattleSpawnMonsterCommand` per cooldown, do not rely on a multi-phase `AIAttackStrategy` plan that sends a tank to a destroyer/wall-break position first; rebuilding the plan each decision repeats that first phase. Ordinary ground/tank monsters should use the A* shortest ground spawn path, while destroyer-specific positions remain limited to actual destroyer orders.
- Treat cleanup separately. Non-strict runs may still be `success=true` with `cleanupStatus=NEEDS_ENVIRONMENT`.

Verification:
- `python tools/harness/precommit.py --all` PASS, `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` PASS.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]` before and after E2E.
- `unity-cli --project Mdfproject test --mode EditMode` PASS, `43/43`.
- `python tools/harness/mp/build_player.py --launch-smoke --exit-after-seconds 5` created `artifacts/builds/20260507-175726/MDF-MPTest.exe`; `build-metadata.json` reports `result=Succeeded`, Development, AllowDebugging. The launch-smoke wrapper timed out after 10 seconds and left PID `23764`, so smoke cleanup remains part of the Windows cleanup environment blocker.

Battle recheck artifacts using `artifacts/builds/20260507-175726/MDF-MPTest.exe`:
- Balanced: `artifacts/mp/20260507-180042-human-bot-battle-progression`, command `--seed 6101 --bot-persona balanced`, `functionalSuccess=true`, `cleanupStatus=NEEDS_ENVIRONMENT`. Metrics: `BuyUnit=7`, `RerollShop=0`, `rerollBeforeThreeSold=false`, `battleReached=true`, `finalFieldUnitTotal=6`, `acceptedBattleCommandSeq=5`, `spawnMonsterSeq=3`, `activateSkillSeq=2`, `useMagicScrollSeq=0`.
- Unit: `artifacts/mp/20260507-180341-human-bot-battle-progression`, command `--seed 6102 --bot-persona unit`, `functionalSuccess=true`, `cleanupStatus=NEEDS_ENVIRONMENT`. Metrics: `BuyUnit=9`, `RerollShop=0`, `rerollBeforeThreeSold=false`, `battleReached=true`, `finalFieldUnitTotal=7`, `acceptedBattleCommandSeq=6`, `spawnMonsterSeq=3`, `activateSkillSeq=3`, `useMagicScrollSeq=0`.
- Maze: `artifacts/mp/20260507-180640-human-bot-battle-progression`, command `--seed 6103 --bot-persona maze`, `functionalSuccess=true`, `cleanupStatus=NEEDS_ENVIRONMENT`. Metrics: `BuyUnit=5`, `RerollShop=1`, first reroll `soldSlotCount=4`, `rerollBeforeThreeSold=false`, `battleReached=true`, `finalFieldUnitTotal=5`, `acceptedBattleCommandSeq=3`, `spawnMonsterSeq=3`, `activateSkillSeq=0`, final evidence `useMagicScrollSeq=0`.
- All three had `battle-command-evidence.json.success=true` with empty errors/warnings, matching host/client `acceptedBattleCommandSeq` and `spawnMonsterSeq`, and `battle-comparison-latest.json.success=true`.
- The Maze run also logged a server AI `UseMagicScroll` opportunity and execution in `build-host.Player.log` (`scroll_accepted`, `scroll_effect_applied`, `battle_command_executed`), proving the scroll command path remains active when a scroll opportunity appears. It occurred outside the final paired host/client evidence window, so keep the dedicated `magic-scroll-command` case for strict scroll snapshot proof.

Pitfalls:
- `battle-command-evidence.json` may be captured after the game returns to Prepare; use its `battleObserved=true` plus `bot-metrics-summary.json.summary.battleReached=true`, not final `currentState`, to decide whether battle was reached.
- `useMagicScrollSeq=0` in final evidence does not prove the scroll path was unavailable for the whole run; check `[MPTEST]` scroll logs and the dedicated magic scroll case when scroll behavior is the feature under test.

Lifecycle notes:
- 2026-05-09: Verified HumanBot battle spawn planning after changing AIAttackStrategy to evaluate all outer spawn cells and keep ordinary ground/tank monsters on the ground shortest path.
- 2026-05-09: verified artifact `artifacts/mp/20260508-221828-human-bot-3round-progression`
- 2026-05-09: Verified AI/HumanBot policy parity change with shared MdfBotProfile policies, disabled AI auto-spawn bootstrap, 2-player battle artifact artifacts/mp/20260509-004831-human-bot-battle-progression, and 3-round artifact artifacts/mp/20260509-004943-human-bot-3round-progression.
- 2026-05-09: verified artifact `artifacts/mp/20260509-004943-human-bot-3round-progression`
- 2026-05-09: Verified actual 2-player AI-fill smoke after parity change: 1 human plus 1 server AI reached ready Game snapshot with cleanupStatus=PASS and orphanedPids=[].
- 2026-05-09: verified artifact `artifacts/mp/20260509-005651-ai-fill-2p-smoke`
- 2026-05-09: Verified actual server AI battle command emission: 2-player AI-fill battle smoke observed BattleSpawnMonster with acceptedBattleCommandSeq=1, spawnMonsterSeq=1, cleanupStatus=PASS, orphanedPids=[].
- 2026-05-09: verified artifact `artifacts/mp/20260509-005809-ai-fill-2p-battle-smoke`
- 2026-05-09: 2-human + 2 AI fill visual MP run reached round 3 with spawnMonsterSeq=41 and rejectedBattleCommandCount=0 after slot-based attack pool selection fix.
- 2026-05-09: verified artifact `artifacts/mp/20260509-023108-2human-aifill-visible-3round`
- 2026-05-10: HumanBot peers progressed through prepare into Battle1/Battle2 while wave pool assertions were sampled.
- 2026-05-10: verified artifact `artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless`
- 2026-05-11: Used 2 HumanBot clients through Prepare/Battle1/Battle2 to verify monster HP bar reset behavior.
- 2026-05-11: verified artifact `artifacts/mp/20260510-170014-monster-healthbar-battle2-2hbot-2ai-headless`
- 2026-05-16: verified artifact `artifacts/mp/20260515-201909-matrix/20260515-201914-human-bot-battle-progression/result.json`
- 2026-05-16: verified artifact `artifacts/mp/20260516-032200-matrix/20260516-032206-human-bot-battle-progression`
- 2026-05-16: verified artifact `artifacts/mp/20260516-043338-human-bot-battle-progression`
- 2026-05-16: verified artifact `artifacts/mp/20260516-050511-human-bot-battle-progression`
- 2026-05-16: verified artifact `artifacts/mp/20260516-053255-human-bot-battle-progression`
- 2026-05-16: verified artifact `artifacts/mp/20260516-071704-human-bot-battle-progression`
- 2026-05-16: verified artifact `artifacts/mp/20260516-083543-human-bot-battle-progression`
- 2026-05-16: verified artifact `artifacts/mp/20260516-090318-human-bot-battle-progression`
- 2026-05-30: verified artifact `artifacts/mp/20260530-053015-matrix battle profile PASS cleanupStatus=PASS orphanedPids=[]`
- 2026-05-31: Verified melee slash VFX CombatScheduler integration with targeted human-bot battle progression.
- 2026-05-31: verified artifact `artifacts/mp/20260530-171651-matrix/20260530-171657-human-bot-battle-progression`
- 2026-05-31: verified artifact `artifacts/mp/20260531-065212-matrix/20260531-065218-human-bot-battle-progression/result.json`
- 2026-06-01: verified artifact `artifacts/mp/20260531-224905-matrix/20260531-224911-human-bot-battle-progression/result.json`
- 2026-05-13: verified artifact `artifacts/mp/20260513-122315-matrix/20260513-122324-human-bot-battle-progression`
- 2026-07-13: verified artifact `artifacts/mp/20260713-055128-human-bot-battle-progression/result.json`
- 2026-07-13: verified artifact `artifacts/mp/20260713-063037-human-bot-battle-progression/result.json`
- 2026-07-14: verified artifact `artifacts/mp/20260714-043422-human-bot-3round-progression/result.json`
- 2026-07-14: verified artifact `E:/UnityProjects/mdf/artifacts/mp/20260714-151104-final-lobby-cadence/20260714-061104-human-bot-3round-progression`
- 2026-07-15: verified artifact `artifacts/mp/20260715-044948-matrix/20260715-045616-human-bot-battle-progression/result.json`
- 2026-07-15: verified artifact `artifacts/mp/20260715-053152-matrix/20260715-053751-human-bot-battle-progression/result.json`
