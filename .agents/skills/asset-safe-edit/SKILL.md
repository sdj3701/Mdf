---
name: asset-safe-edit
description: Safely edit MDF Unity assets and reserialize through Unity.
---

# Asset safe edit

Before editing Unity YAML, inspect nearby existing patterns.

After editing `.prefab`, `.unity`, `.asset`, `.mat`, `.controller`, `.anim`:

```bash
unity-cli --project Mdfproject reserialize <paths>
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
```

Do not edit vendor assets unless explicitly approved.
