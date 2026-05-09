## mp-human-bot-seed-sweep: Treat seeds as diagnostic labels

Status: active
Pinned: false
Category: AI/HumanBot
Created: 2026-05-06
Last used: 2026-05-06
Last verified: 2026-05-06
Use count: 1
Review after: 2026-08-04
Triggers: seed sweep, stochastic soak, multiple HumanBot prepare runs, random outcome hashes
Applies to: `tools/harness/mp/run_human_bot_seed_sweep.py`, Phase 24 stochastic HumanBot sweep
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
One HumanBot run can miss random-state sync bugs. A seed sweep should run the same random-aware scenario across several seeds and aggregate artifacts without claiming command replay or deterministic RNG control.

Recipe:
- Use `python tools/harness/mp/run_human_bot_seed_sweep.py --seeds 5101,5102,5103 --player-path <Development player>` for the default Phase 24 proof.
- Alternatively pass `--seed-start <n> --seed-count <count>`.
- The sweep creates a parent artifact with one `seed-<seed>/` child root per seed and passes that root to `run_human_bot_prepare_progression.py`.
- By default, stop on the first failing seed. Use `--continue-on-fail` only when collecting multiple failures in one pass is more useful than preserving time.
- Use `--include-4p` only for optional 4-player expansion; Phase 24's required proof is the 2-peer prepare sweep.
- Review `seed-sweep-summary.json` and `result.json`, not just stdout. The summary records child artifact paths, commands issued, command types, bot journals, screenshot paths, random outcome hashes, comparisons, and the first failure path when a seed fails.
- Keep `seedSemantics.deterministicReplayClaim=false`. A seed is a diagnostic run label unless each gameplay RNG source is explicitly controlled and replay-verified.

Verification:
- `artifacts/mp/20260505-190714-human-bot-seed-sweep/result.json` reported `success=true`, `failures=[]`, seeds `5101,5102,5103`.
- Each seed child artifact passed `human-bot-prepare-assertions.json` with `commandsIssued=1`, `lastCommandType=SelectAugment`, and a durable selected augment hash delta.
- Each seed's `comparison-latest.json` reported `success=true`.
- Each seed's `random-outcome-summary.json` reported `matchesClient=true` for both players' shop, augment, and field hashes.
- `rg "result=fail|phase=error|parseError" artifacts/mp/20260505-190714-human-bot-seed-sweep` found no matches.

Pitfalls:
- Do not infer deterministic replay from repeated seed labels. The summary deliberately records known uncontrolled sources such as non-ledgered `UnityEngine.Random`, wall-clock/process timing, and Photon scheduling.
- Child runners from earlier phases may not persist their own `result.json`; the sweep must persist parent `result.json` and per-seed `seed-result.json` with child assertion evidence.
- Use the latest Development player that includes HumanBot and augment snapshot support; do not accidentally select a production-negative build.
