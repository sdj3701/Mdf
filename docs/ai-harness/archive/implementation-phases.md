# MDF Harness Implementation Phases

Status note, 2026-05-08: this file is historical for the original MVP overlay phases. Current work should use `../content-development-routine.md`, `../verification-profile-selector.md`, `../feature-implementation-loop.md`, `../mp-test-protocol.md`, `../state-snapshot-schema.md`, and `../learned-recipes.md`.

## Phase 0 — Baseline

- Inspect Unity version, packages, Fusion build info, scenes, tests.
- Run unity-cli status/list if Editor is open.
- Record compatibility and blockers.
- Do not modify gameplay code.

## Phase 1 — Docs and instruction files

- Update `AGENTS.md` under 70 lines.
- Update `.agent/rules/projectrull.md` into clean project rules.
- Add `docs/ai-harness/*`.
- Keep SimpleRTS-specific rules out.

## Phase 2 — Mechanical enforcement

Implement `tools/harness/precommit.py` and Codex hooks.

Block:

- `git commit --no-verify` / `git commit -n`
- destructive repo commands unless explicitly approved
- vendor edits under `Mdfproject/Assets/Photon/Fusion/**`
- UnityEditor references outside Editor folders or `#if UNITY_EDITOR`
- automation server without compile/runtime gates
- automation server external bind or no token

Warn:

- client-trusted `playerId`, gold, HP, wall, augment, shop, spawn data
- `RpcSources.All` without validation
- persistent state only in RPC side effect
- new command without CommandType/serialize/deserialize
- Host Migration change touching only one side of recovery path

## Phase 3 — Skills and knowledge capture

Add `.agents/skills`:

- `use-learned-recipes`
- `capture-learning`
- `verify-unity`
- `review-fusion-sync`
- `mp-harness-test`
- `build-player`
- `asset-safe-edit`
- `entropy-gc`

Add `.codex` hooks for reminders and stop gates.

## Phase 4 — unity-cli custom tools

Implement Editor-only tools:

- `mp_start_host`
- `mp_join_client`
- `mp_load_game`
- `mp_dump_state`
- `mp_assert_state`
- `mp_command`
- `mp_screenshot`
- `mp_stop`
- `mp_build_player`

Verify `unity-cli --project Mdfproject list` shows the tools.

## Phase 5 — Runtime MP bootstrap

Implement:

- command-line parser
- `[MPTEST]` logger
- test bootstrap
- artifact directory setup
- deterministic session/token handling
- `Application.runInBackground = true` in test mode

## Phase 6 — State snapshot and assertions

Implement snapshot writer and comparer for GameManagers, players, fields, monsters, shop, battle mapping, AI, and host migration.

## Phase 7 — Build-side automation server

Implement loopback/token-gated test server.

Endpoints:

- `/ping`
- `/quit`
- `/dumpState`
- `/startHost`
- `/join`
- `/loadGame`
- `/command`
- `/assertState`
- `/screenshot`
- `/logs/recent`

## Phase 8 — Tests

Add EditMode tests for parsers, snapshot compare, safety guards, and precommit rules.

Add PlayMode smoke tests for bootstrap/snapshot where possible.

## Phase 9 — E2E MVP matrix

Implement scripts under `tools/harness/mp`:

- `run_matrix.py`
- `run_editor_host_build_client.py`
- `run_build_host_editor_client.py`
- `run_build_host_build_client.py`
- `launch_player.py`
- `build_client.py`
- `compare_state_snapshots.py`
- `collect_artifacts.py`
- `parse_mptest_logs.py`

## Phase 10 — MDF regression hardening

Turn real MDF risks into assertions:

- GameManagers duplicate/missing after scene load or migration.
- playerId duplicates or stale PlayerRef.
- shop/augment sync mismatch.
- wall/permanent wall desync.
- unit placement/register mismatch.
- battle opponent mapping mismatch.
- monster spawn/auto-spawn mismatch.
- AI fill/takeover mismatch.

## Phase 11 — 4-player and AI fill

Add 1 host + 3 client matrix and fewer humans + AI slots.

## Phase 12 — Disconnect/reconnect

Terminate client process, assert AI takeover if supported, then reconnect with same token and assert same `playerId` reclaim.

## Phase 13 — Host Migration feasibility and E2E

First prove feasibility. Then implement PASS only if official Fusion host migration callback/resume path is observed with artifacts.

## Phase 14 — Soak

Repeat durable commands such as reroll/wall placement/unit purchase under deterministic conditions. Do not call ping-only soak a gameplay sync pass.
