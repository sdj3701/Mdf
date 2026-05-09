## learned-recipe-lifecycle-management-v1: Keep recipe memory evidence-based

Status: active
Pinned: false
Category: harness
Created: 2026-05-08
Last used: 2026-05-09
Last verified: 2026-05-09
Use count: 3
Review after: 2026-08-06
Triggers: learned recipes, recipe metadata, stale recipes, archive candidates, reusable methods
Applies to: `docs/ai-harness/learned-recipes.md`, `docs/ai-harness/recipe-lifecycle.md`, `tools/harness/recipes/*`
Verified by: `python -m py_compile tools/harness/recipes/touch_recipe.py tools/harness/recipes/scan_stale_recipes.py .codex/hooks/post_tool_use_knowledge_capture.py .codex/hooks/stop_verify_gate.py tools/harness/precommit.py`; `python tools/harness/recipes/scan_stale_recipes.py`; `python tools/harness/precommit.py --self-test`; `python tools/harness/precommit.py --all`; `python tools/harness/validate_overlay.py`; `unity-cli --project Mdfproject console --type error --stacktrace user`
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Recipe:
- Keep `learned-recipes.md` as the short index and move long detailed recipes to `docs/ai-harness/recipes/*.md` incrementally.
- Use `touch_recipe.py --used` when a recipe was consulted or applied.
- Use `touch_recipe.py --verified --artifact <path>` only when command output, a test, or an artifact proves the recipe still works.
- Use `scan_stale_recipes.py` to list stale, archive, and deprecated-reference candidates; the scanner must never delete automatically.
- Keep pinned safety, cleanup, security, token, Host Migration, reconnect, and automation-server-gate recipes out of auto-archive candidates.

Lifecycle notes:
- 2026-05-08: Verified split-file touch behavior and --verified evidence guard with precommit self-test.
- 2026-05-09: Normalized legacy learned recipe statuses and metadata; verified with py_compile, scan_stale_recipes.py --json, precommit --self-test, precommit --all, and validate_overlay.
