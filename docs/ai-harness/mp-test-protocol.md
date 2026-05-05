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
  failure-summary.md
```

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

## Progressed-state PASS rule

Progressed-state reconnect, disconnect, and Host Migration tests must first save a synchronized HumanBot checkpoint. Later assertions compare against that checkpoint where the state should be preserved. Randomness before the checkpoint is allowed. Random divergence after the checkpoint is a failure unless the state transition is explicitly documented and gated under `--mpTest`.

For same-token reconnect cases that need a spare Photon transport slot, use `--mpMaxPlayers 3 --mpDisableAiFill` so the room can accept the replacement client without creating an extra AI gameplay slot. Use `--mpFreezeGameFlow` only when the scenario's purpose is checkpoint preservation across disconnect/reconnect or migration; it must not be used to prove normal round progression.
