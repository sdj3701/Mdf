#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public enum ContentValidationSeverity
{
    Warning,
    Error
}

public sealed class ContentValidationIssue
{
    public ContentValidationIssue(ContentValidationSeverity severity, string code, string assetPath, string message)
    {
        Severity = severity;
        Code = code;
        AssetPath = assetPath;
        Message = message;
    }

    public ContentValidationSeverity Severity { get; }
    public string Code { get; }
    public string AssetPath { get; }
    public string Message { get; }

    public override string ToString() => $"[{Severity}] {Code} {AssetPath}: {Message}";
}

/// <summary>
/// Validates identities directly from AssetDatabase so Addressable loads, network snapshots, and
/// host migration all use the same authored content set.
/// </summary>
public static class ContentIdentityValidator
{
    private const string AssetsRoot = "Assets";
    private const string IdentityRegistryPath = "ProjectSettings/MDFContentIdentityRegistry.json";
    private static readonly Regex ContentIdPattern =
        new Regex("^[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [MenuItem("Tools/MDF/Validate Content Identities")]
    public static void ValidateFromMenu()
    {
        IReadOnlyList<ContentValidationIssue> issues = ValidateProjectContent();
        foreach (ContentValidationIssue issue in issues)
        {
            if (issue.Severity == ContentValidationSeverity.Error)
            {
                Debug.LogError(issue);
            }
            else
            {
                Debug.LogWarning(issue);
            }
        }

        int errorCount = issues.Count(issue => issue.Severity == ContentValidationSeverity.Error);
        if (errorCount == 0)
        {
            Debug.Log($"[ContentIdentityValidator] PASS ({issues.Count} warning(s)).");
        }
        else
        {
            Debug.LogError($"[ContentIdentityValidator] FAIL ({errorCount} error(s), {issues.Count} total issue(s)).");
        }
    }

    public static IReadOnlyList<ContentValidationIssue> ValidateProjectContent()
    {
        var issues = new List<ContentValidationIssue>();
        AugmentData[] augments = LoadAssets<AugmentData>();
        SkillData[] skills = LoadAssets<SkillData>();
        UnitData[] units = LoadAssets<UnitData>();
        MonsterData[] monsters = LoadAssets<MonsterData>();
        MagicScrollData[] scrolls = LoadAssets<MagicScrollData>();
        KingUnitData[] kingUnits = LoadAssets<KingUnitData>();
        KingBuffData[] kingBuffs = LoadAssets<KingBuffData>();
        KingSkillData[] kingSkills = LoadAssets<KingSkillData>();
        WallLevelData[] wallLevels = LoadAssets<WallLevelData>();
        WallProgressionData[] wallProgressions = LoadAssets<WallProgressionData>();
        WaveDatabase[] waveDatabases = LoadAssets<WaveDatabase>();
        AddressableAssetSettings addressables = AddressableAssetSettingsDefaultObject.Settings;

        ValidateAugments(augments, addressables, issues);
        ValidateSkills(skills, issues);
        ValidateUnits(units, issues);
        ValidateMonsters(monsters, issues);
        ValidateMagicScrolls(scrolls, issues);
        ValidateKingData(kingUnits, kingBuffs, kingSkills, issues);
        ValidateWallData(wallLevels, wallProgressions, issues);
        ValidateWaveDatabases(waveDatabases, monsters, issues);
        ValidateAddressableAddresses(addressables, issues);
        ValidateFusionCapacities(augments, monsters, wallProgressions, waveDatabases, issues);

        UnityEngine.Object[] allIdentityAssets = augments.Cast<UnityEngine.Object>()
            .Concat(skills)
            .Concat(units)
            .Concat(monsters)
            .Concat(scrolls)
            .Concat(kingUnits)
            .Concat(kingBuffs)
            .Concat(kingSkills)
            .Concat(wallLevels)
            .Concat(wallProgressions)
            .Concat(waveDatabases)
            .ToArray();
        ValidateGlobalContentIdentities(allIdentityAssets, issues);
        ValidateImmutableIdentityRegistry(allIdentityAssets, issues);
        return issues;
    }

    private static T[] LoadAssets<T>() where T : UnityEngine.Object
    {
        return AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { AssetsRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<T>)
            .Where(asset => asset != null)
            .ToArray();
    }

    private static void ValidateAugments(
        IReadOnlyList<AugmentData> augments,
        AddressableAssetSettings addressables,
        ICollection<ContentValidationIssue> issues)
    {
        ValidateIdentityGroup(
            augments,
            augment => augment.ContentId,
            augment => augment.augmentName,
            "augment",
            issues);

        foreach (AugmentData augment in augments)
        {
            string path = AssetDatabase.GetAssetPath(augment);
            if (addressables == null)
            {
                AddError(issues, "addressables.settings_missing", path, "Addressable settings are unavailable.");
            }
            else
            {
                AddressableAssetEntry entry = addressables.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
                if (entry == null || !entry.labels.Contains("Augment"))
                {
                    AddError(issues, "augment.addressable_missing", path, "Augment must be Addressable with the 'Augment' label.");
                }
            }

            AugmentEffectCompositionValidator.Validate(augment, issues);
        }
    }

    private static void ValidateSkills(
        IReadOnlyList<SkillData> skills,
        ICollection<ContentValidationIssue> issues)
    {
        ValidateIdentityGroup(
            skills,
            skill => skill.ContentId,
            skill => skill.skillName,
            "skill",
            issues);

        foreach (SkillData skill in skills)
        {
            string path = AssetDatabase.GetAssetPath(skill);
            if (skill.targetingStrategy == null)
            {
                AddError(issues, "skill.targeting_missing", path, "Skill requires a targetingStrategy.");
            }

            if (skill.effects == null || skill.effects.Count == 0 || skill.effects.Any(effect => effect == null))
            {
                AddError(issues, "skill.effects_missing", path, "Skill requires a non-empty effect list without null entries.");
            }
        }
    }

    private static void ValidateUnits(
        IReadOnlyList<UnitData> units,
        ICollection<ContentValidationIssue> issues)
    {
        ValidateIdentityGroup(
            units,
            unit => unit.ContentId,
            unit => unit.unitName,
            "unit",
            issues);

        foreach (UnitData unit in units)
        {
            string path = AssetDatabase.GetAssetPath(unit);
            if (string.IsNullOrWhiteSpace(unit.unitIcon))
            {
                AddError(issues, "unit.icon_missing", path, "Unit requires an Addressable icon key.");
            }
            ValidateRequiredAddressableKeys(unit.prefabsByStarLevel, 3, "unit.prefab", path, issues);
            ValidateOptionalAllOrNoneAddressableKeys(unit.skillsByStarLevel, 3, "unit.skill", path, issues);
            if (unit.basicAttackVfxProfile == null)
            {
                AddError(issues, "unit.basic_attack_vfx_missing", path, "Unit requires a basicAttackVfxProfile.");
            }
            if (unit.cost < 0 || unit.baseHealth <= 0f || unit.attackSpeed <= 0f || unit.attackRange < 0f)
            {
                AddError(issues, "unit.core_stats_invalid", path,
                    "Unit cost/health/attackSpeed/attackRange must be within supported non-negative ranges.");
            }
        }
    }

    private static void ValidateMonsters(
        IReadOnlyList<MonsterData> monsters,
        ICollection<ContentValidationIssue> issues)
    {
        ValidateIdentityGroup(
            monsters,
            monster => monster.ContentId,
            monster => monster.monsterName,
            "monster",
            issues);

        foreach (MonsterData monster in monsters)
        {
            string path = AssetDatabase.GetAssetPath(monster);
            if (string.IsNullOrWhiteSpace(monster.monsterIcon))
            {
                AddError(issues, "monster.icon_missing", path, "Monster requires an Addressable icon key.");
            }
            if (string.IsNullOrWhiteSpace(monster.monsterPrefab))
            {
                AddError(issues, "monster.prefab_missing", path, "Monster requires an Addressable prefab key.");
            }
            if (monster.attackType == MonsterAttackType.Ranged && string.IsNullOrWhiteSpace(monster.projectilePrefab))
            {
                AddError(issues, "monster.projectile_missing", path,
                    "Ranged MonsterData requires an Addressable projectile prefab key.");
            }
            if (!monster.IsBoss && monster.blackMagicCost <= 0)
            {
                AddError(issues, "monster.black_magic_cost_invalid", path,
                    "Every non-boss attack-sequence monster requires a positive Black Magic cost.");
            }
            if (monster.maxHealth <= 0 || monster.attackSpeed <= 0f || monster.moveSpeed <= 0f)
            {
                AddError(issues, "monster.core_stats_invalid", path,
                    "Monster maxHealth, attackSpeed, and moveSpeed must be positive.");
            }
        }
    }

    private static void ValidateMagicScrolls(
        IReadOnlyList<MagicScrollData> scrolls,
        ICollection<ContentValidationIssue> issues)
    {
        ValidateIdentityGroup(
            scrolls,
            scroll => scroll.ContentId,
            scroll => scroll.scrollName,
            "scroll",
            issues);

        foreach (MagicScrollData scroll in scrolls)
        {
            string path = AssetDatabase.GetAssetPath(scroll);
            if (scroll.icon == null)
            {
                AddError(issues, "scroll.icon_missing", path, "Magic scroll requires an icon.");
            }
            if (scroll.skillData == null)
            {
                AddError(issues, "scroll.skill_missing", path, "Magic scroll requires skillData.");
            }
        }
    }

    private static void ValidateKingData(
        IReadOnlyList<KingUnitData> kingUnits,
        IReadOnlyList<KingBuffData> kingBuffs,
        IReadOnlyList<KingSkillData> kingSkills,
        ICollection<ContentValidationIssue> issues)
    {
        ValidateIdentityGroup(
            kingUnits,
            data => data.ContentId,
            data => data.baseUnitData != null ? data.baseUnitData.unitName : data.name,
            "king_unit",
            issues);
        ValidateIdentityGroup(kingBuffs, data => data.ContentId, data => data.buffName, "king_buff", issues);
        ValidateIdentityGroup(kingSkills, data => data.ContentId, data => data.skillName, "king_skill", issues);

        foreach (KingUnitData data in kingUnits)
        {
            string path = AssetDatabase.GetAssetPath(data);
            if (data.baseUnitData == null || data.kingBuff == null || data.kingSkill == null)
            {
                AddError(issues, "king_unit.required_reference_missing", path,
                    "KingUnitData requires baseUnitData, kingBuff, and kingSkill.");
            }
        }

        foreach (KingSelectionCatalog.Entry entry in KingSelectionCatalog.Entries)
        {
            KingUnitData[] matches = kingUnits.Where(data =>
                    data != null && string.Equals(data.name, entry.KingUnitKey, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                AddError(issues, "king_catalog.asset_resolution_invalid", entry.KingUnitKey,
                    $"Catalog entry '{entry.ContentId}' must resolve to exactly one KingUnitData asset; found {matches.Length}.");
                continue;
            }

            KingUnitData authoredData = matches[0];

            if (!string.Equals(authoredData.ContentId, entry.ContentId, StringComparison.Ordinal) ||
                authoredData.ContentIdHash != entry.KeyHash)
            {
                AddError(issues, "king_catalog.content_identity_mismatch", AssetDatabase.GetAssetPath(authoredData),
                    $"Catalog identity '{entry.ContentId}' ({entry.KeyHash}) must match KingUnitData.contentId " +
                    $"'{authoredData.ContentId}' ({authoredData.ContentIdHash}).");
            }

            if (entry.LegacyKeyHash != StableDataKeyUtility.StableKeyHash(entry.KingUnitKey))
            {
                AddError(issues, "king_catalog.legacy_alias_mismatch", AssetDatabase.GetAssetPath(authoredData),
                    "The compatibility alias must remain the shipped kingUnitKey hash.");
            }
        }

        foreach (KingBuffData data in kingBuffs)
        {
            string path = AssetDatabase.GetAssetPath(data);
            if (data.modifiers == null || data.modifiers.Length == 0)
            {
                AddError(issues, "king_buff.modifiers_missing", path,
                    "King buff requires at least one authored modifier.");
            }
        }

        foreach (KingSkillData data in kingSkills)
        {
            string path = AssetDatabase.GetAssetPath(data);
            if (string.IsNullOrWhiteSpace(data.iconKey))
            {
                AddError(issues, "king_skill.icon_missing", path,
                    "King skill requires an Addressable icon key.");
            }
            if (data.statusEffect != StatusEffectType.None && data.statusDuration <= 0f)
            {
                AddError(issues, "king_skill.status_duration_invalid", path,
                    "A king skill with a status effect requires a positive duration.");
            }
        }
    }

    private static void ValidateWallData(
        IReadOnlyList<WallLevelData> wallLevels,
        IReadOnlyList<WallProgressionData> wallProgressions,
        ICollection<ContentValidationIssue> issues)
    {
        ValidateIdentityGroup(
            wallLevels,
            data => data.ContentId,
            data => $"level_{data.Level}",
            "wall_level",
            issues);
        ValidateIdentityGroup(
            wallProgressions,
            data => data.ContentId,
            data => data.name,
            "wall_progression",
            issues);

        foreach (WallLevelData data in wallLevels)
        {
            string path = AssetDatabase.GetAssetPath(data);
            if (data.Level <= 0 || data.MaxHealth <= 0f)
            {
                AddError(issues, "wall_level.core_stats_invalid", path,
                    "Wall level and max health must be positive.");
            }
            if (data.VisualMesh == null || data.VisualMaterials == null || data.VisualMaterials.Length == 0
                || data.VisualMaterials.Any(material => material == null))
            {
                AddError(issues, "wall_level.visual_missing", path,
                    "Wall level requires a mesh and a non-empty material list without null entries.");
            }
        }

        foreach (WallProgressionData data in wallProgressions)
        {
            string path = AssetDatabase.GetAssetPath(data);
            if (!data.IsValid(out string reason))
            {
                AddError(issues, "wall_progression.invalid", path, reason);
            }
        }
    }

    private static void ValidateWaveDatabases(
        IReadOnlyList<WaveDatabase> waveDatabases,
        IReadOnlyList<MonsterData> monsters,
        ICollection<ContentValidationIssue> issues)
    {
        ValidateIdentityGroup(
            waveDatabases,
            data => data.ContentId,
            data => data.name,
            "wave_database",
            issues);

        foreach (WaveDatabase database in waveDatabases)
        {
            string path = AssetDatabase.GetAssetPath(database);
            if (database.rounds == null || database.rounds.Count == 0)
            {
                AddError(issues, "wave_database.rounds_missing", path,
                    "WaveDatabase requires at least one authored round.");
            }
            else
            {
                var roundNumbers = new HashSet<int>();
                foreach (RoundWaveData round in database.rounds)
                {
                    if (round == null || round.roundNumber <= 0 || !roundNumbers.Add(round.roundNumber))
                    {
                        AddError(issues, "wave_database.round_invalid", path,
                            "Rounds must be non-null and have unique positive round numbers.");
                        continue;
                    }
                    if (round.monsters == null || round.monsters.Count == 0
                        || round.monsters.Any(entry => entry == null || entry.monsterData == null || entry.count <= 0))
                    {
                        AddError(issues, "wave_database.entry_invalid", path,
                            $"Round {round.roundNumber} requires valid MonsterData/count entries.");
                    }
                }
            }

            var catalog = database.attackSequenceMonsterCatalog ?? new List<MonsterData>();
            if (catalog.Any(data => data == null || data.IsBoss)
                || catalog.Where(data => data != null).Distinct().Count() != catalog.Count)
            {
                AddError(issues, "wave_database.attack_catalog_invalid", path,
                    "Attack catalog must contain each non-boss MonsterData once and no null/boss entries.");
            }

            MonsterData[] expected = monsters.Where(data => data != null && !data.IsBoss).ToArray();
            MonsterData[] missing = expected.Where(data => !catalog.Contains(data)).ToArray();
            if (missing.Length > 0)
            {
                AddError(issues, "wave_database.attack_catalog_incomplete", path,
                    $"Attack catalog is missing: {string.Join(", ", missing.Select(data => data.name))}.");
            }
        }
    }

    private static void ValidateRequiredAddressableKeys(
        IReadOnlyList<string> keys,
        int requiredCount,
        string codePrefix,
        string assetPath,
        ICollection<ContentValidationIssue> issues)
    {
        if (keys == null || keys.Count < requiredCount)
        {
            AddError(issues, $"{codePrefix}_array_invalid", assetPath,
                $"Expected at least {requiredCount} authored keys.");
            return;
        }

        for (int i = 0; i < requiredCount; i++)
        {
            if (string.IsNullOrWhiteSpace(keys[i]))
            {
                AddError(issues, $"{codePrefix}_missing", assetPath,
                    $"Required key at index {i} is empty.");
            }
        }
    }

    private static void ValidateOptionalAllOrNoneAddressableKeys(
        IReadOnlyList<string> keys,
        int expectedCount,
        string codePrefix,
        string assetPath,
        ICollection<ContentValidationIssue> issues)
    {
        if (keys == null || keys.Count < expectedCount)
        {
            AddError(issues, $"{codePrefix}_array_invalid", assetPath,
                $"Expected at least {expectedCount} authored slots, even when this feature is unused.");
            return;
        }

        int populatedCount = 0;
        for (int i = 0; i < expectedCount; i++)
        {
            if (!string.IsNullOrWhiteSpace(keys[i]))
            {
                populatedCount++;
            }
        }

        if (populatedCount != 0 && populatedCount != expectedCount)
        {
            AddError(issues, $"{codePrefix}_partial", assetPath,
                $"Optional star-level data must populate either none or all {expectedCount} slots.");
        }
    }

    private static void ValidateIdentityGroup<T>(
        IReadOnlyList<T> assets,
        Func<T, string> contentIdSelector,
        Func<T, string> displayNameSelector,
        string kind,
        ICollection<ContentValidationIssue> issues)
        where T : UnityEngine.Object
    {
        var ids = new Dictionary<string, T>(StringComparer.Ordinal);
        var hashes = new Dictionary<int, T>();
        var names = new Dictionary<string, T>(StringComparer.Ordinal);

        foreach (T asset in assets)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            string contentId = StableDataKeyUtility.NormalizeContentId(contentIdSelector(asset));
            string displayName = displayNameSelector(asset)?.Trim();

            if (string.IsNullOrEmpty(contentId))
            {
                AddError(issues, $"{kind}.content_id_missing", path, "Immutable contentId is required.");
            }
            else
            {
                if (!ContentIdPattern.IsMatch(contentId))
                {
                    AddError(issues, $"{kind}.content_id_format", path, $"Invalid contentId '{contentId}'.");
                }

                if (ids.TryGetValue(contentId, out T duplicateId))
                {
                    AddError(issues, $"{kind}.content_id_duplicate", path,
                        $"contentId '{contentId}' is also used by {AssetDatabase.GetAssetPath(duplicateId)}.");
                }
                else
                {
                    ids.Add(contentId, asset);
                }

                int hash = StableDataKeyUtility.StableContentIdHash(contentId);
                if (hash == 0)
                {
                    AddError(issues, $"{kind}.content_id_zero_hash", path, $"contentId '{contentId}' hashes to zero.");
                }
                else if (hashes.TryGetValue(hash, out T collision) && contentIdSelector(collision) != contentId)
                {
                    AddError(issues, $"{kind}.content_id_hash_collision", path,
                        $"contentId '{contentId}' collides with {AssetDatabase.GetAssetPath(collision)} (hash={hash}).");
                }
                else
                {
                    hashes[hash] = asset;
                }
            }

            if (string.IsNullOrEmpty(displayName))
            {
                AddError(issues, $"{kind}.display_name_missing", path, "Display name is required.");
            }
            else if (names.TryGetValue(displayName, out T duplicateName))
            {
                AddError(issues, $"{kind}.display_name_duplicate", path,
                    $"Display name '{displayName}' is also used by {AssetDatabase.GetAssetPath(duplicateName)}.");
            }
            else
            {
                names.Add(displayName, asset);
            }
        }
    }

    private static void ValidateGlobalContentIdentities(
        IEnumerable<UnityEngine.Object> assets,
        ICollection<ContentValidationIssue> issues)
    {
        var ids = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
        var hashes = new Dictionary<int, (string Identity, UnityEngine.Object Asset, bool Canonical)>();
        foreach (UnityEngine.Object asset in assets ?? Enumerable.Empty<UnityEngine.Object>())
        {
            if (asset == null)
            {
                continue;
            }

            string contentId = StableDataKeyUtility.NormalizeContentId(GetContentId(asset));
            if (string.IsNullOrEmpty(contentId))
            {
                continue;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (ids.TryGetValue(contentId, out UnityEngine.Object duplicate) && duplicate != asset)
            {
                AddError(issues, "content_id.global_duplicate", assetPath,
                    $"Global contentId '{contentId}' is also used by {AssetDatabase.GetAssetPath(duplicate)}.");
            }
            else
            {
                ids[contentId] = asset;
            }

            int hash = StableDataKeyUtility.StableContentIdHash(contentId);
            if (hashes.TryGetValue(hash, out var collision)
                && (collision.Asset != asset ||
                    !string.Equals(collision.Identity, contentId, StringComparison.Ordinal)))
            {
                string collisionCode = collision.Canonical
                    ? "content_id.global_hash_collision"
                    : "content_id.legacy_hash_cross_collision";
                AddError(issues, collisionCode, assetPath,
                    $"contentId '{contentId}' collides with " +
                    $"{(collision.Canonical ? "contentId" : "legacy alias")} '{collision.Identity}' from " +
                    $"{AssetDatabase.GetAssetPath(collision.Asset)} (hash={hash}).");
            }
            else
            {
                hashes[hash] = (contentId, asset, true);
            }

            foreach (string legacyAlias in EnumerateLegacyIdentityAliases(asset)
                         .Where(alias => !string.IsNullOrWhiteSpace(alias))
                         .Select(StableDataKeyUtility.NormalizeKey)
                         .Where(alias => !string.IsNullOrEmpty(alias))
                         .Distinct(StringComparer.Ordinal))
            {
                int legacyHash = StableDataKeyUtility.StableKeyHash(legacyAlias);
                if (legacyHash == 0)
                {
                    AddError(issues, "content_id.legacy_alias_zero_hash", assetPath,
                        $"Legacy alias '{legacyAlias}' hashes to zero.");
                    continue;
                }

                if (hashes.TryGetValue(legacyHash, out var legacyCollision) &&
                    legacyCollision.Asset != asset)
                {
                    AddError(issues, "content_id.legacy_hash_cross_collision", assetPath,
                        $"Legacy alias '{legacyAlias}' collides with " +
                        $"{(legacyCollision.Canonical ? "contentId" : "legacy alias")} " +
                        $"'{legacyCollision.Identity}' from {AssetDatabase.GetAssetPath(legacyCollision.Asset)} " +
                        $"(hash={legacyHash}).");
                    continue;
                }

                if (!hashes.ContainsKey(legacyHash))
                {
                    hashes.Add(legacyHash, (legacyAlias, asset, false));
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateLegacyIdentityAliases(UnityEngine.Object asset)
    {
        switch (asset)
        {
            case UnitData unit:
                yield return unit.name;
                yield return unit.unitName;
                break;
            case MonsterData monster:
                yield return monster.name;
                yield return monster.monsterName;
                yield return monster.monsterPrefab;
                break;
            case AugmentData augment:
                yield return augment.name;
                yield return augment.augmentName;
                break;
            case MagicScrollData scroll:
                yield return scroll.name;
                yield return scroll.scrollName;
                break;
            case KingUnitData king:
                yield return king.name;
                break;
        }
    }

    private static void ValidateAddressableAddresses(
        AddressableAssetSettings addressables,
        ICollection<ContentValidationIssue> issues)
    {
        if (addressables == null)
        {
            AddError(issues, "addressables.settings_missing", string.Empty,
                "Addressable settings are unavailable, so global address uniqueness cannot be validated.");
            return;
        }

        var addressOwners = new Dictionary<string, AddressableAssetEntry>(StringComparer.Ordinal);
        foreach (AddressableAssetGroup group in addressables.groups.Where(group => group != null))
        {
            foreach (AddressableAssetEntry entry in group.entries.Where(entry => entry != null))
            {
                string address = entry.address?.Trim();
                if (string.IsNullOrEmpty(address))
                {
                    AddError(issues, "addressables.address_missing", AssetDatabase.GUIDToAssetPath(entry.guid),
                        $"Addressable entry in group '{group.Name}' has an empty address.");
                    continue;
                }

                if (addressOwners.TryGetValue(address, out AddressableAssetEntry duplicate)
                    && !string.Equals(duplicate.guid, entry.guid, StringComparison.OrdinalIgnoreCase))
                {
                    AddError(issues, "addressables.address_duplicate", AssetDatabase.GUIDToAssetPath(entry.guid),
                        $"Address '{address}' is also used by {AssetDatabase.GUIDToAssetPath(duplicate.guid)}.");
                }
                else
                {
                    addressOwners[address] = entry;
                }
            }
        }
    }

    private static void ValidateFusionCapacities(
        IReadOnlyCollection<AugmentData> augments,
        IReadOnlyCollection<MonsterData> monsters,
        IReadOnlyCollection<WallProgressionData> wallProgressions,
        IReadOnlyCollection<WaveDatabase> waveDatabases,
        ICollection<ContentValidationIssue> issues)
    {
        if (!CombatSchedulerCapacityConfig.ValidateConfiguration(out string schedulerReason))
        {
            AddError(issues, "fusion.combat_scheduler_capacity_invalid", nameof(CombatSchedulerCapacityConfig),
                schedulerReason ?? "CombatScheduler capacity configuration is invalid.");
        }

        int selectedAugmentCapacity = ReadPrivateConstant(
            typeof(PlayerManager), "SELECTED_AUGMENT_SNAPSHOT_CAPACITY", issues);
        int attackPoolCapacity = ReadPrivateConstant(
            typeof(PlayerManager), "ATTACK_POOL_SNAPSHOT_CAPACITY", issues);
        int augmentRuntimeCapacity = ReadPrivateConstant(
            typeof(PlayerManager), "AUGMENT_RUNTIME_MIGRATION_CAPACITY", issues);
        int wallHealthCapacity = ReadPrivateConstant(
            typeof(PlayerManager), "WALL_HEALTH_MIGRATION_CAPACITY", issues);
        int survivorBossCapacity = ReadPrivateConstant(
            typeof(GameManagers), "SURVIVOR_BOSS_PAYLOAD_CAPACITY", issues);

        if (selectedAugmentCapacity > 0 && augments.Count > selectedAugmentCapacity)
        {
            AddError(issues, "fusion.selected_augment_capacity_exceeded", nameof(PlayerManager),
                $"Authored augment count {augments.Count} exceeds selected snapshot capacity {selectedAugmentCapacity}.");
        }
        if (augmentRuntimeCapacity > 0 && augments.Count > augmentRuntimeCapacity)
        {
            AddError(issues, "fusion.augment_runtime_capacity_exceeded", nameof(PlayerManager),
                $"Authored augment count {augments.Count} exceeds migration capacity {augmentRuntimeCapacity}.");
        }
        if (attackPoolCapacity > 0 && monsters.Count > attackPoolCapacity)
        {
            AddError(issues, "fusion.attack_pool_capacity_exceeded", nameof(PlayerManager),
                $"Authored MonsterData count {monsters.Count} exceeds attack pool capacity {attackPoolCapacity}.");
        }
        if (attackPoolCapacity > 0)
        {
            foreach (WaveDatabase database in waveDatabases)
            {
                int catalogCount = database?.attackSequenceMonsterCatalog?.Count ?? 0;
                if (catalogCount > attackPoolCapacity)
                {
                    AddError(issues, "fusion.attack_catalog_capacity_exceeded", AssetDatabase.GetAssetPath(database),
                        $"Attack catalog count {catalogCount} exceeds snapshot capacity {attackPoolCapacity}.");
                }
            }
        }

        int defaultFieldCells = CombatSchedulerCapacityConfig.DefaultFieldGridWidth
                                * CombatSchedulerCapacityConfig.DefaultFieldGridHeight;
        if (wallHealthCapacity > 0 && defaultFieldCells > wallHealthCapacity)
        {
            AddError(issues, "fusion.wall_health_capacity_exceeded", nameof(PlayerManager),
                $"Default field can contain {defaultFieldCells} wall cells, exceeding migration capacity {wallHealthCapacity}.");
        }
        if (wallProgressions.Count == 0)
        {
            AddError(issues, "fusion.wall_progression_missing", nameof(WallProgressionData),
                "At least one validated wall progression is required for wall migration.");
        }

        if (survivorBossCapacity > 0)
        {
            AddWarning(issues, "fusion.survivor_boss_dynamic_bound", nameof(GameManagers),
                $"Survivor boss ownership is dynamically repeatable and cannot be proven below the fixed capacity " +
                $"{survivorBossCapacity}; runtime overflow must remain a hard migration failure.");
        }
        if (attackPoolCapacity > 0)
        {
            AddWarning(issues, "fusion.attack_pool_dynamic_bound", nameof(PlayerManager),
                $"Repeated boss augments can grow runtime ownership beyond the authored catalog; runtime overflow " +
                $"handling must remain enabled for the fixed capacity {attackPoolCapacity}.");
        }
    }

    private static int ReadPrivateConstant(
        Type declaringType,
        string fieldName,
        ICollection<ContentValidationIssue> issues)
    {
        FieldInfo field = declaringType.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        if (field == null || !field.IsLiteral || field.FieldType != typeof(int))
        {
            AddError(issues, "fusion.capacity_constant_missing", declaringType.Name,
                $"Expected private const int {fieldName}; validator and Fusion schema have drifted.");
            return -1;
        }

        return (int)field.GetRawConstantValue();
    }

    [Serializable]
    private sealed class ContentIdentityRegistry
    {
        public int version = 0;
        public List<ContentIdentityRegistryEntry> entries = new List<ContentIdentityRegistryEntry>();
    }

    [Serializable]
    private sealed class ContentIdentityRegistryEntry
    {
        public string guid = string.Empty;
        public string kind = string.Empty;
        public string contentId = string.Empty;
        public string assetPath = string.Empty;
    }

    private static void ValidateImmutableIdentityRegistry(
        IEnumerable<UnityEngine.Object> assets,
        ICollection<ContentValidationIssue> issues)
    {
        string registryAbsolutePath = Path.Combine(
            Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
            IdentityRegistryPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(registryAbsolutePath))
        {
            AddError(issues, "content_id.registry_missing", IdentityRegistryPath,
                "The committed GUID-to-contentId registry is required to detect identity changes after release.");
            return;
        }

        ContentIdentityRegistry registry;
        try
        {
            registry = JsonUtility.FromJson<ContentIdentityRegistry>(File.ReadAllText(registryAbsolutePath));
        }
        catch (Exception exception)
        {
            AddError(issues, "content_id.registry_invalid_json", IdentityRegistryPath, exception.Message);
            return;
        }

        if (registry == null || registry.version != 1 || registry.entries == null)
        {
            AddError(issues, "content_id.registry_invalid_schema", IdentityRegistryPath,
                "Expected registry version 1 with an entries array.");
            return;
        }

        var registryByGuid = new Dictionary<string, ContentIdentityRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (ContentIdentityRegistryEntry entry in registry.entries)
        {
            string guid = entry?.guid?.Trim();
            if (string.IsNullOrEmpty(guid))
            {
                AddError(issues, "content_id.registry_guid_missing", IdentityRegistryPath,
                    "Every registry entry requires an asset GUID.");
                continue;
            }

            if (registryByGuid.ContainsKey(guid))
            {
                AddError(issues, "content_id.registry_guid_duplicate", IdentityRegistryPath,
                    $"Asset GUID '{guid}' appears more than once.");
            }
            else
            {
                registryByGuid.Add(guid, entry);
            }
        }

        var currentGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        UnityEngine.Object[] identityAssets = (assets ?? Enumerable.Empty<UnityEngine.Object>())
            .Where(asset => asset != null)
            .ToArray();
        foreach (IGrouping<string, UnityEngine.Object> group in identityAssets.GroupBy(GetContentKind))
        {
            ValidateRegisteredAssets(group, group.Key, registryByGuid, currentGuids, issues);
        }

        foreach (ContentIdentityRegistryEntry entry in registry.entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.guid) || currentGuids.Contains(entry.guid.Trim()))
            {
                continue;
            }

            string currentPath = AssetDatabase.GUIDToAssetPath(entry.guid.Trim());
            AddError(issues, "content_id.registry_stale", IdentityRegistryPath,
                $"Registry entry '{entry.guid}' ({entry.contentId}) no longer resolves to a tracked identity asset. currentPath='{currentPath}'.");
        }
    }

    private static void ValidateRegisteredAssets<T>(
        IEnumerable<T> assets,
        string kind,
        IReadOnlyDictionary<string, ContentIdentityRegistryEntry> registryByGuid,
        ISet<string> currentGuids,
        ICollection<ContentValidationIssue> issues)
        where T : UnityEngine.Object
    {
        foreach (T asset in assets)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            currentGuids.Add(guid);
            string contentId = GetContentId(asset);

            if (!registryByGuid.TryGetValue(guid, out ContentIdentityRegistryEntry registered))
            {
                AddError(issues, "content_id.registry_entry_missing", assetPath,
                    $"New {kind} content must be explicitly added to {IdentityRegistryPath} before build.");
                continue;
            }

            string registeredId = StableDataKeyUtility.NormalizeContentId(registered.contentId);
            if (!string.Equals(registeredId, contentId, StringComparison.Ordinal))
            {
                AddError(issues, "content_id.immutable_id_changed", assetPath,
                    $"Committed contentId is '{registeredId}', but the asset now contains '{contentId}'. Add a new asset/GUID instead of changing a shipped identity.");
            }

            if (!string.Equals(registered.kind?.Trim(), kind, StringComparison.OrdinalIgnoreCase))
            {
                AddError(issues, "content_id.registry_kind_mismatch", assetPath,
                    $"Registry kind is '{registered.kind}', expected '{kind}'.");
            }
        }
    }

    private static string GetContentId(UnityEngine.Object asset)
    {
        if (asset is IStableContentIdentity stableIdentity)
        {
            return stableIdentity.ContentId;
        }

        return string.Empty;
    }

    private static string GetContentKind(UnityEngine.Object asset)
    {
        if (asset is AugmentData) return "augment";
        if (asset is SkillData) return "skill";
        if (asset is UnitData) return "unit";
        if (asset is MonsterData) return "monster";
        if (asset is MagicScrollData) return "scroll";
        if (asset is KingUnitData) return "king_unit";
        if (asset is KingBuffData) return "king_buff";
        if (asset is KingSkillData) return "king_skill";
        if (asset is WallLevelData) return "wall_level";
        if (asset is WallProgressionData) return "wall_progression";
        if (asset is WaveDatabase) return "wave_database";
        return "unknown";
    }

    private static void AddError(
        ICollection<ContentValidationIssue> issues,
        string code,
        string path,
        string message)
    {
        issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, code, path, message));
    }

    private static void AddWarning(
        ICollection<ContentValidationIssue> issues,
        string code,
        string path,
        string message)
    {
        issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, code, path, message));
    }
}

public sealed class ContentIdentityBuildValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        ContentValidationIssue[] errors = ContentIdentityValidator.ValidateProjectContent()
            .Where(issue => issue.Severity == ContentValidationSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
        {
            throw new BuildFailedException(
                "Content identity validation failed:\n" + string.Join("\n", errors.Select(issue => issue.ToString())));
        }
    }
}
#endif
