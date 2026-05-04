# MDF Feature Implementation Loop

## Purpose

This is the default workflow whenever Codex is asked to implement or modify gameplay, UI, networking, commands, AI, Host Migration, or test automation.

The goal is to prevent “compile-only PASS”. A feature is not done until Codex has evidence from Unity compile, console, tests, and relevant multiplayer E2E artifacts.

## Loop

1. **Analyze**
   - Read `AGENTS.md`, this file, `project-structure.md`, `fusion-sync-rules.md`, and any feature-specific docs.
   - Map actual code paths before editing.
   - Spawn/request `mdf_code_mapper` if the path is non-trivial.

2. **Plan**
   - List files to change.
   - Identify network authority, command serialization, UI success events, data/Addressables dependencies, and test impact.
   - Identify which E2E case proves the feature.

3. **Implement minimally**
   - Make the smallest targeted change.
   - Do not edit vendor folders.
   - If adding a command, update `CommandType`, serialization, deserialization, UI/AI callers, and snapshot/assertions as needed.

4. **Static verification**
   - Run `python tools/harness/precommit.py --all` or staged equivalent.
   - Use `mdf_fusion_reviewer` for authority-sensitive changes.

5. **Unity verification**
   - `unity-cli --project Mdfproject status`
   - `unity-cli --project Mdfproject editor refresh --compile`
   - `unity-cli --project Mdfproject console --type error --stacktrace user`
   - Tests when relevant: EditMode and PlayMode.

6. **Build/E2E verification**
   - Build a Development player when the feature affects multiplayer, commands, scenes, UI flow, or networking.
   - Run the smallest relevant matrix:
     - Editor Host + Build Client
     - Build Host + Editor Client
     - Build Host + Build Client when Editor state may hide the bug
   - Capture `[MPTEST]` logs, screenshots, stdout/stderr, Unity console, and state snapshots.

7. **Triage**
   - Compare snapshots.
   - Use logs and screenshots to identify root cause.
   - If failure is real, fix and repeat from step 3.
   - If the environment cannot run, report `NEEDS_ENVIRONMENT` and do not claim PASS.

8. **Knowledge capture**
   - If a new stable command, timing, screenshot trick, build path, or failure pattern is found, update `learned-recipes.md` before the final response.

## Feature completion contract

A final report must include:

- Files changed.
- Commands run.
- PASS/FAIL per command.
- Artifact paths.
- Multiplayer cases run or skipped with reasons.
- Remaining risks.

## Copy-paste feature prompt template

```text
Implement this feature using the MDF harness loop:

<describe feature>

Read first:
- AGENTS.md
- docs/ai-harness/feature-implementation-loop.md
- docs/ai-harness/project-structure.md
- docs/ai-harness/fusion-sync-rules.md
- docs/ai-harness/mp-test-protocol.md
- docs/ai-harness/state-snapshot-schema.md
- docs/ai-harness/learned-recipes.md

Start by mapping the real code path and producing a concise implementation plan. Then implement minimally.

Use subagents explicitly:
- mdf_code_mapper before editing if the code path is unclear
- mdf_fusion_reviewer after authority/networking/command changes
- mdf_asset_guard after prefab/scene/asset changes
- mdf_unity_verifier after implementation
- mdf_mp_test_runner for multiplayer E2E

Verification minimum:
- python tools/harness/precommit.py --all
- unity-cli --project Mdfproject status
- unity-cli --project Mdfproject editor refresh --compile
- unity-cli --project Mdfproject console --type error --stacktrace user
- relevant EditMode/PlayMode tests
- relevant Editor/Build multiplayer E2E matrix if multiplayer-visible

If a command, flag, timing, screenshot, build, or failure recipe is discovered, update learned-recipes.md.
Do not claim PASS unless command outputs and artifacts prove it.
```
