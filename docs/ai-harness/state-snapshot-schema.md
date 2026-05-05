# MDF State Snapshot Schema

## Rule

Snapshots must contain stable comparable game state, not raw Unity object dumps.

## JSON shape

```json
{
  "version": 1,
  "role": "host|client|editor-host|editor-client",
  "caseName": "game_smoke",
  "session": "mp-...",
  "scene": "Game",
  "timestampUtc": "2026-05-04T10:00:00.000Z",
  "runner": {
    "isRunning": true,
    "gameMode": "Host",
    "isServer": true,
    "isClient": true,
    "tick": 12345,
    "activePlayerCount": 2,
    "maxPlayers": 4,
    "localPlayerRef": "PlayerRef:1"
  },
  "game": {
    "hasGameManagers": true,
    "currentState": "Prepare",
    "currentRound": 1,
    "phaseTimerRemaining": 41.2,
    "firstAttackerPlayerId": 0,
    "battleOpponentsHash": "sha256:...",
    "matchFirstAttackerHash": "sha256:..."
  },
  "players": [
    {
      "playerId": 0,
      "networkId": "123",
      "playerRef": "PlayerRef:1",
      "connectionTokenHash": "sha256:...",
      "hasInputAuthority": true,
      "hasStateAuthority": true,
      "isLocal": true,
      "isAI": false,
      "isConnected": true,
      "health": 100,
      "gold": 5,
      "wallCount": 5,
      "isActivelyFighting": false,
      "isAttackerInCurrentBattle": false,
      "shop": {
        "revision": 1,
        "round": 1,
        "count": 5,
        "itemsHash": "sha256:..."
      },
      "augment": {
        "available": true,
        "selectedCount": 0,
        "presentedCount": 3,
        "presentedHash": "sha256:...",
        "selectedHash": "sha256:..."
      },
      "field": {
        "ready": true,
        "gridHash": "sha256:...",
        "placedUnitCount": 2,
        "placedUnitsHash": "sha256:...",
        "destructibleWallCount": 3,
        "permanentWallCount": 5,
        "wallHash": "sha256:...",
        "pathReady": true,
        "goalReady": true
      },
      "monsters": {
        "aliveCount": 0,
        "livingHash": "sha256:...",
        "autoSpawnRunning": false
      },
      "ai": {
        "controllerRegistered": false,
        "prepareReady": true,
        "combatReady": true
      }
    }
  ],
  "objects": {
    "networkObjectCount": 12,
    "playerManagerCount": 2,
    "unitCount": 4,
    "monsterCount": 0,
    "wallCount": 10
  },
  "commands": {
    "lastSequence": 3,
    "queueDepth": 0,
    "lastCommand": "RerollShop"
  },
  "hostMigration": {
    "handlerExists": true,
    "isMigrating": false,
    "recoverySucceeded": false,
    "aiTakeoverReady": true,
    "lastEvent": "none",
    "eventCount": 0,
    "onHostMigrationCount": 0,
    "nonNullTokenCount": 0,
    "resumeCount": 0,
    "startGameSuccessCount": 0,
    "completeCount": 0,
    "failureCount": 0
  },
  "test": {
    "bot": {
      "enabled": false,
      "running": false,
      "persona": "none",
      "commandsIssued": 0,
      "lastDecision": null,
      "lastCommandType": null,
      "lastError": null,
      "journalPath": null
    },
    "randomOutcomes": {
      "journalPath": null,
      "lastCategory": null,
      "lastPlayerId": null,
      "lastHash": null,
      "lastRevision": null
    }
  },
  "errors": []
}
```

## Comparable fields

Exact or hash-equal after stable wait:

- session
- scene
- `currentState`
- `currentRound`
- battle opponent/match mapping hashes
- player ids and player count
- player HP/gold/wall counts
- shop snapshot hashes
- augment presented/selected hashes when available
- field unit/wall aggregate hashes
- monster alive counts/hashes after battle stabilization
- command sequence/last durable command

Allowed differences:

- tick delta
- timer small delta
- local player ref
- peer role fields such as `isServer`
- camera/UI-only state
- object list order
- visual transform epsilon when interpolation is expected

Never require equal:

- raw `PlayerRef` after reconnect/migration
- raw Unity instance IDs
- transient VFX/projectile objects unless the scenario specifically tests them
- fixed random shop item names, augment names, wall coordinates, or battle pairings across different runs

Random-aware rule:

- The same `playerId` must have the same replicated random outcome hash on every peer.
- Different `playerId` values do not need identical shop, augment, wall, or battle outcomes.
- A seed is a diagnostic label unless the specific random source is proven to be controlled.

## Required assertions

MVP:

- expected player count
- GameManagers exists
- every `playerId` unique and >= 0
- every player has field/runtime readiness
- snapshots from peers have same durable player/field/game state

Prepare smoke:

- `currentState == Prepare`
- shop count is 5 or expected project value
- HP/gold/wall count valid
- selected augment state is either pending or synced consistently

Battle smoke:

- `currentState` is `Battle1` or `Battle2`
- attacker/defender mapping exists
- active battle flags are consistent
- monster spawner readiness true for defenders

Host migration:

- migration callback/resume events exist in logs
- post-migration GameManagers exists on active runner
- player/field/wall/shop/AI state restored
- no duplicate `playerId`
- stale `PlayerRef` not used as durable identity

HumanBot progression:

- bot-driven player remains a connected human and `isAI == false`
- `ai.controllerRegistered == false` for the HumanBot player
- `test.bot.commandsIssued > 0` after the progression target
- host/client snapshots agree on the same player's shop, augment, field, wall, unit, battle, HP, gold, and command hashes
- no fixed random shop, wall, or augment value is required
