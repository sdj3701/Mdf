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
   - For randomized progression work, identify the same-player hashes/invariants to compare instead of expected random values.

3. **Implement minimally**
   - Make the smallest targeted change.
   - Do not edit vendor folders.
   - If adding a command, update `CommandType`, serialization, deserialization, UI/AI callers, and snapshot/assertions as needed.
   - For battle features, keep strategic monster spawn, magic scroll use, and manual/strategic skill use on the command path:
     - `BattleSpawnMonsterCommand` owns battle monster spawn validation and pool consumption.
     - `UseMagicScrollCommand` owns scroll inventory consumption and gameplay effects.
     - `ActivateSkillCommand` owns manual/strategic skill activation.
     - Presentation RPCs/VFX helpers do not apply durable gameplay effects.
     - HumanBot remains a real human peer and does not attach/register `AIPlayerController`.

4. **Static verification**
   - Run `python tools/harness/precommit.py --all` or staged equivalent.
   - Use `mdf_fusion_reviewer` for authority-sensitive changes.
   - Treat battle-command BLOCKs as hard failures. Treat WARNs as required manual review items and record whether they are intentional low-level mechanisms or real bypasses.

5. **Unity verification**
   - `unity-cli --project Mdfproject status`
   - `unity-cli --project Mdfproject editor refresh --compile`
   - `unity-cli --project Mdfproject console --type error --stacktrace user`
   - Tests when relevant: EditMode and PlayMode.

6. **Build/E2E verification**
   - Build a Development player when the feature affects multiplayer, commands, scenes, UI flow, or networking.
   - Run the smallest relevant matrix profile or targeted case:
     - `python tools/harness/mp/run_matrix.py --profile smoke` for cheap Editor/Build coverage.
     - `python tools/harness/mp/run_matrix.py --profile random-aware` for AI, prepare, content, or random progression changes.
     - `python tools/harness/mp/run_matrix.py --profile battle` for battle command changes.
     - `python tools/harness/mp/run_matrix.py --profile nightly` only for merge/nightly coverage.
     - `python tools/harness/mp/run_matrix.py --case <case>` for a targeted regression.
   - `--case all` preserves the existing default subset and is not nightly.
   - Capture `[MPTEST]` logs, screenshots, stdout/stderr, Unity console, and state snapshots.
   - For HumanBot or progressed-state scenarios, wait on state gates such as scene, player count, snapshot readiness, accepted command, round/state, and migration/reconnect events. Avoid wall-clock sleeps as proof.

7. **Triage**
   - Compare snapshots.
   - Use logs and screenshots to identify root cause.
   - If failure is real, fix and repeat from step 3.
   - If the environment cannot run, report `NEEDS_ENVIRONMENT` and do not claim PASS.

8. **Knowledge capture**
   - If a new stable command, timing, screenshot trick, build path, or failure pattern is found, update `learned-recipes.md` before the final response.

## Randomized progression loop

For Phase 18 and later random-aware harness work:

1. Do one phase at a time.
2. Stop after the phase's required verification commands.
3. Do not advance to the next phase unless command output and artifacts prove PASS.
4. Use HumanBotDriver to create progressed state before reconnect, disconnect, or Host Migration tests.
5. Keep command journals as diagnostics, not as the primary reproduction engine.
6. Compare host/client/build/editor snapshots by same-player hashes and invariants.
7. Do not weaken Host Migration, reconnect, or field assertions because randomness exists. Randomness before a checkpoint is allowed; divergence after a checkpoint is not.

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
