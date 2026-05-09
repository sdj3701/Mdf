## mp-disconnect-ai-takeover: Kill the client process and assert the durable slot

Status: active
Pinned: false
Category: AI/HumanBot
Created: 2026-05-05
Last used: 2026-05-05
Last verified: 2026-05-05
Use count: 1
Review after: 2026-08-03
Triggers: normal client disconnect, AI takeover, same `playerId` field preservation
Applies to: `NetworkManager.OnPlayerLeft`, `AIPlayerController`, `tools/harness/mp/run_disconnect_ai_takeover.py`
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Normal disconnect takeover is not proven by logs alone. The host snapshot must show the disconnected player's durable `playerId` still exists, input authority cleared, AI controller registered, field ready, and no duplicate `playerId`.

Recipe:
- Start host and client in `MatchingLobby` first, then have the host load `Game`.
- Record the client-local `playerId` and connection token hash from the pre-disconnect client snapshot.
- Terminate the client process with `kill`, not `/quit`.
- Poll the host snapshot until `runner.activePlayerCount == 1`, player count remains unchanged, target `isAI == true`, `isConnected == false`, `ai.controllerRegistered == true`, and field readiness is true.

Verification:
- `artifacts/mp/20260504-230459-disconnect-ai-takeover/disconnect-ai-takeover-assertions.json` showed target `playerId=1`, `activePlayerCount=1`, `playerCount=2`, `targetIsAI=true`, `targetConnected=false`, `targetFieldReady=true`, and `targetAiRegistered=true`.
- `mptest.timeline.jsonl` in the same artifact includes `[MPTEST] disconnect_cache result=pass` and `[MPTEST] disconnect_ai_takeover result=pass`.

Pitfalls:
- `NetworkManager._spawnedCharacters` may point to the lobby/network player object, not the Game `PlayerManager`. Disconnect takeover must find the runtime `PlayerManager` by `InputAuthority`, cache by connection token, clear input authority, and attach `AIPlayerController` without despawning the durable slot.
