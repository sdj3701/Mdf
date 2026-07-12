## battle-snapshot-coverage-v1: Hash late-game battle state semantically

Status: active
Pinned: true
Category: host-migration, battle
Created: 2026-05-06
Last used: 2026-07-12
Last verified: 2026-05-11
Use count: 4
Review after: 2026-08-04
Triggers: late-game sync drift, survivor boss assignment, active augment effects, monster spawn/combat divergence
Applies to: `MPTestStateSnapshot`, `MPTestAssertions`, `compare_state_snapshots.py`, HumanBot/Host Migration battle progression
Verified by: see Verification section below; migrated from old Status: verified-local; artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless; artifacts/mp/20260510-170014-monster-healthbar-battle2-2hbot-2ai-headless
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Prepare-phase HumanBot snapshots covered shops, augments, walls, and units, but battle progression still had blind spots. Monster snapshots mostly exposed alive count and a legacy living hash, survivor boss pending/assignment state was private manager state, and active augment effect/target state was not compared separately.

Recipe:
- Keep existing hashes and comparisons; add semantic hashes instead of replacing durable coverage.
- Hash active battle role state as `game.battleActiveHash` from `playerId`, opponent id, first attacker id, fighting flag, and attacker flag.
- Hash survivor boss pending/assignment state with gameplay IDs, origin player, boss data key, invasion flag, and coarse HP buckets; also compare pending/assignment counts so non-zero host-only state does not disappear behind `unknown`.
- Hash augment active effects/targets from selected augments, active monster-summon augments, owned bosses, owned scrolls, permanent stat buckets, and resolved player/opponent target when available. Compare active effect/target counts as well as hashes.
- Hash monsters by stable type/trait/boss key, count, coarse HP/max-HP bucket, defender/attacker player ids, and Networked boss gameplay id. Do not hash raw Unity instance IDs or exact interpolated transforms.
- Hash manual/strategic defender skill readiness with semantic unit data, grid position, skill key, activation mode, AI strategic flag, mana bucket, status/casting/dead flags, and target count.
- In `compare_state_snapshots.py`, use strict comparison for conditional required values. When battle phase, non-zero survivor counts, or command counters make a field required, any `unknown`/missing value fails, including both peers missing the value. For optional absent state, both peers may remain `unknown`.
- Mirror the same strict comparison in `MPTestAssertions.CompareDurable` and add negative EditMode tests. Python-only strictness is not enough because custom Unity CLI assertions can use the C# path.
- For active effect target keys, do not fall back to `NetworkObject.Id` or `GameObject.name`. Use semantic target keys: unit owner/data/star/grid cell, monster owner/data/boss metadata/HP bucket/navigation or coarse position, and wall owner/grid/HP bucket.
- Use a read-only battle-map lookup for target hashes. Back it with the state-authority-published Networked battle map and battle-start RPC observations; do not call mutating fallback methods such as `GetBattleOpponent()` from snapshot capture.
- Keep `FirstAttackerPlayerId` and battle-map publication authority-only. Client migration/readiness paths may cache `BattleOpponentSnapshotIds` and `BattleFirstAttackerSnapshotIds` into local dictionaries for comparison, but must not rebuild/publish or assign Networked fields.
- If monster snapshots scan a player's `monsterParent`, also add a live `Monster` fallback filtered by Networked owner id. Client-side monster initialization should still reparent replicated monsters under the owner's monster parent, but the snapshot must not depend on a one-shot init RPC being observed.
- Mirror monster owner id, monster data key, type, traits, and boss gameplay metadata into Networked fields so reconnect/Host Migration peers can rebind and hash living monsters from durable identity.
- Mirror boss gameplay metadata into Networked fields and use those accessors for boss gameplay decisions, not only for snapshot hashes.

Verification:
- `python tools/harness/precommit.py --all` reported `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed and `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed 8/8.
- Rebuilt Development player at `artifacts/builds/20260506-004439/MDF-MPTest.exe`; launch smoke exited `0`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --player-path artifacts/builds/20260506-004439/MDF-MPTest.exe` passed with artifact `artifacts/mp/20260506-004520-human-bot-prepare`.
- Direct CLI comparison now works: `python tools/harness/mp/compare_state_snapshots.py artifacts/mp/20260506-004520-human-bot-prepare/snapshots/build-host-prepare-progressed.json artifacts/mp/20260506-004520-human-bot-prepare/snapshots/build-client-prepare-progressed.json` returned `success: true`.
- Phase 10 hardened `compare_state_snapshots.py` with one-sided-unknown failures for attack pool, owned scroll, manual skill readiness, semantic monster hashes, and active effect hashes.
- Phase 10 reviewer follow-up aligned C# durable assertions with the Python comparator, made battle hashes required during `Battle1`/`Battle2`, required survivor hashes when survivor counts are non-zero, required `commands.lastCommand` once any battle command counter advances, and added both-one-sided and both-missing negative EditMode tests for those required values.

Lifecycle notes:
- 2026-05-10: Used attackMonsterPool snapshot diagnostics to verify common wave counts and augment-only extras across round 1/2 Battle1/Battle2.
- 2026-05-10: verified artifact `artifacts/mp/20260510-113305-wave-common-2hbot-2ai-headless`
- 2026-05-11: Snapshot monster parts exposed Networked HP, status bar visibility, and fill permille for HP bar pooling verification.
- 2026-05-11: verified artifact `artifacts/mp/20260510-170014-monster-healthbar-battle2-2hbot-2ai-headless`
