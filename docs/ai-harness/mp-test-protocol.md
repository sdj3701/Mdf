# MDF Multiplayer Test Protocol

## Roles

The harness must test both host directions:

1. Editor Host + Build Client.
2. Build Host + Editor Client.
3. Later: Build Host + Build Client.
4. Later: 4-player matrix and host migration.

## Runtime args

```text
--mpTest
--mpRole host|client
--mpSession <session>
--mpMaxPlayers <2-4>
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
--mpScenario <scenario>
--mpDisableAiFill
--mpFreezeGameFlow
--mpHumanBot
--mpBotPersona balanced|maze|shop|unit|passive
--mpBotSeed <int>
--mpBotDurationSeconds <seconds>
--mpBotStopAtRound <round>
--mpBotMaxCommands <n>
--mpBotRecordJournal <path>
```

In `--mpTest`, set `Application.runInBackground = true` and emit `[MPTEST]` logs.

## MPTEST log format

Every harness event starts with `[MPTEST]` and uses stable key/value fields.

Example:

```text
[MPTEST] ts=2026-05-04T10:00:00.123Z case=editor-host-build-client role=host phase=start session=mp-abc scene=Title result=begin msg="start host"
[MPTEST] ts=... role=client phase=connected session=mp-abc players=2 tick=123 result=pass
[MPTEST] ts=... role=host phase=assert name=player_count expected=2 actual=2 result=pass
[MPTEST] ts=... role=client phase=error code=scene_timeout result=fail msg="Game scene not loaded"
```

Required fields when available:

- `ts`
- `case`
- `role`
- `session`
- `phase`
- `scene`
- `tick`
- `playerId`
- `result`
- `code`
- `msg`

## Timeline vs snapshot

`[MPTEST]` logs tell what happened and when.

State snapshots prove that peers agree on durable game state.

Never treat logs alone as synchronization PASS.

## MVP scenarios

### `lobby_smoke`

- Start host/client.
- Verify same session.
- Verify expected active player count.
- Verify `NetworkPlayer` ready/nickname state if available.

### `game_smoke`

- Load `Game` scene.
- Verify `GameManagers` exists and is spawned.
- Verify `PlayerManager` count.
- Verify each player has `playerId >= 0`.
- Verify local player can be relinked.

### `prepare_smoke`

- Wait for `GameManagers.currentState == Prepare` or trigger first prepare flow.
- Verify shop snapshot exists for each active player.
- Verify player HP/gold/walls have reasonable values.
- Optionally run a deterministic command such as reroll or wall placement if safe.

### `human_bot_prepare_progression`

- Start a real host/client session through the normal room flow.
- Enable `--mpHumanBot` on at least one connected human client.
- Confirm the bot player remains `isAI=false` and has no registered `AIPlayerController`.
- Wait for at least one accepted meaningful command such as `SelectAugment`, `BuyUnit`, `PlaceWall`, `MoveUnit`, or `RerollShop`.
- Compare host/client snapshots with random-aware rules:
  - same player's shop hashes match,
  - same player's augment hashes match when presented,
  - same player's field wall/unit hashes match,
  - no duplicate `playerId`,
  - command sequence/revision is monotonic where available.

Do not assert fixed shop item names, fixed wall hashes, or fixed augment names.

### `battle_smoke`

- Advance or wait to `Battle1`/`Battle2`.
- Verify battle opponents mapping.
- Verify attacker/defender flags.
- Verify monster spawner readiness.
- Optionally spawn one test monster only through authority-gated test command.

## Scenario categories

Use categories to choose the smallest case that proves the change. A longer wall-clock runtime does not automatically mean deeper game progression; seed sweeps are long by repetition, while long/endurance cases must advance one match deeper.

| Category | Purpose | Examples | Default automation scope |
| --- | --- | --- | --- |
| `smoke` | Cheap launch, lobby, Game scene, player-count, and basic snapshot readiness. | `editor-host-build-client`, `build-host-editor-client`, `build-host-build-client`, `four-player-smoke`, `ai-fill-smoke` | Safe for frequent local checks and default smoke. |
| `progression` | HumanBot-driven durable randomized state mutation through normal request paths. | `human-bot-prepare`, `human-bot-4p-progression`, `human-bot-seed-sweep` | First-Prepare progression; not proof of multi-round play. |
| `battle` | Battle entry, battle command validation, monster/scroll/effect hashes, and command telemetry. | `battle-spawn-monster-command`, `magic-scroll-command`, `human-bot-battle-progression`, `battle-seed-sweep` | Early Battle1/Battle2 coverage; usually round 1. |
| `lifecycle` | Reconnect, disconnect/AI takeover, or Host Migration around a synchronized checkpoint. | `progressed-reconnect-after-battle`, `progressed-disconnect-after-battle`, `progressed-host-migration-after-battle` | Early progressed checkpoint plus recovery; not sustained round progression. |
| `long` | Bounded multi-round normal progression with per-state and per-round checkpoint comparisons. | `human-bot-3round-progression` | Opt-in profile only; never part of smoke. |
| `long-lifecycle` | Lifecycle insertion after a completed multi-round checkpoint. | `3round-reconnect`, `3round-disconnect-ai-takeover`, `3round-host-migration` | Opt-in; requires the 3-round checkpoint to pass before the lifecycle event. |
| `endurance` | Game-to-end or bounded timeout/stall classification with progress evidence. | `human-bot-game-to-end` | Explicit opt-in only; not normal regression unless accepted. |

## E2E artifact layout

```text
artifacts/mp/<timestamp>-<case>/
  run.json
  command-transcript.log
  editor.log
  host.stdout.log
  host.stderr.log
  client-1.stdout.log
  client-1.stderr.log
  mptest.timeline.jsonl
  snapshots/
    host-pre.json
    client-1-pre.json
    host-post.json
    client-1-post.json
  screenshots/
    editor.png
    host.png
    client-1.png
  result.json
  comparison.json
  bot-journal.jsonl
  random-outcomes.jsonl
  checkpoint-summary.json
  cleanup-report.json
  failure-summary.md
```

## Matrix profiles

`tools/harness/mp/run_matrix.py` supports targeted `--case` runs and named `--profile` runs.

Use:

- Feature development: `python tools/harness/mp/run_matrix.py --profile smoke` or a targeted `--case <case>`.
- AI, prepare, or random progression changes: `python tools/harness/mp/run_matrix.py --profile random-aware`.
- Battle command changes: `python tools/harness/mp/run_matrix.py --profile battle`.
- Multi-round progression changes: `python tools/harness/mp/run_matrix.py --profile long`.
- Long lifecycle changes: `python tools/harness/mp/run_matrix.py --profile long-lifecycle`.
- Game-to-end endurance checks: `python tools/harness/mp/run_matrix.py --profile endurance`.
- Merge or nightly coverage: `python tools/harness/mp/run_matrix.py --profile nightly`.

Profiles:

- `smoke`: `editor-host-build-client`, `build-host-editor-client`, `build-host-build-client`.
- `regression`: `smoke`, `ai-fill-smoke`, `disconnect-ai-takeover`, `same-token-reconnect`, `four-player-smoke`, `human-bot-prepare`.
- `battle`: `battle-spawn-monster-command`, `magic-scroll-command`, `human-bot-battle-progression`.
- `lifecycle`: `progressed-reconnect-after-battle`, `progressed-disconnect-after-battle`, `progressed-host-migration-after-battle`.
- `long`: `human-bot-3round-progression`.
- `long-lifecycle`: `3round-reconnect`, `3round-disconnect-ai-takeover`, `3round-host-migration`.
- `random-aware`: `human-bot-prepare`, `human-bot-4p-progression`, progressed lifecycle cases, and `human-bot-seed-sweep`.
- `nightly`: `regression`, `battle`, `lifecycle`, `battle-seed-sweep`, and `human-bot-seed-sweep`.

Additional profiles after long-progression implementation:

- `endurance`: `human-bot-game-to-end`.
- `full-regression`: `regression`, `battle`, `lifecycle`, and `long`.

`nightly` currently remains the pre-long nightly set. Use `full-regression` when long progression must be included.
`endurance` remains explicit opt-in and is not included in `nightly`.

Do not add `long` or `endurance` cases to `smoke`. Do not claim GameToEnd PASS unless `GameOver` is actually reached.

Headless build-player mode:

- `--headless-player` adds Unity player `-batchmode -nographics` for build peers and records `headlessPlayer` in command/result/cleanup artifacts.
- `--profile smoke` defaults to headless build players. Use `--no-headless-player` when collecting screenshots or debugging visual output.
- Prepare and battle logic E2E may run headless when screenshots are not assertions; screenshot artifacts should be recorded as skipped in that mode.
- Screenshot or visual-debugging cases must stay in graphics mode.

`--case all` is a backward-compatible alias for the existing default subset:

```text
editor-host-build-client
build-host-editor-client
build-host-build-client
ai-fill-smoke
disconnect-ai-takeover
same-token-reconnect
four-player-smoke
```

Do not treat `--case all` as nightly. Use `--list-cases`, `--list-profiles`, and `--dry-run` before adding a profile to automation.

## Matrix flow: Editor Host + Build Client

```text
1. unity-cli status / compile / console check.
2. Build Development player.
3. Clear Editor console.
4. Use `mp_start_host` with session/maxPlayers/scene/scenario.
5. Launch build client with `--mpRole client` and automation server.
6. Poll build `/ping` and Editor `mp_dump_state`.
7. Wait for expected players and Game scene.
8. Run scenario-specific command.
9. Dump snapshots from both peers.
10. Compare comparable fields.
11. Capture screenshots and logs.
12. Cleanup all processes and stop Editor play mode.
```

## Matrix flow: Build Host + Editor Client

```text
1. Build Development player.
2. Launch build host with `--mpRole host` and automation server.
3. Use `mp_join_client` from Editor.
4. Wait for Game scene and expected players.
5. Run scenario-specific command.
6. Dump and compare snapshots.
7. Collect artifacts and cleanup.
```

## Failure categories

For the debug order and minimum failure artifact set, see `failure-triage.md`.

- `compile_error`
- `console_error`
- `player_launch_failed`
- `automation_ping_timeout`
- `session_join_timeout`
- `scene_timeout`
- `player_count_mismatch`
- `gamemanagers_missing`
- `snapshot_schema_error`
- `snapshot_mismatch`
- `authority_violation`
- `host_migration_not_triggered`
- `artifact_missing`
- `bot_not_running`
- `bot_misclassified_as_ai`
- `bot_no_meaningful_command`
- `random_outcome_mismatch`
- `progressed_checkpoint_mismatch`

## PASS rule

A case passes only when:

- all required peers launched,
- both/control channels responded,
- scenario assertions passed,
- snapshots compared,
- random-aware assertions passed for every same-player replicated outcome in scope,
- artifacts exist,
- cleanup succeeded or failures were recorded.
- cleanup status is recorded separately when the case emits `cleanup-report.json`.

## Progressed-state PASS rule

Progressed-state reconnect, disconnect, and Host Migration tests must first save a synchronized HumanBot checkpoint. Later assertions compare against that checkpoint where the state should be preserved. Randomness before the checkpoint is allowed. Random divergence after the checkpoint is a failure unless the state transition is explicitly documented and gated under `--mpTest`.

For same-token reconnect cases that need a spare Photon transport slot, use `--mpMaxPlayers 3 --mpDisableAiFill` so the room can accept the replacement client without creating an extra AI gameplay slot. Use `--mpFreezeGameFlow` only when the scenario's purpose is checkpoint preservation across disconnect/reconnect or migration; it must not be used to prove normal round progression.
