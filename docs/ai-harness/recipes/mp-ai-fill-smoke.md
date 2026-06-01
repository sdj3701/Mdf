## mp-ai-fill-smoke: Max-player AI slots are supported

Status: active
Pinned: false
Category: AI/HumanBot
Created: 2026-05-05
Last used: 2026-06-01
Last verified: 2026-06-01
Use count: 22
Review after: 2026-08-03
Triggers: AI fill, fewer humans than max players, 4 slots
Applies to: `run_ai_fill_smoke.py`, `GameManagers.DeterminePlayerCount`
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/mp/20260509-023108-2human-aifill-visible-3round; artifacts/mp/20260510-103350-two-humanbot-two-ai-upgrade-check; artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless; artifacts/mp/20260510-170014-monster-healthbar-battle2-2hbot-2ai-headless; artifacts/mp/20260514-035507-two-humanbot-two-ai-smoke; artifacts/mp/20260514-044306-two-humanbot-two-ai-smoke; artifacts/mp/20260514-051726-two-humanbot-two-ai-smoke; artifacts/mp/20260514-061112-two-humanbot-two-ai-smoke; artifacts/mp/20260519-071043-two-humanbot-two-ai-smoke; artifacts/mp/20260521-081533-two-humanbot-two-ai-smoke/result.json; artifacts/mp/20260521-082714-two-humanbot-two-ai-smoke/result.json; artifacts/mp/20260521-083622-two-humanbot-two-ai-smoke/result.json; artifacts/mp/20260522-044836-two-humanbot-two-ai-smoke/result.json; artifacts/mp/20260522-052526-two-humanbot-two-ai-smoke; artifacts/mp/20260601-021130-two-humanbot-two-ai-smoke/game-to-end-move-result.json; artifacts/mp/20260601-032131-two-humanbot-two-ai-smoke/result.json
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
AI fill should be proven by snapshot fields, not by logs alone.

Recipe:
- Launch one build host with `--mpMaxPlayers 4 --mpAutoStart --mpLoadGame`.
- Wait for Game scene snapshot with 4 `PlayerManager` entries.
- Assert AI controller registered for 3 non-human slots and fields are ready.

Verification:
- `artifacts/mp/20260504-205404-ai-fill-smoke/ai-fill-assertions.json` reported `playerCount=4`, `aiCount=3`, playerIds `[0,1,2,3]`, and `allFieldsReady=true`.

Verification update:
- `artifacts/mp/20260504-230444-ai-fill-smoke/ai-fill-assertions.json` reported `playerCount=4`, `aiCount=3`, playerIds `[0,1,2,3]`, and `allFieldsReady=true` on build `artifacts/builds/20260504-230414/MDF-MPTest.exe`.

Lifecycle notes:
- 2026-05-09: 2 real peers + 2 AI fill visual MP run verified AI identity replicated to client snapshots, game-ready host/client aiCount=2, round 3 snapshot match.
- 2026-05-09: verified artifact `artifacts/mp/20260509-023108-2human-aifill-visible-3round`
- 2026-05-10: Verified 2 real HumanBot peers plus 2 AI fill players in headless 4-player upgrade check on 2026-05-10; humanCount=2 aiCount=2 cleanupStatus=PASS orphanedPids=[].
- 2026-05-10: verified artifact `artifacts/mp/20260510-103350-two-humanbot-two-ai-upgrade-check`
- 2026-05-10: Verified AI fill produced exactly 2 AI players alongside 2 HumanBot-controlled real peers in headless wave invariant check.
- 2026-05-10: verified artifact `artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless`
- 2026-05-11: Verified 2 connected HumanBot players plus 2 server AI fill players in maxPlayers=4 session.
- 2026-05-11: verified artifact `artifacts/mp/20260510-170014-monster-healthbar-battle2-2hbot-2ai-headless`
- 2026-05-13: Verified 2 HumanBot-controlled real peers plus 2 AI fill players with successful MoveUnit commands.
- 2026-05-13: verified artifact `artifacts/mp/20260513-131556-two-humanbot-two-ai-smoke`
- 2026-05-14: 2 HumanBot + 2 AI smoke passed after UIToolkit attack sequence/resource HUD change; cleanupStatus=PASS orphanedPids=[].
- 2026-05-14: verified artifact `artifacts/mp/20260514-035507-two-humanbot-two-ai-smoke`
- 2026-05-14: 2 HumanBot + 2 AI smoke passed after whole-ground monster spawn click fix; playerCount=4 humanCount=2 aiCount=2, cleanupStatus=PASS orphanedPids=[].
- 2026-05-14: verified artifact `artifacts/mp/20260514-044306-two-humanbot-two-ai-smoke`
- 2026-05-14: 2 HumanBot + 2 AI smoke passed after monster card tap suppression; playerCount=4 humanCount=2 aiCount=2 cleanupStatus=PASS orphanedPids=[].
- 2026-05-14: verified artifact `artifacts/mp/20260514-051726-two-humanbot-two-ai-smoke`
- 2026-05-14: Verified 2 HumanBot + 2 AI 4-player round 2 move smoke.
- 2026-05-14: verified artifact `artifacts/mp/20260514-061112-two-humanbot-two-ai-smoke`
- 2026-05-14: Used for 2 HumanBot + 2 AI GameOver movement validation. AI fill stayed present in long 4-player runs, but full E2E was not marked verified because final snapshot comparison diverged in separate long-run state.
- 2026-05-14: used artifact `artifacts/mp/20260514-064919-two-humanbot-two-ai-smoke`
- 2026-05-19: Verified max-player 4P composition with 2 HumanBot peers and 2 AI fill players; assertions reported playerCount=4 humanCount=2 aiCount=2.
- 2026-05-19: verified artifact `artifacts/mp/20260519-071043-two-humanbot-two-ai-smoke`
- 2026-05-21: Verified 2 HumanBot + 2 AI smoke after UI Toolkit ranking and prepare HUD changes.
- 2026-05-21: verified artifact `artifacts/mp/20260521-081533-two-humanbot-two-ai-smoke/result.json`
- 2026-05-21: Verified 2 HumanBot + 2 AI smoke after round-relative PlayerRanking UI layout change.
- 2026-05-21: verified artifact `artifacts/mp/20260521-082714-two-humanbot-two-ai-smoke/result.json`
- 2026-05-21: Verified 2 HumanBot + 2 AI smoke after PlayerRanking self/opponent position adjustment.
- 2026-05-21: verified artifact `artifacts/mp/20260521-083622-two-humanbot-two-ai-smoke/result.json`
- 2026-05-22: Reused 2 HumanBot + 2 AI smoke with --move-round 2 after input/ranking UI fix; PASS cleanupStatus=PASS orphanedPids=[].
- 2026-05-22: verified artifact `artifacts/mp/20260522-044836-two-humanbot-two-ai-smoke/result.json`
- 2026-05-22: Verified wall create/remove refund and move commands through round 5 in 2 HumanBot + 2 AI MP smoke.
- 2026-05-22: verified artifact `artifacts/mp/20260522-052526-two-humanbot-two-ai-smoke`
- 2026-05-31: Verified 2 HumanBot peers plus 2 AI fill players in visual 4-player smoke after PlayerRanking role icon and wall placement input fixes; cleanupStatus=PASS orphanedPids=[].
- 2026-05-31: verified artifact `artifacts/mp/20260531-041001-two-humanbot-two-ai-smoke/result.json`
- 2026-06-01: Verified finalHost players total=4 human=2 ai=2 in two-humanbot-two-ai-smoke run.
- 2026-06-01: verified artifact `artifacts/mp/20260601-021130-two-humanbot-two-ai-smoke/game-to-end-move-result.json`
- 2026-06-01: verified artifact `artifacts/mp/20260601-032131-two-humanbot-two-ai-smoke/result.json`
