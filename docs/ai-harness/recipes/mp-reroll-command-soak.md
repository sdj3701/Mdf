## mp-reroll-command-soak: Use reroll_shop for focused durable command proof

Status: active
Pinned: false
Category: harness
Created: 2026-05-05
Last used: 2026-05-05
Last verified: 2026-05-05
Use count: 1
Review after: 2026-08-03
Triggers: durable command soak, `CommandProcessor`, shop sync
Applies to: `/command`, `tools/harness/mp/run_durable_command_soak.py`
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
The command harness should use commands that exist in real gameplay/AI behavior where possible. `RerollShopAction` uses `RerollShopCommand`, so `reroll_shop` is the current safe AI-behavior command probe.

Recipe:
- Support only `reroll_shop` through `/command` for now.
- Issue it to the server/host peer, validate `playerId`, runner/server state, `CommandProcessor`, shop DB readiness, and reroll cost.
- Verify only command-relevant durable fields: target player gold decreases by cost, shop revision advances, and host/client agree on gold, shop revision, shop count, and shop items hash.
- Launch peers with the same real room flow as E2E: `MatchingLobby` session first, then host loads `Game`.
- Treat the 10-iteration run as the default full reroll soak. The report must include `iterationsRequested`, `iterationsCompleted`, `success`, and per-iteration mutation/agreement evidence.
- Keep full snapshot comparisons separate; this proves the AI-style command path, not every gameplay action.

Verification:
- `artifacts/mp/20260504-211819-durable-command-soak/durable-command-report.json` passed 3 iterations for `playerId=0`.
- Gold moved `27 -> 25 -> 23 -> 21`; host/client shop revisions matched `2`, `3`, `4`; host/client shop item hashes matched every iteration.
- `artifacts/mp/20260504-221931-durable-command-soak/durable-command-report.json` passed 2 iterations on the real room flow with the new build. The pre-command snapshots showed both peers agreed on 36 permanent walls per player and matching `wallHash`.
- `artifacts/mp/20260505-111707-durable-command-soak/durable-command-report.json` passed the full 10-iteration run for `playerId=0`.
- Gold moved `27 -> 7`; host/client shop revisions matched `2..11`; every iteration recorded `goldMutated=true`, `revisionMonotonic=true`, and `peersAgree=true`.
- `artifacts/mp/20260505-113356-durable-command-soak/durable-command-report.json` repeated the full 10-iteration run successfully on the latest build after Host Migration scene-tracking changes.

Pitfalls:
- This is a focused durable command proof, not a replacement for broader AI behavior coverage such as buy, place wall, move unit, augment selection, or combat spawn orders.
