## mp-final-random-aware-audit: Pair accepted-command logs with durable deltas

Status: active
Pinned: true
Category: host-migration, reconnect
Created: 2026-05-06
Last used: 2026-05-06
Last verified: 2026-05-06
Use count: 1
Review after: 2026-08-04
Triggers: final audit, `accepted_command`, bot journal, random outcome summary, snapshot comparison
Applies to: final random-aware audit, HumanBot progression, progressed reconnect/disconnect, progressed Host Migration
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Bot command submission alone is not enough for final PASS. The final audit needs evidence that a real client request reached State Authority, passed validation, changed durable state, and replicated the same-player random-derived hashes across peers.

Recipe:
- Build or select a Development player that includes the latest C# changes.
- Require `[MPTEST] phase=accepted_command result=pass` in the host timeline for client-request scenarios. This proves server-side RPC validation accepted the command after authoritative `playerId` correction.
- Still require durable delta evidence such as selected augment hash, wall count/hash, shop revision/hash, or placed-unit hash. `accepted_command` is not enough by itself.
- Require random outcome summaries where every same-player `matchesClient` or host-vs-client `matches` entry is true.
- Require checkpoint snapshots and comparison JSONs for before-bot, after first accepted command, final progressed state, reconnect/disconnect, and Host Migration.
- Run `rg "result=fail|phase=error|parseError" <artifact dirs>` and require no matches. `parse_mptest_logs.py` now splits concatenated `[MPTEST]` markers so normal Unity log concatenation should not create parser errors.

Verification:
- Build `artifacts/builds/20260505-201620/MDF-MPTest.exe` succeeded as a Development player and launch smoke exited `0`.
- `artifacts/mp/20260505-201703-human-bot-prepare` passed with `SelectAugment`, `accepted_command`, durable selected augment hash delta, comparison success, and same-player random outcome matches.
- `artifacts/mp/20260505-201737-human-bot-4p-progression` passed with three client bots, three `accepted_command` entries, total bot commands `3`, battle mapping hashes, and host-vs-client same-player matches.
- `artifacts/mp/20260505-201824-progressed-same-token-reconnect` passed with target/full/role/progressed preservation assertions.
- `artifacts/mp/20260505-201914-progressed-disconnect-ai-takeover` passed with the progressed target preserved after AI takeover.
- `artifacts/mp/20260505-201956-progressed-host-migration-e2e` passed with `accepted_command` entries for `SelectAugment` and `PlaceWall`, Host Migration callback/token/resume proof, durable pre/post mismatches `[]`, and no raw `InvalidOperationException` or `Failed to free` log matches.
- `artifacts/mp/20260505-202050-human-bot-seed-sweep` passed seeds `5101,5102,5103` with `deterministicReplayClaim=false` and random-aware comparisons.
- `rg "result=fail|phase=error|parseError"` over the final artifact set found no matches. Accepted-command timeline events preserve RpcInfo `source=[Player:x]` and store the artifact file path separately as `logSource`.

Pitfalls:
- `accepted_command` is logged before the final `GameManagers` lookup and broadcast, so pair it with the durable snapshot delta and comparison result.
- Do not treat a host-side HumanBot as proof of the real client request path. Use connected client bots for that evidence.
