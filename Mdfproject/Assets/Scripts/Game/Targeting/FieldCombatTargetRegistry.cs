using System;
using System.Collections.Generic;
using Fusion;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// A transient, field-scoped index used by authority-side basic attack targeting.
/// It is rebuilt from live scene objects and is intentionally not networked or snapshotted.
/// </summary>
public sealed class FieldCombatTargetRegistry
{
    public enum QueryStatus
    {
        Unavailable,
        NoTarget,
        Found
    }

    private struct UnitEntry
    {
        public Unit Actor;
        public Collider PrimaryCollider;
        public int InstanceId;
    }

    private struct MonsterEntry
    {
        public Monster Actor;
        public Collider PrimaryCollider;
        public int InstanceId;
    }

    private const int InitialUnitCapacity = 96;
    private const int InitialMonsterCapacity = 256;
    private const float DistanceTieEpsilon = 0.0001f;

    private static readonly ProfilerMarker FindMonsterMarker =
        new ProfilerMarker("MDF.Targeting.FindNearestMonster");
    private static readonly ProfilerMarker FindUnitMarker =
        new ProfilerMarker("MDF.Targeting.FindPriorityUnit");

    private readonly FieldManager _fieldManager;
    private readonly List<UnitEntry> _units = new List<UnitEntry>(InitialUnitCapacity);
    private readonly List<MonsterEntry> _monsters = new List<MonsterEntry>(InitialMonsterCapacity);
    private readonly Dictionary<int, int> _unitIndices = new Dictionary<int, int>(InitialUnitCapacity);
    private readonly Dictionary<int, int> _monsterIndices = new Dictionary<int, int>(InitialMonsterCapacity);
    private readonly Dictionary<int, float> _nextSearchTimes = new Dictionary<int, float>(InitialUnitCapacity + InitialMonsterCapacity);

    public int RegisteredUnitCount => _units.Count;
    public int RegisteredMonsterCount => _monsters.Count;
    public long QueryCount { get; private set; }
    public long CandidatesVisited { get; private set; }
    public int MaxCandidatesVisitedPerQuery { get; private set; }

    public FieldCombatTargetRegistry(FieldManager fieldManager)
    {
        _fieldManager = fieldManager;
    }

    public void RegisterUnit(Unit unit)
    {
        if (unit == null || unit.IsDead || !unit.gameObject.activeInHierarchy)
        {
            return;
        }

        int instanceId = unit.GetInstanceID();
        if (_unitIndices.TryGetValue(instanceId, out int index))
        {
            UnitEntry existing = _units[index];
            existing.Actor = unit;
            if (existing.PrimaryCollider == null)
            {
                existing.PrimaryCollider = FindPrimaryCollider(unit);
            }
            _units[index] = existing;
            return;
        }

        _unitIndices.Add(instanceId, _units.Count);
        _units.Add(new UnitEntry
        {
            Actor = unit,
            PrimaryCollider = FindPrimaryCollider(unit),
            InstanceId = instanceId
        });
    }

    public void UnregisterUnit(Unit unit)
    {
        if (unit == null)
        {
            return;
        }

        int instanceId = unit.GetInstanceID();
        if (_unitIndices.TryGetValue(instanceId, out int index))
        {
            RemoveUnitAt(index);
        }
        _nextSearchTimes.Remove(instanceId);
    }

    public void RegisterMonster(Monster monster)
    {
        if (monster == null)
        {
            return;
        }

        int instanceId = monster.GetInstanceID();
        if (_monsterIndices.TryGetValue(instanceId, out int index))
        {
            MonsterEntry existing = _monsters[index];
            existing.Actor = monster;
            if (existing.PrimaryCollider == null)
            {
                existing.PrimaryCollider = FindPrimaryCollider(monster);
            }
            _monsters[index] = existing;
            return;
        }

        _monsterIndices.Add(instanceId, _monsters.Count);
        _monsters.Add(new MonsterEntry
        {
            Actor = monster,
            PrimaryCollider = FindPrimaryCollider(monster),
            InstanceId = instanceId
        });
    }

    public bool ContainsMonster(Monster monster)
    {
        return monster != null && _monsterIndices.ContainsKey(monster.GetInstanceID());
    }

    public void UnregisterMonster(Monster monster)
    {
        if (monster == null)
        {
            return;
        }

        int instanceId = monster.GetInstanceID();
        if (_monsterIndices.TryGetValue(instanceId, out int index))
        {
            RemoveMonsterAt(index);
        }
        _nextSearchTimes.Remove(instanceId);
    }

    public void RebuildUnits(IEnumerable<Unit> units)
    {
        _units.Clear();
        _unitIndices.Clear();

        if (units == null)
        {
            return;
        }

        foreach (Unit unit in units)
        {
            RegisterUnit(unit);
        }
    }

    public void RebuildMonsters(IEnumerable<Monster> monsters)
    {
        _monsters.Clear();
        _monsterIndices.Clear();

        if (monsters == null)
        {
            return;
        }

        foreach (Monster monster in monsters)
        {
            RegisterMonster(monster);
        }
    }

    public void Clear()
    {
        _units.Clear();
        _monsters.Clear();
        _unitIndices.Clear();
        _monsterIndices.Clear();
        _nextSearchTimes.Clear();
        QueryCount = 0;
        CandidatesVisited = 0;
        MaxCandidatesVisitedPerQuery = 0;
    }

    /// <summary>
    /// Appends the distributed, migration-durable members of one materialized zone pulse without
    /// allocating or walking the entire scene. Callers share <paramref name="visitedIds"/> across
    /// fields so a temporarily duplicated registry entry cannot apply an effect twice.
    /// </summary>
    public void CollectPendingZonePulseTargets(
        int pulseToken,
        NetworkRunner expectedRunner,
        List<NetworkObject> results,
        HashSet<uint> visitedIds)
    {
        if (pulseToken <= 0 || results == null || visitedIds == null)
        {
            return;
        }

        for (int i = 0; i < _units.Count; i++)
        {
            Unit unit = _units[i].Actor;
            NetworkObject targetObject = unit != null ? unit.Object : null;
            if (targetObject == null || !targetObject.IsValid || targetObject.Runner != expectedRunner ||
                unit.PendingZonePulseDebtToken != pulseToken || !visitedIds.Add(targetObject.Id.Raw))
            {
                continue;
            }

            results.Add(targetObject);
        }

        for (int i = 0; i < _monsters.Count; i++)
        {
            Monster monster = _monsters[i].Actor;
            NetworkObject targetObject = monster != null ? monster.Object : null;
            if (targetObject == null || !targetObject.IsValid || targetObject.Runner != expectedRunner ||
                monster.PendingZonePulseDebtToken != pulseToken || !visitedIds.Add(targetObject.Id.Raw))
            {
                continue;
            }

            results.Add(targetObject);
        }
    }

    public void ResetSearchSchedule(UnityEngine.Object requester)
    {
        if (requester != null)
        {
            _nextSearchTimes.Remove(requester.GetInstanceID());
        }
    }

    /// <summary>
    /// Returns true only when this requester owns the current distributed search slot.
    /// The first search is spread over one interval to avoid a battle-start spike.
    /// </summary>
    public bool TryBeginSearch(UnityEngine.Object requester, float now, float interval)
    {
        if (requester == null || interval <= 0f)
        {
            return requester != null;
        }

        int instanceId = requester.GetInstanceID();
        if (!_nextSearchTimes.TryGetValue(instanceId, out float nextSearchTime))
        {
            _nextSearchTimes[instanceId] = now + ComputeInitialSearchDelay(instanceId, interval);
            return false;
        }

        if (now + Mathf.Epsilon < nextSearchTime)
        {
            return false;
        }

        _nextSearchTimes[instanceId] = now + interval;
        return true;
    }

    public static float ComputeInitialSearchDelay(int stableId, float interval)
    {
        if (interval <= 0f)
        {
            return 0f;
        }

        unchecked
        {
            uint hash = (uint)stableId;
            hash ^= hash >> 16;
            hash *= 0x7feb352dU;
            hash ^= hash >> 15;
            hash *= 0x846ca68bU;
            hash ^= hash >> 16;
            float normalized = ((hash & 1023U) + 0.5f) / 1024f;
            return interval * normalized;
        }
    }

    public QueryStatus FindNearestMonster(
        Unit seeker,
        float range,
        bool groundOnly,
        LayerMask layerMask,
        out CombatTargetHandle target)
    {
        target = default;
        if (!CanQuery(seeker, seeker != null ? seeker.OwnerPlayerIdForRoster : -1))
        {
            return QueryStatus.Unavailable;
        }

        using (FindMonsterMarker.Auto())
        {
            QueryCount++;
            int visited = 0;
            float rangeSqr = Mathf.Max(0f, range) * Mathf.Max(0f, range);
            float bestDistanceSqr = float.MaxValue;
            ulong bestStableOrder = ulong.MaxValue;
            MonsterEntry bestEntry = default;
            bool found = false;

            for (int i = 0; i < _monsters.Count; i++)
            {
                MonsterEntry entry = _monsters[i];
                Monster monster = entry.Actor;
                if (monster == null)
                {
                    RemoveMonsterAt(i--);
                    continue;
                }

                visited++;
                if (!IsMonsterCandidateValid(monster, groundOnly) ||
                    !IsActorOnCurrentRunner(monster) ||
                    !MatchesFieldOwner(monster.SnapshotOwnerPlayerId))
                {
                    continue;
                }

                Collider primaryCollider = ResolveCollider(monster, entry.PrimaryCollider, layerMask);
                if (primaryCollider == null)
                {
                    continue;
                }

                if (primaryCollider != entry.PrimaryCollider)
                {
                    entry.PrimaryCollider = primaryCollider;
                    _monsters[i] = entry;
                }

                Vector3 closestPoint = primaryCollider.ClosestPoint(seeker.transform.position);
                float eligibilityDistanceSqr = groundOnly
                    ? HorizontalDistanceSqr(seeker.transform.position, closestPoint)
                    : (seeker.transform.position - closestPoint).sqrMagnitude;
                if (eligibilityDistanceSqr > rangeSqr)
                {
                    continue;
                }

                float distanceSqr = groundOnly
                    ? eligibilityDistanceSqr
                    : (seeker.transform.position - primaryCollider.transform.position).sqrMagnitude;

                ulong stableOrder = GetStableOrder(monster);
                if (!found || distanceSqr < bestDistanceSqr - DistanceTieEpsilon ||
                    (Mathf.Abs(distanceSqr - bestDistanceSqr) <= DistanceTieEpsilon && stableOrder < bestStableOrder))
                {
                    found = true;
                    bestDistanceSqr = distanceSqr;
                    bestStableOrder = stableOrder;
                    bestEntry = entry;
                }
            }

            RecordCandidatesVisited(visited);
            if (!found)
            {
                return QueryStatus.NoTarget;
            }

            target = CombatTargetHandle.Capture(bestEntry.Actor, bestEntry.PrimaryCollider);
            return QueryStatus.Found;
        }
    }

    public QueryStatus FindPriorityUnit(
        Monster seeker,
        float range,
        LayerMask layerMask,
        out CombatTargetHandle target)
    {
        target = default;
        if (!CanQuery(seeker, seeker != null ? seeker.SnapshotOwnerPlayerId : -1))
        {
            return QueryStatus.Unavailable;
        }

        using (FindUnitMarker.Auto())
        {
            QueryCount++;
            int visited = 0;
            float rangeSqr = Mathf.Max(0f, range) * Mathf.Max(0f, range);
            float bestRangedDistanceSqr = float.MaxValue;
            float bestMeleeDistanceSqr = float.MaxValue;
            ulong bestRangedOrder = ulong.MaxValue;
            ulong bestMeleeOrder = ulong.MaxValue;
            UnitEntry bestRanged = default;
            UnitEntry bestMelee = default;
            bool foundRanged = false;
            bool foundMelee = false;

            for (int i = 0; i < _units.Count; i++)
            {
                UnitEntry entry = _units[i];
                Unit unit = entry.Actor;
                if (unit == null)
                {
                    RemoveUnitAt(i--);
                    continue;
                }

                visited++;
                if (!IsUnitCandidateValid(unit) ||
                    !IsActorOnCurrentRunner(unit) ||
                    !MatchesFieldOwner(unit.OwnerPlayerIdForRoster))
                {
                    continue;
                }

                Collider primaryCollider = ResolveCollider(unit, entry.PrimaryCollider, layerMask);
                if (primaryCollider == null)
                {
                    continue;
                }

                if (primaryCollider != entry.PrimaryCollider)
                {
                    entry.PrimaryCollider = primaryCollider;
                    _units[i] = entry;
                }

                Vector3 closestPoint = primaryCollider.ClosestPoint(seeker.transform.position);
                if ((seeker.transform.position - closestPoint).sqrMagnitude > rangeSqr)
                {
                    continue;
                }


                float distanceSqr = (seeker.transform.position - unit.transform.position).sqrMagnitude;

                ulong stableOrder = GetStableOrder(unit);
                if (unit.Data.unitType == UnitType.Ranged)
                {
                    if (!foundRanged || distanceSqr < bestRangedDistanceSqr - DistanceTieEpsilon ||
                        (Mathf.Abs(distanceSqr - bestRangedDistanceSqr) <= DistanceTieEpsilon && stableOrder < bestRangedOrder))
                    {
                        foundRanged = true;
                        bestRangedDistanceSqr = distanceSqr;
                        bestRangedOrder = stableOrder;
                        bestRanged = entry;
                    }
                }
                else if (!foundMelee || distanceSqr < bestMeleeDistanceSqr - DistanceTieEpsilon ||
                         (Mathf.Abs(distanceSqr - bestMeleeDistanceSqr) <= DistanceTieEpsilon && stableOrder < bestMeleeOrder))
                {
                    foundMelee = true;
                    bestMeleeDistanceSqr = distanceSqr;
                    bestMeleeOrder = stableOrder;
                    bestMelee = entry;
                }
            }

            RecordCandidatesVisited(visited);
            if (foundRanged)
            {
                target = CombatTargetHandle.Capture(bestRanged.Actor, bestRanged.PrimaryCollider);
                return QueryStatus.Found;
            }

            if (foundMelee)
            {
                target = CombatTargetHandle.Capture(bestMelee.Actor, bestMelee.PrimaryCollider);
                return QueryStatus.Found;
            }

            return QueryStatus.NoTarget;
        }
    }

    public static bool FieldOwnerIdsMatch(int fieldOwnerPlayerId, int candidateOwnerPlayerId)
    {
        return fieldOwnerPlayerId >= 0 && candidateOwnerPlayerId >= 0 && fieldOwnerPlayerId == candidateOwnerPlayerId;
    }

    private bool CanQuery(NetworkBehaviour seeker, int seekerOwnerPlayerId)
    {
        if (seeker == null)
        {
            return false;
        }

        if (_fieldManager == null)
        {
            return true;
        }

        PlayerManager fieldOwner = _fieldManager.playerManager;
        if (fieldOwner == null)
        {
            return false;
        }

        NetworkRunner expectedRunner = fieldOwner.Runner;
        if (expectedRunner != null)
        {
            if (!expectedRunner.IsRunning || seeker.Object == null || !seeker.Object.IsValid ||
                seeker.Runner != expectedRunner || !seeker.Object.HasStateAuthority)
            {
                return false;
            }
        }
        else if (seeker.Runner != null || seeker.Object != null && seeker.Object.IsValid)
        {
            return false;
        }

        int fieldOwnerPlayerId = fieldOwner.playerId;
        if (fieldOwnerPlayerId < 0 || seekerOwnerPlayerId < 0)
        {
            return !Application.isPlaying;
        }

        return fieldOwnerPlayerId == seekerOwnerPlayerId;
    }

    private bool MatchesFieldOwner(int candidateOwnerPlayerId)
    {
        if (_fieldManager == null || _fieldManager.playerManager == null)
        {
            return true;
        }

        int fieldOwnerPlayerId = _fieldManager.playerManager.playerId;
        if (fieldOwnerPlayerId < 0 || candidateOwnerPlayerId < 0)
        {
            return !Application.isPlaying;
        }

        return FieldOwnerIdsMatch(fieldOwnerPlayerId, candidateOwnerPlayerId);
    }

    private bool IsActorOnCurrentRunner(NetworkBehaviour actor)
    {
        if (actor == null || _fieldManager == null || _fieldManager.playerManager == null)
        {
            return actor != null;
        }

        NetworkRunner expectedRunner = _fieldManager.playerManager.Runner;
        if (expectedRunner == null)
        {
            return actor.Runner == null && (actor.Object == null || !actor.Object.IsValid);
        }

        if (!expectedRunner.IsRunning)
        {
            return false;
        }

        NetworkObject networkObject = actor.Object;
        return networkObject != null &&
               networkObject.IsValid &&
               actor.Runner == expectedRunner;
    }

    private static bool IsUnitCandidateValid(Unit unit)
    {
        return unit != null &&
               unit.gameObject.activeInHierarchy &&
               !unit.IsDead &&
               unit.Data != null &&
               unit.CurrentHealth > 0f;
    }

    private static bool IsMonsterCandidateValid(Monster monster, bool groundOnly)
    {
        return monster != null &&
               monster.gameObject.activeInHierarchy &&
               monster.Data != null &&
               monster.CurrentHealth > 0f &&
               (!groundOnly || monster.Data.monsterType != MonsterType.Flying);
    }

    private static Collider ResolveCollider(Component actor, Collider cachedCollider, LayerMask layerMask)
    {
        if (IsColliderUsable(cachedCollider, layerMask))
        {
            return cachedCollider;
        }

        if (actor == null)
        {
            return null;
        }

        Collider rootCollider = actor.GetComponent<Collider>();
        if (IsColliderUsable(rootCollider, layerMask))
        {
            return rootCollider;
        }

        return FindMatchingColliderInChildren(actor.transform, layerMask);
    }

    private static Collider FindPrimaryCollider(Component actor)
    {
        if (actor == null)
        {
            return null;
        }

        Collider rootCollider = actor.GetComponent<Collider>();
        return rootCollider != null ? rootCollider : actor.GetComponentInChildren<Collider>(true);
    }

    private static bool LayerMatches(int layer, LayerMask layerMask)
    {
        return (layerMask.value & (1 << layer)) != 0;
    }

    private static bool IsColliderUsable(Collider collider, LayerMask layerMask)
    {
        return collider != null &&
               collider.enabled &&
               collider.gameObject.activeInHierarchy &&
               LayerMatches(collider.gameObject.layer, layerMask);
    }

    private static Collider FindMatchingColliderInChildren(Transform root, LayerMask layerMask)
    {
        if (root == null)
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            Collider childCollider = child.GetComponent<Collider>();
            if (IsColliderUsable(childCollider, layerMask))
            {
                return childCollider;
            }

            Collider nested = FindMatchingColliderInChildren(child, layerMask);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private static ulong GetStableOrder(NetworkBehaviour actor)
    {
        if (actor != null && actor.Object != null && actor.Object.IsValid)
        {
            return actor.Object.Id.Raw;
        }

        return (1UL << 32) + unchecked((uint)(actor != null ? actor.GetInstanceID() : int.MaxValue));
    }

    private void RecordCandidatesVisited(int count)
    {
        CandidatesVisited += count;
        if (count > MaxCandidatesVisitedPerQuery)
        {
            MaxCandidatesVisitedPerQuery = count;
        }
    }

    private void RemoveUnitAt(int index)
    {
        int lastIndex = _units.Count - 1;
        UnitEntry removed = _units[index];
        _unitIndices.Remove(removed.InstanceId);
        _nextSearchTimes.Remove(removed.InstanceId);

        if (index != lastIndex)
        {
            UnitEntry moved = _units[lastIndex];
            _units[index] = moved;
            _unitIndices[moved.InstanceId] = index;
        }
        _units.RemoveAt(lastIndex);
    }

    private void RemoveMonsterAt(int index)
    {
        int lastIndex = _monsters.Count - 1;
        MonsterEntry removed = _monsters[index];
        _monsterIndices.Remove(removed.InstanceId);
        _nextSearchTimes.Remove(removed.InstanceId);

        if (index != lastIndex)
        {
            MonsterEntry moved = _monsters[lastIndex];
            _monsters[index] = moved;
            _monsterIndices[moved.InstanceId] = index;
        }
        _monsters.RemoveAt(lastIndex);
    }
}

/// <summary>
/// Captures enough identity to reject a pooled NetworkBehaviour reused for a different NetworkObject.
/// </summary>
public readonly struct CombatTargetHandle
{
    public readonly MonoBehaviour Actor;
    public readonly Transform Transform;
    public readonly Collider Collider;
    public readonly NetworkRunner Runner;
    public readonly uint NetworkIdRaw;
    public readonly int InstanceId;
    public readonly int LifecycleGeneration;

    private CombatTargetHandle(
        MonoBehaviour actor,
        Transform transform,
        Collider collider,
        NetworkRunner runner,
        uint networkIdRaw,
        int instanceId,
        int lifecycleGeneration)
    {
        Actor = actor;
        Transform = transform;
        Collider = collider;
        Runner = runner;
        NetworkIdRaw = networkIdRaw;
        InstanceId = instanceId;
        LifecycleGeneration = lifecycleGeneration;
    }

    public bool IsCurrentLifecycle(MonoBehaviour expectedActor = null)
    {
        if (Actor == null || Transform == null || Actor.GetInstanceID() != InstanceId ||
            !Actor.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (expectedActor != null && Actor != expectedActor)
        {
            return false;
        }

        if (GetLifecycleGeneration(Actor) != LifecycleGeneration)
        {
            return false;
        }

        if (NetworkIdRaw == 0)
        {
            if (Actor is NetworkBehaviour offlineNetworkBehaviour &&
                offlineNetworkBehaviour.Object != null &&
                offlineNetworkBehaviour.Object.IsValid)
            {
                return false;
            }

            return true;
        }

        if (!(Actor is NetworkBehaviour networkBehaviour) ||
            networkBehaviour.Object == null ||
            !networkBehaviour.Object.IsValid)
        {
            return false;
        }

        return networkBehaviour.Runner == Runner && networkBehaviour.Object.Id.Raw == NetworkIdRaw;
    }

    public Vector3 ClosestPoint(Vector3 from)
    {
        return Collider != null ? Collider.ClosestPoint(from) : Transform != null ? Transform.position : from;
    }

    public static CombatTargetHandle Capture(MonoBehaviour actor, Collider collider = null)
    {
        if (actor == null)
        {
            return default;
        }

        NetworkBehaviour networkBehaviour = actor as NetworkBehaviour;
        NetworkObject networkObject = networkBehaviour != null ? networkBehaviour.Object : null;
        bool hasNetworkIdentity = networkObject != null && networkObject.IsValid;
        Collider resolvedCollider = collider != null ? collider : actor.GetComponent<Collider>();
        if (resolvedCollider == null)
        {
            resolvedCollider = actor.GetComponentInChildren<Collider>(true);
        }

        return new CombatTargetHandle(
            actor,
            actor.transform,
            resolvedCollider,
            hasNetworkIdentity ? networkBehaviour.Runner : null,
            hasNetworkIdentity ? networkObject.Id.Raw : 0,
            actor.GetInstanceID(),
            GetLifecycleGeneration(actor));
    }

    private static int GetLifecycleGeneration(MonoBehaviour actor)
    {
        if (actor is Monster monster)
        {
            return monster.CombatTargetLifecycleGeneration;
        }

        if (actor is Unit unit)
        {
            return unit.CombatTargetLifecycleGeneration;
        }

        return 0;
    }
}
