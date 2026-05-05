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
```

Every domain endpoint must call the same authority-gated code path as real gameplay or be clearly marked `test_only_state_probe`.

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

## `/screenshot`

MVP options:

1. Use Unity `ScreenCapture.CaptureScreenshot` in the build process.
2. Use external platform screenshot only from matrix script.
3. If unavailable, return `not_supported` and let matrix rely on logs/snapshots.

## `/logs/recent`

Return recent `[MPTEST]` lines and optionally user logs. Do not return unbounded logs.

## Cleanup

`/quit` should:

- write a final `[MPTEST]` line,
- flush artifact buffers,
- stop automation server,
- quit application in build,
- stop play mode only in Editor-side tools.

## Production safety assertion

Add a static/precommit test that fails if automation server code lacks both compile gate and runtime gate. A non-development build must not open any automation port. Use `tools/harness/mp/run_production_negative_automation.py` to produce artifact proof that a normal build launched with `--mpTest --mpAutomationPort --mpAutomationToken` does not respond to `/ping`.
