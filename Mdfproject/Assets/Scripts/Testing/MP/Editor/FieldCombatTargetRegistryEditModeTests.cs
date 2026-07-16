using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class FieldCombatTargetRegistryEditModeTests
{
    private static readonly FieldInfo UnitDataField = typeof(Unit).GetField(
        "unitData",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo MonsterDataField = typeof(Monster).GetField(
        "_monsterData",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo MonsterSpawnGenerationField = typeof(Monster).GetField(
        "_spawnGeneration",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo MonsterAttackSpeedField = typeof(Monster).GetField(
        "_currentAttackSpeed",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo MonsterCurrentBlockerIdField = typeof(Monster).GetField(
        "currentBlockerId",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly List<Object> _createdObjects = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = _createdObjects.Count - 1; i >= 0; i--)
        {
            if (_createdObjects[i] != null)
            {
                Object.DestroyImmediate(_createdObjects[i]);
            }
        }
        _createdObjects.Clear();
    }

    [Test]
    public void RegistrationIsIdempotentAndSwapRemovalKeepsCountsCorrect()
    {
        var registry = new FieldCombatTargetRegistry(null);
        Unit first = CreateUnit(UnitType.Melee, Vector3.zero);
        Unit second = CreateUnit(UnitType.Ranged, Vector3.right);
        Monster monster = CreateMonster(MonsterType.Ground, Vector3.forward);

        registry.RegisterUnit(first);
        registry.RegisterUnit(first);
        registry.RegisterUnit(second);
        registry.RegisterMonster(monster);
        registry.RegisterMonster(monster);

        Assert.That(registry.RegisteredUnitCount, Is.EqualTo(2));
        Assert.That(registry.RegisteredMonsterCount, Is.EqualTo(1));

        registry.UnregisterUnit(first);
        Assert.That(registry.RegisteredUnitCount, Is.EqualTo(1));
        registry.UnregisterUnit(second);
        registry.UnregisterMonster(monster);

        Assert.That(registry.RegisteredUnitCount, Is.Zero);
        Assert.That(registry.RegisteredMonsterCount, Is.Zero);
    }

    [Test]
    public void GroundOnlySearchExcludesFlyingAndReturnsNearestValidMonster()
    {
        var registry = new FieldCombatTargetRegistry(null);
        Unit seeker = CreateUnit(UnitType.Melee, Vector3.zero);
        Monster flying = CreateMonster(MonsterType.Flying, new Vector3(0.5f, 0f, 0f));
        Monster ground = CreateMonster(MonsterType.Ground, new Vector3(2f, 0f, 0f));
        registry.RegisterMonster(flying);
        registry.RegisterMonster(ground);

        FieldCombatTargetRegistry.QueryStatus status = registry.FindNearestMonster(
            seeker,
            5f,
            true,
            ~0,
            out CombatTargetHandle handle);

        Assert.That(status, Is.EqualTo(FieldCombatTargetRegistry.QueryStatus.Found));
        Assert.That(handle.Actor, Is.SameAs(ground));

        status = registry.FindNearestMonster(seeker, 5f, false, ~0, out handle);
        Assert.That(status, Is.EqualTo(FieldCombatTargetRegistry.QueryStatus.Found));
        Assert.That(handle.Actor, Is.SameAs(flying));
    }

    [Test]
    public void MonsterSearchPreservesRangedUnitPriorityBeforeDistance()
    {
        var registry = new FieldCombatTargetRegistry(null);
        Monster seeker = CreateMonster(MonsterType.Ground, Vector3.zero, 10f);
        Unit closeMelee = CreateUnit(UnitType.Melee, new Vector3(1f, 0f, 0f));
        Unit fartherRanged = CreateUnit(UnitType.Ranged, new Vector3(4f, 0f, 0f));
        registry.RegisterUnit(closeMelee);
        registry.RegisterUnit(fartherRanged);

        FieldCombatTargetRegistry.QueryStatus status = registry.FindPriorityUnit(
            seeker,
            10f,
            ~0,
            out CombatTargetHandle handle);

        Assert.That(status, Is.EqualTo(FieldCombatTargetRegistry.QueryStatus.Found));
        Assert.That(handle.Actor, Is.SameAs(fartherRanged));

        fartherRanged.currentHP = 0f;
        status = registry.FindPriorityUnit(seeker, 10f, ~0, out handle);
        Assert.That(status, Is.EqualTo(FieldCombatTargetRegistry.QueryStatus.Found));
        Assert.That(handle.Actor, Is.SameAs(closeMelee));
    }

    [Test]
    public void SearchCadenceStaggersFirstQueryAndThrottlesFollowingQueries()
    {
        var registry = new FieldCombatTargetRegistry(null);
        GameObject requester = Track(new GameObject("TargetRequester"));
        const float start = 10f;
        const float interval = 0.15f;
        float delay = FieldCombatTargetRegistry.ComputeInitialSearchDelay(
            requester.GetInstanceID(),
            interval);

        Assert.That(delay, Is.GreaterThan(0f).And.LessThan(interval));
        Assert.That(registry.TryBeginSearch(requester, start, interval), Is.False);
        Assert.That(registry.TryBeginSearch(requester, start + delay + 0.0001f, interval), Is.True);
        Assert.That(registry.TryBeginSearch(requester, start + delay + 0.01f, interval), Is.False);
        Assert.That(registry.TryBeginSearch(requester, start + delay + interval + 0.001f, interval), Is.True);
    }

    [Test]
    public void TargetHandleRejectsInactiveLifecycleAndFieldIdsCannotCross()
    {
        Monster target = CreateMonster(MonsterType.Ground, Vector3.zero);
        CombatTargetHandle handle = CombatTargetHandle.Capture(target, target.GetComponent<Collider>());

        Assert.That(handle.IsCurrentLifecycle(target), Is.True);
        target.gameObject.SetActive(false);
        Assert.That(handle.IsCurrentLifecycle(target), Is.False);
        target.gameObject.SetActive(true);
        int generation = (int)MonsterSpawnGenerationField.GetValue(target);
        MonsterSpawnGenerationField.SetValue(target, generation + 1);
        Assert.That(handle.IsCurrentLifecycle(target), Is.False);

        Assert.That(FieldCombatTargetRegistry.FieldOwnerIdsMatch(3, 3), Is.True);
        Assert.That(FieldCombatTargetRegistry.FieldOwnerIdsMatch(3, 4), Is.False);
        Assert.That(FieldCombatTargetRegistry.FieldOwnerIdsMatch(-1, 3), Is.False);
    }

    [Test]
    public void BlockingAttackTargetValidationRejectsPooledTargetAfterHealthReset()
    {
        Monster target = CreateMonster(MonsterType.Ground, Vector3.zero);
        CombatTargetHandle handle = CombatTargetHandle.Capture(target, target.GetComponent<Collider>());
        MethodInfo validator = typeof(Monster).GetMethod(
            "IsBlockingAttackTargetCurrent",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(validator, Is.Not.Null);
        Assert.That((bool)validator.Invoke(null, new object[] { target, handle }), Is.True);

        target.gameObject.SetActive(false);
        target.currentHP = 100f;
        Assert.That(
            (bool)validator.Invoke(null, new object[] { target, handle }),
            Is.False,
            "an inactive pooled blocker must stay invalid even when pool reset restores its HP");
    }

    [Test]
    public void BlockingAttackLoopExitsAndReleasesPooledWallAfterHealthReset()
    {
        Monster attacker = CreateMonster(MonsterType.Ground, Vector3.zero);
        Monster pooledBlocker = CreateMonster(MonsterType.Ground, Vector3.forward);
        MonsterAttackSpeedField.SetValue(attacker, 1f);
        MonsterCurrentBlockerIdField.SetValue(attacker, pooledBlocker.GetInstanceID());
        MethodInfo attackLoopMethod = typeof(Monster).GetMethod(
            "AttackLoop",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(attackLoopMethod, Is.Not.Null);
        IEnumerator attackLoop = (IEnumerator)attackLoopMethod.Invoke(attacker, new object[] { pooledBlocker });
        Assert.That(attackLoop.MoveNext(), Is.True, "the first attack delay should begin");

        pooledBlocker.gameObject.SetActive(false);
        pooledBlocker.currentHP = 100f;

        Assert.That(attackLoop.MoveNext(), Is.False,
            "a pooled blocker must end the attack loop even when its reset HP is positive");
        Assert.That((int)MonsterCurrentBlockerIdField.GetValue(attacker), Is.Zero,
            "ending the stale attack loop must release the blocker before pathing resumes");
    }

    [Test]
    public void QueryRecoversFromWrongLayerRootColliderUsingMatchingChildCollider()
    {
        const int targetLayer = 13;
        var registry = new FieldCombatTargetRegistry(null);
        Unit seeker = CreateUnit(UnitType.Ranged, Vector3.zero);
        Monster target = CreateMonster(MonsterType.Ground, new Vector3(1f, 0f, 0f));
        target.GetComponent<Collider>().gameObject.layer = 0;

        var child = new GameObject("EnemyLayerCollider");
        child.layer = targetLayer;
        child.transform.SetParent(target.transform, false);
        child.AddComponent<SphereCollider>().radius = 0.3f;
        registry.RegisterMonster(target);

        FieldCombatTargetRegistry.QueryStatus status = registry.FindNearestMonster(
            seeker,
            5f,
            false,
            1 << targetLayer,
            out CombatTargetHandle handle);

        Assert.That(status, Is.EqualTo(FieldCombatTargetRegistry.QueryStatus.Found));
        Assert.That(handle.Actor, Is.SameAs(target));
        Assert.That(handle.Collider.gameObject.layer, Is.EqualTo(targetLayer));
    }

    private Unit CreateUnit(UnitType type, Vector3 position)
    {
        GameObject gameObject = Track(new GameObject($"Unit_{type}"));
        gameObject.transform.position = position;
        gameObject.AddComponent<SphereCollider>().radius = 0.25f;
        Unit unit = gameObject.AddComponent<Unit>();
        UnitData data = Track(ScriptableObject.CreateInstance<UnitData>());
        data.unitType = type;
        data.attackRange = 10f;
        data.baseHealth = 100f;
        UnitDataField.SetValue(unit, data);
        unit.maxHP = 100f;
        unit.currentHP = 100f;
        return unit;
    }

    private Monster CreateMonster(MonsterType type, Vector3 position, float attackRange = 5f)
    {
        GameObject gameObject = Track(new GameObject($"Monster_{type}"));
        gameObject.transform.position = position;
        gameObject.AddComponent<SphereCollider>().radius = 0.25f;
        Monster monster = gameObject.AddComponent<Monster>();
        MonsterData data = Track(ScriptableObject.CreateInstance<MonsterData>());
        data.monsterType = type;
        data.attackRange = attackRange;
        data.maxHealth = 100;
        MonsterDataField.SetValue(monster, data);
        monster.currentHP = 100f;
        return monster;
    }

    private T Track<T>(T created) where T : Object
    {
        _createdObjects.Add(created);
        return created;
    }
}
