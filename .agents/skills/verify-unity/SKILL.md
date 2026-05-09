---
name: verify-unity
description: Verify MDF Unity changes with unity-cli compile, console, and tests.
---

# Verify Unity

Run after C# changes:

```bash
unity-cli --project Mdfproject status
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
```

After Unity YAML edits:

```bash
unity-cli --project Mdfproject reserialize <changed assets>
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
```

When relevant:

```bash
unity-cli --project Mdfproject test --mode EditMode
unity-cli --project Mdfproject test --mode PlayMode
```

Report PASS only with command output evidence.
