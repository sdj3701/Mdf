# MDF Build-side Automation Server Contract

## Purpose

The build-side automation server lets Codex control and inspect a built player process. It is not a gameplay server and must never be available in production builds.

## Safety gates

Compile gate:

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
```

Runtime gates:

- `--mpTest` is required.
- `--mpAutomationPort` is required.
- `--mpAutomationToken` is required.
- Bind only to `127.0.0.1` or `localhost`.
- Reject every request missing the token.
- Do not log raw connection tokens, Photon AppId, auth tokens, or player secrets.
- Unity API calls must run on Unity main thread through `MPTestMainThreadDispatcher`.

## Transport

Preferred MVP: `HttpListener` or `TcpListener` with simple HTTP-like JSON endpoints. Choose the most stable option for Unity 2021 scripting runtime and document it in `learned-recipes.md`.

All responses are JSON:

```json
{
  "success": true,
  "message": "ok",
  "timestampUtc": "2026-05-04T10:00:00.000Z",
  "data": {}
}
```

On failure:

```json
{
  "success": false,
  "message": "scene timeout",
  "timestampUtc": "2026-05-04T10:00:00.000Z",
  "error": {
    "code": "scene_timeout",
    "details": "Game scene not loaded within 30s"
  }
}
```

## Authentication

Accept token via one of:

- `Authorization: Bearer <token>` header
- `X-MPTest-Token: <token>` header
- `?token=<token>` only as fallback for simple clients

Prefer headers. Do not print token values.

## Required endpoints

```text
GET  /ping
POST /quit
GET  /dumpState
POST /startHost
POST /join
POST /loadGame
POST /command
POST /assertState
GET  /screenshot
GET  /logs/recent
```

## MDF domain endpoints

Add only after MVP passes:

```text
POST /scenario/prepareSmoke
POST /scenario/battleSmoke
POST /scenario/startBattle
POST /scenario/placeWall
POST /scenario/rerollShop
POST /scenario/selectAugment
POST /scenario/spawnMonsterTest
POST /scenario/useMagicScrollTest
POST /scenario/hostMigrationProbe
POST /scenario/simulateDisconnect
POST /bot/start
POST /bot/stop
GET  /bot/status
GET  /bot/journal
```

Every domain endpoint must call the same authority-gated code path as real gameplay or be clearly marked `test_only_state_probe`.

`/bot/*` endpoints are test-only control and observation endpoints for `MPTestHumanBotDriver`.

- They must be compiled only for `UNITY_EDITOR || DEVELOPMENT_BUILD`.
- They must require `--mpTest`, loopback bind, and per-run token auth like every other automation endpoint.
- `/bot/start` must not attach `AIPlayerController` to a human player.
- `/bot/status` must report the bot as test harness state, not as gameplay AI ownership.
- `/bot/journal` must only expose bounded journal data from the current artifact path.

## `/dumpState`

Returns the state snapshot described in `state-snapshot-schema.md`.

## `/command`

Generic command envelope:

```json
{
  "name": "reroll_shop",
  "playerId": 0,
  "args": {},
  "timeoutSeconds": 10
}
```

The server must validate that the command is safe for the current scenario and test mode.
Currently supported:

- `reroll_shop`: must be issued to the server/host peer; validates `playerId`, runner/server state, `CommandProcessor`, shop DB readiness, and reroll cost before queuing `RerollShopCommand`.
- `select_king`: lobby-only; accepts `playerId` plus an allow-listed canonical `kingKey` such as `UnitData_King_Mage`. It must be sent to that slot's owning input-authority peer and calls `NetworkPlayer.RequestKingSelection`, preserving the production RPC validation path.
- `select_demon`: lobby-only and owner-private; accepts `playerId` plus an allow-listed `demonKey` such as `demon.blood_lord`. It must be sent to that slot's owning input-authority peer and calls `NetworkPlayer.RequestDemonSelection`. The selection is absent from replicated lobby state and becomes public through `PlayerManager.selectedDemonKeyHash` only after entering the game scene.
- `select_map_theme`: lobby-only cosmetic selection; accepts `playerId` plus `classic`, `arena`, or an allow-listed `map.theme.*` content id. It must be sent to that slot's owning input-authority peer and calls `NetworkPlayer.RequestMapThemeSelection`; different players may select different themes.
- `activate_king_skill`: may be issued by the owning input-authority peer or State Authority; validates the requested player, defense phase/role, loaded King data, and the once-per-defense allowance before queuing `ActivateKingSkillCommand` through the shared command path.
- `activate_demon_skill`: may be issued by the owning input-authority peer or State Authority; validates the requested player, attack phase/role, loaded Demon data, living owned summoned-monster targets, and the once-per-attack-sequence allowance before queuing `ActivateDemonSkillCommand` through the shared command path.
- `move_unit`: server-only Prepare command. Explicit `{from,to}` probes use the production Goal reservation; a regular unit targeting `field.goalCell` must return `unit_goal_cell_blocked` and leave `placedUnitsHash` unchanged.

`run_two_humanbot_two_ai_smoke.py --verify-king-goal-placement` adds three consecutive per-peer King anchor/scale/drift, camera-facing yaw, and recently-applied Animator IK head-look samples plus an explicit Goal-cell move rejection. Its graphical AI-field capture hides shop/augment overlays, re-dumps the viewed field state, and requires `kingHeadLookApplied=true` before preserving the screenshot. It writes `king-goal-placement-verification.json`, `king-goal-ai-field-screenshots.json`, and `goal-cell-rejection.json`.

Editor peer parity for lobby selection uses `unity-cli --project Mdfproject mp_command --command select_king --player_id <id> --king_key <UnitData_King_*>`; issue it to the Editor peer that owns the requested slot.
Demon-selection parity uses `unity-cli --project Mdfproject mp_command --command select_demon --player_id <id> --demon_key <demon.*>` on the owning Editor peer. Lobby snapshots and remote `NetworkPlayer` state must not expose the selection; game-scene snapshots compare `selectedDemonKeyHash` and the Demon runtime fields across peers.
Map-theme parity uses `unity-cli --project Mdfproject mp_command --command select_map_theme --player_id <id> --map_theme <classic|arena|map.theme.*>` on the owning Editor peer. `run_editor_host_build_client.py --editor-map-theme classic --build-map-theme arena` verifies each player's selected/applied ids and presentation readiness on both peers without requiring the two players' themes to match.

## `/screenshot`

MVP options:

1. Use Unity `ScreenCapture.CaptureScreenshot` in the build process.
2. Use external platform screenshot only from matrix script.
3. If unavailable, return `not_supported` and let matrix rely on logs/snapshots.

## `/logs/recent`

Return recent `[MPTEST]` lines and optionally user logs. Do not return unbounded logs.

## Cleanup

`/quit` should:

- send the HTTP response before shutdown work or schedule shutdown after response,
- stop accepting new automation commands,
- stop HumanBot if present,
- freeze test game flow,
- find the active `NetworkRunner` and call `Runner.Shutdown` with a timeout,
- log `[MPTEST]` phases `quit_requested`, `human_bot_stop_requested`, `runner_shutdown_begin`, `runner_shutdown_complete` or `runner_shutdown_timeout`, `automation_server_stop`, and `application_quit_called`,
- stop automation server after runner shutdown is attempted,
- quit application in build,
- stop play mode only in Editor-side tools.

Cleanup reports for player-launching Python harnesses should record graceful `/quit`, wait, Windows Job Object termination, process terminate/kill, and taskkill fallback in that order, with Job Object diagnostics when available.

## Production safety assertion

Add a static/precommit test that fails if automation server code lacks both compile gate and runtime gate. A non-development build must not open any automation port. Use `tools/harness/mp/run_production_negative_automation.py` to produce artifact proof that a normal build launched with `--mpTest --mpAutomationPort --mpAutomationToken` does not respond to `/ping`.
