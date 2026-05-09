# Learned Recipe Lifecycle

`docs/ai-harness/learned-recipes.md` is operational memory, not a scratchpad. Keep useful verified recipes available, but make age, evidence, and replacement status visible.

## Required Metadata

Each new or migrated recipe should carry this metadata block:

```md
## recipe-id: Short title

Status: active
Pinned: false
Category: harness | unity-cli | multiplayer | cleanup | feature-workflow | asset | host-migration | other
Created: YYYY-MM-DD
Last used: YYYY-MM-DD
Last verified: YYYY-MM-DD
Use count: 1
Review after: YYYY-MM-DD
Triggers: command, symptom, or workflow keywords
Applies to: file paths, tools, scenes, or systems
Verified by: command, test, artifact, or exact evidence
Replacement: none
Archive policy: archive when unused for 180 days and not pinned/protected
```

Allowed `Status` values are `active`, `stale`, `archived`, `deprecated`, and `pinned`.

## Evidence Rules

- `Last used` means the recipe was consulted or applied.
- `Last verified` means a command, test, or artifact proved the recipe still works.
- Do not update `Last verified` without evidence.
- `Use count` increments when a recipe is used or verified.
- `Pinned: true` means the recipe should remain visible even when old.
- Deprecated recipes must name a `Replacement` or state the reason no replacement exists.

## Archive Policy

- Do not auto-delete recipes.
- Archive only when safe and after review.
- Never auto-archive pinned recipes.
- Never auto-archive safety, cleanup, security, token, Host Migration, reconnect, or automation-server-gate recipes without explicit human direction.
- Prefer `docs/ai-harness/recipes/archive/` for historical recipes and `docs/ai-harness/recipes/deprecated/` for deprecated recipes that still need to be discoverable.

## File Layout

- `docs/ai-harness/learned-recipes.md` remains the index and high-signal quick reference.
- Detailed long recipes should live in `docs/ai-harness/recipes/*.md`.
- Archived recipes should live in `docs/ai-harness/recipes/archive/*.md`.
- Deprecated recipes may live in `docs/ai-harness/recipes/deprecated/*.md` when keeping them separate improves clarity.

## Tooling

- Mark use: `python tools/harness/recipes/touch_recipe.py --id <recipe-id> --used`
- Mark verification: `python tools/harness/recipes/touch_recipe.py --id <recipe-id> --verified --artifact <path>`
- `--verified` requires evidence via `--artifact` or `--note`; use `--used` when the recipe was only consulted.
- If the id does not exist, `touch_recipe.py` creates `docs/ai-harness/recipes/<recipe-id>.md` instead of growing the index.
- Scan stale candidates: `python tools/harness/recipes/scan_stale_recipes.py`
