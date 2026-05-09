---
name: use-learned-recipes
description: Search existing MDF harness recipes before Unity CLI, screenshot, build, asset, Photon, or multiplayer automation work.
---

# Use learned recipes

1. Search first:

```bash
rg -n "<topic keywords>" docs/ai-harness .agents/skills tools/harness
```

2. Prefer verified recipes indexed in `docs/ai-harness/learned-recipes.md` and stored under `docs/ai-harness/recipes/*.md`.
3. If a recipe is stale, verify with `--help` or a dry run, then update it.
4. Do not invent unity-cli flags when `--help` can confirm them.
