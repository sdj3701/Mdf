## mp-human-bot-4p-progression: Three client bots prove 4-player sync

Status: active
Pinned: false
Category: AI/HumanBot
Created: 2026-05-06
Last used: 2026-05-06
Last verified: 2026-05-06
Use count: 1
Review after: 2026-08-04
Triggers: 4-player HumanBot, build host + 3 build clients, random-aware host-vs-client comparison
Applies to: `tools/harness/mp/run_human_bot_4p_progression.py`, Phase 21 HumanBot progression smoke
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
4-player HumanBot progression must prove all slots remain human and that each same-player randomized outcome is equal across every observing peer. It should not use host-side bot commands as the primary proof because host commands bypass client request validation.

Recipe:
- Reuse the real room flow from `run_four_player_smoke.py`: launch host and all three clients into `MatchingLobby`, wait for `runner.activePlayerCount == 4` on every peer, then host-load `Game`.
- Enable `--mpHumanBot` only on the three build clients, with distinct personas such as `balanced`, `maze`, and `shop`.
- Pause client bots before Game load, capture a synchronized `before-bot` checkpoint, then start all bots with bounded `maxCommands=1`.
- Require each client bot to issue at least one command where possible, and prove acceptance with durable host snapshot deltas for that bot's `playerId`.
- Compare host against every client after progression. Keep same-player shop, augment, field wall/unit, battle, HP, gold, and known command fields strict.
- Assert every `PlayerManager` remains `isConnected=true`, `isAI=false`, and `ai.controllerRegistered=false` on every peer.

Verification:
- `artifacts/mp/20260505-172826-human-bot-4p-progression/human-bot-4p-assertions.json` reported `success=true`, `totalBotCommands=3`, and one `SelectAugment` durable delta for each client bot.
- `random-outcome-summary.json` showed matching same-player shop, augment, and field hashes from host to `client-1`, `client-2`, and `client-3`.
- `bot-comparison-host-vs-client-1-latest.json`, `bot-comparison-host-vs-client-2-latest.json`, and `bot-comparison-host-vs-client-3-latest.json` all reported `success=true`.

Pitfalls:
- By round 1, the final checkpoint can naturally be `Battle1`; this is acceptable only if all peer game state/round/battle hashes match. Do not weaken mismatch checks.
- If later phases require all four slots to issue bot commands, add a host bot deliberately and document that host-side `RequestCommandExecution` uses the State Authority path.
