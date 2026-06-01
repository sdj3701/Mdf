## mp-ai-fill-smoke: Max-player AI slots are supported

Status: active
Pinned: false
Category: AI/HumanBot
Created: 2026-05-05
Last used: 2026-06-01
Last verified: 2026-06-01
Use count: 8
Review after: 2026-08-03
Triggers: AI fill, fewer humans than max players, 4 slots
Applies to: `run_ai_fill_smoke.py`, `GameManagers.DeterminePlayerCount`
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/mp/20260509-023108-2human-aifill-visible-3round; artifacts/mp/20260510-103350-two-humanbot-two-ai-upgrade-check; artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless; artifacts/mp/20260510-170014-monster-healthbar-battle2-2hbot-2ai-headless; artifacts/mp/20260601-033513-two-hbot-two-ai-monster-spawn/result.json
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
- 2026-06-01: verified artifact `artifacts/mp/20260601-033513-two-hbot-two-ai-monster-spawn/result.json`
