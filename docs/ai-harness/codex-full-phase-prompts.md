# MDF Codex Full Phase Prompts

Use these prompts in order. Do not ask Codex to run all phases at once unless you explicitly want a long uncontrolled implementation. The safest flow is: paste one phase, review the report, then paste the next phase.

## Phase 0/1 — Baseline and overlay validation

```text
Read first:
- AGENTS.md
- .agent/rules/projectrull.md
- docs/ai-harness/index.md
- docs/ai-harness/project-structure.md
- docs/ai-harness/review-findings.md
- docs/ai-harness/feature-implementation-loop.md
- docs/ai-harness/unity-cli-recipes.md
- docs/ai-harness/learned-recipes.md

Start with Phase 0/1 only.

Goal:
Validate the MDF harness overlay against the actual repository before implementing runtime harness code.

Do:
1. Inspect ProjectSettings/ProjectVersion.txt, Packages/manifest.json, packages-lock.json, EditorBuildSettings.asset, and Assets/Scripts.
2. Confirm Unity version, Fusion version/path, Test Framework version, unity-cli connector presence, main scenes, and build scenes.
3. Confirm primary runtime paths: NetworkManager, HostMigrationHandler, NetworkPlayer, GameSceneInitializer, GameManagers partials, PlayerManager, FieldManager, ShopManager, CommandProcessor, AIPlayerController.
4. Confirm whether this repo already contains Assets/Scripts/Testing/MP or tools/harness/mp code.
5. Run, if available:
   - python tools/harness/validate_overlay.py
   - python tools/harness/precommit.py --self-test
   - unity-cli --project Mdfproject status
   - unity-cli --project Mdfproject list
6. If unity-cli flags differ, run --help and update docs/ai-harness/learned-recipes.md.

Allowed modifications:
- AGENTS.md only if it is inaccurate or over 70 lines.
- docs/ai-harness/*.md only to correct repo-specific assumptions.
- Do not implement runtime C# harness yet.
- Do not modify gameplay code.

Report:
- Unity/Fusion/Test Framework/unity-cli facts.
- Files changed.
- Commands run and outputs summarized.
- Assumptions corrected.
- Blockers before Phase 2.
```

## Phase 2 — Mechanical enforcement

```text
Proceed with Phase 2 only: mechanical enforcement.

Read:
- AGENTS.md
- docs/ai-harness/index.md
- docs/ai-harness/fusion-sync-rules.md
- docs/ai-harness/review-findings.md
- docs/ai-harness/knowledge-capture.md

Goal:
Make it hard for Codex to bypass guardrails or expose test automation in production.

Implement or update:
- tools/harness/precommit.py
- tools/harness/install_git_hooks.sh
- .codex/hooks/*.py
- .codex/hooks.json
- .codex/config.toml

BLOCK checks:
- git commit --no-verify / git commit -n
- destructive commands such as rm -rf Assets, ProjectSettings, Packages, Library, or git clean -xfd unless explicitly approved
- edits to Mdfproject/Assets/Photon/Fusion/** and other vendor/sample folders unless explicitly approved
- UnityEditor references outside Editor folders or #if UNITY_EDITOR
- MPTestAutomationServer or similar test server without UNITY_EDITOR || DEVELOPMENT_BUILD gate
- automation server without --mpTest runtime gate
- automation server binding outside 127.0.0.1/localhost/IPAddress.Loopback
- automation endpoint without per-run token/auth

WARN checks:
- RpcSources.All without RpcInfo/manual authority validation
- client-supplied playerId/gold/health/wall/shop/augment/spawn/cooldown trusted without authority validation
- persistent state represented only by RPC side effects
- NetworkRunner.Instances usage without justification
- PlayerRef treated as durable identity
- command class missing CommandType/Serialize/Deserialize coverage
- Host Migration partial edits without touching related recovery files
- excessive Debug.Log in tick/update paths

Verification:
- python tools/harness/precommit.py --self-test
- python tools/harness/precommit.py --all
- python tools/harness/validate_overlay.py
- unity-cli --project Mdfproject status
- unity-cli --project Mdfproject editor refresh --compile
- unity-cli --project Mdfproject console --type error --stacktrace user

Use mdf_unity_verifier for verification if available.
Stop after Phase 2 and report exact files changed, commands run, PASS/FAIL, and blockers.
```

## Phase 3 — Skills, custom agents, and knowledge capture

```text
Proceed with Phase 3 only: Codex skills, custom agents, and knowledge capture.

Read:
- AGENTS.md
- docs/ai-harness/knowledge-capture.md
- docs/ai-harness/learned-recipes.md
- docs/ai-harness/feature-implementation-loop.md

Goal:
Ensure future sessions reuse learned CLI/build/screenshot/Photon methods and use verifier agents instead of self-grading.

Create/update skills:
- .agents/skills/use-learned-recipes/SKILL.md
- .agents/skills/capture-learning/SKILL.md
- .agents/skills/verify-unity/SKILL.md
- .agents/skills/review-fusion-sync/SKILL.md
- .agents/skills/mp-harness-test/SKILL.md
- .agents/skills/build-player/SKILL.md
- .agents/skills/asset-safe-edit/SKILL.md
- .agents/skills/entropy-gc/SKILL.md

Create/update custom agents:
- .codex/agents/mdf_code_mapper.toml
- .codex/agents/mdf_fusion_reviewer.toml
- .codex/agents/mdf_unity_verifier.toml
- .codex/agents/mdf_mp_test_runner.toml
- .codex/agents/mdf_asset_guard.toml

Rules:
- Keep AGENTS.md under 70 lines.
- Put long procedures in docs/ai-harness or skills, not AGENTS.md.
- Subagents must be explicitly requested in later prompts.
- Verifier agents must not edit gameplay code.

Verification:
- python tools/harness/validate_overlay.py
- check every referenced skill/agent/doc exists
- unity-cli --project Mdfproject status

Stop after Phase 3 and report files changed, validation, and blockers.
```

## Phase 4 — Unity CLI custom tools

```text
Proceed with Phase 4 only: MDF unity-cli custom tools.

Read:
- AGENTS.md
- docs/ai-harness/project-structure.md
- docs/ai-harness/unity-cli-recipes.md
- docs/ai-harness/mp-test-protocol.md
- docs/ai-harness/state-snapshot-schema.md
- docs/ai-harness/learned-recipes.md

Use $use-learned-recipes before starting.
Use mdf_code_mapper to confirm actual NetworkManager/GameManagers entry points before editing.

Goal:
Add Editor-only unity-cli custom tools that can start/join/load/dump/assert/control the MDF multiplayer harness from the Editor side.

Implement under:
- Mdfproject/Assets/Scripts/Testing/MP/Editor/MPTestUnityCliTools.cs
- Mdfproject/Assets/Scripts/Testing/MP/Editor/BuildAutomation.cs

Required custom tools:
- mp_start_host
- mp_join_client
- mp_load_game
- mp_dump_state
- mp_assert_state
- mp_command
- mp_screenshot
- mp_stop
- mp_build_player

Optional MDF domain tools if safe:
- mp_prepare_smoke
- mp_battle_smoke
- mp_ai_fill_probe
- mp_host_migration_probe

Rules:
- Editor-only code must be inside Editor folder or #if UNITY_EDITOR.
- Use [UnityCliTool] with discoverable Parameters classes where practical.
- Do not edit Photon/Fusion vendor files.
- Do not add production UI or menu dependencies.
- If a tool cannot yet perform the action, return explicit not_implemented JSON, not silent success.

Verification:
- unity-cli --project Mdfproject status
- unity-cli --project Mdfproject editor refresh --compile
- unity-cli --project Mdfproject console --type error --stacktrace user
- unity-cli --project Mdfproject list
- confirm mp_* tools appear in list

Use mdf_unity_verifier before reporting PASS.
If you discover a reliable command pattern, update learned-recipes.md.
Stop after Phase 4.
```

## Phase 5 — Runtime bootstrap and MPTEST timeline

```text
Proceed with Phase 5 only: runtime MP test bootstrap and [MPTEST] logs.

Read:
- docs/ai-harness/mp-test-protocol.md
- docs/ai-harness/automation-server-contract.md
- docs/ai-harness/fusion-sync-rules.md
- docs/ai-harness/project-structure.md

Goal:
Make Editor and built players start deterministic test sessions through command-line args and emit structured timeline logs.

Implement under Mdfproject/Assets/Scripts/Testing/MP:
- MPTestCommandLine.cs
- MPTestBootstrap.cs
- MPTestLogger.cs
- MPTestCommands.cs if needed

Required args:
- --mpTest
- --mpRole host|client
- --mpSession <session>
- --mpMaxPlayers <n>
- --mpScene Game
- --mpAutoStart
- --mpLoadGame
- --mpExitAfterSeconds <seconds>
- --mpAutomationPort <port>
- --mpAutomationToken <token>
- --mpConnectionToken <token>
- --mpCase <caseName>
- --mpArtifactDir <path>
- --mpSeed <seed>
- --mpScenario lobby_smoke|game_smoke|prepare_smoke|battle_smoke|host_migration

Rules:
- Only activate behavior when --mpTest is present.
- Set Application.runInBackground = true in --mpTest mode.
- Do not print raw connection tokens or automation tokens.
- [MPTEST] logs must include case, role, session, phase, tick if available, and error code/message on failures.
- Do not expose automation server yet; that is Phase 7.

Verification:
- compile/console checks
- EditMode tests if parser tests exist
- learned-recipes update for command-line quirks

Use mdf_fusion_reviewer after runtime/network bootstrap changes.
Use mdf_unity_verifier before PASS.
Stop after Phase 5.
```

## Phase 6 — State snapshot and assertions

```text
Proceed with Phase 6 only: MDF state snapshot and assertions.

Read:
- docs/ai-harness/state-snapshot-schema.md
- docs/ai-harness/fusion-sync-rules.md
- docs/ai-harness/project-structure.md

Goal:
Create comparable snapshots that prove durable multiplayer state, not just logs.

Implement:
- Mdfproject/Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs
- Mdfproject/Assets/Scripts/Testing/MP/MPTestAssertions.cs

Snapshot must include stable fields for:
- runner/session/scene/tick/player count
- GameManagers state, round, timer, battle pairing hashes
- PlayerManager playerId, connection token hash, HP, gold, wall count, alive/eliminated state
- shop revision/count/items hash when available
- FieldManager grid/wall/unit aggregate hashes
- monsters/alive counts/spawner readiness
- AI controller status
- command sequence/queue state when available
- HostMigrationHandler status and last migration event when available
- errors array

Rules:
- Sort arrays deterministically.
- Do not compare raw Unity instance IDs.
- Do not require PlayerRef stability across reconnect/migration.
- Allow tick/timer/visual transform epsilon.
- Mark fields unknown instead of inventing data.

Verification:
- compile/console checks
- EditMode tests for serialization/comparison if possible
- mdf_fusion_reviewer and mdf_unity_verifier

Stop after Phase 6.
```

## Phase 7 — Build-side automation server

```text
Proceed with Phase 7 only: build-side automation server.

Read:
- docs/ai-harness/automation-server-contract.md
- docs/ai-harness/mp-test-protocol.md
- docs/ai-harness/state-snapshot-schema.md
- docs/ai-harness/fusion-sync-rules.md

Goal:
Allow Codex to inspect/control built players during E2E without exposing production attack surface.

Implement:
- Mdfproject/Assets/Scripts/Testing/MP/MPTestAutomationServer.cs
- Mdfproject/Assets/Scripts/Testing/MP/MPTestMainThreadDispatcher.cs
- MPTestAutomationClient.cs only if useful

Required gates:
- #if UNITY_EDITOR || DEVELOPMENT_BUILD
- --mpTest required
- --mpAutomationToken required
- bind only 127.0.0.1/localhost/IPAddress.Loopback
- every mutating/read endpoint checks token
- Unity APIs run on main thread
- no raw token/AppId/user secret in logs or artifacts

Endpoints:
- GET /ping
- POST /quit
- GET /dumpState
- POST /startHost
- POST /join
- POST /loadGame
- POST /command
- POST /assertState
- GET /screenshot
- GET /logs/recent

Response:
- JSON only
- success, message, timestampUtc, data/error
- explicit error code for failure

Verification:
- compile/console checks
- python tools/harness/precommit.py --all
- static proof non-development builds cannot run automation server
- tests if available
- mdf_fusion_reviewer and mdf_unity_verifier

Stop after Phase 7.
```

## Phase 8 — Unity tests

```text
Proceed with Phase 8 only: EditMode and PlayMode tests.

Read:
- docs/ai-harness/implementation-phases.md
- docs/ai-harness/state-snapshot-schema.md
- docs/ai-harness/automation-server-contract.md

Goal:
Add focused tests for harness internals without requiring Photon Cloud connectivity unless marked integration.

Required EditMode tests:
- command-line parser
- [MPTEST] log format
- snapshot serialization and comparison
- automation server safety gate/static validation
- precommit self-test or fixture checks

Required PlayMode tests if practical:
- MPTestBootstrap smoke
- snapshot smoke in Game scene or test scene
- no automation activation without --mpTest
- main-thread dispatcher smoke

Rules:
- Prefer deterministic assertions.
- Do not create fragile UI timing tests unless the test directly needs UI.
- If Test Framework 1.1 flags differ, use --help/fallback and update learned-recipes.

Verification:
- unity-cli --project Mdfproject editor refresh --compile
- unity-cli --project Mdfproject console --type error --stacktrace user
- unity-cli --project Mdfproject test --mode EditMode
- unity-cli --project Mdfproject test --mode PlayMode

Use mdf_unity_verifier.
Stop after Phase 8.
```

## Phase 9 — Development build pipeline

```text
Proceed with Phase 9 only: Development build pipeline.

Read:
- docs/ai-harness/unity-cli-recipes.md
- docs/ai-harness/mp-test-protocol.md
- docs/ai-harness/failure-triage.md

Goal:
Build a deterministic Development player that can be launched by E2E scripts with --mpTest args.

Implement/update:
- Mdfproject/Assets/Scripts/Testing/MP/Editor/BuildAutomation.cs
- tools/harness/mp/build_player.py or equivalent
- docs/ai-harness/learned-recipes.md for verified build output paths

Requirements:
- Build target defaults to current desktop platform.
- Build output under artifacts/builds or a documented ignored path.
- Include Development Build.
- Do not require production secrets.
- Copy or discover player log path.
- Record build metadata: Unity version, scenes, timestamp, output path.

Verification:
- unity-cli --project Mdfproject mp_build_player or fallback Unity executeMethod
- build artifact exists
- build launches with --mpTest --mpRole host --mpExitAfterSeconds 5 if environment allows
- logs collected

Stop after Phase 9.
```

## Phase 10 — E2E MVP matrix

```text
Proceed with Phase 10 only: E2E MVP matrix.

Read:
- docs/ai-harness/mp-test-protocol.md
- docs/ai-harness/automation-server-contract.md
- docs/ai-harness/state-snapshot-schema.md
- docs/ai-harness/failure-triage.md

Goal:
Prove the core loop: Editor Host + Build Client and Build Host + Editor Client.

Implement under tools/harness/mp:
- common.py
- launch_player.py
- automation_client.py
- compare_state_snapshots.py
- parse_mptest_logs.py
- collect_artifacts.py
- run_editor_host_build_client.py
- run_build_host_editor_client.py
- run_matrix.py

Required flow:
1. Build or reuse Development player.
2. Generate unique session, automation ports, automation tokens, connection tokens, artifact dir.
3. Start host and client in the requested direction.
4. Wait for /ping where build automation server exists.
5. Wait for session/scene/expected players.
6. Dump snapshots from both peers.
7. Run deterministic prepare/game smoke command if safe.
8. Dump snapshots again.
9. Compare durable state.
10. Capture screenshots/logs/stdout/stderr/[MPTEST]/command transcript.
11. Cleanup all processes and stop Editor play mode.

Verification:
- dry-run mode
- shortest real Editor Host + Build Client if environment allows
- shortest real Build Host + Editor Client if environment allows

Use mdf_mp_test_runner and mdf_unity_verifier.
Do not claim PASS without artifacts.
Stop after Phase 10.
```

## Phase 11 — Build Host + Build Client

```text
Proceed with Phase 11 only: Build Host + Build Client.

Goal:
Prove that built players can both host and join without relying on Editor-only state.

Implement:
- tools/harness/mp/run_build_host_build_client.py
- add case to run_matrix.py

Required assertions:
- both build peers start with --mpTest
- host and client automation servers respond
- same session
- Game scene reached
- expected player count 2
- durable state snapshots match
- no production automation exposure

Collect artifacts from both processes.
Stop after Phase 11.
```

## Phase 12 — AI fill and disconnect -> AI takeover

```text
Proceed with Phase 12 only: AI fill and disconnect/takeover proof.

Goal:
Prove AI fill or AI takeover only if the game code supports it and the harness can assert it.

Implement one or both based on actual code evidence:
- tools/harness/mp/run_ai_fill_smoke.py
- tools/harness/mp/run_disconnect_ai_takeover.py

AI fill assertions:
- expected human peers < max players
- remaining playerIds filled by AI or documented NOT_SUPPORTED
- AI controller registered
- fields/player state exist for AI slots

Disconnect assertions:
- start host + client
- record client playerId
- terminate client process without normal /quit if testing disconnect
- host detects leave
- same playerId/field remains if AI takeover is supported
- AI controller active and no duplicate playerId

Do not claim PASS if only logs exist without snapshot assertions.
Stop after Phase 12.
```

## Phase 13 — Same-token reconnect

```text
Proceed with Phase 13 only: same-token reconnect.

Goal:
Prove reconnect identity with --mpConnectionToken if supported.

Required flow:
1. Start host + client A with token C.
2. Record playerId and connectionTokenHash.
3. Kill client A.
4. Wait for disconnect/AI takeover or safe disconnected state.
5. Start client B with same token C and new automation port/token.
6. Assert same playerId reclaimed, PlayerRef may differ, AI released if applicable.
7. Compare snapshots and collect artifacts.

Do not log raw token. Do not require PlayerRef equality.
Report NOT_SUPPORTED if project code lacks reconnect cache/logic.
Stop after Phase 13.
```

## Phase 14 — 4-player smoke

```text
Proceed with Phase 14 only: 4-player smoke.

Goal:
Prove max-player path with one host plus three clients.

Implement:
- tools/harness/mp/run_four_player_smoke.py

Requirements:
- 4 automation ports and tokens
- 4 connection tokens
- deterministic artifact directory per peer
- expected player count 4
- unique playerIds 0..3 or project-supported range
- each player has PlayerManager and FieldManager readiness
- snapshots comparable across all peers
- cleanup all processes

Run 3-player only if 4-player is blocked by environment, but report exact reason.
Stop after Phase 14.
```

## Phase 15 — Host Migration feasibility

```text
Proceed with Phase 15 only: Host Migration feasibility.

Read:
- docs/ai-harness/host-migration-test-plan.md

Goal:
Do not implement fake migration PASS. First prove callbacks and tokens occur.

Required:
- inspect NetworkProjectConfig.fusion for host migration settings
- inspect NetworkManager.OnHostMigration and HostMigrationHandler
- add [MPTEST] migration logs if missing
- add snapshot fields for last migration event if missing
- run a controlled host-drop probe if environment allows

Feasibility PASS requires:
- original host drop, not normal /quit
- OnHostMigration called on surviving peer
- non-null HostMigrationToken
- new runner started or a precise blocker identified
- artifacts collected

If not feasible, report HOST_MIGRATION_NOT_PROVEN with exact missing condition.
Stop after Phase 15.
```

## Phase 16 — Host Migration E2E

```text
Proceed with Phase 16 only if Phase 15 feasibility passed.

Goal:
Prove end-to-end Host Migration recovery.

Required flow:
1. Start host + at least one client.
2. Reach Game scene and stable state.
3. Dump pre-migration snapshots.
4. Kill/drop host process.
5. Wait for surviving peer migration callback, token, resume, rebuild.
6. Dump post-migration snapshots.
7. Assert GameManagers restored, playerIds unique, fields/shops/walls/HP restored, AI reconciliation complete or documented, stale PlayerRef not used.
8. Run a simple post-migration command if safe.
9. Collect artifacts.

Do not claim PASS if the game restarted fresh.
Stop after Phase 16.
```

## Phase 17 — Durable command soak

```text
Proceed with Phase 17 only: durable command soak.

Goal:
Prove repeated multiplayer commands mutate durable gameplay state consistently, not just queue ping commands.

Choose a safe deterministic command based on current project support:
- RerollShop if cost can be controlled and snapshot can verify shop revision/hash;
- PlaceWall/RemoveWall if grid cell can be found deterministically;
- BuyUnit if shop/gold/unit data are stable;
- test-only durable counter only if clearly marked as harness-only and not gameplay PASS.

Required:
- N iterations, default 10
- dump snapshot before/after each or at checkpoints
- assert command sequence monotonicity
- assert durable state mutation and peer agreement
- collect performance/profiler data if useful

If no safe durable command exists, report NEEDS_GAMEPLAY_HOOK and propose minimal hook.
Stop after Phase 17.
```

## Phase 18 - Random-aware harness doctrine/docs

```text
Proceed with Phase 18 only: random-aware progression harness planning.

Goal:
Update the harness docs so future phases prioritize HumanBot-first random-aware testing instead of exact command replay.

Read:
- AGENTS.md
- docs/ai-harness/index.md
- docs/ai-harness/feature-implementation-loop.md
- docs/ai-harness/fusion-sync-rules.md
- docs/ai-harness/mp-test-protocol.md
- docs/ai-harness/state-snapshot-schema.md
- docs/ai-harness/learned-recipes.md
- Mdfproject/Assets/Scripts/Commands/AI/AIPlayerController.cs
- Mdfproject/Assets/Scripts/AI/BehaviorTree/**/*.cs
- Mdfproject/Assets/Scripts/Commands/Core/CommandProcessor.cs
- Mdfproject/Assets/Scripts/Managers/ShopManager.cs
- Mdfproject/Assets/Scripts/Managers/AugmentManager.cs
- Mdfproject/Assets/Scripts/Managers/FieldManager.cs
- Mdfproject/Assets/Scripts/Managers/GameManagers.cs

Implement docs only unless a tiny self-test update is necessary.

Create or update:
- docs/ai-harness/randomized-progression-test-plan.md
- docs/ai-harness/human-bot-driver-design.md
- docs/ai-harness/state-snapshot-schema.md
- docs/ai-harness/mp-test-protocol.md
- docs/ai-harness/automation-server-contract.md if endpoint docs are needed
- docs/ai-harness/feature-implementation-loop.md
- docs/ai-harness/learned-recipes.md

Required content:
- exact command replay is diagnostic, not the main progression strategy
- HumanBotDriver is test-only, drives a real human peer, keeps `isAI=false`, never registers `AIPlayerController`, and uses real command request paths
- random-aware assertions compare same-player hashes and invariants, not fixed random values
- bot decision, accepted command, random outcome, and checkpoint journals
- progressed-state prepare, battle entry, reconnect, disconnect, Host Migration, 4-player, and seed sweep scenarios
- deterministic seed/RNG refactor is optional future work

Verification:
- python tools/harness/validate_overlay.py
- python tools/harness/precommit.py --all

Stop after Phase 18 and report files changed and the new phase plan.
```

## Phase 19 - HumanBotDriver core

```text
Proceed with Phase 19 only: MPTestHumanBotDriver core.

Goal:
Implement a test-only HumanBotDriver that drives a real connected human client using AI-like decision policies.

Important:
- Do not attach AIPlayerController to a human player.
- Do not register the human bot in ComponentRegistry as AIPlayerController.
- The snapshot must continue to report this peer as human/isAI=false.
- Commands must go through CommandProcessor.RequestCommandExecution and server authority validation.

Read:
- docs/ai-harness/randomized-progression-test-plan.md
- docs/ai-harness/human-bot-driver-design.md
- docs/ai-harness/fusion-sync-rules.md
- Mdfproject/Assets/Scripts/Commands/AI/AIPlayerController.cs
- Mdfproject/Assets/Scripts/AI/BehaviorTree/Nodes/Actions/*.cs
- Mdfproject/Assets/Scripts/AI/Planning/MazePlanner.cs
- Mdfproject/Assets/Scripts/AI/BehaviorTree/AIPacer.cs
- Mdfproject/Assets/Scripts/Commands/Core/CommandProcessor.cs
- Mdfproject/Assets/Scripts/Testing/MP/MPTestBootstrap.cs
- Mdfproject/Assets/Scripts/Testing/MP/MPTestAutomationServer.cs
- Mdfproject/Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs

Implement:
- Mdfproject/Assets/Scripts/Testing/MP/MPTestHumanBotDriver.cs
- Mdfproject/Assets/Scripts/Testing/MP/MPTestHumanBotPolicy.cs
- Mdfproject/Assets/Scripts/Testing/MP/MPTestBotPersona.cs
- Mdfproject/Assets/Scripts/Testing/MP/MPTestBotJournal.cs if useful
- command-line args documented in human-bot-driver-design.md
- /bot/start, /bot/stop, /bot/status, /bot/journal if practical
- test-only snapshot bot status

Verification:
- python tools/harness/precommit.py --all
- unity-cli --project Mdfproject editor refresh --compile
- unity-cli --project Mdfproject console --type error --stacktrace user
- unity-cli --project Mdfproject test --mode EditMode

Use mdf_code_mapper if code paths are unclear, mdf_fusion_reviewer after command/networking changes, and mdf_unity_verifier before reporting.
Stop after Phase 19. Do not claim gameplay progression PASS yet.
```

## Phase 20 - 2-peer HumanBot prepare progression

```text
Proceed with Phase 20 only: 2-peer HumanBot prepare progression E2E.

Goal:
Prove that a real connected client peer can progress Prepare phase using MPTestHumanBotDriver under randomized shop/wall/augment conditions.

Implement:
- tools/harness/mp/run_human_bot_prepare_progression.py
- optional matrix case: human-bot-prepare

Required assertions:
- bot player is connected human, not AI
- bot commandsIssued > 0
- at least one meaningful command is accepted
- no duplicate playerId
- host/client shop, augment, field grid/wall/unit hashes agree for the same player
- command sequence/revision monotonic where available
- no MPTEST error phase or user console errors

Verification:
- python tools/harness/precommit.py --all
- unity-cli --project Mdfproject editor refresh --compile
- unity-cli --project Mdfproject console --type error --stacktrace user
- unity-cli --project Mdfproject test --mode EditMode
- python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001

Use mdf_mp_test_runner and mdf_unity_verifier. Stop after Phase 20.
```

## Phase 21 - 4-player HumanBot progression smoke

```text
Proceed with Phase 21 only: 4-player HumanBot progression smoke.

Goal:
Extend HumanBot progression to a 4-player match.

Implement:
- tools/harness/mp/run_human_bot_4p_progression.py
- matrix case: human-bot-4p-progression, not default-all until runtime is stable

Required assertions:
- activePlayerCount = 4
- all four players remain human/isAI=false
- each bot issues at least one command where possible
- host snapshot agrees with every client for the same player's game, shop, augment, field, unit, battle, HP, gold, and command state
- no duplicate playerId or command sequence divergence

Verification:
- python tools/harness/precommit.py --all
- unity-cli --project Mdfproject editor refresh --compile
- unity-cli --project Mdfproject console --type error --stacktrace user
- python tools/harness/mp/run_human_bot_4p_progression.py --seed 2001

Use mdf_mp_test_runner and mdf_unity_verifier. Stop after Phase 21.
```

## Phase 22 - Progressed-state reconnect / disconnect

```text
Proceed with Phase 22 only: progressed-state reconnect and disconnect tests.

Goal:
Run disconnect -> AI takeover and same-token reconnect after HumanBot has progressed the game state.

Implement:
- tools/harness/mp/run_progressed_disconnect_ai_takeover.py
- tools/harness/mp/run_progressed_same_token_reconnect.py

Shared setup:
- build host + build client
- enable --mpHumanBot on the client
- wait for commandsIssued >= N, durable state changed, and host/client checkpoint equality

Case A:
- kill the client process
- assert same playerId remains, isConnected=false, isAI=true, AI controller registered, and progressed field/shop/wall state preserved

Case B:
- relaunch with same --mpConnectionToken
- assert same playerId reclaimed, isConnected=true, isAI=false, and progressed state is visible on the reconnected client

Verification:
- python tools/harness/precommit.py --all
- unity-cli --project Mdfproject editor refresh --compile
- unity-cli --project Mdfproject console --type error --stacktrace user
- python tools/harness/mp/run_progressed_disconnect_ai_takeover.py --seed 3001
- python tools/harness/mp/run_progressed_same_token_reconnect.py --seed 3002

Use mdf_fusion_reviewer, mdf_mp_test_runner, and mdf_unity_verifier. Stop after Phase 22.
```

## Phase 23 - Progressed-state Host Migration

```text
Proceed with Phase 23 only: progressed-state Host Migration E2E.

Goal:
Prove Host Migration after randomized HumanBot progression.

Implement:
- tools/harness/mp/run_progressed_host_migration_e2e.py

Required flow:
- launch host plus at least one client
- enable HumanBot on one or more human clients
- progress until meaningful commands and pre-migration full comparison succeed
- kill the host process
- require OnHostMigration, non-null token, StartGame with migration token, HostMigrationResume, and recovery complete
- compare pre/post durable state with random-aware rules

Verification:
- python tools/harness/precommit.py --all
- unity-cli --project Mdfproject editor refresh --compile
- unity-cli --project Mdfproject console --type error --stacktrace user
- python tools/harness/mp/run_progressed_host_migration_e2e.py --seed 4001

Use mdf_fusion_reviewer, mdf_mp_test_runner, and mdf_unity_verifier. Stop after Phase 23.
```

## Phase 24 - Random seed sweep / stochastic soak

```text
Proceed with Phase 24 only: stochastic HumanBot seed sweep.

Goal:
Run multiple randomized HumanBot progression cases to find sync bugs that one seed may miss.

Implement:
- tools/harness/mp/run_human_bot_seed_sweep.py

Requirements:
- accept --seeds or --seed-start/--seed-count
- run 2-peer prepare progression per seed
- optionally run 4-player progression with --include-4p
- collect seed-specific journals, snapshots, comparisons, random outcome hashes, and screenshots
- stop on first failure unless --continue-on-fail is set

Verification:
- python tools/harness/mp/run_human_bot_seed_sweep.py --seeds 5101,5102,5103
- python tools/harness/precommit.py --all
- unity-cli --project Mdfproject console --type error --stacktrace user

Use mdf_mp_test_runner. Stop after Phase 24.
```

## Phase 25 - Random authority hardening

```text
Proceed with Phase 25 only: random authority hardening.

Goal:
Audit and harden authoritative random outcomes used by shop, augments, initial walls, battle mapping, and AI/bot progression.

Read:
- docs/ai-harness/randomized-progression-test-plan.md
- docs/ai-harness/fusion-sync-rules.md
- docs/ai-harness/state-snapshot-schema.md
- Mdfproject/Assets/Scripts/Managers/ShopManager.cs
- Mdfproject/Assets/Scripts/Managers/AugmentManager.cs
- Mdfproject/Assets/Scripts/Managers/FieldManager.cs
- Mdfproject/Assets/Scripts/Managers/GameManagers.cs
- Mdfproject/Assets/Scripts/AI/Planning/MazePlanner.cs
- Mdfproject/Assets/Scripts/AI/BehaviorTree/AIPacer.cs

Audit:
- find UnityEngine.Random, System.Random, Environment.TickCount usage
- classify authoritative gameplay random, client visual/random delay, AI/bot decision pacing, or test-only
- harden authoritative gameplay random with authority-only generation, outcome sync, snapshot hash/revision, optional test-only seed control, and optional random outcome journal

Verification:
- python tools/harness/precommit.py --all
- unity-cli --project Mdfproject editor refresh --compile
- unity-cli --project Mdfproject console --type error --stacktrace user
- relevant HumanBot progression tests
- progressed Host Migration if authoritative random paths changed

Use mdf_code_mapper, mdf_fusion_reviewer, mdf_mp_test_runner, and mdf_unity_verifier. Stop after Phase 25 and report remaining RNG risks.
```

## Final random-aware audit

```text
Run final random-aware MDF harness audit. Do not add new features.

Audit:
- HumanBotDriver exists and is test-only.
- HumanBotDriver does not register as AIPlayerController.
- HumanBot player remains connected human/isAI=false in snapshots.
- HumanBot commands go through the real client request path where required.
- Random-aware assertions do not expect fixed shop/wall/augment values.
- Same player's random outcomes are equal across host/client/build/editor snapshots.
- Bot decision journal, accepted command journal, random outcome summary, and checkpoint snapshots are collected.
- 2-peer, 4-player, progressed reconnect/disconnect, progressed Host Migration, and seed sweep artifacts exist or exact blockers are documented.
- learned-recipes.md documents verified HumanBot and random-aware commands.

Run:
- python tools/harness/validate_overlay.py
- python tools/harness/precommit.py --self-test
- python tools/harness/precommit.py --all
- unity-cli --project Mdfproject status
- unity-cli --project Mdfproject editor refresh --compile
- unity-cli --project Mdfproject console --type error --stacktrace user
- unity-cli --project Mdfproject test --mode EditMode
- python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001
- python tools/harness/mp/run_human_bot_4p_progression.py --seed 2001
- python tools/harness/mp/run_progressed_same_token_reconnect.py --seed 3002
- python tools/harness/mp/run_progressed_host_migration_e2e.py --seed 4001
- python tools/harness/mp/run_human_bot_seed_sweep.py --seeds 5101,5102,5103

Use mdf_fusion_reviewer, mdf_mp_test_runner, and mdf_unity_verifier.
Do not claim final PASS if any required command or artifact is missing.
```

## Final audit

```text
Run final MDF harness audit. Do not add new features.

Read all docs under docs/ai-harness.

Audit:
1. AGENTS.md is under 70 lines and accurate.
2. projectrull.md is concise and accurate.
3. All referenced docs, skills, hooks, agents, and scripts exist.
4. No Claude-only or SimpleRTS-specific instructions remain unless marked historical.
5. Runtime automation cannot run in production builds.
6. Photon/Fusion and other vendor files were not edited.
7. unity-cli commands are verified for this Unity 2021 project or documented as fallback.
8. learned-recipes.md contains discoveries from implementation.
9. Precommit false-positive risk is acceptable; uncertain semantic checks are WARN, not BLOCK.
10. E2E artifacts are deterministic and include logs/screenshots/snapshots/transcripts.

Run:
- git status --short
- python tools/harness/validate_overlay.py
- python tools/harness/precommit.py --self-test
- python tools/harness/precommit.py --all
- unity-cli --project Mdfproject status
- unity-cli --project Mdfproject editor refresh --compile
- unity-cli --project Mdfproject console --type error --stacktrace user
- unity-cli --project Mdfproject test --mode EditMode
- unity-cli --project Mdfproject test --mode PlayMode
- shortest available E2E matrix case

Use subagents:
- mdf_fusion_reviewer for network/authority audit
- mdf_asset_guard for asset audit
- mdf_unity_verifier for Unity verification
- mdf_mp_test_runner for E2E audit

Final report:
- PASS/FAIL
- commands run and outputs summarized
- artifacts produced
- remaining blockers
- exact next manual action if any

Do not claim final PASS if any required command was skipped without reason.
```
