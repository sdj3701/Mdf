## automatic-content-workflow-hardening-v2: Keep short-prompt routing light and dry-runs environment-free

Status: active
Pinned: false
Category: feature-workflow
Created: 2026-05-08
Last used: 2026-05-08
Last verified: 2026-05-08
Use count: 1
Review after: 2026-08-06
Triggers: short feature routing false positives, run_matrix dry-run requiring player builds, stale phase prompt reminders, context bundle noise
Applies to: UserPromptSubmit, SessionStart, Stop gate, matrix dry-run, verification selector, context bundles
Verified by: see preserved recipe notes below; migrated from old Status: documented
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Recipe:
- Matrix `--dry-run` must return planned case results from `run_matrix.py` without executing child case scripts or requiring a built player.
- SessionStart should point normal work to `AGENTS.md`, learned recipes, `content-development-routine.md`, and `mdf-content-feature`; reserve archived phase prompts for explicit historical harness-bootstrap or overlay rebuild audit work.
- UserPromptSubmit should require a strong action verb and avoid explanation/result-analysis prompts.
- Stop gate should waive E2E artifact/cleanup/orphan/snapshot fields only when the final report gives an exact E2E skipped reason; if E2E ran, keep `cleanupStatus=PASS`, `orphanedPids=[]`, no `phase=error`, and snapshot comparison success strict.
- Selector AI-only requests should not automatically force `random-aware`; use battle, prepare, random, long, lifecycle, and endurance categories to scale proof.
- Context bundles should exclude `__pycache__`, `*.pyc`, `AGENTS.md.meta`, `.codex/session-state`, `_context_packer/output`, and `_context_bundles`.
- On Windows, hook JSON input should be read from `sys.stdin.buffer` and decoded as UTF-8 first. PowerShell pipelines can replace raw Korean text with `?` before Python sees it, so hook self-tests should pass UTF-8 bytes directly through `subprocess.run(input=...)` or use JSON `\u` escapes.

Pitfalls:
- `테스트` alone is not a strong feature action.
- Do not delete tracked Unity or repo `.meta` files casually; exclude root-level noise from bundles instead.
