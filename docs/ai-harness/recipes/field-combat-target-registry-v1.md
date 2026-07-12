## field-combat-target-registry-v1: Field-scoped retained basic-attack targeting

Status: active
Pinned: false
Category: performance, battle, host-migration
Created: 2026-07-13
Last used: 2026-07-13
Last verified: 2026-07-13
Use count: 1
Review after: 2026-08-10
Triggers: per-frame OverlapSphere allocation, basic attack target churn, cross-field target, pooled target reuse, target search spike
Applies to: `FieldCombatTargetRegistry`, `FieldManager`, `Unit`, `Monster`
Verified by: `artifacts/mp/20260712-215401-two-humanbot-two-ai-smoke/result.json`; `artifacts/mp/20260712-215951-progressed-host-migration-after-battle/post-battle-host-migration-result.json`
Replacement: none
Archive policy: keep while basic attack targeting uses pooled Fusion actors

Problem:
Authority-side Unit and ranged Monster basic attacks used allocating `Physics.OverlapSphere` searches whenever they could attack. Searching every frame multiplied CPU and GC by actor count, and plain Component references could survive pool reuse or Host Migration with the wrong field/Runner identity.

Recipe:
- Own one non-Networked registry per `FieldManager`; never use a global target singleton or add target indexing to `CombatScheduler`.
- Register live Units and Monsters idempotently. Unit battle death may leave the durable field roster intact, but dead actors must not remain target candidates. Unregister on network despawn/pool reset/destroy and re-register on initialize/rebind/respawn.
- Keep a valid current basic-attack target. Only reacquire when it becomes dead, inactive, out of range, on another field, or belongs to another Runner/lifecycle.
- Limit reacquisition to roughly 5–10 Hz and derive the first offset from stable actor identity so battle start does not create one search spike.
- Preserve gameplay-specific ordering: melee blocked Monster first; ground-only for melee acquisition; ranged Monster prefers ranged Unit before melee Unit.
- Return `Unavailable`, `NoTarget`, and `Found` separately. A ready empty registry must not fall back to Physics. Allow `OverlapSphereNonAlloc` fallback only in a genuine local no-Runner mode.
- Capture InstanceID, Runner, NetworkId, collider, and actor spawn generation in retained/pending target handles. Revalidate at animation impact before scheduling or applying damage.
- Rebuild the transient index from `placedUnits` and the current `monsterParent` after migration roster recovery. Include round/state/Runner/monster-parent identity in the readiness key.
- Suspend acquisition and impact execution while `HostMigrationHandler.IsMigrating`; resume only after the migration flow reaches its terminal success state.

Verification:
- Focused EditMode registry tests cover idempotent registration, ground/flying filters, ranged-unit priority, staggered cadence, layer-aware collider recovery, inactive actors, field IDs, and spawn-generation invalidation.
- Round-1 combat reached round-2 Prepare with 20 synchronized live Units in `artifacts/mp/20260712-215401-two-humanbot-two-ai-smoke`.
- Post-battle Host Migration completed with `recoverySucceeded=true`, `failureCount=0`, durable `mismatches=[]`, cleanup PASS, and no orphaned player PIDs in `artifacts/mp/20260712-215951-progressed-host-migration-after-battle`.

Pitfalls:
- Do not unregister Units in `OnDisable`; pooled despawn and battle presentation disable are different lifecycles.
- Do not treat a non-running old Runner as local/offline authority.
- Do not centralize skill, zone, splash, path-wall, or contact-block queries without preserving their separate selection rules.
- Start with a field-local linear list. Add a spatial hash only after Profiler evidence shows candidate scans, rather than index maintenance, are the remaining bottleneck.
