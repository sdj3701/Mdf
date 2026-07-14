#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        AddressableAssetSettings addressables = AddressableAssetSettingsDefaultObject.Settings;

        ValidateAugments(augments, addressables, issues);
        ValidateSkills(skills, issues);
        ValidateCrossTypeIdentities(augments, skills, issues);
        ValidateImmutableIdentityRegistry(augments, skills, issues);
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

            switch (augment.effectType)
            {
                case EffectType.GrantMagicScroll:
                    if (augment.magicScrollData == null)
                    {
                        AddError(issues, "augment.scroll_missing", path, "GrantMagicScroll requires magicScrollData.");
                    }
                    else if (augment.magicScrollData.skillData == null)
                    {
                        AddError(issues, "augment.scroll_skill_missing", path, "Granted scroll requires skillData.");
                    }
                    break;

                case EffectType.SpawnMonsterOnEnemyField:
                    if (augment.isBossSummon)
                    {
                        if (augment.bossMonsterData == null)
                        {
                            AddError(issues, "augment.boss_missing", path, "Boss summon requires bossMonsterData.");
                        }
                    }
                    else if (augment.monsterSpawnEntries == null ||
                             !augment.monsterSpawnEntries.Any(entry => entry != null && entry.monsterData != null && entry.count > 0))
                    {
                        AddError(issues, "augment.spawn_entries_missing", path, "Monster summon requires at least one valid spawn entry.");
                    }
                    break;

                case EffectType.StrengthenMonsterType:
                    if (augment.strengthenedMonsterData == null)
                    {
                        AddError(issues, "augment.strengthened_monster_missing", path, "Monster strengthening requires strengthenedMonsterData.");
                    }
                    break;
            }
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

    private static void ValidateCrossTypeIdentities(
        IEnumerable<AugmentData> augments,
        IEnumerable<SkillData> skills,
        ICollection<ContentValidationIssue> issues)
    {
        var ids = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
        var hashes = new Dictionary<int, (string ContentId, UnityEngine.Object Asset)>();
        IEnumerable<UnityEngine.Object> assets = augments.Cast<UnityEngine.Object>().Concat(skills);
        foreach (UnityEngine.Object asset in assets)
        {
            string contentId = GetContentId(asset);
            if (string.IsNullOrWhiteSpace(contentId))
            {
                continue;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (ids.TryGetValue(contentId, out UnityEngine.Object duplicate))
            {
                AddError(issues, "content_id.cross_type_duplicate", assetPath,
                    $"Global contentId '{contentId}' is also used by {AssetDatabase.GetAssetPath(duplicate)}.");
            }
            else
            {
                ids.Add(contentId, asset);
            }

            int hash = StableDataKeyUtility.StableContentIdHash(contentId);
            if (hashes.TryGetValue(hash, out var existing) &&
                !string.Equals(existing.ContentId, contentId, StringComparison.Ordinal))
            {
                AddError(issues, "content_id.cross_type_hash_collision", assetPath,
                    $"'{contentId}' collides with '{existing.ContentId}' from {AssetDatabase.GetAssetPath(existing.Asset)} (hash={hash}).");
            }
            else
            {
                hashes[hash] = (contentId, asset);
            }
        }
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
        IEnumerable<AugmentData> augments,
        IEnumerable<SkillData> skills,
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
        ValidateRegisteredAssets(augments, "augment", registryByGuid, currentGuids, issues);
        ValidateRegisteredAssets(skills, "skill", registryByGuid, currentGuids, issues);

        foreach (ContentIdentityRegistryEntry entry in registry.entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.guid) || currentGuids.Contains(entry.guid.Trim()))
            {
                continue;
            }

            string currentPath = AssetDatabase.GUIDToAssetPath(entry.guid.Trim());
            AddError(issues, "content_id.registry_stale", IdentityRegistryPath,
                $"Registry entry '{entry.guid}' ({entry.contentId}) no longer resolves to a tracked AugmentData/SkillData asset. currentPath='{currentPath}'.");
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
        if (asset is AugmentData augment)
        {
            return augment.ContentId;
        }

        return asset is SkillData skill ? skill.ContentId : string.Empty;
    }

    private static void AddError(
        ICollection<ContentValidationIssue> issues,
        string code,
        string path,
        string message)
    {
        issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, code, path, message));
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
