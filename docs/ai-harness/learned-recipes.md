# Learned Recipes

This file starts intentionally small. Codex must update it when it verifies MDF-specific commands or workarounds.

## unity-cli-project-selector: Always target Mdfproject

Status: verified-from-repo
Last verified: 2026-05-05
Applies to: MDF repo layout, unity-cli
Triggers: unity-cli, status, console, test, build

Problem:
The repository root is not the Unity project root. The Unity project is under `Mdfproject`.

Recipe:
Use `unity-cli --project Mdfproject ...` or an absolute path to `Mdfproject` for all Editor automation.

Verification:
- `ProjectSettings/ProjectVersion.txt` exists under `Mdfproject`.
- `Packages/manifest.json` exists under `Mdfproject`.
- `unity-cli --project Mdfproject status` found the active Editor at `E:/UnityProjects/mdf/Mdfproject`.

Pitfalls:
- Running unity-cli from the repo root without `--project` may target the wrong Editor if multiple projects are open.

## unity-cli-0.3.15-baseline: Verified local CLI syntax

Status: verified-local
Last verified: 2026-05-05
Applies to: unity-cli v0.3.15, connector 0.3.15, Unity 2021.3.45f1
Triggers: unity-cli, status, list, editor refresh, console, test, screenshot

Problem:
`unity-cli list` reports connector tool schema names such as `run_tests`, `refresh_unity`, and `manage_editor`, while the CLI uses shorthand commands such as `test`, `editor refresh`, and `console`.

Recipe:
- Use `unity-cli --project Mdfproject status` to confirm the active Editor and connector version.
- Use `unity-cli --project Mdfproject list` to inspect registered tools and future `mp_*` custom tools.
- Verified syntax:
  - `unity-cli --project Mdfproject editor refresh --compile`
  - `unity-cli --project Mdfproject console --type error --stacktrace user`
  - `unity-cli --project Mdfproject test --mode EditMode`
  - `unity-cli --project Mdfproject test --mode PlayMode`
  - `unity-cli --project Mdfproject screenshot --view game --output_path artifacts/<case>/editor.png`

Verification:
- `unity-cli --help`, `unity-cli editor --help`, `unity-cli test --help`, `unity-cli console --help`, and `unity-cli screenshot --help` all confirmed this syntax.
- `unity-cli --project Mdfproject list` returned built-ins only; before Phase 4 no `mp_*` tools are expected.

Pitfalls:
- v0.3.15 prints an available update to v0.3.18. Do not update mid-verification unless the task explicitly asks for a CLI upgrade.

## precommit-windows-ascii-output: Keep hook output CP949-safe

Status: verified-local
Last verified: 2026-05-05
Applies to: Windows PowerShell, Python 3.11, tools/harness/precommit.py
Triggers: precommit, hooks, Windows, UnicodeEncodeError

Problem:
PowerShell using the default CP949 console can raise `UnicodeEncodeError` when Python hook output contains emoji characters.

Recipe:
- Keep `tools/harness/precommit.py` output ASCII-only for `BLOCK`, `WARN`, and summary lines.
- If a hook must print non-ASCII text, first verify the active console encoding or force UTF-8 explicitly.

Verification:
- `python tools/harness/precommit.py --self-test` passed after replacing emoji prefixes with ASCII.
- `python tools/harness/precommit.py --all` completed and reported warnings instead of crashing.

Pitfalls:
- Do not treat hook output styling as harmless; failed output encoding can block verification before checks finish.

## precommit-all-changed-vendor: Check modified vendor paths without scanning all vendor files

Status: verified-local
Last verified: 2026-05-05
Applies to: tools/harness/precommit.py --all
Triggers: precommit, vendor, Photon, TMP, Toon Shader

Problem:
`--all` should not scan every existing vendor file, but it still must catch modified protected vendor paths.

Recipe:
- Exclude vendor folders during normal full-tree scanning.
- Add changed vendor files from `git status --porcelain` back into the check set so edits are blocked.

Verification:
- `python tools/harness/precommit.py --self-test` includes a vendor edit path case.
- `python tools/harness/precommit.py --all` completed with `0 errors`.

Pitfalls:
- Fully scanning vendor folders can create noisy warnings from third-party sample code.

## unity-2021-test-framework-fallback

Status: verified-local
Last verified: 2026-05-05
Applies to: Unity 2021.3.45f1, Test Framework 1.1.33
Triggers: test, EditMode, PlayMode, command line

Problem:
unity-cli test should be preferred, but older Unity/Test Framework setups may need Unity command-line fallback.

Recipe:
1. Try `unity-cli --project Mdfproject test --mode EditMode`.
2. Try `unity-cli --project Mdfproject test --mode PlayMode`.
3. If unavailable, use Unity `-runTests -batchmode -projectPath Mdfproject -testResults <xml> -testPlatform EditMode|PlayMode`.

Verification:
- `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests` passed 8/8 tests.
- `unity-cli --project Mdfproject test --mode PlayMode` executed successfully but found 0 tests in the current non-asmdef project layout.

Pitfalls:
- Do not assume Unity 6 test flags.
- Runtime scripts live in predefined `Assembly-CSharp`; PlayMode tests that reference them are not practical without introducing an asmdef boundary. Prefer EditMode tests for harness internals and E2E scripts for runtime multiplayer behavior unless the project adopts asmdefs.
- Treat `PlayMode total=0` as a documented limitation, not as runtime PASS by itself. Runtime multiplayer behavior is proven by Editor/build E2E artifacts until the project adopts test asmdefs.

## unity-cli-custom-tool-args: Invoke mp_* tools with CLI flags

Status: verified-local
Last verified: 2026-05-05
Applies to: unity-cli connector 0.3.15 custom tools
Triggers: mp_dump_state, mp_assert_state, mp_build_player, custom tool smoke

Problem:
Custom `[UnityCliTool]` commands need a reliable invocation form for harness scripts and manual smoke checks.

Recipe:
Invoke registered custom tools as top-level unity-cli subcommands. Pass parameters as snake_case flags that match the tool `JObject` keys.

Example:

```powershell
unity-cli --project Mdfproject mp_dump_state --role editor --case_name phase6_snapshot_smoke
```

Verification:
- `unity-cli --project Mdfproject mp_dump_state --role editor --case_name phase6_snapshot_smoke` returned a JSON snapshot from the open Editor in the `Title` scene.

Pitfalls:
- A snapshot from `Title` is only a tool smoke. It is not a gameplay PASS because `GameManagers` and players are absent.

## mp-automation-server-gates: Keep build control loopback-only

Status: verified-local
Last verified: 2026-05-05
Applies to: `MPTestAutomationServer`, Development Build harness control
Triggers: automation server, HttpListener, --mpTest, --mpAutomationToken

Problem:
The build-side automation server is useful for E2E control but must not become a production surface.

Recipe:
- Compile the server and dispatcher only under `UNITY_EDITOR || DEVELOPMENT_BUILD`.
- Start the server only when `--mpTest`, `--mpAutomationPort`, and `--mpAutomationToken` are present.
- Bind `HttpListener` to `IPAddress.Loopback`, not wildcard prefixes.
- Require `Authorization: Bearer`, `X-MPTest-Token`, or fallback `?token=` on every endpoint.
- Dispatch Unity API work through `MPTestMainThreadDispatcher`.

Verification:
- `python tools\harness\precommit.py --all` returned `0 errors`.
- `python tools\harness\precommit.py --self-test` returned all PASS.
- `unity-cli --project Mdfproject editor refresh --compile` and console error check passed after adding the missing `GameCore.Enums` import.

Pitfalls:
- Do not log token values. Log only `AutomationTokenHash`/`ConnectionTokenHash`.

## mp-production-negative-automation: Prove normal builds do not expose /ping

Status: verified-local
Last verified: 2026-05-05
Applies to: `tools/harness/mp/run_production_negative_automation.py`, `mp_build_player --development_build false`
Triggers: production automation safety, Phase 7 hardening, normal build proof

Problem:
Static gates prove intent, but the harness should also be able to produce an artifact showing a normal non-development player does not expose the automation server even when launched with `--mpTest`, `--mpAutomationPort`, and `--mpAutomationToken`.

Recipe:
- Build a normal player with `unity-cli --project Mdfproject mp_build_player --development_build false --allow_debugging false`.
- Launch that player with `--mpTest`, an automation port, a per-run token, and `--mpExitAfterSeconds`.
- Poll `http://127.0.0.1:<port>/ping` with the token.
- PASS only when `build-metadata.json` reports `developmentBuild=false`, `/ping` never responds, and the player exits without timeout.
- Store `production-negative-automation.json`, redacted launch command, stdout/stderr, copied `Player.log` when available, and build wrapper metadata under `artifacts/mp/<timestamp>-production-negative-automation/`.

Verification:
- `artifacts/mp/20260505-110846-production-negative-automation/production-negative-automation.json` reported `success=true`, `productionAutomationDisabled=true`, `developmentBuild=false`, `/ping` did not respond, and the player exited with code `0`.
- The paired build metadata under `artifacts/builds/20260505-110846-production-negative/build-metadata.json` reported `options=None`, `developmentBuild=false`, and `allowDebugging=false`.

Pitfalls:
- Do not reuse Development player builds for this proof.
- Do not treat a missing token failure from a Development build as production-negative proof; the build metadata must show `developmentBuild=false`.
- E2E helpers that auto-select the latest build must skip `developmentBuild=false` metadata, otherwise a production-negative proof build can accidentally become the next multiplayer test player.

## mp-build-player-artifacts: Development build and launch smoke

Status: verified-local
Last verified: 2026-05-05
Applies to: `mp_build_player`, `tools/harness/mp/build_player.py`
Triggers: Development Build, build artifact, player launch smoke

Problem:
E2E needs a deterministic Development player plus evidence that `--mpTest` starts, logs, and exits cleanly.

Recipe:
- Use `python tools\harness\mp\build_player.py --launch-smoke --exit-after-seconds 5`.
- Build output is under `artifacts/builds/<timestamp>/`.
- `build-metadata.json` records Unity version, build target, scenes, output path, Development/AllowDebugging flags, and Player.log source path.
- Launch smoke artifacts include stdout/stderr, copied `Player.log`, redacted command JSON, and `launch-smoke.json`.

Verification:
- Build output `artifacts/builds/20260504-203544/MDF-MPTest.exe` exists.
- `build-metadata.json` reported `result=Succeeded`, target `StandaloneWindows64`, Development Build enabled, and scenes `Title`, `MatchingLobby`, `JoinLobby`, `Game`.
- Launch smoke exited `0` and copied `Player.log` with `[MPTEST] bootstrap`, `automation_server`, and `exit_after_seconds` lines.

Pitfalls:
- On Windows, Python subprocess text capture can hit CP949 decode failures on unity-cli output. Use `encoding="utf-8", errors="replace"` in harness scripts that capture command output.

## mp-e2e-mvp-start-order: Reproduce real room flow before Game load

Status: verified-local
Last verified: 2026-05-05
Applies to: Phase 10/11 E2E matrix, `compare_state_snapshots.py`, `FieldManager`
Triggers: Editor/Build E2E, build/build E2E, Game scene, FieldManager wall snapshots

Problem:
Older E2E scripts launched the host with `--mpAutoStart --mpLoadGame --mpScene Game` before the client was in the room. This let `GameManagers.SetupPlayersAndGrids()` fill missing slots as AI/late-join state, which produced client-side permanent wall gaps and `wallHash` mismatches. A second edge case allowed client wall-map rebuilds to include a stray permanent wall candidate unless filtered by the server's authoritative permanent-wall cell list.

Evidence:
- `artifacts/mp/20260504-204857-editor-host-build-client/comparison.json` failed on `player.0.field.wallHash`.
- `artifacts/mp/20260504-204948-build-host-editor-client/comparison.json` failed on `player.0.field.wallHash`.
- `artifacts/mp/20260504-205150-build-host-build-client/comparison.json` failed on `player.0.field.wallHash`, so the blocker is not Editor-only.
- `artifacts/mp/20260504-212154-build-host-build-client/comparison.json` on the latest build still failed on `player.0.field.wallHash` and also exposed a transient `game.currentState` split (`Prepare` vs `Setup`) at snapshot time.

Recipe:
- Launch all peers with `--mpTest` and automation enabled, but without `--mpAutoStart` and without `--mpLoadGame`.
- Start host/client into `MatchingLobby` first, wait until every peer reports `runner.activePlayerCount == expectedPlayers`, then have the host call `/loadGame` or `mp_load_game` for `Game`.
- Wait for field readiness and stable full snapshot agreement before PASS.
- Keep wall hashes in comparisons; do not suppress them to make MVP pass.
- In `FieldManager`, client peers must wait for server permanent-wall sync. Rebuilds should filter permanent wall candidates through the authoritative cell list received from `RPC_ApplyPermanentWalls`.

Verification:
- `artifacts/mp/20260504-221902-build-host-build-client/comparison.json` passed with `errors=[]`.
- `artifacts/mp/20260504-222012-editor-host-build-client/comparison.json` passed with `errors=[]`.
- `artifacts/mp/20260504-222048-build-host-editor-client/comparison.json` passed with `errors=[]`.

Pitfalls:
- `unity-cli editor play --wait` can return while the connector reports `reloading` or `playing`; harness scripts should poll `unity-cli status` and accept `playing` as controllable.

## mp-ai-fill-smoke: Max-player AI slots are supported

Status: verified-local
Last verified: 2026-05-05
Applies to: `run_ai_fill_smoke.py`, `GameManagers.DeterminePlayerCount`
Triggers: AI fill, fewer humans than max players, 4 slots

Problem:
AI fill should be proven by snapshot fields, not by logs alone.

Recipe:
- Launch one build host with `--mpMaxPlayers 4 --mpAutoStart --mpLoadGame`.
- Wait for Game scene snapshot with 4 `PlayerManager` entries.
- Assert AI controller registered for 3 non-human slots and fields are ready.

Verification:
- `artifacts/mp/20260504-205404-ai-fill-smoke/ai-fill-assertions.json` reported `playerCount=4`, `aiCount=3`, playerIds `[0,1,2,3]`, and `allFieldsReady=true`.

Verification update:
- `artifacts/mp/20260504-230444-ai-fill-smoke/ai-fill-assertions.json` reported `playerCount=4`, `aiCount=3`, playerIds `[0,1,2,3]`, and `allFieldsReady=true` on build `artifacts/builds/20260504-230414/MDF-MPTest.exe`.

## mp-disconnect-ai-takeover: Kill the client process and assert the durable slot

Status: verified-local
Last verified: 2026-05-05
Applies to: `NetworkManager.OnPlayerLeft`, `AIPlayerController`, `tools/harness/mp/run_disconnect_ai_takeover.py`
Triggers: normal client disconnect, AI takeover, same `playerId` field preservation

Problem:
Normal disconnect takeover is not proven by logs alone. The host snapshot must show the disconnected player's durable `playerId` still exists, input authority cleared, AI controller registered, field ready, and no duplicate `playerId`.

Recipe:
- Start host and client in `MatchingLobby` first, then have the host load `Game`.
- Record the client-local `playerId` and connection token hash from the pre-disconnect client snapshot.
- Terminate the client process with `kill`, not `/quit`.
- Poll the host snapshot until `runner.activePlayerCount == 1`, player count remains unchanged, target `isAI == true`, `isConnected == false`, `ai.controllerRegistered == true`, and field readiness is true.

Verification:
- `artifacts/mp/20260504-230459-disconnect-ai-takeover/disconnect-ai-takeover-assertions.json` showed target `playerId=1`, `activePlayerCount=1`, `playerCount=2`, `targetIsAI=true`, `targetConnected=false`, `targetFieldReady=true`, and `targetAiRegistered=true`.
- `mptest.timeline.jsonl` in the same artifact includes `[MPTEST] disconnect_cache result=pass` and `[MPTEST] disconnect_ai_takeover result=pass`.

Pitfalls:
- `NetworkManager._spawnedCharacters` may point to the lobby/network player object, not the Game `PlayerManager`. Disconnect takeover must find the runtime `PlayerManager` by `InputAuthority`, cache by connection token, clear input authority, and attach `AIPlayerController` without despawning the durable slot.

## mp-same-token-reconnect: Verify target identity, keep full-world drift visible

Status: verified-local
Last verified: 2026-05-05
Applies to: `NetworkManager.TryReassociateDisconnectedPlayer`, `tools/harness/mp/run_same_token_reconnect.py`
Triggers: same `--mpConnectionToken`, reconnect identity, PlayerRef changes

Problem:
Same-token reconnect must prove durable identity, not raw `PlayerRef` stability. In a full 2-slot Photon session the immediate replacement client can be rejected before game-code reassociation, so the harness uses `--mpMaxPlayers 3` to leave a transport slot while asserting the same target `playerId`.

Recipe:
- Start host and client A in `MatchingLobby`, then host loads `Game`.
- Record client A's local `playerId` and connection token hash.
- Kill client A, wait for disconnect AI takeover of that same `playerId`.
- Launch client B with the same `--mpConnectionToken` and join the existing `Game` session.
- PASS criteria are target-specific: host target exists, host target `isAI == false`, host target `isConnected == true`, client B local `playerId` equals the recorded target, client token hash matches, playerIds are unique, and target host/client comparison succeeds.

Verification:
- `artifacts/mp/20260504-231146-same-token-reconnect/same-token-reconnect-assertions.json` showed target `playerId=1`, `clientLocalPlayerId=1`, matching token hash `6163355D`, `hostTargetIsAI=false`, `hostTargetConnected=true`, and `targetComparison.success=true`.
- `mptest.timeline.jsonl` includes `[MPTEST] same_token_reconnect result=pass joinedPlayerRef=[Player:3] playerId=1`.
- `artifacts/mp/20260505-113304-same-token-reconnect/full-comparison.json` reported `success=true` with no errors after strict full-world reconnect assertions were enabled.

Pitfalls:
- Older `full-comparison.json` artifacts exposed non-target late-join wall drift. Do not use Phase 13 target identity PASS as proof that arbitrary late join fully reconstructs the whole match world.
- Phase 13B requires `fullComparison.success=true`. Use `--target-only` only when intentionally rechecking identity reclaim separately from full-world sync.
- When applying authoritative permanent-wall cells to late-join clients, destructible wall-map rebuilds must not classify objects in authoritative permanent cells as destructible walls.

## mp-four-player-real-room-flow: 4-player smoke must join before Game load

Status: verified-local
Last verified: 2026-05-05
Applies to: `tools/harness/mp/run_four_player_smoke.py`
Triggers: 4-player smoke, build host + 3 build clients, field wall snapshots

Problem:
The 4-player smoke must not let the host enter `Game` before the three clients join. Otherwise missing human slots become AI slots and wall hashes diverge.

Evidence:
- `artifacts/mp/20260504-205755-four-player-smoke/failure-summary.md` recorded `four_player_state_timeout` and snapshot mismatches for all three clients.
- `comparison-host-vs-client-1.json`, `comparison-host-vs-client-2.json`, and `comparison-host-vs-client-3.json` all failed on `player.0.field.wallHash`.

Recipe:
- Launch all four peers without auto-start/load-game.
- Start the host and three clients into `MatchingLobby`, wait for `runner.activePlayerCount == 4` on every peer, then have the host load `Game`.
- Compare host snapshots against each client and keep `wallHash` strict.

Verification:
- `artifacts/mp/20260504-222127-four-player-smoke/comparison-host-vs-client-1.json` passed.
- `artifacts/mp/20260504-222127-four-player-smoke/comparison-host-vs-client-2.json` passed.
- `artifacts/mp/20260504-222127-four-player-smoke/comparison-host-vs-client-3.json` passed.

## mp-host-migration-probe: Kill the host process, then prove Fusion callback/token/resume

Status: verified-local
Last verified: 2026-05-05
Applies to: `tools/harness/mp/run_host_migration_probe.py`, Host Migration Phase 15
Triggers: Host Migration feasibility, process kill, `HostMigrationToken`

Problem:
Host Migration cannot be proven with `/quit` or code inspection. The surviving peer must show Fusion callback, non-null token, new runner start, `HostMigrationResume`, and recovery completion.

Recipe:
- Build a Development player after any Host Migration instrumentation changes.
- Launch build host and build client with `--mpTest`.
- Wait for both peers to reach `Game`.
- Kill only the host process with `process.kill`.
- Poll the surviving client's `/dumpState` until `hostMigration.completeCount > 0` or timeout.
- Require `onHostMigrationCount > 0`, `nonNullTokenCount > 0`, `startGameSuccessCount > 0`, `resumeCount > 0`, `recoverySucceeded=true`, and `failureCount=0`.

Verification:
- `artifacts/mp/20260504-210930-host-migration-feasibility/host-migration-result.json` reported `hostWasKilled=true`, `EnableAutoUpdate=true`, `onHostMigrationCount=1`, `nonNullTokenCount=4`, `resumeCount=2`, `startGameSuccessCount=1`, `completeCount=1`, `recoverySucceeded=true`, and `failures=[]`.

Pitfalls:
- The callback proof is only feasibility. It does not prove durable player control or gameplay state preservation.

## mp-host-migration-e2e-blocker: Callback succeeds but durable E2E still fails

Status: observed-blocker
Last verified: 2026-05-05
Applies to: `tools/harness/mp/run_host_migration_e2e.py`, Host Migration Phase 16
Triggers: Host Migration E2E, durable state comparison

Problem:
The surviving client can become the new Host through Fusion Host Migration, but strict durable state checks currently fail.

Evidence:
- `artifacts/mp/20260504-211233-host-migration-e2e/host-migration-e2e-result.json` showed callback/token/resume/recovery success and `failureCount=0`.
- The same artifact failed E2E on `no_connected_human_survivor_after_migration`, `player_0_gold_changed`, `player_0_field_wallHash_changed`, `player_1_gold_changed`, `player_1_wallCount_changed`, and `player_1_field_wallHash_changed`.
- `artifacts/mp/20260505-113106-host-migration-e2e/host-migration-e2e-result.json` used the real `MatchingLobby -> Game` flow and preserved the active scene after migration, but still failed durable state: `GameManagers` restored as `Setup/R0`, both player gold values reset to `20`, shop snapshots were empty, and player 1 permanent wall hash changed.
- Waiting an extra 3 seconds before killing the host (`--migration-settle-seconds 3`) produced `artifacts/mp/20260505-112939-host-migration-e2e`, which was worse: the token resumed only one player and lost the connected human survivor. Keep settle as an opt-in diagnostic, not the default.

Recipe:
- Treat Phase 15 as feasibility only.
- Treat Phase 16 as blocked until durable human identity/control and pre/post gameplay state are preserved or explicitly paused with documented semantics.
- Keep the host kill path; do not replace it with graceful `/quit`.
- Host Migration E2E must follow the real room flow: start both peers in `MatchingLobby`, wait for both to join, then load `Game`.
- `NetworkManager` on clients must remember the last Fusion scene from `OnSceneLoadDone`; otherwise migration restart can resume into `MatchingLobby`.
- The next repair needs a real MDF durable migration snapshot or equivalent production recovery path for `GameManagers`, all `PlayerManager` state, shop snapshots, and permanent wall cells. Do not mark Phase 16 PASS from callback/token/resume alone.

## mp-reroll-command-soak: Use reroll_shop for focused durable command proof

Status: verified-local
Last verified: 2026-05-05
Applies to: `/command`, `tools/harness/mp/run_durable_command_soak.py`
Triggers: durable command soak, `CommandProcessor`, shop sync

Problem:
The command harness should use commands that exist in real gameplay/AI behavior where possible. `RerollShopAction` uses `RerollShopCommand`, so `reroll_shop` is the current safe AI-behavior command probe.

Recipe:
- Support only `reroll_shop` through `/command` for now.
- Issue it to the server/host peer, validate `playerId`, runner/server state, `CommandProcessor`, shop DB readiness, and reroll cost.
- Verify only command-relevant durable fields: target player gold decreases by cost, shop revision advances, and host/client agree on gold, shop revision, shop count, and shop items hash.
- Launch peers with the same real room flow as E2E: `MatchingLobby` session first, then host loads `Game`.
- Treat the 10-iteration run as the default full reroll soak. The report must include `iterationsRequested`, `iterationsCompleted`, `success`, and per-iteration mutation/agreement evidence.
- Keep full snapshot comparisons separate; this proves the AI-style command path, not every gameplay action.

Verification:
- `artifacts/mp/20260504-211819-durable-command-soak/durable-command-report.json` passed 3 iterations for `playerId=0`.
- Gold moved `27 -> 25 -> 23 -> 21`; host/client shop revisions matched `2`, `3`, `4`; host/client shop item hashes matched every iteration.
- `artifacts/mp/20260504-221931-durable-command-soak/durable-command-report.json` passed 2 iterations on the real room flow with the new build. The pre-command snapshots showed both peers agreed on 36 permanent walls per player and matching `wallHash`.
- `artifacts/mp/20260505-111707-durable-command-soak/durable-command-report.json` passed the full 10-iteration run for `playerId=0`.
- Gold moved `27 -> 7`; host/client shop revisions matched `2..11`; every iteration recorded `goldMutated=true`, `revisionMonotonic=true`, and `peersAgree=true`.
- `artifacts/mp/20260505-113356-durable-command-soak/durable-command-report.json` repeated the full 10-iteration run successfully on the latest build after Host Migration scene-tracking changes.

Pitfalls:
- This is a focused durable command proof, not a replacement for broader AI behavior coverage such as buy, place wall, move unit, augment selection, or combat spawn orders.

## mp-editor-reroll-command-parity: Editor mp_command must run only on a host Play Mode peer

Status: verified-compile
Last verified: 2026-05-05
Applies to: `unity-cli --project Mdfproject mp_command --command reroll_shop --player_id <id>`
Triggers: Phase 4 hardening, Editor-side command parity

Problem:
Editor-side `mp_command` should match the build-side `/command` validation path without allowing accidental command execution from Edit Mode or a client peer.

Recipe:
- Register `mp_command` parameters as `command` and `player_id`.
- Support only `reroll_shop` until broader gameplay commands are explicitly added.
- Before queueing `RerollShopCommand`, require Editor Play Mode, `GameManagers` runner running, server/host peer, `GameManagers` State Authority, `CommandProcessor`, valid `playerId`, `ShopManager`, shop database readiness, and enough gold for the reroll cost.
- Return the same practical JSON shape as build-side `/command`: `success`, `message`, `timestampUtc`, and either `data` or `error.code/error.details`.
- Do not run `mp_command` as verification unless the Editor is already in a valid host Play Mode state.

Verification:
- `unity-cli --project Mdfproject editor refresh --compile` completed compilation.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject list` showed `mp_command` with `command` and `player_id` parameters.
- Editor state probe reported `isPlaying=False`, `hasGameManagers=False`, so live command execution was skipped as `NEEDS_ENVIRONMENT`.

## harness-gitignore-artifacts: Commit harness sources, ignore generated proof output

Status: verified-local
Last verified: 2026-05-05
Applies to: `.gitignore`, `artifacts/`, `tools/harness/**/__pycache__`
Triggers: large git status after E2E/build harness runs

Problem:
Harness E2E and build-player runs can create thousands of local proof files and Unity player binaries. These are evidence for the current machine/run, not portable source needed by another clone.

Recipe:
- Commit harness sources and configuration: `tools/harness/**/*.py`, `Mdfproject/Assets/Scripts/Testing/MP/**`, `.agents/skills/**`, `.codex/agents/**`, `.codex/hooks/**`, `docs/ai-harness/**`, and required `.meta` files.
- Ignore generated output: `/artifacts/`, `__pycache__/`, and `*.py[cod]`.
- Preserve reusable evidence by summarizing artifact paths and pass/fail facts in `docs/ai-harness/learned-recipes.md`, not by committing the raw artifact directory.

Verification:
- Before ignore update, `git status --porcelain=v1 -uall` reported 2817 entries, including 2688 under `artifacts/` and 22 Python cache files.
- After adding ignore rules, `git status --porcelain=v1 -uall` reported 108 entries, leaving only commit candidates and tracked modifications.

## host-migration-durable-pass: Snapshot MDF state beyond Fusion token resume

Status: verified-local
Last verified: 2026-05-05
Applies to: `HostMigrationHandler`, `PlayerManager`, `FieldManager`, `run_host_migration_e2e.py`
Triggers: Phase 16, Host Migration durable E2E, wallHash/gold/shop/player identity drift

Problem:
Fusion Host Migration callback/token/resume can succeed while MDF gameplay state still drifts. In failing artifacts, `GameManagers.Instance` was null at migration start, so cached game state fell back to Setup/R0 and post-migration gold/shop/wall state changed. A later failure showed the survivor object could resume with the wrong durable `playerId` and no `InputAuthority`.

Recipe:
- Capture a durable MDF snapshot at `StartMigration` from the active runner, not only `GameManagers.Instance`.
- Include `currentRound/currentState`, all `PlayerManager` HP/gold/wallCount/shop snapshot, permanent wall flat cells, wall hash, local input authority, and AI/connected observations.
- Apply this snapshot on the new host before `RestoreAfterHostMigration`, matching players by local input authority first, wall hash second, then current `playerId`.
- Reassign the survivor's transient `InputAuthority` to the new runner local player, but keep durable identity as MDF `playerId`.
- Do not reset player HP/gold/wallCount in `PlayerManager.Spawned()` during Host Migration.
- Restore permanent wall cells from the snapshot and rebuild wall maps before post-migration assertions.

Verification:
- `artifacts/mp/20260505-115931-host-migration-e2e/host-migration-e2e-result.json` passed with `failures=[]` after identity/control and durable state restore.
- `artifacts/mp/20260505-123330-host-migration-e2e/host-migration-e2e-result.json` passed on the latest build after wall-sync hardening.
- `durable-state-report.json` for the passing run reported no per-player mismatches.

## mp-client-permanent-wall-sync-race: Queue and fallback permanent wall RPCs

Status: verified-local
Last verified: 2026-05-05
Applies to: `PlayerManager.RPC_ApplyPermanentWalls`, `FieldManager.ApplyPermanentWallsFromServer`, `run_matrix.py`
Triggers: matrix-only `build-host-editor-client` wallHash mismatch, `state_ready_timeout`, client permanentWallCount 0/1

Problem:
`build-host-editor-client` could pass alone but fail when run after `editor-host-build-client` in the matrix. The build host broadcast permanent wall coordinates while the Editor client was still initializing, and `RPC_ApplyPermanentWalls` dropped the message when `fieldManager` was null. In another timing path, the client had authoritative wall cells but never matched enough network wall objects, leaving `field.ready=false` and `wallHash=unknown`. A later standalone failure showed a stricter variant where the Editor client missed the one-time permanent wall coordinate RPC entirely and stayed at `permanentWallCount` 0/1 until timeout.

Recipe:
- Queue `RPC_ApplyPermanentWalls` payloads when `fieldManager` is not ready and drain them after `Rpc_InitializePlayer` rebinds runtime references.
- On the State Authority, rebuild a deterministic permanent wall coordinate payload from authoritative cells and rebroadcast it for a short window after `Rpc_InitializePlayer`; one-shot RPC state is not enough for late or slow peers.
- For clients only, if network permanent wall objects do not arrive by the sync timeout, create local non-authoritative placeholder walls from the authoritative cell list and rebuild wall maps.
- Keep server generation authoritative; the fallback is only a client recovery path for missed/delayed network wall visuals/maps.
- In `run_matrix.py`, clean the Editor between cases with `mp_stop`, `editor stop`, `unity-cli status`, and a short inter-case delay.

Verification:
- Before the fix, `artifacts/mp/20260505-120203-matrix` and `artifacts/mp/20260505-122021-matrix` failed only `build-host-editor-client` with `state_ready_timeout` and `player.0.field.wallHash` mismatch.
- `artifacts/mp/20260505-122826-matrix/matrix-summary.json` passed all matrix cases: Editor Host + Build Client, Build Host + Editor Client, Build Host + Build Client, AI fill, disconnect AI takeover, same-token reconnect, and 4-player smoke.
- `artifacts/mp/20260505-125052-build-host-editor-client` reproduced the standalone miss with `state_ready_timeout` and `snapshot_mismatch`.
- `artifacts/mp/20260505-125705-build-host-editor-client` passed after adding the authority rebroadcast payload and rebuilding the Development player at `artifacts/builds/20260505-125616/MDF-MPTest.exe`.

## authority-hardening-command-rpc-gate: Validate client commands on the owned PlayerManager

Status: verified-local
Last verified: 2026-05-05
Applies to: `PlayerManager.RPC_RequestCommandToServer`, `GameManagers.RPC_RequestSpawnMonster`, `NetworkManager.CacheDisconnectedPlayerData`
Triggers: precommit `client_trust`, `rpc_all`, `playerref_durable`, Authority Hardening

Problem:
Client command requests previously reached server broadcast with only the owned `PlayerManager` RPC source restriction. The legacy `NetworkManager.RPC_RequestCommandToServer` still contained a trust-all validation stub, monster spawn requests accepted client-supplied battle/spawn data without matching the RPC source to the attacker, and disconnect cache could fall back to transient `PlayerRef`.

Recipe:
- Route gameplay client requests through the `PlayerManager` object owned by the caller and require `RpcInfo.Source == Object.InputAuthority`.
- Normalize RPC arrays before use, require the authoritative `playerId`, and reject sync/notify/server-only commands from clients.
- Validate command-specific authority facts before queueing: Prepare/Battle phase, shop database readiness, gold/cost, shop slot state, augment choice, unit/wall ownership, grid bounds, wall stock, and skill unit ownership.
- Treat client `PlaceUnit` as not authority-safe until an authoritative inventory/bench ownership model exists.
- For battle monster spawn RPCs, require source ownership of the attacker, active attacker status, battle defender mapping, field bounds, matching monster pool entry, and boss origin consistency.
- Never use `PlayerRef` as a durable disconnect/reconnect cache key; skip caching when the durable connection token is missing.
- Keep gameplay readiness strict. If the harness needs to recognize a peer as snapshot-ready after late or duplicate initialization, compute that in `MPTestStateSnapshot`; do not broaden `PlayerManager.IsReadyForPlayerActions`, because Host Migration flow uses it as a resume gate.

Verification:
- `python tools/harness/precommit.py --all` reports `0 errors, 11 warnings` after hardening, down from the previous `0 errors, 17 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed and `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed 8/8.
- Rebuilt Development player at `artifacts/builds/20260505-141419/MDF-MPTest.exe`.
- `artifacts/mp/20260505-142027-durable-command-soak/durable-command-report.json` passed 10/10 `reroll_shop` iterations.
- `artifacts/mp/20260505-141541-matrix/matrix-summary.json` passed all default matrix cases.
- `artifacts/mp/20260505-142056-host-migration-feasibility` and `artifacts/mp/20260505-141502-host-migration-e2e` passed after the token-cache hardening and snapshot readiness separation.

Pitfalls:
- Remaining precommit WARNs are heuristics or broader debt, not BLOCK errors. Keep `CommandProcessor` and command-level validation warnings until broader server-authoritative command coverage is added.
