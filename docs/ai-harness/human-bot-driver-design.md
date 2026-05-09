# MDF HumanBotDriver Design

## Purpose

`MPTestHumanBotDriver` is a test-only driver that makes a real connected human peer play through the normal client command request path using AI-like policy. It exists to progress MDF into randomized mid-game and late-game states without pretending that command-only replay can reproduce the same game.

## Non-Negotiable Rules

- Active only when `--mpTest` and `--mpHumanBot` are both present.
- Runs only on a local player with Input Authority.
- The driven player remains human: `isAI=false`.
- Do not add or attach `AIPlayerController` to the human peer.
- Do not register the human bot in `ComponentRegistry` as `AIPlayerController`.
- Use `CommandProcessor.RequestCommandExecution` so client peers route through `PlayerManager.RPC_RequestCommandToServer`.
- Keep State Authority validation active.
- Never introduce production behavior changes.

The existing snapshot detects AI through:

```text
ComponentRegistry.Has<AIPlayerController>(playerId.ToString())
```

That is why HumanBot must be separate from `AIPlayerController`.

## Proposed Files

```text
Mdfproject/Assets/Scripts/Testing/MP/
  MPTestHumanBotDriver.cs
  MPTestHumanBotPolicy.cs
  MPTestBotPersona.cs
  MPTestBotJournal.cs
```

All runtime behavior must be guarded by `UNITY_EDITOR || DEVELOPMENT_BUILD` where appropriate and by runtime `--mpTest`.

Status note, 2026-05-08:

- `MPTestHumanBotDriver` now uses shared `PrepareDecisionPolicy` and `BattleDecisionPolicy` directly.
- `MPTestHumanBotPolicy` is an obsolete adapter for legacy test callers only. Do not add new behavior there.
- HumanBot command emission should go through `HumanClientCommandEmitter`, which submits prepare commands through `CommandProcessor.RequestCommandExecution` and battle commands through the validated request/executor path.

## Command-Line Arguments

```text
--mpHumanBot
--mpBotPersona balanced|maze|shop|unit|passive
--mpBotSeed <int>
--mpBotDurationSeconds <seconds>
--mpBotStopAtRound <round>
--mpBotMaxCommands <n>
--mpBotRecordJournal <path>
```

`--mpBotSeed` controls bot policy tie-breaks and pacing only unless a later phase explicitly controls gameplay RNG. It must not be documented as deterministic replay proof.

## Policy Shape

HumanBot observes current replicated state and chooses a bounded action.

Prepare-phase priority:

1. Select an augment if presented.
2. Buy the best affordable shop unit from the current randomized shop, especially before maze/wall focus when the field lacks a minimum army core.
3. Rearrange units when a legal move is available.
4. Build or extend walls only after the army core and soft composition target are satisfied.
5. Reroll only as a late shop action when at least three shop slots are sold and no high-value affordable purchase remains.
6. Otherwise wait.

Combat can initially be observe-only. Do not fake battle commands if the current player request path is not authority-safe.

## Real Command Path

The current prepare command path is:

```text
MPTestHumanBotDriver -> PrepareDecisionPolicy -> HumanClientCommandEmitter
  -> CommandProcessor.RequestCommandExecution(ICommand)
  -> if State Authority: GameManagers.RPC_BroadcastCommandToClients
  -> if client: local PlayerManager.RPC_RequestCommandToServer
  -> PlayerManager validates phase, cost, ownership, grid, shop, augment, and RPC source
  -> GameManagers.RPC_BroadcastCommandToClients
  -> CommandProcessor.ReceiveAndEnqueueCommand
  -> CommandProcessor.ProcessCommands
```

Battle decisions use `BattleDecisionPolicy` and `HumanClientCommandEmitter`, then route to `RPC_RequestBattleSpawnMonster` / `RPC_RequestUseMagicScrollCommand` or the host-side executor as appropriate.

Known Phase 19 command safety notes:

- `BuyUnit`, `MoveUnit`, `SwapUnit`, `SellUnit`, `PlaceWall`, `RemoveWall`, `RerollShop`, `SelectAugment`, `ActivateSkill`, and `RequestSyncData` have server validation paths.
- `PlaceUnit` is currently rejected from clients as `place_unit_requires_authoritative_inventory`.
- Start with commands already used by AI behavior actions: select augment, place wall, buy unit, move/rearrange, and reroll.

## Cadence and Stop Conditions

The driver must have hard stop conditions:

- maximum command count,
- maximum duration,
- stop at or after configured round,
- cooldown between decisions; the current shared default is at least 0.7 seconds between HumanBot command decisions,
- one in-flight decision at a time,
- no retry loop that ignores identical rejection reasons.

Every decision should emit a `[MPTEST]` line and optionally a JSONL journal record.

## Personas

```text
balanced - tries augment, maze, buy, reroll, rearrange in balanced order
maze     - prioritizes path/maze wall construction
shop     - prioritizes buy/reroll decisions
unit     - prioritizes buy and unit movement when legal
passive  - observes and records state with minimal commands
```

The persona changes policy weighting. It must not bypass server authority.

## Automation Server Endpoints

Phase 19 should add:

```text
POST /bot/start
POST /bot/stop
GET  /bot/status
GET  /bot/journal
```

Endpoint rules:

- Same compile, runtime, loopback, and token gates as the existing automation server.
- `/bot/start` only succeeds under `--mpTest`.
- `/bot/status` reports test-only state, not gameplay authority.
- Journal paths must stay under the configured artifact directory unless explicitly provided by the harness.

## Snapshot Additions

Add a test-only section:

```json
{
  "test": {
    "bot": {
      "enabled": true,
      "running": true,
      "persona": "balanced",
      "commandsIssued": 3,
      "lastDecision": "buy_best_affordable",
      "lastCommandType": "BuyUnit",
      "lastError": null,
      "journalPath": "artifacts/mp/.../client-1-bot.jsonl"
    }
  }
}
```

Do not encode HumanBot status as `player.ai.controllerRegistered`; that field is reserved for real AI takeover/fill status.

## PASS Boundary

Phase 19 PASS proves the driver core compiles, is test-gated, reports status, and can request commands through the normal path. It does not prove multiplayer progression. Phase 20 is the first gameplay progression PASS gate.
