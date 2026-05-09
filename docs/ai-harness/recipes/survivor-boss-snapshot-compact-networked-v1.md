## survivor-boss-snapshot-compact-networked-v1: Keep GameManagers replicated snapshots under Fusion word limits

Status: active
Pinned: false
Category: other
Created: 2026-05-08
Last used: 2026-05-08
Last verified: 2026-05-08
Use count: 1
Review after: 2026-08-06
Triggers: persistent authority-only gameplay state appears as non-zero on host and `unknown`/zero on client snapshots
Applies to: `GameManagers`, `SurvivorBossManager`, `MPTestStateSnapshot`, long HumanBot progression
Verified by: see Verification section below; migrated from old Status: verified
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Recipe:
- Treat survivor-boss pending/assignment as persistent State Authority-owned gameplay state; do not weaken snapshot comparison for host-only non-zero state.
- Mirror only compact snapshot evidence on `GameManagers`: pending/assignment counts and stable `sha256` hash hex in `[Networked]` fields.
- Let `MPTestStateSnapshot` read the replicated `GameManagers` count/hash values on every peer.
- Keep the full mutable survivor-boss lists in `SurvivorBossManager` on the authority side; add client mutation guards for extract/ID allocation paths.
- Rebuild the Development player after C# networking changes before rerunning build/build E2E.

Verification:
- A large `NetworkArray<NetworkString<_64>>` survivor-boss snapshot exceeded Fusion object word limits and caused early migration/startup failure; replacing it with compact count/hash fields fixed the build-run startup.
- `python tools/harness/mp/build_player.py --launch-smoke --exit-after-seconds 5 --headless-player --orphan-threshold 0 --cleanup-timeout-seconds 20` built `artifacts/builds/20260508-022528/MDF-MPTest.exe` with launch smoke cleanup PASS.
- `python tools/harness/mp/run_human_bot_3round_progression.py --seed 8101 --headless-player --player-path artifacts/builds/20260508-022528/MDF-MPTest.exe` PASS at `artifacts/mp/20260508-022613-human-bot-3round-progression`: `maxRoundReached=4`, `completionReason=target_round_complete`, `cleanupStatus=PASS`, `10/10` checkpoints passed.

Pitfalls:
- Unity compile can pass even when the already-built player is stale. Always pass the newly built player path when validating C# networking changes.
- Do not add high-capacity string arrays to `GameManagers` casually; Fusion can fail at runtime with object word-limit assertions even after clean C# compile.
