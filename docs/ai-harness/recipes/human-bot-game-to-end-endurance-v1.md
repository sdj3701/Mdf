## human-bot-game-to-end-endurance-v1: Classify endurance without hiding GameOver status

Status: active
Pinned: false
Category: AI/HumanBot
Created: 2026-05-08
Last used: 2026-05-08
Last verified: 2026-05-08
Use count: 1
Review after: 2026-08-06
Triggers: need to prove actual GameOver or distinguish timeout/stall/tuning in a bounded run
Applies to: `tools/harness/mp/run_human_bot_game_to_end.py`, endurance profile, HumanBot long progression
Verified by: see Verification section below; migrated from old Status: verified
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Recipe:
- Use a separate `gameToEndPass` field from script/process `success`. Only set `gameToEndPass=true` and `finalStatus=PASS` when both final snapshots are `GameOver`, final snapshot comparison passes, player IDs are unique, command counters agree, no `[MPTEST]` failure/error lines exist, and cleanup passes.
- When the bounded run reaches `--max-duration-seconds` or `--max-rounds` before GameOver, classify `NEEDS_TUNING`, `TIMEOUT`, or `STALLED`; do not label it GameToEnd PASS. `--allow-timeout-result` may allow a classified timeout command to exit successfully for CI diagnostics, but `gameToEndPass` must remain false.
- Run HumanBot on both build peers for endurance when using `--bot-prepare-mode augment-only`; this avoids prepare placement drift while producing enough battle pressure for GameOver.
- Capture every Prepare/Battle1/Battle2/GameOver checkpoint plus a progress timeline containing round/state, HP, monster, and command evidence.

Verification:
- `python tools/harness/mp/run_human_bot_game_to_end.py --seed 9101 --headless-player --max-duration-seconds 900 --allow-timeout-result --player-path artifacts/builds/20260508-022528/MDF-MPTest.exe` PASS at `artifacts/mp/20260508-023844-human-bot-game-to-end`.
- The result recorded `gameToEndPass=true`, `finalStatus=PASS`, `cleanupStatus=PASS`, `maxRoundReached=6`, `currentState=GameOver` on host/client, final comparison success, and `22/22` checkpoint comparisons passed.
- Bot metrics recorded `BattleSpawnMonster=177`, `SelectAugment=12`, and no rejected decision reasons.

Pitfalls:
- Do not use `--allow-timeout-result` as a shortcut to claim PASS; it only accepts bounded timeout classification when GameOver is not reached.
- Keep endurance out of smoke/regression/nightly unless explicitly accepted; use `--profile endurance` for opt-in runs.
