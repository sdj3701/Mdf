#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class UnitMeleeRangeEditModeTests
{
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void TryBlockMonsterRejectsSpawnTriggerOutsideBlockContact()
    {
        var unitData = CreateMeleeUnitData();
        var monsterData = CreateGroundMonsterData();
        var unitGo = new GameObject("unit");
        var monsterGo = new GameObject("monster");

        try
        {
            var unit = unitGo.AddComponent<Unit>();
            var monster = monsterGo.AddComponent<Monster>();
            InitializeUnitForRangeTest(unit, unitData, attackRange: 1f);
            InitializeMonsterForRangeTest(monster, monsterData, hp: 100f);

            unitGo.transform.position = Vector3.zero;
            monsterGo.transform.position = new Vector3(0.9f, 0f, 0f);

            Assert.That(InvokeMeleeAttackable(unit, monster), Is.True);
            Assert.That(unit.TryBlockMonster(monster), Is.False);
            Assert.That(monster.IsBlocked(), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(unitGo);
            Object.DestroyImmediate(monsterGo);
            Object.DestroyImmediate(unitData);
            Object.DestroyImmediate(monsterData);
        }
    }

    [Test]
    public void MeleeAttackabilityRejectsTargetsOutsideHorizontalReach()
    {
        var unitData = CreateMeleeUnitData();
        var monsterData = CreateGroundMonsterData();
        var unitGo = new GameObject("unit");
        var monsterGo = new GameObject("monster");

        try
        {
            var unit = unitGo.AddComponent<Unit>();
            var monster = monsterGo.AddComponent<Monster>();
            InitializeUnitForRangeTest(unit, unitData, attackRange: 1f);
            InitializeMonsterForRangeTest(monster, monsterData, hp: 100f);

            unitGo.transform.position = Vector3.zero;
            monsterGo.transform.position = new Vector3(1.05f, 5f, 0f);
            Assert.That(InvokeMeleeAttackable(unit, monster), Is.True);

            monsterGo.transform.position = new Vector3(1.25f, 0f, 0f);
            Assert.That(InvokeMeleeAttackable(unit, monster), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(unitGo);
            Object.DestroyImmediate(monsterGo);
            Object.DestroyImmediate(unitData);
            Object.DestroyImmediate(monsterData);
        }
    }

    private static UnitData CreateMeleeUnitData()
    {
        var data = ScriptableObject.CreateInstance<UnitData>();
        data.unitName = "test_melee";
        data.unitType = UnitType.Melee;
        data.attackRange = 1f;
        data.blockCount = 2;
        return data;
    }

    private static MonsterData CreateGroundMonsterData()
    {
        var data = ScriptableObject.CreateInstance<MonsterData>();
        data.monsterName = "test_ground";
        data.monsterType = MonsterType.Ground;
        data.traits = MonsterTraits.None;
        data.maxHealth = 100;
        return data;
    }

    private static void InitializeUnitForRangeTest(Unit unit, UnitData data, float attackRange)
    {
        SetPrivateField(unit, "unitData", data);
        SetPrivateField(unit, "_localAttackRange", attackRange);
    }

    private static void InitializeMonsterForRangeTest(Monster monster, MonsterData data, float hp)
    {
        SetPrivateField(monster, "_monsterData", data);
        SetPrivateField(monster, "_localHP", hp);
        SetPrivateField(monster, "_localMaxHP", hp);
        SetPrivateField(monster, "_hasLocalHealthValues", true);
    }

    private static bool InvokeMeleeAttackable(Unit unit, Monster monster)
    {
        var method = typeof(Unit).GetMethod("IsMeleeMonsterAttackable", InstancePrivate);
        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(unit, new object[] { monster });
    }

    private static void SetPrivateField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
    {
        var field = typeof(TTarget).GetField(fieldName, InstancePrivate);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(target, value);
    }
}
#endif
