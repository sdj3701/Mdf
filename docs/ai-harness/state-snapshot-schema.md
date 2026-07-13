# MDF State Snapshot Schema

## Rule

Snapshots must contain stable comparable game state, not raw Unity object dumps.

`connectionTokenHash` is the durable one-way identity carrier replicated by the
authoritative `PlayerManager`; raw connection tokens must never appear in snapshots.
When `destructibleWallCount > 0`, `destructibleWallHealthHash` is required and is
compared across peers and across host migration.

## JSON shape

```json
{
  "version": 1,
  "role": "host|client|editor-host|editor-client",
  "caseName": "game_smoke",
  "session": "mp-...",
  "scene": "03_Game",
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
    "battlePhase": "None|Battle1|Battle2",
    "currentRound": 1,
    "phaseTimerRemaining": 41.2,
    "firstAttackerPlayerId": 0,
    "battleOpponentsHash": "sha256:...",
    "matchFirstAttackerHash": "sha256:...",
    "battleActiveHash": "sha256:...",
    "survivorBossPendingHash": "sha256:...",
    "survivorBossAssignmentHash": "sha256:...",
    "survivorBossPendingCount": 0,
    "survivorBossAssignmentCount": 0
  },
  "players": [
    {
      "playerId": 0,
      "networkId": "123",
      "playerRef": "PlayerRef:1",
      "connectionTokenHash": "64 lowercase SHA-256 hex chars",
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
      "blackMagicCurrent": 10,
      "blackMagicMaximum": 10,
      "blackMagicMaxBonus": 0,
      "blackMagicRevision": 1,
      "blackMagicSequenceId": 5,
      "attackMonsterPoolHash": "sha256:...",
      "ownedScrollsHash": "sha256:...",
      "ownedScrollRevision": 0,
      "manualSkillReadyHash": "sha256:...",
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
        "selectedHash": "sha256:...",
        "activeEffectCount": 1,
        "activeTargetCount": 1,
        "activeEffectHash": "sha256:...",
        "activeTargetHash": "sha256:..."
      },
      "field": {
        "ready": true,
        "gridHash": "sha256:...",
        "placedUnitCount": 2,
        "placedUnitsHash": "sha256:...",
        "destructibleWallCount": 3,
        "destructibleWallHealthHash": "sha256:...",
        "permanentWallCount": 5,
        "wallHash": "sha256:...",
        "pathReady": true,
        "goalReady": true
      },
      "monsters": {
        "aliveCount": 0,
        "livingHash": "sha256:...",
        "typeHash": "sha256:...",
        "typeCountHpHash": "sha256:...",
        "ownerOriginHash": "sha256:...",
        "targetPlayerHash": "sha256:...",
        "hpBucketHash": "sha256:...",
        "bossPoolIdentityHash": "sha256:...",
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
  "effects": {
    "activeBuffCount": 0,
    "activeStatusCount": 0,
    "zoneCount": 0,
    "activeBuffHash": "sha256:...",
    "activeStatusHash": "sha256:...",
    "zoneHash": "sha256:..."
  },
  "commands": {
    "lastSequence": 3,
    "queueDepth": 0,
    "lastCommand": "RerollShop",
    "acceptedBattleCommandSeq": 0,
    "spawnMonsterSeq": 0,
    "useMagicScrollSeq": 0,
    "activateSkillSeq": 0,
    "rejectedBattleCommandCount": 0
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
- scene, compared through the harness scene alias map so legacy CLI aliases such as `Game` and canonical numbered names such as `03_Game` are equivalent
- `currentState`
- `battlePhase` (`None`, `Battle1`, or `Battle2`)
- `currentRound`
- battle opponent/match/active-role mapping hashes
- survivor boss pending/assignment counts and hashes when available; non-zero state on only one peer is a failure
- player ids and player count
- player HP/gold/wall counts
- player Black Magic current/maximum/personal maximum bonus/revision/attack-sequence identity
- shop snapshot hashes
- augment presented/selected counts, active effect/target counts, and active effect/target hashes when available
- field unit/wall aggregate hashes
- monster alive counts, legacy living hashes, semantic type hashes, owner/origin hashes, type/count/HP-bucket hashes, target/player hashes, HP bucket hashes, and boss/pool identity hashes after battle stabilization
- active buff/status/zone counts and semantic hashes after scroll or skill effects
- command sequence/last durable command, including accepted battle command sequence, monster spawn sequence, magic scroll use sequence, and rejected battle command count
- attack monster pool hash after authoritative spawn acceptance; each slot part includes its `blackMagicCost` and spend `mode` (`black-magic` or `boss-entitlement`)
- manual/strategic skill readiness hashes after defender skill state stabilizes
- For conditional Phase 10 required values such as battle hashes during `Battle1`/`Battle2`, survivor hashes with non-zero counts, and `commands.lastCommand` with advanced battle command counters, any `unknown`/missing value is a failure, including both peers missing the value. For optional absent state, such as no living boss monsters, both sides may remain `unknown`.

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
- `battleOpponentsHash` and `matchFirstAttackerHash` come from the state-authority battle map or its Networked read-only fallback, not from mutating lookup methods
- active battle flags are consistent
- `attackMonsterPoolHash` is deterministic for null, empty, and non-empty pools; empty-vs-nonempty pool drift must not be hidden behind `unknown`
- attacker `blackMagicCurrent`, `blackMagicMaximum`, `blackMagicMaxBonus`, `blackMagicRevision`, and `blackMagicSequenceId` are exact-equal across peers; migration must preserve them without refilling the active sequence
- `ownedScrollsHash` is deterministic for null, empty, and non-empty scroll inventories and includes the authoritative scroll revision
- `useMagicScrollSeq` increments only when State Authority applies a scroll's gameplay effects
- `activateSkillSeq` increments only when State Authority executes an accepted manual/strategic `ActivateSkillCommand`
- `manualSkillReadyHash` compares configured skill capability with semantic unit data, grid position, star level, and configured skill key. It intentionally excludes current mana, loaded `SkillData`, activation overrides, casting state, target counts, and other frame-local readiness values because peer snapshots are not captured on the same simulation frame.
- `effects.activeStatusHash`, `effects.activeBuffHash`, and `effects.zoneHash` compare active duration effects using semantic target keys. Unit targets include owner/data/star/grid; monster targets include owner/data/boss metadata/HP bucket/navigation or coarse position; wall targets include owner/grid/HP bucket. Do not fall back to raw Unity instance IDs or object names.
- Phase 6 scroll effect coverage snapshots active buff/status/zone state for peer comparison; durable Host Migration restoration of active effect timers remains a later battle migration blocker until proven by artifacts
- monster spawner readiness true for defenders
- `game.battleActiveHash` is equal across peers after a stable wait
- `players[].monsters.typeHash`, `ownerOriginHash`, `typeCountHpHash`, `hpBucketHash`, `targetPlayerHash`, and `bossPoolIdentityHash` are equal when living monsters exist
- monster snapshot collection uses owner-id fallback over live replicated monsters, so a missed one-shot init RPC must not hide a living monster
- survivor boss pending/assignment hashes are equal across comparable authority snapshots when pending or assigned survivor bosses exist

Host migration:

- migration callback/resume events exist in logs
- post-migration GameManagers exists on active runner
- player/field/wall/shop/AI state restored
- Black Magic current/maximum/bonus/revision/sequence id restored exactly; host migration never starts a new attack-sequence refill by itself
- no duplicate `playerId`
- stale `PlayerRef` not used as durable identity

HumanBot progression:

- bot-driven player remains a connected human and `isAI == false`
- `ai.controllerRegistered == false` for the HumanBot player
- `test.bot.commandsIssued > 0` after the progression target
- host/client snapshots agree on the same player's shop, augment, field, wall, unit, battle, monster pool, HP, gold, and command hashes
- no fixed random shop, wall, or augment value is required
