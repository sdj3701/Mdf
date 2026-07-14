## mp-four-player-real-room-flow: 4-player smoke must join before Game load

Status: active
Pinned: false
Category: harness
Created: 2026-05-05
Last used: 2026-06-03
Last verified: 2026-06-03
Use count: 3
Review after: 2026-08-03
Triggers: 4-player smoke, build host + 3 build clients, field wall snapshots
Applies to: `tools/harness/mp/run_four_player_smoke.py`
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/mp/20260521-045119-matrix/20260521-045122-four-player-smoke; artifacts/mp/20260603-042245-two-humanbot-two-ai-smoke/result.json
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
The 4-player smoke must not let the host enter `Game` before the three clients join. Otherwise missing human slots become AI slots and wall hashes diverge.

Evidence:
- `artifacts/mp/20260504-205755-four-player-smoke/failure-summary.md` recorded `four_player_state_timeout` and snapshot mismatches for all three clients.
- `comparison-host-vs-client-1.json`, `comparison-host-vs-client-2.json`, and `comparison-host-vs-client-3.json` all failed on `player.0.field.wallHash`.

Recipe:
- Launch all four peers without auto-start/load-game.
- Start the host and three clients into `MatchingLobby`, wait for `runner.activePlayerCount == 4` on every peer, then have the host load `Game`.
- Compare host snapshots against each client and keep `wallHash` strict.

Verification:
- `artifacts/mp/20260504-222127-four-player-smoke/comparison-host-vs-client-1.json` passed.
- `artifacts/mp/20260504-222127-four-player-smoke/comparison-host-vs-client-2.json` passed.
- `artifacts/mp/20260504-222127-four-player-smoke/comparison-host-vs-client-3.json` passed.

Lifecycle notes:
- 2026-05-21: Verified four-player smoke visual run after ranking UI HP max display fix; cleanupStatus=PASS orphanedPids=[].
- 2026-05-21: verified artifact `artifacts/mp/20260521-045119-matrix/20260521-045122-four-player-smoke`
- 2026-06-03: Verified four-player room flow with two connected human bot peers and two AI fills.
- 2026-06-03: verified artifact `artifacts/mp/20260603-042245-two-humanbot-two-ai-smoke/result.json`
