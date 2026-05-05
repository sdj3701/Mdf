# MDF Randomized Progression Test Plan

## Purpose

Phase 18 resets the post-Phase-17 harness direction. The next goal is not exact command replay. The goal is to let real connected human peers progress the game with bot policy, then prove that randomized authoritative outcomes stay synchronized across host, client, Editor, build, reconnect, disconnect, and Host Migration paths.

## Strategy

Use HumanBotDriver first.

```text
real connected peer
  -> human player slot
  -> isAI=false
  -> test-only bot policy chooses actions from current observed state
  -> CommandProcessor.RequestCommandExecution
  -> PlayerManager.RPC_RequestCommandToServer for clients
  -> State Authority validates and broadcasts accepted commands
  -> snapshots compare replicated durable state
```

Do not use command-only replay as the progression engine. MDF has gameplay-critical randomness in shop rolls, permanent walls, augment presentation, AI maze planning, battle pairing, spawn choices, and some target choices. A command tape such as `reroll_shop -> buy slot 2` does not recreate the same state unless every random outcome before the buy is also controlled or journaled.

Command logs stay useful as diagnostics. They are not the Phase 18-25 primary method for building mid-game or late-game state.

## Random-Aware Assertions

Bad assertion:

```text
round 1 player 1 shop slot 2 must be Knight
initial wall hash must be abc123
augment choices must be A/B/C
```

Good assertion:

```text
player 1 shop itemsHash is equal on host and client
player 1 shop count is within the expected range
player 1 wallHash is equal on every peer
player 1 presentedAugmentsHash is equal on every peer when presented
all referenced unit and augment keys resolve to project data
field path readiness stays true when required by the scenario
```

Different players may have different random outcomes. The invariant is that the same `playerId` has the same replicated outcome on every observing peer.

## Authority Rules

- State Authority decides persistent random gameplay outcomes.
- Clients may request commands, but must not finalize shop, augment, wall, battle, reward, HP, gold, or placement state locally.
- RPCs are events or requests. Persistent state must be `[Networked]`, reconstructable from authoritative data, or represented in comparable snapshots.
- Randomness before Host Migration is allowed. Random divergence after Host Migration is not allowed.
- If normal game flow advances while a test is trying to compare migration state, add a test-only freeze or gate under `--mpTest`; do not weaken the comparison.

## Journals

The harness should collect separate journals so failures can be diagnosed without pretending that command replay is deterministic.

```text
bot decision journal
  what the bot saw, which persona/policy evaluated it, and which command it requested

accepted command journal
  what the server accepted, with command type, authoritative playerId, sequence/revision, and rejection reason when applicable

random outcome journal
  State Authority outcomes such as shop hashes, augment hashes, wall hashes, battle mapping hashes, and revisions

checkpoint snapshots
  peer snapshots before/after meaningful progression and before/after reconnect, disconnect, or migration events
```

Recommended JSONL fields:

```json
{
  "kind": "bot_decision",
  "seq": 34,
  "playerId": 1,
  "persona": "balanced",
  "gameState": "Prepare",
  "round": 2,
  "observed": {
    "gold": 7,
    "shopRevision": 5,
    "shopItemsHash": "sha256:...",
    "wallHash": "sha256:...",
    "presentedAugmentsHash": "sha256:..."
  },
  "decision": {
    "commandType": "BuyUnit",
    "reason": "best_affordable_score",
    "shopSlot": 2
  }
}
```

```json
{
  "kind": "random_outcome",
  "seq": 12,
  "source": "StateAuthority",
  "category": "shop",
  "playerId": 1,
  "round": 2,
  "revision": 6,
  "itemsHash": "sha256:...",
  "itemCount": 5
}
```

## Progressed-State Scenarios

Phase 20 proves 2-peer Prepare progression:

- Build host plus build client.
- The client stays a connected human peer.
- `--mpHumanBot` drives Prepare-phase choices from the current randomized state.
- At least one meaningful accepted command occurs.
- Host/client snapshots agree with random-aware rules.

Phase 21 proves 4-player progression smoke:

- One host plus three clients.
- All bot-driven slots remain human and `isAI=false`.
- Each player's replicated random outcomes are equal across every peer.

Phase 22 proves progressed reconnect and disconnect:

- First create a progressed checkpoint with HumanBot.
- Kill the client for disconnect-to-AI takeover.
- Reconnect with the same token for identity reclaim.
- Preserve progressed shop, wall, field, augment, HP, gold, and playerId state.

Phase 23 proves progressed Host Migration:

- Create a progressed checkpoint with randomized HumanBot state.
- Kill the host process.
- Require Fusion callback/token/resume plus MDF durable state preservation.
- Compare pre/post durable state with random-aware rules.

Phase 24 runs stochastic seed sweep:

- Treat seed as a diagnostic run label unless every random source is proven controlled.
- Collect seed-specific journals, snapshots, comparisons, and first failing checkpoint.

Phase 25 hardens random authority:

- Audit `UnityEngine.Random`, `System.Random`, and time/tick-derived randomness.
- Classify each random source.
- Harden only authoritative gameplay outcomes that need sync, snapshot, reconnect, or Host Migration coverage.

## Phase Plan

```text
Phase 18 - Random-aware harness doctrine/docs
Phase 19 - HumanBotDriver core
Phase 20 - 2-peer HumanBot prepare progression
Phase 21 - 4-player HumanBot progression smoke
Phase 22 - Progressed-state reconnect / disconnect
Phase 23 - Progressed-state Host Migration
Phase 24 - Random seed sweep / stochastic soak
Phase 25 - Random authority hardening
Final audit - random-aware MDF harness audit
```

Do not advance to the next phase unless the current phase has command output and artifacts proving PASS. If a required command cannot run locally, report `NEEDS_ENVIRONMENT` or `BLOCKED` and stop the phase.

## Future Work

Deterministic seed control, `NetworkRNG`, or a full random ledger may become useful later. They are optional future hardening, not the Phase 18 strategy. The near-term strategy is server-authoritative random outcomes plus snapshot equality across peers.
