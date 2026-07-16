## mp-same-token-reconnect: Verify target identity, keep full-world drift visible

Status: active
Pinned: true
Category: reconnect, security
Created: 2026-05-05
Last used: 2026-07-12
Last verified: 2026-07-12
Use count: 6
Review after: 2026-08-03
Triggers: same `--mpConnectionToken`, reconnect identity, PlayerRef changes
Applies to: `NetworkManager.TryReassociateDisconnectedPlayer`, `tools/harness/mp/run_same_token_reconnect.py`
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/mp/20260712-113414-progressed-same-token-reconnect; artifacts/mp/20260712-115502-progressed-same-token-reconnect
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Same-token reconnect must prove durable identity, not raw `PlayerRef` stability. In a full 2-slot Photon session the immediate replacement client can be rejected before game-code reassociation, so the harness uses `--mpMaxPlayers 3` to leave a transport slot while asserting the same target `playerId`.

Recipe:
- Start host and client A in `MatchingLobby`, then host loads `Game`.
- Record client A's local `playerId` and connection token hash.
- Kill client A, wait for disconnect AI takeover of that same `playerId`.
- Launch client B with the same `--mpConnectionToken` and join the existing `Game` session.
- PASS criteria are target-specific: host target exists, host target `isAI == false`, host target `isConnected == true`, client B local `playerId` equals the recorded target, client token hash matches, playerIds are unique, and target host/client comparison succeeds.

Verification:
- `artifacts/mp/20260504-231146-same-token-reconnect/same-token-reconnect-assertions.json` showed target `playerId=1`, `clientLocalPlayerId=1`, matching token hash `6163355D`, `hostTargetIsAI=false`, `hostTargetConnected=true`, and `targetComparison.success=true`.
- `mptest.timeline.jsonl` includes `[MPTEST] same_token_reconnect result=pass joinedPlayerRef=[Player:3] playerId=1`.
- `artifacts/mp/20260505-113304-same-token-reconnect/full-comparison.json` reported `success=true` with no errors after strict full-world reconnect assertions were enabled.

Pitfalls:
- Older `full-comparison.json` artifacts exposed non-target late-join wall drift. Do not use Phase 13 target identity PASS as proof that arbitrary late join fully reconstructs the whole match world.
- Phase 13B requires `fullComparison.success=true`. Use `--target-only` only when intentionally rechecking identity reclaim separately from full-world sync.
- When applying authoritative permanent-wall cells to late-join clients, destructible wall-map rebuilds must not classify objects in authoritative permanent cells as destructible walls.

Lifecycle notes:
- 2026-07-12: verified artifact `artifacts/mp/20260712-113414-progressed-same-token-reconnect`
- 2026-07-12: verified artifact `artifacts/mp/20260712-115502-progressed-same-token-reconnect`
