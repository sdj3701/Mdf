#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public sealed class UnitFirstPurchasePrewarmEditModeTests
{
    private const string PlayerRootPath = "Assets/Prefabs/Player_Root.prefab";
    private const string StatusBarPath = "Assets/Prefabs/UI/Unit/UI_Can_StatusBar.prefab";
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    [Test]
    public void DependencyCollectorDeduplicatesEveryStarSkillAndBasicAttackAddress()
    {
        UnitData unit = ScriptableObject.CreateInstance<UnitData>();
        BasicAttackVfxProfile profile = ScriptableObject.CreateInstance<BasicAttackVfxProfile>();
        try
        {
            unit.prefabsByStarLevel = new[] { " UnitB ", "UnitA", "UnitB" };
            unit.skillsByStarLevel = new[] { " SkillB ", "SkillA", "SkillB" };
            profile.slashConfigsByStarLevel = new[]
            {
                BasicAttackVfxConfig.CreateDefault(" SlashB "),
                BasicAttackVfxConfig.CreateDefault("SlashA"),
                BasicAttackVfxConfig.CreateDefault("SlashB")
            };
            profile.projectileVfxConfig = ProjectileVfxConfig.CreateDefault(" Projectile ");
            profile.projectileVfxConfig.muzzleFlashKey = " Muzzle ";
            profile.projectileVfxConfig.impactFlashKey = "Impact";
            unit.basicAttackVfxProfile = profile;

            object keys = CollectDependencyKeys(new[] { unit });
            CollectionAssert.AreEqual(new[] { "UnitA", "UnitB" }, ReadKeyList(keys, "UnitPrefabKeys"));
            CollectionAssert.AreEqual(new[] { "SkillA", "SkillB" }, ReadKeyList(keys, "SkillDataKeys"));
            CollectionAssert.AreEqual(
                new[] { "Impact", "Muzzle", "Projectile", "SlashA", "SlashB" },
                ReadKeyList(keys, "BasicAttackVfxKeys"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(unit);
        }
    }

    [Test]
    public void BootCatalogPrewarmKeysResolveAndSkillDirectPresentationsAreDiscoverable()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        Assert.That(settings, Is.Not.Null);

        List<AddressableAssetEntry> entries = settings.groups
            .Where(group => group != null)
            .SelectMany(group => group.entries)
            .ToList();
        List<UnitData> bootUnits = entries
            .Where(entry => entry.labels.Contains("mdf-boot-data"))
            .Select(entry => AssetDatabase.LoadAssetAtPath<UnitData>(AssetDatabase.GUIDToAssetPath(entry.guid)))
            .Where(unit => unit != null)
            .ToList();
        Assert.That(bootUnits, Is.Not.Empty);

        object keys = CollectDependencyKeys(bootUnits);
        IReadOnlyList<string> prefabKeys = ReadKeyList(keys, "UnitPrefabKeys");
        IReadOnlyList<string> skillKeys = ReadKeyList(keys, "SkillDataKeys");
        IReadOnlyList<string> attackVfxKeys = ReadKeyList(keys, "BasicAttackVfxKeys");
        Assert.That(prefabKeys, Is.Not.Empty);
        Assert.That(skillKeys, Is.Not.Empty);
        Assert.That(attackVfxKeys, Is.Not.Empty);

        AssertAddressesResolve<GameObject>(entries, prefabKeys);
        AssertAddressesResolve<GameObject>(entries, attackVfxKeys);

        int directSkillPresentationCount = 0;
        foreach (string skillKey in skillKeys)
        {
            SkillData skill = ResolveAddress<SkillData>(entries, skillKey);
            Assert.That(skill, Is.Not.Null, skillKey);
            List<GameObject> directPrefabs = CollectSkillPresentationPrefabs(skill);
            directSkillPresentationCount += directPrefabs.Count;
            foreach (GameObject prefab in directPrefabs)
            {
                Assert.That(AssetDatabase.GetAssetPath(prefab), Is.Not.Empty,
                    $"{skillKey} direct presentation must be retained by its SkillData dependency graph");
            }
        }

        Assert.That(directSkillPresentationCount, Is.GreaterThan(0),
            "the boot skills currently include directly referenced VFX/zone prefabs that need shader warmup");
    }

    [Test]
    public void SkillPresentationCollectorHandlesDirectVfxNestedZoneAndCycles()
    {
        SkillData skill = ScriptableObject.CreateInstance<SkillData>();
        ZoneEffect zone = ScriptableObject.CreateInstance<ZoneEffect>();
        var directVfx = new GameObject("DirectSkillVfx");
        var zoneVfx = new GameObject("ZoneVfx");
        try
        {
            skill.vfxPrefab = directVfx;
            skill.effects = new List<SkillEffect> { zone };
            zone.zonePrefab = zoneVfx;
            zone.effectsPerTick = new List<SkillEffect> { zone };

            List<GameObject> presentations = CollectSkillPresentationPrefabs(skill);
            CollectionAssert.AreEquivalent(new[] { directVfx, zoneVfx }, presentations);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(directVfx);
            UnityEngine.Object.DestroyImmediate(zoneVfx);
            UnityEngine.Object.DestroyImmediate(zone);
            UnityEngine.Object.DestroyImmediate(skill);
        }
    }

    [Test]
    public void AttachStatusBarReusesAndRebindsTheExistingPooledChild()
    {
        var managerObject = new GameObject("StatusBarFieldManagerTest");
        var unitObject = new GameObject("PooledUnitStatusBarTest");
        unitObject.SetActive(false);
        var statusBarObject = new GameObject("ExistingStatusBar");
        statusBarObject.transform.SetParent(unitObject.transform, false);
        try
        {
            FieldManager fieldManager = managerObject.AddComponent<FieldManager>();
            Unit unit = unitObject.AddComponent<Unit>();
            StatusBarUI existing = statusBarObject.AddComponent<StatusBarUI>();
            MethodInfo attach = typeof(FieldManager).GetMethod(
                "AttachStatusBar",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(attach, Is.Not.Null);

            StatusBarUI first = (StatusBarUI)attach.Invoke(
                fieldManager,
                new object[] { unitObject, new Action<StatusBarUI>(unit.SetStatusBar) });
            StatusBarUI second = (StatusBarUI)attach.Invoke(
                fieldManager,
                new object[] { unitObject, new Action<StatusBarUI>(unit.SetStatusBar) });

            Assert.That(first, Is.SameAs(existing));
            Assert.That(second, Is.SameAs(existing));
            Assert.That(unitObject.GetComponentsInChildren<StatusBarUI>(true), Has.Length.EqualTo(1));

            FieldInfo boundStatusBar = typeof(Unit).GetField(
                "statusBarUI",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(boundStatusBar, Is.Not.Null);
            Assert.That(boundStatusBar.GetValue(unit), Is.SameAs(existing));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(unitObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void StatusBarReserveRemovesInstantiateFromTheFirstAttachAndStaysBounded()
    {
        var managerObject = new GameObject("StatusBarReserveFieldManagerTest");
        var prefabObject = new GameObject("StatusBarReservePrefab");
        var unitObject = new GameObject("FirstPurchasedUnit");
        prefabObject.SetActive(false);
        unitObject.SetActive(false);
        try
        {
            StatusBarUI prefabStatusBar = prefabObject.AddComponent<StatusBarUI>();
            Assert.That(prefabStatusBar, Is.Not.Null);
            FieldManager fieldManager = managerObject.AddComponent<FieldManager>();
            fieldManager.statusBarPrefab = prefabObject;
            Unit unit = unitObject.AddComponent<Unit>();

            MethodInfo prewarm = typeof(FieldManager).GetMethod(
                "PrewarmStatusBarReserve",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo attach = typeof(FieldManager).GetMethod(
                "AttachStatusBar",
                BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo available = typeof(FieldManager).GetProperty(
                "AvailablePrewarmedStatusBarCount",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(prewarm, Is.Not.Null);
            Assert.That(attach, Is.Not.Null);
            Assert.That(available, Is.Not.Null);

            prewarm.Invoke(fieldManager, null);
            int beforeAttach = (int)available.GetValue(fieldManager);
            Assert.That(beforeAttach, Is.EqualTo(4));

            StatusBarUI first = (StatusBarUI)attach.Invoke(
                fieldManager,
                new object[] { unitObject, new Action<StatusBarUI>(unit.SetStatusBar) });
            Assert.That(first, Is.Not.Null);
            Assert.That(first.transform.parent, Is.SameAs(unitObject.transform));
            Assert.That((int)available.GetValue(fieldManager), Is.EqualTo(beforeAttach - 1));

            StatusBarUI reused = (StatusBarUI)attach.Invoke(
                fieldManager,
                new object[] { unitObject, new Action<StatusBarUI>(unit.SetStatusBar) });
            Assert.That(reused, Is.SameAs(first));
            Assert.That((int)available.GetValue(fieldManager), Is.EqualTo(beforeAttach - 1),
                "re-binding a pooled unit must not consume or instantiate another status bar");
            Assert.That(unitObject.GetComponentsInChildren<StatusBarUI>(true), Has.Length.EqualTo(1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(unitObject);
            UnityEngine.Object.DestroyImmediate(prefabObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void UnitNetworkPoolDefaultsToFourPlayersAndClampsToItsConfiguredBound()
    {
        var managerObject = new GameObject("LoadManagerPoolTargetTest");
        managerObject.SetActive(false);
        try
        {
            LoadManager manager = managerObject.AddComponent<LoadManager>();
            Assert.That(manager.ResolveUnitNetworkPoolTarget(null), Is.EqualTo(4));

            FieldInfo maximum = typeof(LoadManager).GetField(
                "maximumUnitNetworkPoolTargetPerPrefab",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(maximum, Is.Not.Null);
            maximum.SetValue(manager, 3);
            Assert.That(manager.ResolveUnitNetworkPoolTarget(null), Is.EqualTo(3));

            string loadSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/LoadManager.cs");
            Assert.That(loadSource, Does.Contain("runner.ActivePlayers.Count()"));
            Assert.That(loadSource, Does.Contain("Mathf.Clamp(targetFreeCountPerPrefab, 1, configuredMaximum)"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void PresentationWarmupMetadataKeepsOnlyTheLatestCompletedPrefabGeneration()
    {
        string source = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/RuntimeAssets/FirstSpawnPresentationPrewarmer.cs");

        Assert.That(source, Does.Contain("LatestGenerationKeyBySemanticKey"));
        Assert.That(source, Does.Contain("PruneCompletedOlderGenerations(semanticKey, key)"));
        Assert.That(source, Does.Contain("candidate.Succeeded"));
        Assert.That(source, Does.Contain("PruneCompletedOlderGenerations(entry.SemanticKey, latestKey)"));
        Assert.That(source, Does.Contain("LatestGenerationKeyBySemanticKey.Clear()"));
    }

    [Test]
    public void UnitNetworkPoolMetadataPrunesStoppedRunnersBetweenSessions()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/LoadManager.cs");

        int prewarmEntry = source.IndexOf(
            "public async UniTask<int> PrewarmUnitNetworkPoolAsync",
            StringComparison.Ordinal);
        int pruneBeforeWork = source.IndexOf(
            "PruneStoppedUnitNetworkPoolPrewarms();",
            prewarmEntry,
            StringComparison.Ordinal);
        int runnerGuard = source.IndexOf(
            "if (runner == null || !runner.IsRunning)",
            prewarmEntry,
            StringComparison.Ordinal);
        Assert.That(prewarmEntry, Is.GreaterThanOrEqualTo(0));
        Assert.That(pruneBeforeWork, Is.GreaterThan(prewarmEntry));
        Assert.That(runnerGuard, Is.GreaterThan(pruneBeforeWork));
        Assert.That(source, Does.Contain("cachedRunner != null && cachedRunner.IsRunning"));
        Assert.That(source, Does.Contain("_unitNetworkPoolPrewarms.Remove(stoppedRunner)"));
        Assert.That(source, Does.Contain("_unitNetworkPoolPrewarms.Remove(runner)"));
    }

    [Test]
    public void StatusBarPrefabIsAlreadyALoadedPlayerRootDependencyAndReuseIsCentralized()
    {
        GameObject playerRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRootPath);
        GameObject expectedStatusBar = AssetDatabase.LoadAssetAtPath<GameObject>(StatusBarPath);
        Assert.That(playerRoot, Is.Not.Null);
        Assert.That(expectedStatusBar, Is.Not.Null);

        FieldManager[] fieldManagers = playerRoot.GetComponentsInChildren<FieldManager>(true);
        Assert.That(fieldManagers, Is.Not.Empty);
        Assert.That(fieldManagers.All(manager => manager.statusBarPrefab == expectedStatusBar), Is.True);
        CollectionAssert.Contains(AssetDatabase.GetDependencies(PlayerRootPath, true), StatusBarPath);

        string fieldSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/FieldManager.cs");
        string playerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.cs");
        string registerSource = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Commands/Sync/RegisterUnitAtCommand.cs");
        string unitSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Game/Units/Unit.cs");
        string loadSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/LoadManager.cs");

        Assert.That(fieldSource, Does.Contain("GetComponentInChildren<StatusBarUI>(includeInactive: true)"));
        Assert.That(fieldSource, Does.Contain("TakePrewarmedStatusBar(host.transform)"));
        Assert.That(fieldSource, Does.Contain("statusBarPrewarmCount = 4"));
        Assert.That(fieldSource, Does.Contain("statusBarUI.ResetForReuse"));
        Assert.That(playerSource, Does.Contain("fieldManager.AttachStatusBar(unit.gameObject, unit.SetStatusBar)"));
        Assert.That(registerSource, Does.Contain("player.fieldManager.AttachStatusBar(unit.gameObject, unit.SetStatusBar)"));
        Assert.That(unitSource, Does.Contain("statusBarUI?.ResetForReuse(initializeImmediately: false)"));
        Assert.That(loadSource, Does.Contain("AssetLoader.LoadAssetAsync<SkillData>"));
        Assert.That(loadSource, Does.Contain("dependencies.BasicAttackVfxKeys"));
        Assert.That(loadSource, Does.Contain("CollectSkillPresentationPrefabs"));
        Assert.That(loadSource, Does.Contain("_unitPrefabAssets.RetainedAssetCount"));
    }

    private static object CollectDependencyKeys(IEnumerable<UnitData> units)
    {
        MethodInfo collector = typeof(LoadManager).GetMethod(
            "CollectUnitPresentationDependencyKeys",
            StaticPrivate);
        Assert.That(collector, Is.Not.Null);
        return collector.Invoke(null, new object[] { units });
    }

    private static IReadOnlyList<string> ReadKeyList(object dependencyKeys, string fieldName)
    {
        Assert.That(dependencyKeys, Is.Not.Null);
        FieldInfo field = dependencyKeys.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(field, Is.Not.Null, fieldName);
        return (IReadOnlyList<string>)field.GetValue(dependencyKeys);
    }

    private static List<GameObject> CollectSkillPresentationPrefabs(SkillData skill)
    {
        MethodInfo collector = typeof(LoadManager).GetMethod(
            "CollectSkillPresentationPrefabs",
            StaticPrivate);
        Assert.That(collector, Is.Not.Null);
        return (List<GameObject>)collector.Invoke(null, new object[] { skill });
    }

    private static void AssertAddressesResolve<T>(
        IReadOnlyList<AddressableAssetEntry> entries,
        IEnumerable<string> addresses)
        where T : UnityEngine.Object
    {
        foreach (string address in addresses)
        {
            Assert.That(ResolveAddress<T>(entries, address), Is.Not.Null, address);
        }
    }

    private static T ResolveAddress<T>(IReadOnlyList<AddressableAssetEntry> entries, string address)
        where T : UnityEngine.Object
    {
        AddressableAssetEntry entry = entries.FirstOrDefault(candidate =>
            string.Equals(candidate.address, address, StringComparison.Ordinal));
        if (entry == null)
        {
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(entry.guid));
    }
}
#endif
