## automatic-content-feature-workflow-v1: Expand short feature prompts internally

Status: active
Pinned: false
Category: AI/HumanBot, battle, feature-workflow
Created: 2026-05-08
Last used: 2026-05-16
Last verified: 2026-05-08
Use count: 2
Review after: 2026-08-06
Triggers: 새 유닛, 새 스크롤, AI 개선, 증강 추가, 몬스터 추가, add unit, make scroll, improve AI, balance shop, feature request
Applies to: short MDF gameplay/content/AI/UI/network requests, content workflow docs, verification profile selection
Verified by: see Verification section below; migrated from old Status: documented
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
MDF feature work previously depended on the user pasting a long feature-loop template. That is brittle for common content requests and can lead to under-specified verification.

Recipe:
- Treat short gameplay/content/AI/UI/network requests as automatic MDF content feature requests.
- Read `docs/ai-harness/content-development-routine.md` and classify the request before editing.
- Use `docs/ai-harness/verification-profile-selector.md` to choose the smallest profile that proves the behavior.
- Keep E2E PASS strict: artifact paths, `cleanupStatus=PASS`, `orphanedPids=[]`, and snapshot comparison success are required.
- Use `--headless-player` for logic E2E unless screenshot or visual artifact proof is required.
- Keep `nightly` aligned with `tools/harness/mp/run_matrix.py`; it is not the same as `full-regression`.
- Treat endurance timeout as `NEEDS_TUNING`, `TIMEOUT`, or `STALLED`, not GameToEnd PASS.

Verification:
- Phase A created `content-development-routine.md` and `verification-profile-selector.md` from the current `run_matrix.py` profile definitions.
- `feature-implementation-loop.md` now marks the long prompt as an internal expansion template and points short requests to the content routine.

Pitfalls:
- Do not ask the user to paste the old template as the normal path.
- Do not run endurance unless explicitly requested.
- Do not continue into heavy E2E when orphan pressure blocks the environment.
