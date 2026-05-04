# MDF unity-cli Recipes

## Project selector

Use the Unity project root explicitly:

```bash
unity-cli --project Mdfproject status
```

If multiple Editors are open, prefer `--project Mdfproject` or the exact absolute project path. The unity-cli connector discovers active Unity instances through local instance files and sends commands to the Editor over localhost.

## Baseline checks

After C# edits:

```bash
unity-cli --project Mdfproject status
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
```

If `--type` or `--stacktrace` differs in the installed CLI, run:

```bash
unity-cli console --help
```

Then update `docs/ai-harness/learned-recipes.md` with the verified syntax.

## Asset YAML edits

After editing `.prefab`, `.unity`, `.asset`, `.mat`, `.controller`, `.anim`, or other Unity YAML:

```bash
unity-cli --project Mdfproject reserialize <changed asset paths>
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
```

Do not hand-edit Unity YAML without reserializing and checking the console.

## Tests

Preferred:

```bash
unity-cli --project Mdfproject test --mode EditMode
unity-cli --project Mdfproject test --mode PlayMode
```

Unity 2021 + Test Framework 1.1 fallback:

```bash
Unity.exe -runTests -batchmode -projectPath Mdfproject -testResults artifacts/EditMode.xml -testPlatform EditMode
Unity.exe -runTests -batchmode -projectPath Mdfproject -testResults artifacts/PlayMode.xml -testPlatform PlayMode
```

Only use fallback after confirming unity-cli cannot run the tests in this environment.

## Useful built-ins

```bash
unity-cli --project Mdfproject list
unity-cli --project Mdfproject console --clear
unity-cli --project Mdfproject editor play --wait
unity-cli --project Mdfproject editor stop
unity-cli --project Mdfproject screenshot --help
unity-cli --project Mdfproject exec "return UnityEngine.Application.unityVersion;"
```

For complex `exec`, pipe code through stdin to avoid shell escaping issues.

## Background Editor reliability

Unity may throttle Editor updates when unfocused. Set Editor Preferences > General > Interaction Mode > No Throttling when available. If this Unity version or local setup lacks that setting, document the fallback in `learned-recipes.md`.

For test builds, `MPTestBootstrap` should set:

```csharp
Application.runInBackground = true;
```

## Required custom tools

Implement these under an Editor-only assembly/folder:

```text
mp_start_host
mp_join_client
mp_load_game
mp_dump_state
mp_assert_state
mp_command
mp_screenshot
mp_stop
mp_build_player
mp_start_prepare_smoke
mp_start_battle_smoke
mp_force_host_migration_probe
```

Rules:

- Class must be static and use `[UnityCliTool]`.
- `HandleCommand(JObject parameters)` returns `SuccessResponse` or `ErrorResponse`.
- Runs on Unity main thread, so Unity APIs are safe.
- Keep parameters discoverable with nested `Parameters` + `[ToolParameter]`.
- Run `unity-cli --project Mdfproject list` after compile and verify tools appear.

## Screenshot artifact rule

When a visual failure is possible:

```bash
unity-cli --project Mdfproject screenshot --help
unity-cli --project Mdfproject screenshot --view game --output_path artifacts/<case>/editor.png
```

If the command differs, capture the working command in `learned-recipes.md`.
