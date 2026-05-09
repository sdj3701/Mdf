## harness-gitignore-artifacts: Commit harness sources, ignore generated proof output

Status: active
Pinned: false
Category: harness
Created: 2026-05-05
Last used: 2026-05-05
Last verified: 2026-05-05
Use count: 1
Review after: 2026-08-03
Triggers: large git status after E2E/build harness runs
Applies to: `.gitignore`, `artifacts/`, `tools/harness/**/__pycache__`
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Harness E2E and build-player runs can create thousands of local proof files and Unity player binaries. These are evidence for the current machine/run, not portable source needed by another clone.

Recipe:
- Commit harness sources and configuration: `tools/harness/**/*.py`, `Mdfproject/Assets/Scripts/Testing/MP/**`, `.agents/skills/**`, `.codex/agents/**`, `.codex/hooks/**`, `docs/ai-harness/**`, and required `.meta` files.
- Ignore generated output: `/artifacts/`, `__pycache__/`, and `*.py[cod]`.
- Preserve reusable evidence by summarizing artifact paths and pass/fail facts in `docs/ai-harness/learned-recipes.md`, not by committing the raw artifact directory.

Verification:
- Before ignore update, `git status --porcelain=v1 -uall` reported 2817 entries, including 2688 under `artifacts/` and 22 Python cache files.
- After adding ignore rules, `git status --porcelain=v1 -uall` reported 108 entries, leaving only commit candidates and tracked modifications.
