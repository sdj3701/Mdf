# MDF Long Progression Test Plan

## Purpose

Current MDF multiplayer tests prove launch, synchronization, command paths, battle entry, and lifecycle recovery from an early progressed checkpoint. They do not prove sustained multi-round progression. This plan records the current duration audit and defines the target long and endurance modes to add after the audit phase.

No runtime behavior is changed by this document.

## Current Duration Audit

### Game Phase Timers

`GameManagers` currently uses these serialized defaults:

| Field | Default | Meaning |
| --- | ---: | --- |
| `firstPreparePhaseTime` | 60 seconds | Round 1 Prepare duration. |
| `preparePhaseTime` | 45 seconds | Prepare duration for later rounds. |
| `combatTime` | 60 seconds | Each `Battle1` and `Battle2` duration. |

Nominal full-round wall-clock coverage, when battles do not finish early:

| Progress target | Timer-only duration |
| --- | ---: |
| Round 1 Prepare -> Round 2 Prepare | 180 seconds |
| Later Prepare -> next Prepare | 165 seconds |
| Round 1 Prepare -> Round 4 Prepare, i.e. 3 full rounds complete | 510 seconds |

These figures exclude scene load, lobby join, snapshot stabilization, battle precheck retry, cleanup, and process launch time. A 3-round E2E therefore needs a much larger script timeout than current smoke/progression defaults.

### HumanBot Runtime Defaults

`MPTestCommandLine` defaults the raw runtime bot limits to zero:

| Runtime arg | Raw default | Meaning |
| --- | ---: | --- |
| `--mpBotDurationSeconds` | 0 | No duration stop unless a harness script passes a value. |
| `--mpBotStopAtRound` | 0 | No round stop unless a harness script passes a value. |
| `--mpBotMaxCommands` | 0 | No command-count stop unless a harness script passes a value. |

`MPTestHumanBotDriver` stops in this order:

1. `duration_reached` when `_durationSeconds > 0` and elapsed realtime reaches it.
2. `max_commands_reached` when `_maxCommands > 0` and issued commands reach it.
3. `stop_round_reached` when `_stopAtRound > 0`, `currentRound >= _stopAtRound`, and at least one command was issued.

The driver evaluates decisions every 0.5 seconds while running.

### Script Defaults and Stop Conditions

| Script or common helper | Bot duration | Bot stop round | Bot max commands | Min commands | Script timeout defaults | What it actually covers |
| --- | ---: | ---: | ---: | ---: | --- | --- |
| `run_human_bot_prepare_progression.py` | 60s | 2 | 1 | 1 | ping 45s, start 45s, lobby 60s, state 90s, bot 120s, request 15s, cleanup 15s | First Prepare only. The test freezes game flow before starting the bot and passes after one meaningful durable delta. |
| `run_human_bot_4p_progression.py` | 90s | 2 | 1 per bot | 3 total | ping 45s, start 45s, lobby 90s, state 120s, bot 150s, request 20s | 4-player prepare progression smoke. It can pass in round 1 Prepare after one command per bot; it does not require battle or round 2. |
| `battle_progression_common.py` used by `run_human_bot_battle_progression.py` | 180s | forced to 0 | 120 | 1 | ping 45s, start 45s, lobby 60s, state 90s, battle 240s, request 20s, migration 120s, takeover 90s, reconnect 120s, cleanup 15s | Early battle progression. It waits for Battle1/Battle2 or accepted battle command evidence, usually in round 1. It does not require a completed round. |
| `run_battle_seed_sweep.py` | inherited by child battle case | inherited by child battle case | inherited by child battle case | inherited by child battle case | `case-timeout` 360s per seed, `seed-count` 3 unless explicit `--seeds`, cleanup 15s | Longer by repetition across seeds, not by deeper round coverage. Each child remains an early battle case. |
| `run_human_bot_seed_sweep.py` | inherited prepare default | inherited prepare default | prepare child default overridden to 1 | prepare child default overridden to 1 | prepare child timeout 240s, optional 4p timeout 360s, `seed-count` 3 unless explicit `--seeds` | Longer by seed count. Prepare children remain first-Prepare progression. |
| Progressed prepare lifecycle scripts (`run_progressed_same_token_reconnect.py`, `run_progressed_disconnect_ai_takeover.py`) | 60s | 2 | 1 | 1 | ping 45s, start/lobby/state 60/60/90s, bot 120s, lifecycle 90-120s | A frozen early progressed prepare checkpoint, then reconnect/disconnect recovery. |
| `run_progressed_host_migration_e2e.py` | 90s | 2 | 2 | 2 | ping 45s, start/lobby/state 60/60/90s, bot 150s, migration 120s | A frozen early progressed prepare checkpoint with at least two commands, then Host Migration recovery. |
| Progressed after-battle lifecycle wrappers | 180s | forced to 0 | 120 | 1 | battle 240s, lifecycle 90-120s | Early battle checkpoint plus lifecycle recovery; not long-round progression. |

## Why Current MP Tests Feel Short

Current HumanBot prepare tests are intentionally smoke/progression tests. They are built to prove that a real connected human peer can request at least one meaningful command through the normal command path and that replicated same-player random outcomes match. They freeze first Prepare so lifecycle tests can compare a stable checkpoint.

Current battle tests are battle-entry and command-path tests. They pass when peers reach battle state or when accepted battle command evidence is observed. They are not designed to let the game naturally cycle through several Prepare/Battle1/Battle2 rounds.

Current seed sweeps can take longer in total wall-clock time, but the extra time comes from repeating short cases across seeds. They do not extend a single match deeper into the game.

## Test Categories

| Category | Purpose | Current examples | Round-depth expectation |
| --- | --- | --- | --- |
| Smoke | Cheap launch/session/scene/player-count checks. | `editor-host-build-client`, `build-host-editor-client`, `build-host-build-client`, `four-player-smoke`, `ai-fill-smoke` | None beyond initial Game/Prepare readiness. |
| Progression | Prove HumanBot can mutate durable randomized state through real request paths. | `human-bot-prepare`, `human-bot-4p-progression`, `human-bot-seed-sweep` | Usually round 1 Prepare only. |
| Battle | Prove battle entry, battle command validation, and battle state snapshot invariants. | `human-bot-battle-progression`, `battle-spawn-monster-command`, `magic-scroll-command`, `battle-seed-sweep` | Early Battle1/Battle2, usually round 1. |
| Lifecycle | Prove reconnect, disconnect/AI takeover, or Host Migration around a synchronized checkpoint. | `progressed-reconnect-after-battle`, `progressed-disconnect-after-battle`, `progressed-host-migration-after-battle`, existing prepare-progressed lifecycle scripts | Early progressed checkpoint, then recovery. |
| Long | Prove bounded multi-round normal progression with checkpoints across repeated Prepare/Battle states. | Planned `human-bot-3round-progression` | Target 3 full rounds by default. |
| Endurance | Prove GameOver or classify timeout/stall with progress evidence. | Planned `human-bot-game-to-end` | Until GameOver or bounded timeout classification. |

## Recommended Target Modes

### `human-bot-3round-progression`

Target:

- Headless build players by default.
- HumanBot enabled on relevant peers.
- `--mpBotStopAtRound 0`.
- Bot duration and command limits high enough to survive the target.
- Default `--target-round 3`.
- Default `--completion-mode round-complete`.
- Default `--max-duration-seconds 900`.

PASS should require either:

- `currentRound >= targetRound + 1` and `currentState == Prepare`, or
- early `GameOver` only when explicitly allowed.

It must capture and compare checkpoints at every Prepare, every Battle1, every Battle2, and final state.

### `human-bot-game-to-end`

Target:

- Headless build players by default.
- HumanBot enabled on all stable relevant peers.
- `--mpBotStopAtRound 0`.
- Default `--max-duration-seconds 2400`.
- Default `--max-rounds 20`.
- Default `--max-commands-per-bot 1000`.

PASS should require actual `GameOver`, agreed final snapshots, recorded winner/loser/final ranking or equivalent game-over state, no duplicate `playerId`, no command divergence, no `[MPTEST] phase=error`, and cleanup PASS.

If the bounded limits are reached before GameOver, classify the result as `TIMEOUT`, `NEEDS_TUNING`, or `STALLED`; do not mark it PASS.

### Long Lifecycle Insertions

Target:

- First prove `human-bot-3round-progression` is stable.
- Then insert reconnect, disconnect/AI takeover, or Host Migration after a target round checkpoint.
- Compare pre-event and post-event snapshots for durable player identity, field/shop/battle/monster/scroll/effect state, and lifecycle-specific recovery proof.
- Implemented entry points:
  - `tools/harness/mp/run_3round_reconnect.py`
  - `tools/harness/mp/run_3round_disconnect_ai_takeover.py`
  - `tools/harness/mp/run_3round_host_migration.py`

## Matrix Profile Direction

Planned profile structure:

- `long`: `human-bot-3round-progression`.
- `endurance`: `human-bot-game-to-end` (explicit opt-in; reports `PASS` only on actual `GameOver`, otherwise `NEEDS_TUNING`, `TIMEOUT`, or `STALLED`).
- `long-lifecycle`: `3round-reconnect`, `3round-disconnect-ai-takeover`, `3round-host-migration`.
- `full-regression`: `regression + battle + lifecycle + long`.
- `nightly`: currently excludes endurance; run `--profile endurance` explicitly unless endurance is later accepted into nightly.

The `smoke` profile must not include long or endurance cases.
