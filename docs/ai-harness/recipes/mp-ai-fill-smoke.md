## mp-ai-fill-smoke: Max-player AI slots are supported

Status: active
Pinned: false
Category: AI/HumanBot
Created: 2026-05-05
Last used: 2026-05-05
Last verified: 2026-05-05
Use count: 1
Review after: 2026-08-03
Triggers: AI fill, fewer humans than max players, 4 slots
Applies to: `run_ai_fill_smoke.py`, `GameManagers.DeterminePlayerCount`
Verified by: see Verification section below; migrated from old Status: verified-local
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
