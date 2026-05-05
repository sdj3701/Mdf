# MDF Codex Multiplayer Harness Plan

## Purpose

This harness lets a Codex agent implement a gameplay feature, verify it in a real Photon Fusion multiplayer environment, inspect logs/screenshots/state snapshots, modify code, and repeat the loop until evidence supports PASS.

Target project:

- Unity root: `Mdfproject`
- Unity version: `2021.3.45f1`
- Fusion: `Assets/Photon/Fusion/build_info.txt` reports `2.0.9 Stable 1566`
- Unity Test Framework: `com.unity.test-framework` `1.1.33`
- unity-cli connector: `com.youngwoocho02.unity-cli-connector`
- Main scenes: `Title`, `MatchingLobby`, `JoinLobby`, `Game`
- Core code: `Mdfproject/Assets/Scripts`


## v2 additions and runbook

The v2 overlay adds these source-of-truth files:

- `review-findings.md` — what was missing in the first MDF overlay and how v2 fixes it.
- `feature-implementation-loop.md` — the default loop for any future gameplay/networking feature: analyze, implement, compile, build, E2E, inspect artifacts, fix, repeat.
- `host-migration-test-plan.md` — feasibility-first Host Migration proof criteria.
- `codex-full-phase-prompts.md` — copy-paste prompts from Phase 0/1 through final audit.
- `.codex/agents/*.toml` — explicit custom agents for mapping, Fusion review, Unity verification, MP E2E, and asset audit.

Do not ask Codex to implement all phases at once. Use `codex-full-phase-prompts.md` one phase at a time.

## Phase 18+ random-aware docs

New Phase 18+ source docs:

- `randomized-progression-test-plan.md` - HumanBot-first random-aware progression doctrine.
- `human-bot-driver-design.md` - test-only real-human-peer bot driver design and constraints.
- `random-authority-audit.md` - Phase 25 classification of authoritative gameplay RNG, AI/bot randomness, client-only randomness, and remaining random-state risks.

After Phase 17, command journals are diagnostics; HumanBot-driven real peer progression is the main path for mid/late-game sync tests.

## Harness goal

The core MVP is not just “compile and unit test”. The MVP is:

1. Build a Development player.
2. Launch host/client pairs with deterministic test arguments.
3. Run both directions:
   - Editor Host + Build Client
   - Build Host + Editor Client
4. Use unity-cli to inspect/control the Editor peer.
5. Use a build-side automation server to inspect/control the built player peer.
6. Emit `[MPTEST]` timeline logs.
7. Dump comparable state snapshots from all peers.
8. Capture screenshots and logs on failure.
9. Let Codex fix code and repeat the loop.

This mirrors the user automation plan: execution args start the build, `[MPTEST]` records event timelines, unity-cli observes the Editor, and build-side automation server controls the build peer. Logs and automation server are complementary, not substitutes.

## What to reuse from the generic DOTS harness guide

Reuse:

- Mechanical enforcement over prompt-only instructions.
- Implementer and verifier separation.
- Skills for repeated workflows.
- Knowledge capture for session-to-session learning.
- Stop/verification gates and artifact-backed PASS rules.

Do not reuse:

- DOTS/ECS rules such as `ISystem`, `SystemBase`, Burst-only guidance.
- Claude-specific `.claude/rules` assumptions.
- Any rule that conflicts with this Fusion/MonoBehaviour/NetworkBehaviour project.

## Project-specific high-risk systems

See `project-structure.md` for the full map. Codex should treat these as the first inspection points:

- `Assets/Scripts/Network/NetworkManager.cs`
- `Assets/Scripts/Network/HostMigrationHandler.cs`
- `Assets/Scripts/Network/NetworkPlayer.cs`
- `Assets/Scripts/Game/GameSceneInitializer.cs`
- `Assets/Scripts/Managers/GameManagers*.cs`
- `Assets/Scripts/Managers/PlayerManager.cs`
- `Assets/Scripts/Managers/FieldManager.cs`
- `Assets/Scripts/Managers/ShopManager.cs`
- `Assets/Scripts/Game/Monsters/MonsterSpawner.cs`
- `Assets/Scripts/Game/Battle/AttackSequenceManager.cs`
- `Assets/Scripts/Commands/Core/CommandProcessor.cs`
- `Assets/Scripts/Commands/PlayerActions/*.cs`
- `Assets/Scripts/Commands/Sync/*.cs`
- `Assets/Scripts/Commands/AI/AIPlayerController.cs`

## Identity model

MDF currently uses:

- `PlayerRef`: Fusion connection/player reference.
- `playerId`: durable gameplay player/field id, `[Networked]` on `PlayerManager`.
- connection token: reconnect / cached migration identity path in `NetworkManager` and `HostMigrationHandler`.

Harness assertions must not assume `PlayerRef` remains stable across reconnect or migration. Assertions should compare durable `playerId`, connection token hash, player field state, and authority state.

## Authority model

Expected authority rules:

- State Authority controls `GameManagers.currentState`, `currentRound`, `phaseTimer`, `FirstAttackerPlayerId`, battle pairing, and server broadcasts.
- State Authority owns player HP/gold/walls/shop/augment snapshots and validates commands.
- Clients request via `PlayerManager.RPC_RequestCommandToServer` or feature-specific request RPCs.
- All persistent state changes must be represented in `[Networked]` properties, deterministic rebuildable data, or state snapshot fields.

## Commands that need special care

`CommandProcessor` serializes and deserializes `ICommand` objects into `CommandType` + int/string/vector arrays. When a new action is added, Codex must update all of:

1. Command class.
2. `CommandType` enum.
3. `CommandProcessor.SerializeCommand()`.
4. `CommandProcessor.DeserializeCommand()`.
5. UI/AI call sites.
6. Sync/notification events if UI must react to authority success.
7. Harness state snapshot or assertion if the command changes durable state.

## Harness runtime files to implement

Target C# files, to be created by Codex in implementation phases:

```text
Mdfproject/Assets/Scripts/Testing/MP/
  MPTestCommandLine.cs
  MPTestBootstrap.cs
  MPTestLogger.cs
  MPTestStateSnapshot.cs
  MPTestAssertions.cs
  MPTestCommands.cs
  MPTestAutomationServer.cs
  MPTestMainThreadDispatcher.cs
  MPTestAutomationClient.cs
  Editor/
    MPTestUnityCliTools.cs
    BuildAutomation.cs
```

Do not create production-facing UI or non-test menu flows for these. Keep all build-side automation gated.

## Required command-line args

```text
--mpTest
--mpRole host|client
--mpSession <session>
--mpMaxPlayers 2|3|4
--mpScene Game
--mpAutoStart
--mpLoadGame
--mpExitAfterSeconds <seconds>
--mpAutomationPort <port>
--mpAutomationToken <token>
--mpConnectionToken <token>
--mpCase <caseName>
--mpArtifactDir <path>
--mpSeed <seed>
--mpScenario lobby_smoke|game_smoke|prepare_smoke|battle_smoke|host_migration
```

Secrets and raw connection tokens must not be printed. Store only stable hashes in snapshots/artifacts.

## Required unity-cli custom tools

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

The implementation should use existing entry points where possible: `NetworkManager.JoinLobby()`, `NetworkManager.StartGame(...)`, `GameSceneInitializer`, `GameManagers`, `CommandProcessor`, and project managers.

## E2E matrix MVP

Case A: Editor Host + Build Client

```text
1. Ensure Editor ready with unity-cli.
2. Build Development player.
3. Start Editor as host in test session.
4. Launch build client with same session.
5. Wait for expected player count and Game scene.
6. Dump state from Editor and build.
7. Run deterministic prepare-phase command if available.
8. Compare state snapshots.
9. Collect logs/screenshots/artifacts.
```

Case B: Build Host + Editor Client

```text
1. Build Development player.
2. Launch build host with automation server.
3. Use unity-cli to join Editor client.
4. Wait for Game scene and expected player count.
5. Dump state from both peers.
6. Run deterministic command.
7. Compare state snapshots.
8. Collect artifacts.
```

Case C: Build Host + Build Client

Use this after A/B pass. It proves the built player can host and a second built player can join without Editor assumptions.

## Advanced matrix

- 4-player smoke: 1 host + 3 clients, expected 4 `PlayerManager` objects and 4 fields.
- AI fill smoke: fewer human peers, remaining slots controlled by AI.
- Disconnect -> AI takeover: terminate one client and assert its `playerId`/field stays occupied by AI if supported by code.
- Same token reconnect: reconnect a client with same `--mpConnectionToken` and assert same `playerId` reclaim.
- Host Migration feasibility: kill/drop host and assert Fusion `OnHostMigration`, `HostMigrationToken`, resume, `GameManagers` recovery, and snapshots.
- Soak: repeat deterministic durable gameplay command, not only ping.

## PASS rules

Codex may report PASS only if artifacts prove all required checks.

Minimum per phase:

- `unity-cli --project Mdfproject status`
- `unity-cli --project Mdfproject editor refresh --compile`
- `unity-cli --project Mdfproject console --type error --stacktrace user`
- EditMode/PlayMode tests when available.
- Relevant E2E matrix run for multiplayer changes.
- `[MPTEST]` timeline and state snapshot artifacts collected.

If a command cannot run in the local environment, Codex must report `BLOCKED` or `NEEDS_ENVIRONMENT`, update learned recipes if it discovered a command difference, and must not claim PASS.

## Implementation phases

See `implementation-phases.md` and `codex-phase-prompts.md`.

Suggested order:

1. Baseline / compatibility.
2. Docs and AGENTS/project rule update.
3. Mechanical enforcement.
4. Skills, subagents, knowledge capture.
5. unity-cli custom tools.
6. Runtime MP bootstrap + `[MPTEST]` logs.
7. Build-side automation server.
8. State snapshot assertions.
9. EditMode/PlayMode tests.
10. E2E matrix MVP.
11. AI fill/disconnect/reconnect.
12. Host Migration feasibility and E2E if supported.
13. Soak and regression hardening.

## Why Host Migration is special

The project already contains `NetworkManager.OnHostMigration` and `HostMigrationHandler.StartGameWithMigrationToken`. That means there are code hooks. It does not mean the harness can claim Host Migration PASS. PASS requires an E2E artifact showing host drop, token-based runner restart, `HostMigrationResume`, restored `GameManagers`, restored players/fields/walls/AI, and comparable snapshots.
