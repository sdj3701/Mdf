## harness-entropy-cleanup-v1: Keep context bundles and generated state out of the source of truth

Status: active
Pinned: true
Category: cleanup
Created: 2026-05-08
Last used: 2026-07-12
Last verified: 2026-05-10
Use count: 4
Review after: 2026-08-06
Triggers: context bundle drift, stale prompts, generated session state, root BAT cleanup, precommit guardrails
Applies to: `.gitignore`, `_context_packer`, `.codex/session-state`, `tools/harness/precommit.py`, harness docs
Verified by: see Verification section below; migrated from old Status: verified; _context_packer/mdf_context_pack.config.json
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Generated local state can leak into context bundles or make future agents follow stale paths. In this pass, `.codex/session-state` JSON files and `_context_packer/output` bundles were generated artifacts, while current docs needed explicit labels for historical phase prompts and obsolete HumanBot adapters.

Recipe:
- Ignore generated outputs with `.gitignore`: `artifacts/`, `_context_packer/output/`, `_context_bundles/`, `.codex/session-state/`, and root `nul`.
- Exclude `.codex/session-state/**` from context packer profiles that include `.codex/**`; also keep the default packer source excludes aligned so a missing config does not re-include session files.
- Remove generated context outputs and session-state JSON files after verifying their resolved paths are inside the repo. Keep the directories, but keep them empty unless a local run is actively using them.
- Keep only `MDF_PACK_CONTEXT.bat` at the repo root for context packing; helper BAT/scripts live under `_context_packer/`.
- Mark old MVP phase docs as historical and point active readers to `content-development-routine.md`, `verification-profile-selector.md`, `feature-implementation-loop.md`, and `randomized-progression-test-plan.md`.
- Keep `MPTestHumanBotPolicy` as an explicit obsolete adapter only; runtime HumanBot uses `PrepareDecisionPolicy`, `BattleDecisionPolicy`, and `HumanClientCommandEmitter`.
- Do not remove field registry entries from `Unit.OnDisable`. Battle death disables units for later respawn. Actual destroy/despawn cleanup should unregister through `Unit.OnDestroy` or explicit merge/sell paths.

Guardrails:
- `tools/harness/precommit.py` BLOCKs context packer profiles that include `.codex/**` without excluding `.codex/session-state/**`.
- `tools/harness/precommit.py` BLOCKs `Unit.OnDisable` bodies that call `UnitDied`.
- Existing guardrails still BLOCK HumanBot/test peers registering `AIPlayerController`, AI direct `SpawnMonsterAtPositionAsync`, and scroll gameplay effects in presentation RPC/helpers.

Verification:
- `git check-ignore -v .codex/session-state/probe.json _context_packer/output/probe.zip _context_bundles/probe.zip nul` matched the expected `.gitignore` entries.
- `python _context_packer/unity_context_pack.py --profile scripts-plus-context --dry-run` listed no `.codex/session-state` files.
- `python tools/harness/validate_overlay.py` PASS, 33 required paths checked and `AGENTS.md <= 70` lines.
- `python tools/harness/precommit.py --self-test` PASS, including context packer and `Unit.OnDisable` guard tests.
- `python tools/harness/precommit.py --all` PASS, `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject status` found one ready Editor, Unity `2021.3.45f1`.
- `unity-cli --project Mdfproject editor refresh --compile` PASS, compilation complete.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` PASS, `43/43`.

Cleanup performed:
- Deleted generated files under `.codex/session-state/`.
- Deleted generated files under `_context_packer/output/`.
- Deleted root generated `nul`.
- Updated `.gitignore`, context packer config/default excludes, current docs, and precommit guardrails.

Remaining intentional legacy:
- `MPTestHumanBotPolicy` remains as an obsolete compatibility adapter for legacy test callers.
- `run_matrix.py --case all` still means the existing default case subset; profile split is the next matrix phase.
- `Mdfproject/Assembly-CSharp-Editor.csproj` is a generated/tracked Unity file with pre-existing ordering churn. Do not hand-edit it; prefer ignoring future generated churn in review unless the team decides to untrack generated project files.

Lifecycle notes:
- 2026-05-10: Verified scripts-plus-context root launcher includes and required excludes on 2026-05-10.
- 2026-05-10: verified artifact `_context_packer/mdf_context_pack.config.json`
