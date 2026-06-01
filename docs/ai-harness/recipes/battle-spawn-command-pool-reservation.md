## battle-spawn-command-pool-reservation: Reserve attack pool before awaited network spawn

Status: active
Pinned: false
Category: battle
Created: 2026-05-06
Last used: 2026-06-01
Last verified: 2026-06-01
Use count: 7
Review after: 2026-08-04
Triggers: strategic attack monster spawn command, duplicate spawn requests, async `Runner.Spawn`, attack pool hash drift
Applies to: `BattleSpawnMonsterCommand`, `GameManagers.RPC_RequestBattleSpawnMonster`, `PlayerManager.AttackMonsterPool`, `MonsterSpawner.ExecuteSpawnPlanAsync`
Verified by: see Verification section below; migrated from old Status: compile-and-smoke-verified; battle E2E still needs a dedicated runner; artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless; artifacts/mp/20260601-033513-two-hbot-two-ai-monster-spawn/result.json; artifacts/mp/20260514-035543-battle-spawn-monster-command; artifacts/mp/20260514-044344-battle-spawn-monster-command; artifacts/mp/20260514-051807-battle-spawn-monster-command; artifacts/mp/20260520-054126-battle-spawn-monster-command
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Battle monster spawn validation can pass for multiple same-slot requests before an awaited network spawn finishes. If the pool is consumed only after spawn, duplicate requests can create extra durable monsters or leave host/client pool hashes inconsistent. Plain client-local pool rebuilds can also hide consumed slots during reconnect or Host Migration recovery.

Recipe:
- Validate command count as exactly one spawn per command. Do not clamp or silently trust client-provided batch counts.
- Keep `RpcInfo.Source` authorization as an early RPC gate, then re-run command validation on State Authority.
- Resolve attacker, defender, battle role, opponent mapping, finite target position, outer spawn zone, and authoritative pool slot before execution.
- Consume or reserve the authoritative pool slot before any awaited spawn call. If `Runner.Spawn` or spawn initialization fails, refund the same slot immediately.
- Use the authoritative pool entry to derive boss id and origin player. Do not trust client-supplied boss/origin fields.
- Include the client's applied authoritative attack-pool snapshot revision in client-requested spawn commands. Do not use the replicated revision alone as proof that the client has applied the matching pool contents.
- Reject missing or mismatched applied revisions and resend the authoritative pool snapshot before accepting another slot-index request.
- Sync attack pool changes with a monotonic revision and ignore stale async client RPC completions.
- Non-authority peers must not rebuild attack pools locally during battle start; request/resend the authority snapshot instead so slot indices always refer to server-owned contents.
- Reconnect and late-join sync must proactively resend the attack-pool snapshot, not only shops/augments/scrolls/walls. Do not rely on the next client spawn click to request the missing pool.
- Host Migration durable snapshots must carry attack-pool refs or stable keys plus counts/revision, and apply them before migration recovery decides whether to rebuild an attack pool.
- If a Fusion `Runner.Spawn` succeeds but later initialization/path validation fails, cleanup must use `Runner.Despawn` for the spawned `NetworkObject`; `Destroy` alone can leave a replicated object alive.
- During migration/recovery, do not refresh an empty attack pool over a positive consumed revision.
- Snapshot `attackMonsterPoolHash`, accepted battle command sequence, spawn sequence, and rejected command count so peer comparisons can detect pool or command drift. Empty/null attack pools need deterministic hashes; required battle pool comparisons must not collapse to `unknown`.
- Mark legacy spawn RPCs deprecated with explicit `battle_spawn_rejected` telemetry instead of leaving early-return reject paths.
- Battle command telemetry is durable snapshot state. Resend it during reconnect/late-join sync and capture/apply it through Host Migration cache; do not leave it as command-time RPC-only state.

Verification:
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed 13/13.
- Development build and launch smoke passed at `artifacts/builds/20260506-031706`.
- Closest existing E2E passed at `artifacts/mp/20260506-031807-human-bot-prepare`, but it only proves prepare progression. A dedicated battle-spawn E2E runner is still required before claiming battle command sync PASS.

Lifecycle notes:
- 2026-05-10: BattleSpawnMonster commands consumed pools while max common-wave counts stayed equal and extras matched selected monster augments.
- 2026-05-10: verified artifact `artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless`
- 2026-06-01: verified artifact `artifacts/mp/20260601-033513-two-hbot-two-ai-monster-spawn/result.json`
- 2026-05-14: Battle spawn command accepted after UIToolkit monster card bridge; spawnMonsterSeq=1 rejectedBattleCommandCount=0.
- 2026-05-14: verified artifact `artifacts/mp/20260514-035543-battle-spawn-monster-command`
- 2026-05-14: Battle spawn command accepted after whole-ground spawn-zone click fix; spawnMonsterSeq=1 rejectedBattleCommandCount=0, spawnSemanticsObserved=true.
- 2026-05-14: verified artifact `artifacts/mp/20260514-044344-battle-spawn-monster-command`
- 2026-05-14: Battle spawn command accepted after card tap no-longer-spawns fix; spawnMonsterSeq=1 rejectedBattleCommandCount=0 spawnSemanticsObserved=true.
- 2026-05-14: verified artifact `artifacts/mp/20260514-051807-battle-spawn-monster-command`
- 2026-05-20: verified artifact `artifacts/mp/20260520-054126-battle-spawn-monster-command`
