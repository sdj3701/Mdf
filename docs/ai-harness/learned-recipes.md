# Learned Recipes

This file starts intentionally small. Codex must update it when it verifies MDF-specific commands or workarounds.

## mp-random-aware-human-bot-doctrine: Compare replicated outcomes, not fixed random values

Status: verified-from-code
Last verified: 2026-05-06
Applies to: Phase 18+ HumanBot progression, randomized shop/wall/augment/battle state, snapshot comparison
Triggers: HumanBot, random-aware, shop hash, augment hash, wallHash, Host Migration, reconnect

Problem:
MDF uses gameplay-critical randomness in shop rerolls, augment presentation, permanent wall selection, AI maze planning, and battle pairing. A command-only replay tape can issue the same commands against a different randomized state and therefore is not a reliable progression engine.

Recipe:
- Use a real connected human peer with test-only HumanBot policy to request commands through `CommandProcessor.RequestCommandExecution`.
- Keep the player human: do not attach `AIPlayerController` and do not register it in `ComponentRegistry`.
- Let State Authority decide persistent random outcomes.
- Compare the same player's replicated `shop.itemsHash`, `augment.presentedHash`, `field.wallHash`, `field.placedUnitsHash`, battle mapping hash, HP, gold, and command/revision fields across peers.
- Treat bot decisions, accepted commands, random outcomes, and checkpoint snapshots as diagnostic journals.

Verification:
- Code mapping confirmed `AIPlayerController` registers in `ComponentRegistry` and `MPTestStateSnapshot` reports `isAI` from `ComponentRegistry.Has<AIPlayerController>(playerId)`.
- Code mapping confirmed client command requests route through `CommandProcessor.RequestCommandExecution` to `PlayerManager.RPC_RequestCommandToServer`, where phase/cost/grid/shop/augment/source validation is enforced.
- Existing compare scripts already compare same-player shop and field hashes; Phase 18 docs extend that doctrine to HumanBot and augment/random outcome journals.
- Phase 19 core verification confirmed `MPTestHumanBotDriver` compiles, stays test-only, does not register `AIPlayerController`, and emits bot status/journal data under the snapshot `test` section.

Pitfalls:
- Do not assert fixed shop items, fixed wall coordinates, fixed augment names, or fixed battle pairings across different runs.
- Do not hide post-checkpoint mismatches as expected randomness.
- `--mpBotSeed` is a diagnostic handle until each gameplay RNG source is explicitly controlled.
- `test.bot.commandsIssued` counts HumanBot request submissions, not confirmed server acceptance. E2E tests must prove accepted commands through durable snapshot deltas, server accepted-command logs, or command/revision evidence.
- A HumanBot running on the host uses the State Authority broadcast path. To prove real client request validation, run `--mpHumanBot` on a connected client peer.

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

## unity-cli-editor-availability-v1: Wait for an open Editor connector

Status: verified-local
Last verified: 2026-05-07
Applies to: unity-cli connector, Windows PowerShell, Unity 2021.3.45f1
Triggers: no Unity instances running, not responding, manual Editor launch, status polling

Problem:
`unity-cli` only talks to an already open Unity Editor with the connector loaded. If no Editor is open it fails with `Error: no Unity instances running`; immediately after launch it may report `not responding` while Unity imports, compiles, or shows `Hold on...`.

Recipe:
- Open `Mdfproject` in Unity 2021.3.45f1, then poll `unity-cli --project Mdfproject status` until it reports `ready`.
- Treat transient `not responding` with a recent heartbeat as startup/import work, not a verification failure.
- Once `ready`, run the normal sequence: `editor refresh --compile`, `console --type error --stacktrace user`, then `test --mode EditMode`.

Verification:
- On 2026-05-07, `unity-cli --project Mdfproject status` first failed with `no Unity instances running`, then reported `not responding`, then became `ready` on port 8090 after the Editor was manually opened.
- `unity-cli --project Mdfproject editor refresh --compile` completed, console errors returned `[]`, and EditMode passed `32/32`.

Pitfalls:
- v0.3.15 can print update notices to stderr even when the status command exits successfully; use the exit code and ready line, not the update notice, as the connector readiness signal.

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

## precommit-zero-warns-with-validated-rpc-rules: Reduce WARNs without hiding authority checks

Status: verified-local
Last verified: 2026-05-06
Applies to: `tools/harness/precommit.py`, command/RPC/tick-log WARN reduction
Triggers: `client_trust`, `rpc_all`, `rpc_persistent_state`, `tick_debug_log`, warning cleanup

Problem:
File-wide regular expressions can keep reporting WARNs after authority hardening is already in place. The common false positives were commented-out logs, wire-format deserialization in `CommandProcessor`, guard log text such as "client peer", validated `RpcSources.All` request RPCs with `RpcInfo`, and migration diagnostics inside tick methods.

Recipe:
- Strip comments before warning scans so commented diagnostic logs do not count as active tick/debug or client-trust risks.
- Inspect RPC methods by body instead of file-wide text. Keep warning on `RpcSources.All` unless the method signature includes `RpcInfo` and the body validates `info.Source`, `Object.InputAuthority`, `IsRpcSourceAuthorizedForPlayer`, or a dedicated request validator.
- Skip persistent-state RPC warnings for `RpcSources.StateAuthority` broadcasts and manually validated request RPCs; those are the expected authority-to-peer sync path.
- Inspect only the actual `Update`, `FixedUpdateNetwork`, and `Render` method bodies for direct `Debug.Log` calls. Move intentional diagnostics into helper methods outside tick bodies when the log is bounded and deliberate.
- Avoid client-trust false positives in guard strings by saying `non-authority peer` instead of `client peer`, and avoid `requested` in neutral trace labels such as shop initialization.
- Rename private wire-format arrays in `CommandProcessor.DeserializeCommand` away from `intParams`/`stringParams` when the arrays are already server-validated before broadcast.

Verification:
- `python tools/harness/precommit.py --self-test` passed, including explicit cases that an unvalidated `RpcSources.All` still warns while a source-validated one does not.
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings` after the cleanup.
- `unity-cli --project Mdfproject editor refresh --compile` completed, `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`, and EditMode passed `8/8`.
- Rebuilt Development player at `artifacts/builds/20260505-230726/MDF-MPTest.exe`; launch smoke exited `0`.
- `artifacts/mp/20260505-230828-human-bot-prepare` passed with `SelectAugment`, `accepted_command`, durable selected augment hash delta, and same-player random outcome matches.

Pitfalls:
- Do not replace WARNs with blanket file allowlists. If a warning is suppressed by smarter logic, add or keep a self-test proving the unsafe shape still warns.
- Do not remove Host Migration, command, or VFX diagnostics just to silence the hook; move bounded diagnostics behind helpers only when they remain intentional and verifiable.

## content-baseline-regression-20260506: Freeze pre-content harness baseline

Status: verified-local
Last verified: 2026-05-06
Applies to: pre-content development baseline, Unity CLI, latest Development player, HumanBot/random-aware regression
Triggers: content development start, baseline freeze, full harness regression, latest Development player

Problem:
Before content work, freeze a known-good harness baseline with static checks, Unity compile/tests, the default matrix, HumanBot progression, progressed reconnect/disconnect, progressed Host Migration, and seed sweep. Record the exact build and artifact paths so later content regressions have a concrete comparison point.

Recipe:
- Use the latest Development player that matches the current C# code. For this baseline: `artifacts/builds/20260505-230726/MDF-MPTest.exe`.
- Run the baseline commands:
  - `python tools/harness/validate_overlay.py`
  - `python tools/harness/precommit.py --self-test`
  - `python tools/harness/precommit.py --all`
  - `unity-cli --project Mdfproject status`
  - `unity-cli --project Mdfproject editor refresh --compile`
  - `unity-cli --project Mdfproject console --type error --stacktrace user`
  - `unity-cli --project Mdfproject test --mode EditMode`
- Run the minimal multiplayer regression set with the same player path:
  - `python tools/harness/mp/run_matrix.py --case all --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`
  - `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`
  - `python tools/harness/mp/run_human_bot_4p_progression.py --seed 2001 --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`
  - `python tools/harness/mp/run_progressed_same_token_reconnect.py --seed 3002 --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`
  - `python tools/harness/mp/run_progressed_disconnect_ai_takeover.py --seed 3001 --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`
  - `python tools/harness/mp/run_progressed_host_migration_e2e.py --seed 4001 --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`
  - `python tools/harness/mp/run_human_bot_seed_sweep.py --seeds 5101,5102,5103 --player-path artifacts/builds/20260505-230726/MDF-MPTest.exe`

Verification:
- `validate_overlay.py` passed, `precommit.py --self-test` passed, and `precommit.py --all` reported `0 errors, 0 warnings`.
- Unity status was ready, compile completed, console errors were `[]`, and EditMode passed `8/8`.
- Build metadata for `artifacts/builds/20260505-230726/MDF-MPTest.exe` reported `result=Succeeded`, `developmentBuild=true`, and `allowDebugging=true`.
- Matrix all passed at `artifacts/mp/20260505-231757-matrix` with `success=true` and all child cases exiting `0`.
- HumanBot prepare passed at `artifacts/mp/20260505-232230-human-bot-prepare` with `SelectAugment`, host `accepted_command`, durable selected augment hash delta, and random outcome matches.
- HumanBot 4p progression passed at `artifacts/mp/20260505-232307-human-bot-4p-progression` with three client bots, three `accepted_command` entries, and host-vs-client random-aware matches.
- Progressed same-token reconnect passed at `artifacts/mp/20260505-232355-progressed-same-token-reconnect` with `success=true` and `failures=[]`.
- Progressed disconnect AI takeover passed at `artifacts/mp/20260505-232449-progressed-disconnect-ai-takeover` with `success=true` and `failures=[]`.
- Progressed Host Migration passed at `artifacts/mp/20260505-232529-progressed-host-migration-e2e` with `accepted_command` entries for `SelectAugment` and `PlaceWall`, callback/token/resume proof, and durable pre/post `mismatches=[]`.
- Seed sweep passed at `artifacts/mp/20260505-232610-human-bot-seed-sweep` for seeds `5101,5102,5103` with `success=true` and `failures=[]`.
- `rg "result=fail|phase=error|parseError" <baseline artifact dirs>` found no matches.
- Scanning `*.Player.log` in the baseline artifact set for `InvalidOperationException`, `NullReferenceException`, `Error when accessing`, and `Failed to free` found no matches.

Pitfalls:
- The matrix command transcript can include cleanup-time `mp_stop` snapshot errors after the scenario already passed, for example `game.battleSnapshot:InvalidOperationException` and `players.allPlayers:InvalidOperationException` in the final Editor `mp_stop` response. Treat this as a cleanup snapshot caveat only when comparison JSONs, result JSONs, saved snapshots, and Player logs are clean.
- Do not update `unity-cli` mid-baseline just because it reports a newer connector version.

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

## mp-human-bot-prepare-progression: Prove client bot progress by durable delta

Status: verified-local
Last verified: 2026-05-06
Applies to: `tools/harness/mp/run_human_bot_prepare_progression.py`, Phase 20 HumanBot prepare progression
Triggers: HumanBot, `/bot/start`, `/bot/status`, selected augment hash, random-aware comparison

Problem:
`test.bot.commandsIssued` only proves the bot submitted a request. Phase 20 needs proof that State Authority accepted a meaningful command and that the resulting randomized state replicated to host and client.

Recipe:
- Launch build host and build client through the real lobby flow, not direct Game autostart.
- Start the client with `--mpHumanBot --mpBotPersona balanced`, then pause it before Game load to capture a clean `before-bot` checkpoint.
- Resume with `/bot/start` and bounded `--mpBotMaxCommands`; for first-command proof use `maxCommands=1` to avoid extra policy work after the accepted delta.
- Treat a command as accepted only when a host snapshot shows a durable delta from the checkpoint, such as `augment.selectedCount/hash`, shop revision/hash, gold, wall count/hash, or placed-unit hash.
- Require host/client snapshot comparison success after the delta, and require the bot player to remain `isConnected=true`, `isAI=false`, and `ai.controllerRegistered=false`.
- Use longer automation request timeouts for this scenario; the main thread can be briefly busy while bot policy or Game scene setup runs.

Verification:
- `artifacts/mp/20260505-171515-human-bot-prepare/human-bot-prepare-assertions.json` reported `success=true`.
- `accepted-command-evidence.json` proved `SelectAugment`, `commandsIssued=1`, and `augment.selectedCount 0 -> 1` for bot player `1`.
- `comparison-latest.json` and `random-outcome-summary.json` showed host/client agreement for same-player shop, augment, and field hashes.
- Build used `artifacts/builds/20260505-171218/MDF-MPTest.exe`, a Development Build with launch-smoke evidence.

Pitfalls:
- `NotifyAugmentSelectedCommand` must update non-server `chosenAugments`; otherwise host selected augment hashes diverge from client snapshots after an accepted selection.
- `parse_mptest_logs.py` splits multiple `[MPTEST]` markers from the same Unity log line and tolerates truncated quoted values, so `mptest.timeline.jsonl` should not produce `parseError` rows from normal Unity log concatenation. It stores the artifact file path as `logSource` so event fields such as `source=[Player:2]` remain intact. Still check raw logs for `result=fail` or `phase=error`.
- Current command sequence fields may be `null`; until accepted command journaling exists, use durable snapshot deltas as the acceptance proof.

## mp-human-bot-4p-progression: Three client bots prove 4-player sync

Status: verified-local
Last verified: 2026-05-06
Applies to: `tools/harness/mp/run_human_bot_4p_progression.py`, Phase 21 HumanBot progression smoke
Triggers: 4-player HumanBot, build host + 3 build clients, random-aware host-vs-client comparison

Problem:
4-player HumanBot progression must prove all slots remain human and that each same-player randomized outcome is equal across every observing peer. It should not use host-side bot commands as the primary proof because host commands bypass client request validation.

Recipe:
- Reuse the real room flow from `run_four_player_smoke.py`: launch host and all three clients into `MatchingLobby`, wait for `runner.activePlayerCount == 4` on every peer, then host-load `Game`.
- Enable `--mpHumanBot` only on the three build clients, with distinct personas such as `balanced`, `maze`, and `shop`.
- Pause client bots before Game load, capture a synchronized `before-bot` checkpoint, then start all bots with bounded `maxCommands=1`.
- Require each client bot to issue at least one command where possible, and prove acceptance with durable host snapshot deltas for that bot's `playerId`.
- Compare host against every client after progression. Keep same-player shop, augment, field wall/unit, battle, HP, gold, and known command fields strict.
- Assert every `PlayerManager` remains `isConnected=true`, `isAI=false`, and `ai.controllerRegistered=false` on every peer.

Verification:
- `artifacts/mp/20260505-172826-human-bot-4p-progression/human-bot-4p-assertions.json` reported `success=true`, `totalBotCommands=3`, and one `SelectAugment` durable delta for each client bot.
- `random-outcome-summary.json` showed matching same-player shop, augment, and field hashes from host to `client-1`, `client-2`, and `client-3`.
- `bot-comparison-host-vs-client-1-latest.json`, `bot-comparison-host-vs-client-2-latest.json`, and `bot-comparison-host-vs-client-3-latest.json` all reported `success=true`.

Pitfalls:
- By round 1, the final checkpoint can naturally be `Battle1`; this is acceptable only if all peer game state/round/battle hashes match. Do not weaken mismatch checks.
- If later phases require all four slots to issue bot commands, add a host bot deliberately and document that host-side `RequestCommandExecution` uses the State Authority path.

## mp-human-bot-seed-sweep: Treat seeds as diagnostic labels

Status: verified-local
Last verified: 2026-05-06
Applies to: `tools/harness/mp/run_human_bot_seed_sweep.py`, Phase 24 stochastic HumanBot sweep
Triggers: seed sweep, stochastic soak, multiple HumanBot prepare runs, random outcome hashes

Problem:
One HumanBot run can miss random-state sync bugs. A seed sweep should run the same random-aware scenario across several seeds and aggregate artifacts without claiming command replay or deterministic RNG control.

Recipe:
- Use `python tools/harness/mp/run_human_bot_seed_sweep.py --seeds 5101,5102,5103 --player-path <Development player>` for the default Phase 24 proof.
- Alternatively pass `--seed-start <n> --seed-count <count>`.
- The sweep creates a parent artifact with one `seed-<seed>/` child root per seed and passes that root to `run_human_bot_prepare_progression.py`.
- By default, stop on the first failing seed. Use `--continue-on-fail` only when collecting multiple failures in one pass is more useful than preserving time.
- Use `--include-4p` only for optional 4-player expansion; Phase 24's required proof is the 2-peer prepare sweep.
- Review `seed-sweep-summary.json` and `result.json`, not just stdout. The summary records child artifact paths, commands issued, command types, bot journals, screenshot paths, random outcome hashes, comparisons, and the first failure path when a seed fails.
- Keep `seedSemantics.deterministicReplayClaim=false`. A seed is a diagnostic run label unless each gameplay RNG source is explicitly controlled and replay-verified.

Verification:
- `artifacts/mp/20260505-190714-human-bot-seed-sweep/result.json` reported `success=true`, `failures=[]`, seeds `5101,5102,5103`.
- Each seed child artifact passed `human-bot-prepare-assertions.json` with `commandsIssued=1`, `lastCommandType=SelectAugment`, and a durable selected augment hash delta.
- Each seed's `comparison-latest.json` reported `success=true`.
- Each seed's `random-outcome-summary.json` reported `matchesClient=true` for both players' shop, augment, and field hashes.
- `rg "result=fail|phase=error|parseError" artifacts/mp/20260505-190714-human-bot-seed-sweep` found no matches.

Pitfalls:
- Do not infer deterministic replay from repeated seed labels. The summary deliberately records known uncontrolled sources such as non-ledgered `UnityEngine.Random`, wall-clock/process timing, and Photon scheduling.
- Child runners from earlier phases may not persist their own `result.json`; the sweep must persist parent `result.json` and per-seed `seed-result.json` with child assertion evidence.
- Use the latest Development player that includes HumanBot and augment snapshot support; do not accidentally select a production-negative build.

## mp-random-authority-audit: Harden boundaries, not replay determinism

Status: verified-local
Last verified: 2026-05-06
Applies to: Phase 25, `docs/ai-harness/random-authority-audit.md`, shop/augment/wall/battle RNG
Triggers: `UnityEngine.Random`, `System.Random`, `Environment.TickCount`, HumanBot random-aware sync, Host Migration random state

Problem:
Random-aware progression only works if durable random outcomes are decided by State Authority and then synced, snapshotted, and migrated. A seed sweep is useful evidence, but it does not prove deterministic replay while shop, augment, wall, battle, survivor boss, and AI planning randomness are still mixed across gameplay and policy code.

Recipe:
- Audit random and time-derived calls with:
  `rg -n "UnityEngine\\.Random|Random\\.Range|Random\\.value|new System\\.Random|System\\.Random|Environment\\.TickCount|DateTime\\.UtcNow|DateTime\\.Now|Guid\\.NewGuid|Stopwatch|Time\\.realtimeSinceStartup" Mdfproject/Assets/Scripts -g "*.cs"`.
- Classify each hit as authoritative gameplay random, client visual/identity random, AI/bot decision pacing, or test-only.
- For authoritative gameplay random, verify the owner, sync path, reconnect path, Host Migration path, and snapshot hash/revision before claiming PASS.
- Prefer narrow client-peer guards on authoritative outcome generators over broad RNG rewrites.
- Keep `UnityEngine.Random.InitState` under `--mpTest` as a diagnostic seed tool; do not claim command replay unless every authoritative RNG source has a ledger or deterministic stream.
- Document remaining un-hashed authoritative risks in `random-authority-audit.md` so later battle phases know what to target.

Verification:
- Phase 25 added `docs/ai-harness/random-authority-audit.md` and guarded shop reroll, augment presentation/selection, and survivor boss pending/target assignment against running non-server client peers.
- `python tools/harness/precommit.py --all` reported `0 errors, 12 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed compilation.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed `8/8`.
- Rebuilt Development player at `artifacts/builds/20260505-192828/MDF-MPTest.exe`; launch smoke exited `0`.
- `artifacts/mp/20260505-192927-human-bot-prepare` passed `run_human_bot_prepare_progression.py --seed 1001` with `lastCommandType=SelectAugment`, a durable selected augment hash delta, and no failures.
- `artifacts/mp/20260505-193030-progressed-host-migration-e2e` passed progressed Host Migration after HumanBot state, with `failures=[]`.
- `artifacts/mp/20260505-193157-human-bot-seed-sweep` passed seeds `5101,5102,5103` with `success=true` and `failures=[]`.

Pitfalls:
- Do not weaken Host Migration comparisons with "randomness changed" explanations. Randomness before migration is allowed; divergence after migration is a failure unless gameplay intentionally advanced under a documented test gate.
- Survivor boss assignment and monster spawn positions still need deeper battle-state snapshot coverage if late-game HumanBot tests start exercising those paths.

## mp-final-random-aware-audit: Pair accepted-command logs with durable deltas

Status: verified-local
Last verified: 2026-05-06
Applies to: final random-aware audit, HumanBot progression, progressed reconnect/disconnect, progressed Host Migration
Triggers: final audit, `accepted_command`, bot journal, random outcome summary, snapshot comparison

Problem:
Bot command submission alone is not enough for final PASS. The final audit needs evidence that a real client request reached State Authority, passed validation, changed durable state, and replicated the same-player random-derived hashes across peers.

Recipe:
- Build or select a Development player that includes the latest C# changes.
- Require `[MPTEST] phase=accepted_command result=pass` in the host timeline for client-request scenarios. This proves server-side RPC validation accepted the command after authoritative `playerId` correction.
- Still require durable delta evidence such as selected augment hash, wall count/hash, shop revision/hash, or placed-unit hash. `accepted_command` is not enough by itself.
- Require random outcome summaries where every same-player `matchesClient` or host-vs-client `matches` entry is true.
- Require checkpoint snapshots and comparison JSONs for before-bot, after first accepted command, final progressed state, reconnect/disconnect, and Host Migration.
- Run `rg "result=fail|phase=error|parseError" <artifact dirs>` and require no matches. `parse_mptest_logs.py` now splits concatenated `[MPTEST]` markers so normal Unity log concatenation should not create parser errors.

Verification:
- Build `artifacts/builds/20260505-201620/MDF-MPTest.exe` succeeded as a Development player and launch smoke exited `0`.
- `artifacts/mp/20260505-201703-human-bot-prepare` passed with `SelectAugment`, `accepted_command`, durable selected augment hash delta, comparison success, and same-player random outcome matches.
- `artifacts/mp/20260505-201737-human-bot-4p-progression` passed with three client bots, three `accepted_command` entries, total bot commands `3`, battle mapping hashes, and host-vs-client same-player matches.
- `artifacts/mp/20260505-201824-progressed-same-token-reconnect` passed with target/full/role/progressed preservation assertions.
- `artifacts/mp/20260505-201914-progressed-disconnect-ai-takeover` passed with the progressed target preserved after AI takeover.
- `artifacts/mp/20260505-201956-progressed-host-migration-e2e` passed with `accepted_command` entries for `SelectAugment` and `PlaceWall`, Host Migration callback/token/resume proof, durable pre/post mismatches `[]`, and no raw `InvalidOperationException` or `Failed to free` log matches.
- `artifacts/mp/20260505-202050-human-bot-seed-sweep` passed seeds `5101,5102,5103` with `deterministicReplayClaim=false` and random-aware comparisons.
- `rg "result=fail|phase=error|parseError"` over the final artifact set found no matches. Accepted-command timeline events preserve RpcInfo `source=[Player:x]` and store the artifact file path separately as `logSource`.

Pitfalls:
- `accepted_command` is logged before the final `GameManagers` lookup and broadcast, so pair it with the durable snapshot delta and comparison result.
- Do not treat a host-side HumanBot as proof of the real client request path. Use connected client bots for that evidence.

## mp-progressed-reconnect-disconnect: Freeze progressed checkpoints without weakening comparisons

Status: verified-local
Last verified: 2026-05-06
Applies to: `tools/harness/mp/run_progressed_disconnect_ai_takeover.py`, `tools/harness/mp/run_progressed_same_token_reconnect.py`, Phase 22
Triggers: HumanBot progressed checkpoint, same-token reconnect, disconnect AI takeover, randomized augment/shop/field state

Problem:
Progressed reconnect/disconnect tests need a synchronized HumanBot-created checkpoint before killing a client. Normal round timers and takeover AI can legitimately continue changing shop, augment, gold, wall, and unit state while the harness waits for reconnect, which makes checkpoint preservation impossible to assert. Same-token reconnect also needs a spare Photon transport slot, but raising `--mpMaxPlayers` normally creates an AI fill gameplay slot that can add random drift before the checkpoint.

Recipe:
- Use `--mpFreezeGameFlow` only in `--mpTest` Development/Editor runs when a test must preserve a checkpoint across disconnect/reconnect. It stops authority game-flow timer transitions and AI controller decisions; HumanBot still runs because it is a separate test-only driver.
- For same-token reconnect, use transport `--mpMaxPlayers 3` but pair it with `--mpDisableAiFill` and an expected gameplay player count of 2. This leaves a Photon slot for client B without creating an extra AI `PlayerManager`.
- Fail fast if the pre-bot checkpoint or HumanBot progressed checkpoint is not synchronized. Do not continue to kill/reconnect from a bad checkpoint.
- Preserve and compare target fingerprints across events: HP, gold, wall count, shop revision/hash, presented and selected augment hashes, field grid/wall/unit hashes.
- Selected and presented augment names must be published by State Authority into Networked snapshot arrays on `PlayerManager`; late-joining/reconnected clients use these arrays when local `AugmentManager` lists are empty.
- Require `targetComparison.success`, `fullComparison.success`, `roleStateComparison.success`, and `progressedPreservation.success` for same-token reconnect.

Verification:
- `artifacts/mp/20260505-183104-progressed-disconnect-ai-takeover` passed with `failures=[]`. Its progressed checkpoint proved `SelectAugment`, and `progressed-preservation-assertions.json` preserved the target shop, augment, and field hashes after AI takeover.
- `artifacts/mp/20260505-183145-progressed-same-token-reconnect` passed with `failures=[]`. `same-token-reconnect-assertions.json` showed target `playerId=1`, matching token hash, `hostTargetIsAI=false`, `hostTargetConnected=true`, full-world comparison success, role-state comparison success, and progressed preservation success.
- Build used `artifacts/builds/20260505-183018/MDF-MPTest.exe`.
- Persist the final runner verdict as `result.json` inside each artifact directory. Stdout `failures=[]` is useful but not enough for strict artifact review.

Pitfalls:
- A support human client consumes the spare Photon slot; do not use that as the same-token workaround.
- `--mpFreezeGameFlow` is a test-only preservation tool, not a gameplay fix. Do not use it in normal progression tests that are meant to prove battle/round advancement.
- A live `NotifyAugmentSelectedCommand` update is not enough for reconnect; reconnect clients need Networked selected/presented augment snapshot names.

## mp-progressed-host-migration: Compare full durable fingerprints after host kill

Status: verified-local
Last verified: 2026-05-06
Applies to: `tools/harness/mp/run_progressed_host_migration_e2e.py`, Phase 23
Triggers: progressed Host Migration, HumanBot checkpoint, process.kill, migration token, durable random state

Problem:
Basic Host Migration callback/token/resume proof does not prove that randomized HumanBot-created state survives migration. A progressed-state test must kill the host only after a synchronized client-bot checkpoint, then compare the new host's durable state against that exact checkpoint. Random mismatches after migration are failures, not expected randomness.

Recipe:
- Launch build host and one build client through `MatchingLobby`; enable `--mpFreezeGameFlow` on both peers so the progressed checkpoint does not drift while the host is killed and recovery completes.
- Enable `--mpHumanBot` only on the survivor client. Pause it before Game load, then start it after a synchronized `before-bot` checkpoint.
- Use at least two bounded HumanBot commands when practical. Seed `4001` with the balanced persona produced `SelectAugment` plus `PlaceWall`, proving both selected augment hash and field wall hash changed before migration.
- Fail fast unless the pre-migration host/client progressed checkpoint comparison succeeds. Save `human-bot-progressed-assertions.json`, `progressed-checkpoint-comparison.json`, and `progressed-random-evidence.json`.
- Kill the host process with `process.kill`, not `/quit`.
- Require the survivor snapshot to prove `OnHostMigration`, non-null migration token, `StartGame` success with the token, `HostMigrationResume`, `completeCount > 0`, `failureCount == 0`, and `recoverySucceeded == true`.
- Compare a full durable fingerprint from the survivor client's progressed checkpoint to the post-migration survivor snapshot: game state/round, battle hashes, HP, gold, wall count, shop revision/hash, presented and selected augment hashes, field grid/wall/unit hashes, and monster hashes.
- Assert the survivor is promoted to Host/server, at least one connected non-AI human remains, player IDs stay unique, and the HumanBot is paused rather than silently resumed.

Verification:
- `python tools/harness/mp/run_progressed_host_migration_e2e.py --seed 4001 --player-path artifacts/builds/20260505-183018/MDF-MPTest.exe` passed with artifact `artifacts/mp/20260505-185323-progressed-host-migration-e2e`.
- `host-migration-proof.json` reported callback/token/resume/startGame/complete success with `failureCount=0`.
- `human-bot-progressed-assertions.json` reported `commandsIssued=2`, `lastCommandType=PlaceWall`, and durable deltas for `wallCount`, selected augment hash, and `field.wallHash`.
- `progressed-host-migration-assertions.json` and `durable-state-report.json` reported `success=true` with `mismatches=[]`.
- `result.json` reported `success=true` and `failures=[]`.

Pitfalls:
- Do not reuse the basic Host Migration durable report alone; it does not compare augment snapshots or all progressed random-state fields.
- If game flow advances during migration, do not loosen comparisons. Use the test-only freeze or add a more explicit test-only migration freeze point.
- A successful migration callback sequence is insufficient if the pre/post durable fingerprint diverges.
- During `HostMigrationResume`, do not read Networked `GameManagers` properties immediately after resume-spawn just to log status. Use the cached migration snapshot label until the object is fully safe to access; the bad pattern caused `Error when accessing GameManagers.currentRound. Networked properties can only be accessed when Spawned() has been called` followed by Fusion cleanup noise in raw player logs.

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

## battle-snapshot-coverage-v1: Hash late-game battle state semantically

Status: verified-local
Last verified: 2026-05-06
Applies to: `MPTestStateSnapshot`, `MPTestAssertions`, `compare_state_snapshots.py`, HumanBot/Host Migration battle progression
Triggers: late-game sync drift, survivor boss assignment, active augment effects, monster spawn/combat divergence

Problem:
Prepare-phase HumanBot snapshots covered shops, augments, walls, and units, but battle progression still had blind spots. Monster snapshots mostly exposed alive count and a legacy living hash, survivor boss pending/assignment state was private manager state, and active augment effect/target state was not compared separately.

Recipe:
- Keep existing hashes and comparisons; add semantic hashes instead of replacing durable coverage.
- Hash active battle role state as `game.battleActiveHash` from `playerId`, opponent id, first attacker id, fighting flag, and attacker flag.
- Hash survivor boss pending/assignment state with gameplay IDs, origin player, boss data key, invasion flag, and coarse HP buckets; also compare pending/assignment counts so non-zero host-only state does not disappear behind `unknown`.
- Hash augment active effects/targets from selected augments, active monster-summon augments, owned bosses, owned scrolls, permanent stat buckets, and resolved player/opponent target when available. Compare active effect/target counts as well as hashes.
- Hash monsters by stable type/trait/boss key, count, coarse HP/max-HP bucket, defender/attacker player ids, and Networked boss gameplay id. Do not hash raw Unity instance IDs or exact interpolated transforms.
- Hash manual/strategic defender skill readiness with semantic unit data, grid position, skill key, activation mode, AI strategic flag, mana bucket, status/casting/dead flags, and target count.
- In `compare_state_snapshots.py`, use strict comparison for conditional required values. When battle phase, non-zero survivor counts, or command counters make a field required, any `unknown`/missing value fails, including both peers missing the value. For optional absent state, both peers may remain `unknown`.
- Mirror the same strict comparison in `MPTestAssertions.CompareDurable` and add negative EditMode tests. Python-only strictness is not enough because custom Unity CLI assertions can use the C# path.
- For active effect target keys, do not fall back to `NetworkObject.Id` or `GameObject.name`. Use semantic target keys: unit owner/data/star/grid cell, monster owner/data/boss metadata/HP bucket/navigation or coarse position, and wall owner/grid/HP bucket.
- Use a read-only battle-map lookup for target hashes. Back it with the state-authority-published Networked battle map and battle-start RPC observations; do not call mutating fallback methods such as `GetBattleOpponent()` from snapshot capture.
- Keep `FirstAttackerPlayerId` and battle-map publication authority-only. Client migration/readiness paths may cache `BattleOpponentSnapshotIds` and `BattleFirstAttackerSnapshotIds` into local dictionaries for comparison, but must not rebuild/publish or assign Networked fields.
- If monster snapshots scan a player's `monsterParent`, also add a live `Monster` fallback filtered by Networked owner id. Client-side monster initialization should still reparent replicated monsters under the owner's monster parent, but the snapshot must not depend on a one-shot init RPC being observed.
- Mirror monster owner id, monster data key, type, traits, and boss gameplay metadata into Networked fields so reconnect/Host Migration peers can rebind and hash living monsters from durable identity.
- Mirror boss gameplay metadata into Networked fields and use those accessors for boss gameplay decisions, not only for snapshot hashes.

Verification:
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed and `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed 8/8.
- Rebuilt Development player at `artifacts/builds/20260506-004439/MDF-MPTest.exe`; launch smoke exited `0`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --player-path artifacts/builds/20260506-004439/MDF-MPTest.exe` passed with artifact `artifacts/mp/20260506-004520-human-bot-prepare`.
- Direct CLI comparison now works: `python tools/harness/mp/compare_state_snapshots.py artifacts/mp/20260506-004520-human-bot-prepare/snapshots/build-host-prepare-progressed.json artifacts/mp/20260506-004520-human-bot-prepare/snapshots/build-client-prepare-progressed.json` returned `success: true`.
- Phase 10 hardened `compare_state_snapshots.py` with one-sided-unknown failures for attack pool, owned scroll, manual skill readiness, semantic monster hashes, and active effect hashes.
- Phase 10 reviewer follow-up aligned C# durable assertions with the Python comparator, made battle hashes required during `Battle1`/`Battle2`, required survivor hashes when survivor counts are non-zero, required `commands.lastCommand` once any battle command counter advances, and added both-one-sided and both-missing negative EditMode tests for those required values.

## battle-command-foundation-authority-guards: Require explicit validation before battle execution

Status: verified-local
Last verified: 2026-05-06
Applies to: `ServerBattleCommandExecutor`, `BattleCommandValidator`, future `BattleSpawnMonsterCommand`, future `UseMagicScrollCommand`
Triggers: battle command foundation, client-requested battle commands, opponent resolution, State Authority validation

Problem:
A generic battle command executor can accidentally become a gameplay bypass if it accepts a missing validation delegate, or if shared opponent-resolution helpers rebuild battle pairings from client/presentation code. That makes future spawn/scroll commands look command-based while skipping source ownership, player role, opponent, target, inventory, or cost checks.

Recipe:
- Reject executable battle commands when validation or execution delegates are missing; do not treat `null` validation as accepted.
- Reject `PresentationOnly` scope before any persistent execution path.
- Require `GameManagers` State Authority and Battle1/Battle2 phase before the server authority executor can run command-specific validation.
- Resolve opponents from the published battle snapshot for client/presentation paths.
- Permit mutating opponent fallback such as `GetBattleOpponent()` only under `ServerAuthorityOnly` plus `HasStateAuthority`.
- Keep `RpcInfo.Source` checks as transient live authorization only; durable gameplay identity remains MDF `playerId` and connection-token state.
- Add edit-mode source guards for the reject codes and the authority-only opponent fallback so later command phases cannot weaken the foundation silently.

Verification:
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed 13/13 after adding the foundation guards.

## battle-spawn-command-pool-reservation: Reserve attack pool before awaited network spawn

Status: compile-and-smoke-verified; battle E2E still needs a dedicated runner
Last verified: 2026-05-06
Applies to: `BattleSpawnMonsterCommand`, `GameManagers.RPC_RequestBattleSpawnMonster`, `PlayerManager.AttackMonsterPool`, `MonsterSpawner.ExecuteSpawnPlanAsync`
Triggers: strategic attack monster spawn command, duplicate spawn requests, async `Runner.Spawn`, attack pool hash drift

Problem:
Battle monster spawn validation can pass for multiple same-slot requests before an awaited network spawn finishes. If the pool is consumed only after spawn, duplicate requests can create extra durable monsters or leave host/client pool hashes inconsistent. Plain client-local pool rebuilds can also hide consumed slots during reconnect or Host Migration recovery.

Recipe:
- Validate command count as exactly one spawn per command. Do not clamp or silently trust client-provided batch counts.
- Keep `RpcInfo.Source` authorization as an early RPC gate, then re-run command validation on State Authority.
- Resolve attacker, defender, battle role, opponent mapping, finite target position, outer spawn zone, and authoritative pool slot before execution.
- Consume or reserve the authoritative pool slot before any awaited spawn call. If `Runner.Spawn` or spawn initialization fails, refund the same slot immediately.
- Use the authoritative pool entry to derive boss id and origin player. Do not trust client-supplied boss/origin fields.
- Include the client's applied authoritative attack-pool snapshot revision in client-requested spawn commands. Do not use the replicated revision alone as proof that the client has applied the matching pool contents.
- Reject missing or mismatched applied revisions and resend the authoritative pool snapshot before accepting another slot-index request.
- Sync attack pool changes with a monotonic revision and ignore stale async client RPC completions.
- Non-authority peers must not rebuild attack pools locally during battle start; request/resend the authority snapshot instead so slot indices always refer to server-owned contents.
- Reconnect and late-join sync must proactively resend the attack-pool snapshot, not only shops/augments/scrolls/walls. Do not rely on the next client spawn click to request the missing pool.
- Host Migration durable snapshots must carry attack-pool refs or stable keys plus counts/revision, and apply them before migration recovery decides whether to rebuild an attack pool.
- If a Fusion `Runner.Spawn` succeeds but later initialization/path validation fails, cleanup must use `Runner.Despawn` for the spawned `NetworkObject`; `Destroy` alone can leave a replicated object alive.
- During migration/recovery, do not refresh an empty attack pool over a positive consumed revision.
- Snapshot `attackMonsterPoolHash`, accepted battle command sequence, spawn sequence, and rejected command count so peer comparisons can detect pool or command drift. Empty/null attack pools need deterministic hashes; required battle pool comparisons must not collapse to `unknown`.
- Mark legacy spawn RPCs deprecated with explicit `battle_spawn_rejected` telemetry instead of leaving early-return reject paths.
- Battle command telemetry is durable snapshot state. Resend it during reconnect/late-join sync and capture/apply it through Host Migration cache; do not leave it as command-time RPC-only state.

Verification:
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed 13/13.
- Development build and launch smoke passed at `artifacts/builds/20260506-031706`.
- Closest existing E2E passed at `artifacts/mp/20260506-031807-human-bot-prepare`, but it only proves prepare progression. A dedicated battle-spawn E2E runner is still required before claiming battle command sync PASS.

## magic-scroll-command-authority-split: Keep scroll gameplay out of presentation RPCs

Status: compile-verified; battle scroll E2E still needs a dedicated runner
Last verified: 2026-05-06
Applies to: `UseMagicScrollCommand`, `GameManagers.RPC_RequestUseMagicScrollCommand`, `PlayerManager.OwnedScrolls`, `ScrollCaster`, `MPTestStateSnapshot`
Triggers: magic scroll use, client-side VFX broadcast, scroll inventory hash drift, buff/status/zone side effects

Problem:
Magic scroll presentation used to create a `ScrollCaster` on every peer and call `SkillEffect.ApplyEffect`. Damage/heal effects often self-guard on target authority, but buff, status, and zone effects can still create client-side durable behavior or snapshot drift if the broadcast path applies gameplay.

Recipe:
- Route scroll use through a State Authority battle command with `casterPlayerId`, stable `scrollSlotIndex`, target position, source reason, and the client's applied owned-scroll revision.
- Validate battle phase, caster source authority, attacker/defender battle roles, finite target, authoritative scroll slot, scroll skill data, and target domain before consumption.
- Consume the scroll only after validation on State Authority. If gameplay application fails, refund the same slot.
- Split `ScrollCaster` into `CastGameplay` and `PlayPresentation`; `CastGameplay` rejects non-authority network peers and `PlayPresentation` only instantiates VFX.
- Leave legacy name-based scroll RPCs as explicit deprecated rejects with `scroll_rejected` telemetry instead of silently returning.
- Track `ownedScrollsHash`, `ownedScrollRevision`, and `useMagicScrollSeq` in snapshots/comparisons so inventory and effect application drift are visible.

Verification:
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject status` reported the Editor ready for `E:/UnityProjects/mdf/Mdfproject`.
- `unity-cli --project Mdfproject editor refresh --compile` completed compilation.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed `13/13`.
- `tools/harness/mp/run_magic_scroll_command.py` and `tools/harness/mp/run_human_bot_battle_progression.py` were not present yet, so scroll command sync still needs dedicated E2E harness implementation before claiming multiplayer artifact PASS.
- Owned scroll RPC sync loads assets asynchronously. Track the latest received revision and recheck it before and after every await so a stale payload cannot overwrite a newer inventory.
- Host Migration durable player snapshots must capture owned scroll asset refs/names plus `OwnedMagicScrollRevision`, restore them on the new State Authority, and resend them to clients before scroll-slot commands can be trusted.
- Duration buff/status/zone effects from scrolls are authority-only after the command split, but they are not yet durable Host Migration state. Treat post-scroll Host Migration/effect preservation as a Phase 6/10/12 blocker until active effect hashes and restore semantics exist.

## magic-scroll-asset-effect-audit-v1: Classify scroll assets and compare active effect hashes

Status: compile-verified; dedicated battle scroll E2E still needs implementation
Last verified: 2026-05-06
Applies to: `MagicScrollData`, scroll `.asset` files, `BuffManager`, `ZoneController`, `MPTestStateSnapshot`, `compare_state_snapshots.py`
Triggers: adding scroll AI metadata, editing scroll assets, auditing scroll buff/status/zone authority, Phase 6 scroll asset/effect audit

Problem:
Scroll asset metadata and runtime duration effects span Unity YAML, authority-only gameplay code, and snapshot comparison. It is easy to classify assets correctly but miss reserialization evidence, or to guard effect application while leaving clear/remove/recalculate paths able to mutate client-local durable effect state.

Recipe:
- Add AI metadata to `MagicScrollData` as serialized enum/bool/float fields, then classify every scroll asset explicitly.
- After editing scroll `.asset` YAML, run `unity-cli --project Mdfproject reserialize <changed scroll asset paths>` and keep the command output in the phase evidence.
- Do not only guard `ApplyBuff` or `ApplyStatusEffect`. Guard `Update`, clear/remove, and stat recalculation entry points too, because `GameEvents.OnGameStateChanged` can fire on non-authority peers from render/UI flow.
- Snapshot active duration effects with semantic keys: target owner/data/star or boss id, effect asset/type, buckets for effect value/tick/damage/slow/range, and coarse zone position. Do not include raw Unity instance IDs.
- Compare `effects.activeBuffCount`, `effects.activeStatusCount`, `effects.zoneCount`, `effects.activeBuffHash`, `effects.activeStatusHash`, and `effects.zoneHash` in both C# assertions and `tools/harness/mp/compare_state_snapshots.py`.
- Treat active buff/status/zone Host Migration timer restoration as unproven until a post-scroll migration artifact exists. Hash comparison proves peer sync at capture time, not durable timer restore.

Verification:
- `unity-cli --project Mdfproject reserialize Mdfproject/Assets/GameData/Scrolls/Scroll_Berserk.asset Mdfproject/Assets/GameData/Scrolls/Scroll_BloodCurse.asset Mdfproject/Assets/GameData/Scrolls/Scroll_Heal.asset Mdfproject/Assets/GameData/Scrolls/Scroll_Stun.asset` returned all four paths.
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed compilation.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed `13/13`.
- `tools/harness/mp/run_magic_scroll_command.py` is still missing, so battle scroll E2E remains `NEEDS_IMPLEMENTATION`.

## unity-cli-editmode-filter-and-force-import-v1: Verify newly added EditMode tests by class filter

Status: verified
Last verified: 2026-05-06
Applies to: `unity-cli --project Mdfproject test --mode EditMode`, newly added tests, forced AssetDatabase import
Triggers: EditMode test runner returns `total=0` for a newly added method filter, Unity does not appear to pick up new test methods after compile

Problem:
After adding new methods to an existing EditMode test class, `unity-cli --project Mdfproject test --mode EditMode --filter <methodName>` can return `total=0` even though the methods are valid and the full/class test run will discover them. Treat a zero-count method-filter run as inconclusive, not PASS.

Recipe:
- Prefer a class filter when verifying new tests in `MPTestHarnessEditModeTests`:
  - `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests`
- If Unity appears stale, force import the changed test file by piping code through stdin to avoid PowerShell quoting problems:
  - `@' ... '@ | unity-cli --project Mdfproject exec`
  - Example body:
    - `UnityEditor.AssetDatabase.ImportAsset("Assets/Scripts/Testing/MP/Editor/MPTestHarnessEditModeTests.cs", UnityEditor.ImportAssetOptions.ForceUpdate);`
    - `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();`
    - `return "forced";`
- Then run:
  - `unity-cli --project Mdfproject editor refresh --compile --force`
  - `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests`
- Do not rely on `total=0` method-filter output as evidence that a new test passed.

Verification:
- `unity-cli --project Mdfproject test --mode EditMode --filter ActivateSkillCommandSourceContainsStrategicManualSkillGuards` returned `total=0`.
- `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests` then discovered the new Phase 8 tests and passed `19/19`.

## human-bot-prepare-teardown-ai-takeover-v1: Keep prepare E2E snapshots from being polluted by teardown takeover

Status: verified
Last verified: 2026-05-06
Applies to: `AIPlayerController`, `NetworkManager.TryEnableDisconnectedAiTakeover`, `run_human_bot_prepare_progression.py`, `run_human_bot_seed_sweep.py`
Triggers: HumanBot prepare seed sweep fails late with host/client augment or active effect hash mismatch after the client process drops near the harness timeout

Problem:
`run_human_bot_prepare_progression.py` waits for stable host/client snapshots after the HumanBot issues a command. If the build client exits or disconnects near the wait timeout, `NetworkManager` can enable disconnected AI takeover for that durable `playerId`. The takeover AI may issue a new prepare command on the host before the final comparison, producing host-only deltas such as a second `SelectAugment` and `augment.activeEffectHash` mismatch. The failure can be mislabeled as `bot_no_meaningful_command` even though the assertions show meaningful deltas.

Recipe:
- Confirm the timeline contains `disconnect_cache` followed by `disconnect_ai_takeover` just before host-only `prepare_decision_policy` or `mdf_decision_emit` lines.
- Confirm `bot-journal-latest.json` shows the HumanBot remained a real client command path before the teardown window.
- In `--mpTest` prepare-only HumanBot scenarios, do not let an AI takeover controller with `InputAuthority == PlayerRef.None` continue prepare decisions that pollute the HumanBot host/client comparison.
- Keep `NotifyAugmentSelectedCommand` presentation-only on clients. Do not add to `chosenAugments`, owned-boss lists, active summon lists, owned scroll lists, or permanent stat bonuses from the notification command; those must come from State Authority replication/sync paths.
- Build augment active-effect snapshot hashes from replicated selected augment snapshots first, and avoid double-counting authority-side derived boss/summon lists as separate peer-equality requirements.
- If a seed sweep fails on `attackMonsterPoolHash` after a boss augment, inspect logs for `InvalidKeyException` on the boss `MonsterData.name`. Attack-pool sync must not partially apply a snapshot when one entry cannot resolve; abort/resync or resolve from loaded `AugmentData`/wave/asset references. Also verify the Addressables address matches the `MonsterData` asset name, for example `MonData_Boss_Elemental`.
- Rebuild the Development player after changing driver/controller code; stale player builds will still show the old bot journal schema and do not prove the current C# path.

Verification:
- Stale build detection: `bot-journal-latest.json` lacked `MdfDecision` fields until a new Development player was built.
- `python tools/harness/mp/build_player.py` produced `artifacts/builds/20260506-053915/MDF-MPTest.exe` with `developmentBuild=true`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --player-path artifacts/builds/20260506-053915/MDF-MPTest.exe` passed and the journal included `score`, `kind`, `attackMonsterPoolHash`, and `ownedScrollsHash`.
- After adding the takeover guard and rebuilding `artifacts/builds/20260506-054901/MDF-MPTest.exe`, `run_human_bot_prepare_progression.py --seed 1001` still passed.
- After removing client-side persistent augment notification effects and duplicate active-effect snapshot counting, `python tools/harness/mp/build_player.py` produced `artifacts/builds/20260506-061424/MDF-MPTest.exe`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --player-path artifacts/builds/20260506-061424/MDF-MPTest.exe` passed with artifact `artifacts/mp/20260506-061500-human-bot-prepare`.
- `python tools/harness/mp/run_human_bot_seed_sweep.py --seeds 5101,5102 --player-path artifacts/builds/20260506-061424/MDF-MPTest.exe` passed with artifact `artifacts/mp/20260506-061538-human-bot-seed-sweep`.
- After removing the remaining presented-augment cache mutation, `artifacts/builds/20260506-062445/MDF-MPTest.exe` passed `run_human_bot_prepare_progression.py --seed 1001` with artifact `artifacts/mp/20260506-062528-human-bot-prepare`.
- The same build's `run_human_bot_seed_sweep.py --seeds 5101,5102` passed seed 5101 but failed seed 5102 on `player.1.attackMonsterPoolHash` mismatch after a boss augment. This is a monster-pool replication blocker to handle in the command/snapshot phases, not a Behavior Tree v2 HumanBot routing failure.
- Artifact inspection found `MonData_Boss_Elemental` failed Addressables resolution because the group address was `MonData_Elemental`; the client skipped that boss entry and applied a partial attack pool.
- After adding read-only fallback resolution plus all-or-nothing attack-pool snapshot apply, `artifacts/builds/20260506-064820/MDF-MPTest.exe` passed `run_human_bot_prepare_progression.py --seed 1001` with artifact `artifacts/mp/20260506-064924-human-bot-prepare` and `run_human_bot_seed_sweep.py --seeds 5101,5102` with artifact `artifacts/mp/20260506-065043-human-bot-seed-sweep`.
- After correcting the Addressables address for GUID `b9ad52bb50cfab741bb309010e961c5b` to `MonData_Boss_Elemental`, reserializing the group asset, and rebuilding, `artifacts/builds/20260506-065615/MDF-MPTest.exe` passed `run_human_bot_prepare_progression.py --seed 1001` with artifact `artifacts/mp/20260506-065647-human-bot-prepare` and `run_human_bot_seed_sweep.py --seeds 5101,5102` with artifact `artifacts/mp/20260506-065810-human-bot-seed-sweep`.

## battle-command-e2e-observer-client-v1: Keep battle command E2E client stable while host HumanBot drives progression

Status: verified
Last verified: 2026-05-06
Applies to: `tools/harness/mp/run_battle_spawn_monster_command.py`, `tools/harness/mp/run_magic_scroll_command.py`, `tools/harness/mp/run_human_bot_battle_progression.py`, battle command snapshot checks
Triggers: battle E2E reaches command execution on the host, but the client returns to `MatchingLobby` or the final snapshot is back in `Prepare`

Problem:
Battle command tests need a connected client snapshot to prove replication. Running an unbounded HumanBot on both build peers can make the client repeatedly emit prepare decisions while scene/GameManagers are transitioning; a failed run showed host `BattleSpawnMonster` evidence advanced while the client returned to `MatchingLobby`, leaving `game_managers_or_command_processor_missing` bot logs and no comparable snapshot. Also, final snapshots can legitimately be in a later `Prepare` phase after battle commands already executed, so requiring the final state to still be `Battle1`/`Battle2` loses valid evidence.

Recipe:
- Default battle command E2E to a host HumanBot driver plus a connected build client observer. Enable `--client-human-bot` only for cases that specifically validate both peer emitters.
- Pause bots before the pre-battle checkpoint, then start the host bot after host/client Game snapshots compare cleanly.
- Use `--bot-prepare-mode augment-only` for battle command E2E. This allows the bot to take a first augment but avoids repeated maze `PlaceWall` planning, which can block the host simulation long enough for the observer client to hit Fusion `Timeout`.
- Treat battle phase as reached if both peers were observed in `Battle1`/`Battle2` during polling, or if replicated battle command counters advanced and snapshot comparison succeeded.
- Keep the success sample strict: host and client command counters must match, monster/scroll semantic hashes must be present when required, and `compare_state_snapshots.py` must pass.
- Poll battle command snapshots frequently enough to catch short-lived battle monsters. Save the first strict host/client live monster semantic match as `snapshots/build-host-spawn-semantic.json`, `snapshots/build-client-spawn-semantic.json`, and `spawn-semantic-comparison.json`; the final snapshot may be later in the battle after the spawned monster has died or reached the goal.
- Build augment snapshot comparisons from State Authority published presented/selected augment snapshots before local UI caches. Non-authority local presented augment lists can remain populated after a selected augment is already replicated, and should not become the durable comparison source.
- For a dedicated magic-scroll command E2E, use the test-only HumanBot option `--mpBotPreferScrollAugment` / `preferScrollAugment=true` so the prepare policy picks a `GrantMagicScroll` augment when one is offered. This creates deterministic scroll inventory evidence without adding production grant hooks.
- Save `battle-command-evidence-latest.json`, `battle-start-comparison.json` when observed, and final `battle-command-evidence.json`; use the final evidence file for PASS/FAIL.
- In `--mpTest` player builds, disable stack traces for `Log` and `Warning` via `Application.SetStackTraceLogType`. Long battle command runs emit many MPTEST and gameplay diagnostics; full stack traces on every log can make Player.log explode and contribute to Fusion `Timeout` disconnects.
- Launch build peers with explicit per-peer `-logFile <artifact>/<peer>.Player.log` so host/client disconnect reasons are not interleaved in the shared Unity Player.log.

Verification:
- Initial failure artifact `artifacts/mp/20260506-075006-battle-spawn-monster-command` showed host `spawnMonsterSeq=86`, client `scene=MatchingLobby`, and repeated client `game_managers_or_command_processor_missing`.
- Follow-up failure artifact `artifacts/mp/20260506-080146-battle-spawn-monster-command` showed client fallback from `OnDisconnectedFromServer:Timeout`; the shared Player.log contained stack traces for routine `[MPTEST]`/gameplay logs.
- Follow-up artifact `artifacts/mp/20260506-081229-battle-spawn-monster-command` used peer-specific Player logs and showed host tick stuck at `349` while wall planning ran between prepare decisions, followed by client `OnDisconnectedFromServer:Timeout`.
- Final Phase 11 build `artifacts/builds/20260506-084532/MDF-MPTest.exe` passed launch smoke.
- `artifacts/mp/20260506-084714-battle-spawn-monster-command` passed with `acceptedBattleCommandSeq=1`, `spawnMonsterSeq=1`, matching host/client command counters, `spawnSemanticsObserved=true`, and `spawn-semantic-comparison.json` success.
- `artifacts/mp/20260506-084621-magic-scroll-command` passed with `acceptedBattleCommandSeq=2`, `spawnMonsterSeq=1`, `useMagicScrollSeq=1`, matching host/client command counters, `scroll_accepted`, `scroll_effect_applied`, and client `scroll_presentation` timeline entries. The bot selected `Aug_Scroll_Heal` with `preferScrollAugment=true`.
- `artifacts/mp/20260506-084753-human-bot-battle-progression` passed with `acceptedBattleCommandSeq=2`, `spawnMonsterSeq=2`, matching host/client command counters, and no failures.

## post-battle-lifecycle-freeze-checkpoint-v1: Freeze only after battle progression before reconnect, disconnect, or Host Migration assertions

Status: provisional
Last verified: 2026-05-06
Applies to: `MPTestCommandLine`, `MPTestAutomationServer`, `battle_progression_common.py`, post-battle reconnect/disconnect/Host Migration runners
Triggers: A post-battle lifecycle E2E must compare a progressed battle checkpoint across client disconnect, same-token reconnect, or Host Migration without hiding normal game-flow advancement as expected randomness

Problem:
Battle command E2E must run without `--mpFreezeGameFlow` at launch so timers can advance into `Battle1`/`Battle2` and HumanBot can emit `BattleSpawnMonsterCommand`/scroll decisions. After the battle checkpoint is captured, however, normal battle simulation can keep advancing while the harness kills a peer or waits for Host Migration. Weakening comparisons would hide real post-replication divergence.

Recipe:
- Launch post-battle lifecycle cases unfrozen and drive battle with the host HumanBot plus an observer client.
- After strict battle command evidence is captured, stop the HumanBot and call the `--mpTest` automation endpoint `/test/freezeGameFlow` on both peers.
- Re-dump a frozen host/client battle checkpoint and require `compare_state_snapshots.py` success before killing a client or host.
- For client disconnect, compare the frozen host checkpoint with the post-takeover host snapshot using semantic battle/world fingerprints, while asserting the dropped durable `playerId` remains present, `isConnected=false`, `isAI=true`, and `ai.controllerRegistered=true`.
- For same-token reconnect, use the same connection token for the replacement build client, assert the local `playerId` and token hash are reclaimed, require full host/client snapshot comparison, and compare the post-reconnect host snapshot against the frozen battle checkpoint.
- For Host Migration, kill the host process, not `/quit`, then require `OnHostMigration`, non-null token, `StartGame` with token/resume/recovery, promoted survivor host/server, a connected non-AI survivor, unique `playerId`s, and frozen battle checkpoint preservation.
- Keep event-time preservation strict. If active monster/effect timers still drift after freeze, report the exact fingerprint mismatch as a lifecycle blocker instead of relaxing the comparison.
- Permanent augment bonuses cannot remain RPC-only. Publish attack damage/speed bonus buckets as Networked player state and compute snapshots from that replicated source; otherwise same-token reconnect can reclaim the player correctly but fail active-effect comparison because the reconnected client missed the original bonus RPC.
- Attack monster pool contents cannot rely only on local lists plus one-shot RPCs during post-battle Host Migration. Publish slot names/counts/boss metadata into Networked snapshot arrays and use those arrays before local `AttackMonsterPool` when capturing migration snapshots or state hashes.

Verification:
- Dry-run coverage passed for `run_progressed_reconnect_after_battle.py --dry-run`, `run_progressed_disconnect_after_battle.py --dry-run`, `run_battle_seed_sweep.py --seeds 7101,7102,7103 --dry-run`, and `run_matrix.py --case progressed-reconnect-after-battle --dry-run`.
- `artifacts/builds/20260506-095411/MDF-MPTest.exe` passed launch smoke after the post-battle lifecycle fixes.
- `artifacts/mp/20260506-095518-progressed-host-migration-after-battle` passed with Host Migration callback/token/resume/start-game/recovery evidence and frozen battle checkpoint preservation.
- `artifacts/mp/20260506-095609-progressed-reconnect-after-battle` passed same-token reconnect with reclaimed `playerId`, `isAI=false`, `ai.controllerRegistered=false`, full snapshot comparison, and battle preservation.
- `artifacts/mp/20260506-095711-progressed-disconnect-after-battle` passed disconnect/AI takeover with the dropped `playerId` preserved as disconnected AI and frozen battle state preserved.
- `artifacts/mp/20260506-095834-battle-seed-sweep` passed seeds `7101,7102,7103`.

## battle-command-precommit-guardrails-v1: Block clear battle command bypasses

Status: verified
Last verified: 2026-05-06
Applies to: `tools/harness/precommit.py`, battle command architecture, AI/HumanBot policies, scroll presentation, manual skill policy
Triggers: future edits near monster spawn, magic scrolls, strategic skills, HumanBot, battle command validators

Problem:
Battle command architecture can regress even when E2E scripts exist if a future edit reintroduces direct AI monster spawn, client-side scroll gameplay effects, manual skill bypasses, or HumanBot-as-AI registration. These are cheaper and safer to catch statically before Unity/E2E runs.

Recipe:
- BLOCK clear unsafe patterns:
  - HumanBot/test human files registering or attaching `AIPlayerController`.
  - AI policy files calling `SpawnMonsterAtPositionAsync`, `.ActivateSkill(`, or `ApplyEffect(` directly.
  - Testing HumanBot files calling `.ActivateSkill(` or `ApplyEffect(` directly.
  - `RPC_BroadcastMagicScrollUsed`, `CreateScrollPresentationLocal`, or `PlayPresentation` calling gameplay effect or inventory-consumption methods.
  - battle command classes missing required authority validation/execution tokens.
- WARN review-only patterns:
  - direct low-level `SpawnMonsterAtPositionAsync` outside the approved low-level mechanism/command files.
  - legacy `RPC_RequestSpawnMonster` or `RPC_RequestUseMagicScroll` calls.
  - direct scroll gameplay/inventory or attack-pool consumption outside command executor files.
- Keep precommit output ASCII-only; Windows PowerShell may run under CP949.
- Keep false positives low by excluding Editor test source from production bypass checks and stripping `#if false` disabled legacy blocks before scanning.

Verification:
- `python tools/harness/precommit.py --self-test` passed guardrail fixtures for HumanBot AI registration, scroll presentation gameplay, and AI direct monster spawn.
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings`.

## ai-behavior-metrics-summary-v1: Summarize HumanBot behavior from artifacts without changing gameplay

Status: verified-from-existing-artifact
Last verified: 2026-05-07
Applies to: `tools/harness/mp/summarize_bot_metrics.py`, HumanBot battle progression, battle seed sweep
Triggers: AI behavior tuning, HumanBot journal review, behavior baseline, seed sweep metrics

Problem:
Before changing AI decision quality, agents need a lightweight baseline that counts what the bot actually did. That baseline should come from existing artifacts and `[MPTEST]` timelines, not from extra gameplay state mutations or policy changes.

Recipe:
- Run or reuse a HumanBot battle artifact with `build-host-bot.jsonl`, `mptest.timeline.jsonl`, snapshots, and `battle-command-evidence.json`.
- Generate metrics with:
  - `python tools/harness/mp/summarize_bot_metrics.py <artifact-dir>`
- The summarizer writes `<artifact-dir>/bot-metrics-summary.json` with command counts by type, rejection reasons, observed gold spend, rerolls, buys, walls, monster spawns, scroll uses, scroll target counts, manual skill uses, round reached, and battle reached.
- `run_human_bot_battle_progression.py` writes this artifact through the shared battle progression runner after timeline collection.
- `run_battle_seed_sweep.py` writes each child artifact's metrics and aggregates them into `bot-metrics-seed-sweep-summary.json`.

Verification:
- `python tools/harness/mp/summarize_bot_metrics.py artifacts/mp/20260506-102324-human-bot-battle-progression` produced `bot-metrics-summary.json`.
- The summary counted `SelectAugment=1`, `BattleSpawnMonster=2`, `monsterSpawns=2`, `battleReached=true`, and no rejected decisions for that existing artifact.
- `python -m py_compile tools/harness/mp/summarize_bot_metrics.py tools/harness/mp/battle_progression_common.py tools/harness/mp/run_battle_seed_sweep.py tools/harness/mp/run_human_bot_battle_progression.py` passed.

Pitfalls:
- `commandsIssued` is bot-issued command intent from `human_bot_decision` timeline lines. It can be ahead of final snapshot command counters when the bot emits a command just after the last strict comparison sample.
- `goldSpent` is an observed estimate from journal gold drops and known command cost fields; if a run gains gold after spending before the next journal entry, it can undercount.
- Use dedicated scroll artifacts to judge scroll quality. A seed sweep may still pass while only some seeds actually use a scroll.

## prepare-policy-composition-e2e-v1: Freeze prepare flow and compare stable unit semantics

Status: verified
Last verified: 2026-05-07
Applies to: `PrepareDecisionPolicy`, `MPTestHumanBotDriver`, `run_human_bot_prepare_progression.py`, `MPTestStateSnapshot`
Triggers: prepare-phase AI/HumanBot economy, buy/reroll tuning, composition-aware purchase scoring

Problem:
Prepare-only HumanBot E2E can be polluted by normal phase timers and by unstable client-local presentation state. A bot that issues many prepare commands may reach Battle or disconnect before the harness captures a stable checkpoint. Unit prefab defaults can also leave client-side star/merge state stale unless registration and destruction paths clean up field registries.

Recipe:
- After host/client reach a clean `Game` prepare checkpoint, call `/test/freezeGameFlow` on both peers before starting the prepare HumanBot. This keeps the test focused on prepare commands while still allowing the bot to emit real client requests.
- Prepare policies should read networked augment/shop snapshots from `PlayerManager` before trusting local `AugmentManager` or `ShopManager` lists. Local presentation lists can lag and cause repeated `SelectAugment` or same-slot `BuyUnit`.
- For prepare snapshots, compare stable unit semantics. Use UnitData/star multisets and avoid NetworkObject IDs, exact HP, battle-only skill readiness, or local placement details that are not the purpose of the economy test. Keep battle snapshots stricter for battle state.
- When `RPC_RegisterUnitAt` receives a unit whose prefab already has `UnitData`, still reinitialize if the authoritative star differs. Otherwise a star-2 shop purchase can remain star-1 on clients.
- On `Unit.OnDestroy`, remove the unit from the owner `FieldManager` registry so client snapshots do not retain despawned merge ingredients after State Authority combines units.
- Treat wall focus as a late prepare action. Do not run expensive maze/wall planning until the basic target composition is actually satisfied; otherwise HumanBot can block the client main thread or compare unsynced wall presentation instead of the buy/reroll behavior under test.

Verification:
- `python tools/harness/precommit.py --all` passed with `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed `43/43`.
- `artifacts/builds/20260507-151458/MDF-MPTest.exe` passed prepare E2E functional assertions for balanced/shop/unit/maze and seed sweep `5101,5102,5103`.

Prepare v2 functional PASS evidence:
- Build: `artifacts/builds/20260507-151458/MDF-MPTest.exe` (`build-metadata.json` result `Succeeded`, Development, AllowDebugging).
- Balanced artifact `artifacts/mp/20260507-151658-human-bot-prepare`: `BuyUnit=4`, `RerollShop=1`, first reroll `soldSlotCount=4`, `rerollBeforeThreeSold=false`, final composition `M1/R3/H0`, final field unit count `4`.
- Shop artifact `artifacts/mp/20260507-151819-human-bot-prepare`: `BuyUnit=4`, `RerollShop=0`, `rerollBeforeThreeSold=false`, final composition `M2/R2/H0`, final field unit count `4`.
- Unit artifact `artifacts/mp/20260507-151937-human-bot-prepare`: `BuyUnit=4`, `RerollShop=0`, `rerollBeforeThreeSold=false`, final composition `M2/R1/H1`, final field unit count `4`.
- Maze artifact `artifacts/mp/20260507-151539-human-bot-prepare`: `BuyUnit=5`, `RerollShop=0`, `rerollBeforeThreeSold=false`, final composition `M1/R2/H2`, final field unit count `5`. Maze did not wall-only while the field unit count was zero.
- Seed sweep artifact `artifacts/mp/20260507-152054-human-bot-seed-sweep`: seeds `5101,5102,5103`, aggregate `BuyUnit=15`, `RerollShop=3`, first reroll `soldSlotCount=3`, `rerollBeforeThreeSold=false`, final field unit total max `4`.
- The target composition `M2/R2/H1` is soft scoring, not a hard pass condition. Do not fail `M1/R3/H0` or `M2/R2/H0` when no healer appeared in the available shop sequence. A future improvement is explicit `UnitData` AI role metadata; current healer detection is name/skill-token heuristic via `UnitCompositionAnalyzer`.
- Cleanup is separate from functional PASS. In this batch, E2E cleanup did not reliably terminate `MDF-MPTest.exe`; `Stop-Process`, CIM terminate, and `/quit` could fail or time out. Treat this as cleanup `NEEDS_ENVIRONMENT` / harness hardening, not a Prepare gameplay failure.
- Next action: harden E2E process cleanup and split matrix profiles so smoke/regression/random-aware/nightly runs have explicit cost and cleanup reporting.

Pitfalls:
- Do not treat `build-host-quit.json` or `build-client-quit.json` with `success=true` as proof the player process exited; it only proves the automation endpoint accepted a quit request. For final artifact review, also check live processes, for example `Get-CimInstance Win32_Process -Filter "name = 'MDF-MPTest.exe'" | Where-Object { $_.CommandLine -match '<artifact timestamp>' } | Select-Object ProcessId,CommandLine`. A clean `result.json` plus live peer processes or a missing quit artifact is a cleanup failure until the harness records actual process exit or kills leftovers.

## harness-entropy-cleanup-v1: Keep context bundles and generated state out of the source of truth

Status: verified
Last verified: 2026-05-08
Applies to: `.gitignore`, `_context_packer`, `.codex/session-state`, `tools/harness/precommit.py`, harness docs
Triggers: context bundle drift, stale prompts, generated session state, root BAT cleanup, precommit guardrails

Problem:
Generated local state can leak into context bundles or make future agents follow stale paths. In this pass, `.codex/session-state` JSON files and `_context_packer/output` bundles were generated artifacts, while current docs needed explicit labels for historical phase prompts and obsolete HumanBot adapters.

Recipe:
- Ignore generated outputs with `.gitignore`: `artifacts/`, `_context_packer/output/`, `_context_bundles/`, `.codex/session-state/`, and root `nul`.
- Exclude `.codex/session-state/**` from context packer profiles that include `.codex/**`; also keep the default packer source excludes aligned so a missing config does not re-include session files.
- Remove generated context outputs and session-state JSON files after verifying their resolved paths are inside the repo. Keep the directories, but keep them empty unless a local run is actively using them.
- Keep only `MDF_PACK_CONTEXT.bat` at the repo root for context packing; helper BAT/scripts live under `_context_packer/`.
- Mark old MVP phase docs as historical and point active readers to `codex-full-phase-prompts.md` plus `randomized-progression-test-plan.md`.
- Keep `MPTestHumanBotPolicy` as an explicit obsolete adapter only; runtime HumanBot uses `PrepareDecisionPolicy`, `BattleDecisionPolicy`, and `HumanClientCommandEmitter`.
- Do not remove field registry entries from `Unit.OnDisable`. Battle death disables units for later respawn. Actual destroy/despawn cleanup should unregister through `Unit.OnDestroy` or explicit merge/sell paths.

Guardrails:
- `tools/harness/precommit.py` BLOCKs context packer profiles that include `.codex/**` without excluding `.codex/session-state/**`.
- `tools/harness/precommit.py` BLOCKs `Unit.OnDisable` bodies that call `UnitDied`.
- Existing guardrails still BLOCK HumanBot/test peers registering `AIPlayerController`, AI direct `SpawnMonsterAtPositionAsync`, and scroll gameplay effects in presentation RPC/helpers.

Verification:
- `git check-ignore -v .codex/session-state/probe.json _context_packer/output/probe.zip _context_bundles/probe.zip nul` matched the expected `.gitignore` entries.
- `python _context_packer/unity_context_pack.py --profile scripts-plus-context --dry-run` listed no `.codex/session-state` files.
- `python tools/harness/validate_overlay.py` PASS, 33 required paths checked and `AGENTS.md <= 70` lines.
- `python tools/harness/precommit.py --self-test` PASS, including context packer and `Unit.OnDisable` guard tests.
- `python tools/harness/precommit.py --all` PASS, `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject status` found one ready Editor, Unity `2021.3.45f1`.
- `unity-cli --project Mdfproject editor refresh --compile` PASS, compilation complete.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` PASS, `43/43`.

Cleanup performed:
- Deleted generated files under `.codex/session-state/`.
- Deleted generated files under `_context_packer/output/`.
- Deleted root generated `nul`.
- Updated `.gitignore`, context packer config/default excludes, current docs, and precommit guardrails.

Remaining intentional legacy:
- `MPTestHumanBotPolicy` remains as an obsolete compatibility adapter for legacy test callers.
- `run_matrix.py --case all` still means the existing default case subset; profile split is the next matrix phase.
- `Mdfproject/Assembly-CSharp-Editor.csproj` is a generated/tracked Unity file with pre-existing ordering churn. Do not hand-edit it; prefer ignoring future generated churn in review unless the team decides to untrack generated project files.

## e2e-process-cleanup-hardening-v1: Report cleanup separately and prove orphaned player PIDs

Status: verified with environment blocker
Last verified: 2026-05-08
Applies to: `tools/harness/mp/launch_player.py`, HumanBot prepare/battle runners, matrix runner
Triggers: `MDF-MPTest.exe` left running after E2E, Windows process cleanup, orphan detection

Problem:
Functional E2E assertions can pass while Unity player processes fail to exit. In this Windows environment, `/quit`, `TerminateProcess` via Python, `Stop-Process`, and `taskkill /PID <pid> /T /F` can all fail to remove the original `MDF-MPTest.exe` PID even though child PIDs are terminated. Treat this as cleanup `NEEDS_ENVIRONMENT` unless strict cleanup is explicitly requested.

Recipe:
- Record the baseline `MDF-MPTest.exe` PIDs before launching an E2E case.
- For each launched player, record PID, parent PID, redacted command line, artifact dir, stdout/stderr paths, and `Player.log` path.
- Cleanup order is: automation `/quit`, wait for exit, Python `terminate`, wait, Python `kill`, wait, CIM live-process check, then Windows `taskkill /PID <pid> /T /F`, followed by a CIM absent wait.
- Always write `cleanup-report.json` with per-peer cleanup steps, `orphanedPids`, `cleanupSuccess`, and `cleanupStatus`.
- Keep gameplay result separate from cleanup: non-strict runs may have `functionalSuccess=true`, `success=true`, and `cleanupStatus=NEEDS_ENVIRONMENT`; strict runs append `cleanup_failed:<status>`.
- `--leave-processes-on-fail` leaves peers alive for debugging only when functional failures already exist.
- `run_matrix.py` forwards cleanup flags only to cases that support them and records per-case `cleanupStatus`; dry-run skips Unity editor cleanup side effects.
- Do not add a pywin32 dependency for Job Objects without approval. The current fallback is documented `taskkill`; a future ctypes Job Object wrapper should attach the player at process creation time if this environment blocker must become a hard PASS.

Verification:
- `python -m py_compile tools\harness\mp\launch_player.py tools\harness\mp\run_human_bot_prepare_progression.py tools\harness\mp\run_human_bot_seed_sweep.py tools\harness\mp\battle_progression_common.py tools\harness\mp\run_matrix.py` PASS.
- `python tools/harness/precommit.py --all` PASS, `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --bot-persona balanced --bot-max-commands 5 --min-commands 3` passed functional assertions with artifact `artifacts/mp/20260507-173915-human-bot-prepare`.
- `artifacts/mp/20260507-173915-human-bot-prepare/result.json` recorded `functionalSuccess=true`, `success=true`, `cleanupSuccess=false`, `cleanupStatus=NEEDS_ENVIRONMENT`, and `orphanedPids=[48936,53784]`.
- `artifacts/mp/20260507-173915-human-bot-prepare/cleanup-report.json` recorded both peers timing out after `/quit` and terminate, still alive by CIM after Python kill, and `taskkill_tree` failing the original PID with "There is no running instance of the task" after terminating child PIDs.
- Prepare metrics for the same artifact: `BuyUnit=4`, `RerollShop=0`, `firstRerollSoldSlotCount=null`, `rerollBeforeThreeSold=false`, final field unit total `3`.

Pitfalls:
- Do not use `Popen.poll()` alone as cleanup proof on Windows. This environment showed `Popen` exit code `1` while CIM/tasklist still listed the same `MDF-MPTest.exe` PID.
- Do not redact or omit cleanup failures. Report exact orphan PIDs and separate cleanup status from gameplay assertions.

## matrix-profile-split-v1: Keep feature smoke cheap and move heavy coverage to profiles

Status: verified
Last verified: 2026-05-08
Applies to: `tools/harness/mp/run_matrix.py`, matrix docs, HumanBot/battle seed sweeps
Triggers: matrix cost control, random-aware runs, battle-heavy runs, nightly automation

Problem:
`run_matrix.py --case all` was doing a fixed default subset, while newer random-aware, battle, lifecycle, and seed sweep cases needed named groups. Without explicit profiles, feature work can accidentally run too much, or nightly automation can accidentally run too little.

Recipe:
- Keep targeted case execution with `--case <case>`.
- Keep `--case all` backward-compatible as the existing default subset: `editor-host-build-client`, `build-host-editor-client`, `build-host-build-client`, `ai-fill-smoke`, `disconnect-ai-takeover`, `same-token-reconnect`, `four-player-smoke`.
- Add `--profile` for named sets:
  - `smoke`: three Editor/Build smoke cases.
  - `regression`: `smoke` plus AI fill, disconnect takeover, same-token reconnect, four-player smoke, and HumanBot prepare.
  - `battle`: battle spawn, magic scroll, and HumanBot battle progression.
  - `lifecycle`: progressed reconnect, disconnect, and Host Migration after battle.
  - `random-aware`: HumanBot prepare, HumanBot 4p, progressed lifecycle cases, and HumanBot seed sweep.
  - `nightly`: regression, battle, lifecycle, battle seed sweep, and HumanBot seed sweep.
- Use `--list-cases` and `--list-profiles` before wiring automation.
- Use `--dry-run` to prove selected cases and child commands. Dry-run must not call Unity Editor cleanup between cases.
- Matrix summary JSON should record `profileName`, `selectedCases`, per-case `functionalSuccess`, per-case `cleanupStatus`, aggregate `functionalSuccess`, aggregate `cleanupStatus`, `overallSuccess`, and child artifact paths.
- Seed sweep profile defaults are diagnostic, not deterministic replay claims: HumanBot prepare sweep uses `5101,5102,5103`; battle sweep uses `7101,7102,7103`.

Verification:
- `python -m py_compile tools\harness\mp\run_matrix.py tools\harness\mp\run_battle_seed_sweep.py tools\harness\mp\run_human_bot_seed_sweep.py` PASS.
- `python tools/harness/mp/run_matrix.py --list-profiles` listed `smoke`, `regression`, `battle`, `lifecycle`, `random-aware`, and `nightly`.
- `python tools/harness/mp/run_matrix.py --list-cases` listed all targeted cases, including `human-bot-seed-sweep`, and documented `--case all`.
- `python tools/harness/mp/run_matrix.py --profile smoke --dry-run` PASS with selected cases `editor-host-build-client`, `build-host-editor-client`, `build-host-build-client`.
- `python tools/harness/mp/run_matrix.py --profile battle --dry-run` PASS with selected cases `battle-spawn-monster-command`, `magic-scroll-command`, `human-bot-battle-progression`.
- `python tools/harness/mp/run_matrix.py --profile random-aware --dry-run` PASS with selected cases `human-bot-prepare`, `human-bot-4p-progression`, `progressed-reconnect-after-battle`, `progressed-disconnect-after-battle`, `progressed-host-migration-after-battle`, `human-bot-seed-sweep`.

Pitfalls:
- Do not silently redefine `--case all` as nightly.
- Do not let `--dry-run` perform Editor `mp_stop` or `editor stop`; it should only create command/selection artifacts.

## human-bot-battle-after-prepare-v2-v1: Full prepare can feed battle command progression

Status: verified with cleanup environment blocker
Last verified: 2026-05-08
Applies to: `run_human_bot_battle_progression.py`, `battle_progression_common.py`, `PrepareDecisionPolicy`, `BattleDecisionPolicy`
Triggers: Prepare v2 recheck, HumanBot battle progression, battle command sync

Problem:
After changing composition-aware prepare buy/reroll behavior, battle progression must prove that full prepare still reaches battle and that battle command counters/snapshots stay synchronized. The battle command-specific scripts can keep `augment-only`, but `run_human_bot_battle_progression.py` should default to full prepare for this recheck.

Recipe:
- Use a Development player built after the current C# changes. Stale builds can miss prepare/battle policy or unit lifecycle fixes.
- `run_human_bot_battle_progression.py` accepts `--bot-persona` as a compatibility alias for `--host-bot-persona`.
- Keep `run_human_bot_battle_progression.py` default `--bot-prepare-mode full` for Prepare v2 rechecks. Use `--bot-prepare-mode augment-only` only when isolating battle command sync.
- For PASS, check both `battle-command-evidence.json` and `bot-metrics-summary.json`:
  - `battle-command-evidence.json.success=true`, empty errors/warnings, host/client command counters match.
  - `battle-comparison-latest.json.success=true`.
  - `bot-metrics-summary.json.summary.battleReached=true`.
  - Buy/reroll metrics preserve Prepare v2 invariants: `BuyUnit > 0`, no `rerollBeforeThreeSold`, and first reroll sold slot count is `>=3` when reroll exists.
- Treat cleanup separately. Non-strict runs may still be `success=true` with `cleanupStatus=NEEDS_ENVIRONMENT`.

Verification:
- `python tools/harness/precommit.py --all` PASS, `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` PASS.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]` before and after E2E.
- `unity-cli --project Mdfproject test --mode EditMode` PASS, `43/43`.
- `python tools/harness/mp/build_player.py --launch-smoke --exit-after-seconds 5` created `artifacts/builds/20260507-175726/MDF-MPTest.exe`; `build-metadata.json` reports `result=Succeeded`, Development, AllowDebugging. The launch-smoke wrapper timed out after 10 seconds and left PID `23764`, so smoke cleanup remains part of the Windows cleanup environment blocker.

Battle recheck artifacts using `artifacts/builds/20260507-175726/MDF-MPTest.exe`:
- Balanced: `artifacts/mp/20260507-180042-human-bot-battle-progression`, command `--seed 6101 --bot-persona balanced`, `functionalSuccess=true`, `cleanupStatus=NEEDS_ENVIRONMENT`. Metrics: `BuyUnit=7`, `RerollShop=0`, `rerollBeforeThreeSold=false`, `battleReached=true`, `finalFieldUnitTotal=6`, `acceptedBattleCommandSeq=5`, `spawnMonsterSeq=3`, `activateSkillSeq=2`, `useMagicScrollSeq=0`.
- Unit: `artifacts/mp/20260507-180341-human-bot-battle-progression`, command `--seed 6102 --bot-persona unit`, `functionalSuccess=true`, `cleanupStatus=NEEDS_ENVIRONMENT`. Metrics: `BuyUnit=9`, `RerollShop=0`, `rerollBeforeThreeSold=false`, `battleReached=true`, `finalFieldUnitTotal=7`, `acceptedBattleCommandSeq=6`, `spawnMonsterSeq=3`, `activateSkillSeq=3`, `useMagicScrollSeq=0`.
- Maze: `artifacts/mp/20260507-180640-human-bot-battle-progression`, command `--seed 6103 --bot-persona maze`, `functionalSuccess=true`, `cleanupStatus=NEEDS_ENVIRONMENT`. Metrics: `BuyUnit=5`, `RerollShop=1`, first reroll `soldSlotCount=4`, `rerollBeforeThreeSold=false`, `battleReached=true`, `finalFieldUnitTotal=5`, `acceptedBattleCommandSeq=3`, `spawnMonsterSeq=3`, `activateSkillSeq=0`, final evidence `useMagicScrollSeq=0`.
- All three had `battle-command-evidence.json.success=true` with empty errors/warnings, matching host/client `acceptedBattleCommandSeq` and `spawnMonsterSeq`, and `battle-comparison-latest.json.success=true`.
- The Maze run also logged a server AI `UseMagicScroll` opportunity and execution in `build-host.Player.log` (`scroll_accepted`, `scroll_effect_applied`, `battle_command_executed`), proving the scroll command path remains active when a scroll opportunity appears. It occurred outside the final paired host/client evidence window, so keep the dedicated `magic-scroll-command` case for strict scroll snapshot proof.

Pitfalls:
- `battle-command-evidence.json` may be captured after the game returns to Prepare; use its `battleObserved=true` plus `bot-metrics-summary.json.summary.battleReached=true`, not final `currentState`, to decide whether battle was reached.
- `useMagicScrollSeq=0` in final evidence does not prove the scroll path was unavailable for the whole run; check `[MPTEST]` scroll logs and the dedicated magic scroll case when scroll behavior is the feature under test.

## windows-e2e-orphan-pressure-v1: Treat high orphan counts as an environment blocker

Status: verified with environment blocker
Last verified: 2026-05-08
Applies to: `tools/harness/mp/launch_player.py`, cleanup reports, final audit E2E retries
Triggers: many live `MDF-MPTest.exe` processes, D3D resource errors, lobby start timeouts, cleanup report token redaction

Problem:
When many old `MDF-MPTest.exe` processes remain alive, new E2E runs can fail before gameplay starts. In the final audit, 75 live MDF test players remained after cleanup attempts. A fresh HumanBot battle retry then failed with D3D `0x887A0005` resource creation errors, host `NetworkManager.JoinLobby` null reference after a duplicate `NetworkRunner`, missing `GameManagers`, and zero bot commands. This is an environment cleanup blocker, not Prepare v2 or battle command gameplay evidence.

Recipe:
- Before optional heavy E2E, count live players with `Get-Process -Name MDF-MPTest` and `Get-CimInstance Win32_Process -Filter "Name = 'MDF-MPTest.exe'"`.
- If the count is high, do not continue piling on matrix, Host Migration, or seed sweep cases unless the goal is specifically to reproduce cleanup pressure. Record `NEEDS_ENVIRONMENT` with the count, failed cleanup methods, and the last artifact path.
- `Stop-Process -Name MDF-MPTest -Force` can be insufficient in this environment; the observed count stayed `75 -> 75`. Do not claim the environment is clean from the command alone.
- Cleanup reports must redact both current-process secrets and unrelated baseline process command lines. Use generic CLI argument redaction for `--mpAutomationToken` and `--mpConnectionToken`, not only exact per-run secret replacement.
- When generated cleanup reports were written before the generic redaction fix, scrub ignored artifact JSON before sharing bundles or logs.

Verification:
- `python -m py_compile tools\harness\mp\launch_player.py` PASS after adding generic token redaction.
- A direct `_redact_text` probe converted `--mpAutomationToken abc --mpConnectionToken=def` to redacted token placeholders.
- `Select-String -Path artifacts\**\*.json -Pattern '--mpAutomationToken\s+[^<\s]|--mpConnectionToken\s+[^<\s]|--mpAutomationToken=[^<\s]|--mpConnectionToken=[^<\s]'` returned no matches after scrubbing generated JSON artifacts.
- `artifacts/mp/20260507-182237-human-bot-battle-progression/result.json` recorded functional failure from environment startup pressure: `host_start_timeout`, `client_join_timeout`, `GameManagers` missing, `commandsIssued=0`, and cleanup `NEEDS_ENVIRONMENT` with orphaned PIDs `28024,9812`.

Pitfalls:
- Do not use a failed high-pressure retry to regress the previously clean functional PASS evidence. Keep the last known functional artifacts and the environment-blocked retry separate in reports.
- Do not print raw live process command lines in final summaries; redact token arguments before showing process diagnostics.

## windows-job-launcher-graceful-headless-v1: Contain, quit, and optionally headless-run MDF players

Status: verified
Last verified: 2026-05-08
Applies to: `tools/harness/mp/launch_player.py`, `build_player.py`, `run_matrix.py`, `MPTestGracefulQuit.cs`
Triggers: Windows cleanup blocker, `/quit` timeout, D3D/GPU pressure, headless smoke/prepare E2E

Recipe:
- Launch Windows MDF players through the ctypes Job Object path. Cleanup reports should show `jobCreated=true`, `assignedToJob=true`, `killOnJobCloseSet=true`, and `headlessPlayer` when enabled.
- Keep cleanup order: automation `/quit`, wait, terminate Job Object, terminate/kill process, `taskkill` fallback.
- Before heavy E2E, block if live `MDF-MPTest.exe` count exceeds `--orphan-threshold` unless `--force-run-with-orphans` is explicitly passed.
- Runtime `/quit` and `--mpExitAfterSeconds` should use graceful MPTest quit logs: `quit_requested`, `human_bot_stop_requested`, `runner_shutdown_begin`/`runner_shutdown_complete` or timeout, `automation_server_stop`, `application_quit_called`.
- Use `--headless-player` for smoke and logic E2E that do not require screenshot assertions. Headless command JSON should include `-batchmode -nographics`, result JSON should include `headlessPlayer=true`, and screenshot artifacts should be marked skipped.
- Smoke matrix cases should call `write_case_cleanup_report` instead of hand-written quit/wait cleanup, so every child artifact records `cleanup-report.json`, `cleanupReportPath`, and `orphanedPids`.

Verification:
- `python -m py_compile tools/harness/mp/launch_player.py tools/harness/mp/build_player.py tools/harness/mp/run_matrix.py` PASS.
- `python tools/harness/precommit.py --all` PASS, `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` PASS and `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` PASS, `43/43`.
- `python tools/harness/mp/build_player.py --launch-smoke --exit-after-seconds 5 --orphan-threshold 0` built `artifacts/builds/20260507-231956/MDF-MPTest.exe`; launch smoke cleanup PASS with leftover count 0 and graceful quit logs in `launch-smoke.Player.log`.
- `python tools/harness/mp/run_matrix.py --profile smoke --dry-run --headless-player` PASS with all three smoke child commands carrying `--headless-player`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --player-path artifacts/builds/20260507-231956/MDF-MPTest.exe --headless-player --orphan-threshold 0` PASS at `artifacts/mp/20260507-232704-human-bot-prepare`; `result.json` recorded `headlessPlayer=true`, cleanup PASS, `orphanedPids=[]`, and live `MDF-MPTest.exe` returned to 0.
- `python tools/harness/mp/run_matrix.py --profile smoke --headless-player` PASS at `artifacts/mp/20260508-000921-matrix`; every child cleanup report recorded `cleanupStatus=PASS`, `orphanedPids=[]`, `headlessPlayer=true`, and `jobCreated=true`/`assignedToJob=true`/`killOnJobCloseSet=true`. Live `MDF-MPTest.exe` returned to 0.

Pitfalls:
- Do not rely on `Popen.poll()` alone as Windows cleanup proof; verify CIM/tasklist absence and cleanup-report orphan lists.
- Do not overwrite a peer's explicit `-logFile` artifact with `collect_player_log(..., peer_name)` after cleanup; use a separate fallback label such as `build-host-or-last` so graceful quit lines remain in the peer `Player.log`.
- Do not default screenshot or visual-debugging cases to headless mode.

## human-bot-ui-close-before-board-actions-v1: Match player shop-close routine before placement

Status: verified
Last verified: 2026-05-08
Applies to: `MPTestHumanBotDriver`, `ShopUIController`, HumanBot prepare progression
Triggers: HumanBot unit purchase followed by maze wall placement or unit movement while the shop UI remains open

Recipe:
- Keep this in the test-only HumanBot path under `UNITY_EDITOR || DEVELOPMENT_BUILD`; do not change normal production quit or gameplay behavior.
- After `PrepareDecisionPolicy` chooses a command but before `HumanClientCommandEmitter.TryEmit`, close the visible local `ShopUIController` for board actions.
- Gate the close routine to `CommandType.PlaceWall` and `CommandType.MoveUnit`; buy, reroll, and augment commands should keep their normal UI behavior.
- Use `ShopUIController.SetContentVisibility(false)` so the HUD toggle state and CanvasGroup/raycast blocking are updated through the same UI API as user-driven shop closing.
- Log `[MPTEST] phase=human_bot_ui code=shop_close_before_board_action` with `commandType`, `playerId`, `shopUiPresent`, `wasVisible`, and `closed` for artifact review.

Verification:
- `unity-cli --project Mdfproject editor refresh --compile` PASS.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests` PASS, `44/44`.
- `unity-cli --project Mdfproject test --mode EditMode` PASS, `44/44`.
- `python tools/harness/precommit.py --all` PASS, `0 errors, 0 warnings`.
- Built Development player `artifacts/builds/20260508-003828/MDF-MPTest.exe`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --bot-persona maze --bot-max-commands 8 --min-commands 6 --player-path artifacts/builds/20260508-003828/MDF-MPTest.exe --headless-player --orphan-threshold 0 --cleanup-timeout-seconds 20` PASS at `artifacts/mp/20260508-004010-human-bot-prepare`.
- The client Player log recorded four `BuyUnit` commands followed by `phase=human_bot_ui code=shop_close_before_board_action result=pass commandType=MoveUnit wasVisible=True closed=True`, then `MoveUnit`.
- `result.json` recorded `cleanupStatus=PASS`, `orphanedPids=[]`, `headlessPlayer=true`, and final live `MDF-MPTest.exe` count returned to 0.

## survivor-boss-snapshot-compact-networked-v1: Keep GameManagers replicated snapshots under Fusion word limits

Status: verified
Last verified: 2026-05-08
Applies to: `GameManagers`, `SurvivorBossManager`, `MPTestStateSnapshot`, long HumanBot progression
Triggers: persistent authority-only gameplay state appears as non-zero on host and `unknown`/zero on client snapshots

Recipe:
- Treat survivor-boss pending/assignment as persistent State Authority-owned gameplay state; do not weaken snapshot comparison for host-only non-zero state.
- Mirror only compact snapshot evidence on `GameManagers`: pending/assignment counts and stable `sha256` hash hex in `[Networked]` fields.
- Let `MPTestStateSnapshot` read the replicated `GameManagers` count/hash values on every peer.
- Keep the full mutable survivor-boss lists in `SurvivorBossManager` on the authority side; add client mutation guards for extract/ID allocation paths.
- Rebuild the Development player after C# networking changes before rerunning build/build E2E.

Verification:
- A large `NetworkArray<NetworkString<_64>>` survivor-boss snapshot exceeded Fusion object word limits and caused early migration/startup failure; replacing it with compact count/hash fields fixed the build-run startup.
- `python tools/harness/mp/build_player.py --launch-smoke --exit-after-seconds 5 --headless-player --orphan-threshold 0 --cleanup-timeout-seconds 20` built `artifacts/builds/20260508-022528/MDF-MPTest.exe` with launch smoke cleanup PASS.
- `python tools/harness/mp/run_human_bot_3round_progression.py --seed 8101 --headless-player --player-path artifacts/builds/20260508-022528/MDF-MPTest.exe` PASS at `artifacts/mp/20260508-022613-human-bot-3round-progression`: `maxRoundReached=4`, `completionReason=target_round_complete`, `cleanupStatus=PASS`, `10/10` checkpoints passed.

Pitfalls:
- Unity compile can pass even when the already-built player is stale. Always pass the newly built player path when validating C# networking changes.
- Do not add high-capacity string arrays to `GameManagers` casually; Fusion can fail at runtime with object word-limit assertions even after clean C# compile.

## human-bot-game-to-end-endurance-v1: Classify endurance without hiding GameOver status

Status: verified
Last verified: 2026-05-08
Applies to: `tools/harness/mp/run_human_bot_game_to_end.py`, endurance profile, HumanBot long progression
Triggers: need to prove actual GameOver or distinguish timeout/stall/tuning in a bounded run

Recipe:
- Use a separate `gameToEndPass` field from script/process `success`. Only set `gameToEndPass=true` and `finalStatus=PASS` when both final snapshots are `GameOver`, final snapshot comparison passes, player IDs are unique, command counters agree, no `[MPTEST]` failure/error lines exist, and cleanup passes.
- When the bounded run reaches `--max-duration-seconds` or `--max-rounds` before GameOver, classify `NEEDS_TUNING`, `TIMEOUT`, or `STALLED`; do not label it GameToEnd PASS. `--allow-timeout-result` may allow a classified timeout command to exit successfully for CI diagnostics, but `gameToEndPass` must remain false.
- Run HumanBot on both build peers for endurance when using `--bot-prepare-mode augment-only`; this avoids prepare placement drift while producing enough battle pressure for GameOver.
- Capture every Prepare/Battle1/Battle2/GameOver checkpoint plus a progress timeline containing round/state, HP, monster, and command evidence.

Verification:
- `python tools/harness/mp/run_human_bot_game_to_end.py --seed 9101 --headless-player --max-duration-seconds 900 --allow-timeout-result --player-path artifacts/builds/20260508-022528/MDF-MPTest.exe` PASS at `artifacts/mp/20260508-023844-human-bot-game-to-end`.
- The result recorded `gameToEndPass=true`, `finalStatus=PASS`, `cleanupStatus=PASS`, `maxRoundReached=6`, `currentState=GameOver` on host/client, final comparison success, and `22/22` checkpoint comparisons passed.
- Bot metrics recorded `BattleSpawnMonster=177`, `SelectAugment=12`, and no rejected decision reasons.

Pitfalls:
- Do not use `--allow-timeout-result` as a shortcut to claim PASS; it only accepts bounded timeout classification when GameOver is not reached.
- Keep endurance out of smoke/regression/nightly unless explicitly accepted; use `--profile endurance` for opt-in runs.

## long-lifecycle-target-round-freeze-v1: Stop HumanBot before inserting long lifecycle events

Status: verified
Last verified: 2026-05-08
Applies to: `tools/harness/mp/long_lifecycle_common.py`, `run_3round_*`, Host Migration/reconnect/disconnect insertion after long progression
Triggers: lifecycle insertion happens immediately after `round-complete`, especially R4 Prepare after a 3-round run

Recipe:
- For `round-complete` long lifecycle cases, start HumanBot with `stopAtRound = targetRound + 1`.
- Still call `/test/bot/stop` and `/test/freezeGameFlow` on both peers before taking the pre-event checkpoint; this gives explicit stop/freeze artifacts.
- Require stable host/client pre-event snapshots after bot stop and freeze before killing a peer or host.
- Use the frozen client snapshot as the Host Migration preservation baseline, and reject the run if post-event state moved to lobby, lost players, or missed Host Migration proof counters.
- Host Migration PASS must also reject duplicate runtime `PlayerManager` state. Gate on `objects.playerManagerCount == expectedPlayers` and scan full player logs for `[HM-SMOKE] FAIL` or `handler_migration_complete_fail`.
- Do not record `handler_migration_complete_success` until AI takeover reconciliation has completed and `aiTakeoverReady=true`.
- If Fusion resumes duplicate `PlayerManager` objects for the same durable `playerId`, rebuild `GameManagers.NetworkPlayers` from the best canonical candidate and despawn stale duplicates before AI takeover and smoke checks.
- Keep this behavior scoped to long lifecycle insertion; long progression and endurance modes should continue using `--mpBotStopAtRound 0` unless their own pass condition requires a stop.

Verification:
- `python -m py_compile tools/harness/mp/run_human_bot_3round_progression.py tools/harness/mp/long_lifecycle_common.py tools/harness/mp/run_3round_host_migration.py tools/harness/mp/run_matrix.py` PASS.
- `python tools/harness/mp/run_matrix.py --profile long-lifecycle --dry-run` PASS.
- `python tools/harness/mp/build_player.py --launch-smoke --exit-after-seconds 5 --headless-player --orphan-threshold 0 --cleanup-timeout-seconds 20` built `artifacts/builds/20260508-034700/MDF-MPTest.exe`; launch smoke cleanup PASS.
- `python tools/harness/mp/run_3round_host_migration.py --seed 8201 --headless-player --player-path artifacts/builds/20260508-034700/MDF-MPTest.exe` PASS at `artifacts/mp/20260508-034748-3round-host-migration`: `target_round_complete`, `maxRoundReached=4`, `host-migration-proof.success=true`, `onHostMigrationCount=1`, `recoverySucceeded=true`, `aiTakeoverReady=true`, post snapshot `playerManagerCount=2`, `[HM-SMOKE] PASS`, preservation success, cleanup PASS.
- `python tools/harness/mp/run_3round_reconnect.py --seed 8201 --headless-player --player-path artifacts/builds/20260508-034700/MDF-MPTest.exe` PASS at `artifacts/mp/20260508-035119-3round-reconnect`: reconnected client reclaimed the same playerId, full comparison/preservation passed, cleanup PASS.
- `python tools/harness/mp/run_3round_disconnect_ai_takeover.py --seed 8201 --headless-player --player-path artifacts/builds/20260508-034700/MDF-MPTest.exe` PASS at `artifacts/mp/20260508-035459-3round-disconnect-ai-takeover`: disconnected player became AI-controlled, preservation passed, cleanup PASS.

Pitfalls:
- If the bot keeps running into R4 Prepare, it can select a new augment between the completion snapshot and the lifecycle kill. That race can make Host Migration either restore a different baseline or fall back to lobby before proof counters advance.
- `GameManagers.AllPlayers` can hide duplicate runtime `PlayerManager` objects because it reads the canonical network array. Always check object counts as well as logical player snapshots after migration.
- A clean C# compile does not update an already-built player; rebuild and pass the fresh `--player-path` after Host Migration or PlayerManager changes.
