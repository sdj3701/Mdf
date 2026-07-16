## matrix-profile-split-v1: Keep feature smoke cheap and move heavy coverage to profiles

Status: active
Pinned: false
Category: battle, feature-workflow
Created: 2026-05-08
Last used: 2026-07-15
Last verified: 2026-07-15
Use count: 19
Review after: 2026-08-06
Triggers: matrix cost control, random-aware runs, battle-heavy runs, nightly automation
Applies to: `tools/harness/mp/run_matrix.py`, matrix docs, HumanBot/battle seed sweeps
Verified by: see Verification section below; migrated from old Status: verified; artifacts/mp/20260510-100906-matrix; artifacts/mp/20260530-171651-matrix; artifacts/mp/20260530-175140-matrix; artifacts/mp/20260712-085632-matrix/matrix-summary.json; artifacts/mp/20260714-142121-matrix/matrix-summary.json; artifacts/mp/20260715-003234-matrix/matrix-summary.json; artifacts/mp/20260715-010530-matrix/matrix-summary.json; artifacts/mp/20260715-011304-matrix/matrix-summary.json; artifacts/mp/20260715-125915-matrix/matrix-summary.json
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
`run_matrix.py --case all` was doing a fixed default subset, while newer random-aware, battle, lifecycle, and seed sweep cases needed named groups. Without explicit profiles, feature work can accidentally run too much, or nightly automation can accidentally run too little.

Recipe:
- Keep targeted case execution with `--case <case>`.
- Keep `--case all` backward-compatible as the existing default subset: `editor-host-build-client`, `build-host-editor-client`, `build-host-build-client`, `ai-fill-smoke`, `disconnect-ai-takeover`, `same-token-reconnect`, `four-player-smoke`.
- Add `--profile` for named sets:
  - `smoke`: three Editor/Build smoke cases.
  - `regression`: `smoke` plus AI fill, disconnect takeover, same-token reconnect, four-player smoke, and HumanBot prepare.
  - `battle`: battle spawn, magic scroll, and HumanBot battle progression.
  - `lifecycle`: progressed reconnect, disconnect, and Host Migration after battle.
  - `random-aware`: HumanBot prepare, HumanBot 4p, progressed lifecycle cases, and HumanBot seed sweep.
  - `nightly`: regression, battle, lifecycle, battle seed sweep, and HumanBot seed sweep.
- Use `--list-cases` and `--list-profiles` before wiring automation.
- Use `--dry-run` to prove selected cases and child commands. Dry-run must not call Unity Editor cleanup between cases.
- Matrix summary JSON should record `profileName`, `selectedCases`, per-case `functionalSuccess`, per-case `cleanupStatus`, aggregate `functionalSuccess`, aggregate `cleanupStatus`, `overallSuccess`, and child artifact paths.
- Seed sweep profile defaults are diagnostic, not deterministic replay claims: HumanBot prepare sweep uses `5101,5102,5103`; battle sweep uses `7101,7102,7103`.

Verification:
- `python -m py_compile tools\harness\mp\run_matrix.py tools\harness\mp\run_battle_seed_sweep.py tools\harness\mp\run_human_bot_seed_sweep.py` PASS.
- `python tools/harness/mp/run_matrix.py --list-profiles` listed `smoke`, `regression`, `battle`, `lifecycle`, `random-aware`, and `nightly`.
- `python tools/harness/mp/run_matrix.py --list-cases` listed all targeted cases, including `human-bot-seed-sweep`, and documented `--case all`.
- `python tools/harness/mp/run_matrix.py --profile smoke --dry-run` PASS with selected cases `editor-host-build-client`, `build-host-editor-client`, `build-host-build-client`.
- `python tools/harness/mp/run_matrix.py --profile battle --dry-run` PASS with selected cases `battle-spawn-monster-command`, `magic-scroll-command`, `human-bot-battle-progression`.
- `python tools/harness/mp/run_matrix.py --profile random-aware --dry-run` PASS with selected cases `human-bot-prepare`, `human-bot-4p-progression`, `progressed-reconnect-after-battle`, `progressed-disconnect-after-battle`, `progressed-host-migration-after-battle`, `human-bot-seed-sweep`.

Pitfalls:
- Do not silently redefine `--case all` as nightly.
- Do not let `--dry-run` perform Editor `mp_stop` or `editor stop`; it should only create command/selection artifacts.

Lifecycle notes:
- 2026-05-10: Verified matrix case integrity and smoke/regression/long dry-runs on 2026-05-10.
- 2026-05-10: verified artifact `artifacts/mp/20260510-100906-matrix`
- 2026-05-31: Used targeted human-bot-battle-progression case instead of full battle profile for VFX event integration.
- 2026-05-31: verified artifact `artifacts/mp/20260530-171651-matrix`
- 2026-05-31: Used targeted progressed-host-migration-after-battle case for scheduler lifecycle hardening.
- 2026-05-31: verified artifact `artifacts/mp/20260530-175140-matrix`
- 2026-07-12: verified artifact `artifacts/mp/20260712-085632-matrix/matrix-summary.json`
- 2026-07-15: verified artifact `artifacts/mp/20260714-142121-matrix/matrix-summary.json`
- 2026-07-15: verified artifact `artifacts/mp/20260715-003234-matrix/matrix-summary.json`
- 2026-07-15: verified artifact `artifacts/mp/20260715-010530-matrix/matrix-summary.json`
- 2026-07-15: verified artifact `artifacts/mp/20260715-011304-matrix/matrix-summary.json`
- 2026-07-15: verified artifact `artifacts/mp/20260715-125915-matrix/matrix-summary.json`
