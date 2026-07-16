using System;
using System.Collections.Generic;

/// <summary>
/// Canonical demon definitions shared by lobby validation, gameplay and diagnostics.
/// Lobby transport keeps the selected hash owner-private; gameplay publishes only the
/// allow-listed hash after the match scene has started.
/// </summary>
public static class DemonSelectionCatalog
{
    public const string PlayerPrefsKey = "SelectedDemonKeyHash";

    public readonly struct Entry
    {
        public Entry(
            string contentId,
            string displayName,
            string iconKey,
            string passiveDescription,
            string skillName,
            string skillDescription,
            float passiveHealthMultiplier,
            float passiveMoveSpeedMultiplier,
            float passiveDamageMultiplier,
            float skillHealthMultiplier,
            float skillMoveSpeedMultiplier,
            float skillDamageMultiplier,
            float skillHealFraction)
        {
            ContentId = StableDataKeyUtility.NormalizeContentId(contentId);
            DisplayName = displayName;
            IconKey = iconKey;
            PassiveDescription = passiveDescription;
            SkillName = skillName;
            SkillDescription = skillDescription;
            PassiveHealthMultiplier = passiveHealthMultiplier;
            PassiveMoveSpeedMultiplier = passiveMoveSpeedMultiplier;
            PassiveDamageMultiplier = passiveDamageMultiplier;
            SkillHealthMultiplier = skillHealthMultiplier;
            SkillMoveSpeedMultiplier = skillMoveSpeedMultiplier;
            SkillDamageMultiplier = skillDamageMultiplier;
            SkillHealFraction = skillHealFraction;
            KeyHash = StableDataKeyUtility.StableContentIdHash(ContentId);
        }

        public string ContentId { get; }
        public string DisplayName { get; }
        public string IconKey { get; }
        public string PassiveDescription { get; }
        public string SkillName { get; }
        public string SkillDescription { get; }
        public float PassiveHealthMultiplier { get; }
        public float PassiveMoveSpeedMultiplier { get; }
        public float PassiveDamageMultiplier { get; }
        public float SkillHealthMultiplier { get; }
        public float SkillMoveSpeedMultiplier { get; }
        public float SkillDamageMultiplier { get; }
        public float SkillHealFraction { get; }
        public int KeyHash { get; }
    }

    private static readonly Entry[] CatalogEntries =
    {
        new Entry(
            "demon.blood_lord", "혈군", "Spr_Port_EvilMage",
            "소환 몬스터 최대 체력 +20%", "피의 만찬", "현재 몬스터를 최대 체력의 35%만큼 회복",
            1.20f, 1f, 1f, 1f, 1f, 1f, 0.35f),
        new Entry(
            "demon.war_fiend", "전쟁마", "Spr_Port_Orc",
            "소환 몬스터 공격력 +18%", "전쟁의 포효", "현재 몬스터의 공격력 +35%",
            1f, 1f, 1.18f, 1f, 1f, 1.35f, 0f),
        new Entry(
            "demon.gale_imp", "질풍귀", "Spr_Port_Bat",
            "소환 몬스터 이동속도 +15%", "지옥바람", "현재 몬스터의 이동속도 +40%",
            1f, 1.15f, 1f, 1f, 1.40f, 1f, 0f),
        new Entry(
            "demon.frenzy_king", "광란왕", "Spr_Port_Skeleton",
            "소환 몬스터 공격력 +10%, 이동속도 +8%", "광란", "현재 몬스터의 공격력 +20%, 이동속도 +25%",
            1f, 1.08f, 1.10f, 1f, 1.25f, 1.20f, 0f),
        new Entry(
            "demon.soul_weaver", "영혼술사", "Spr_Port_Golem",
            "소환 몬스터 최대 체력 +10%, 공격력 +8%", "영혼 갑주", "현재 몬스터 최대 체력 +25% 및 체력 20% 회복",
            1.10f, 1f, 1.08f, 1.25f, 1f, 1f, 0.20f)
    };

    private static readonly Dictionary<int, int> IndexByHash = BuildIndex();

    public static IReadOnlyList<Entry> Entries => CatalogEntries;
    public static int DefaultKeyHash => CatalogEntries[0].KeyHash;

    public static bool IsAllowedHash(int keyHash)
    {
        return keyHash != 0 && IndexByHash.ContainsKey(keyHash);
    }

    public static int NormalizeOrDefaultHash(int keyHash)
    {
        return TryGetByHash(keyHash, out Entry entry) ? entry.KeyHash : DefaultKeyHash;
    }

    public static bool TryGetByHash(int keyHash, out Entry entry)
    {
        if (IndexByHash.TryGetValue(keyHash, out int index))
        {
            entry = CatalogEntries[index];
            return true;
        }

        entry = default;
        return false;
    }

    private static Dictionary<int, int> BuildIndex()
    {
        var result = new Dictionary<int, int>(CatalogEntries.Length);
        for (int i = 0; i < CatalogEntries.Length; i++)
        {
            Entry entry = CatalogEntries[i];
            if (string.IsNullOrWhiteSpace(entry.ContentId) || entry.KeyHash == 0)
            {
                throw new InvalidOperationException("Demon content identity cannot be empty or hash to zero.");
            }

            if (string.IsNullOrWhiteSpace(entry.IconKey)
                || entry.PassiveHealthMultiplier < 1f
                || entry.PassiveMoveSpeedMultiplier < 1f
                || entry.PassiveDamageMultiplier < 1f
                || entry.SkillHealthMultiplier < 1f
                || entry.SkillMoveSpeedMultiplier < 1f
                || entry.SkillDamageMultiplier < 1f
                || entry.SkillHealFraction < 0f)
            {
                throw new InvalidOperationException($"Invalid demon definition: {entry.ContentId}");
            }

            if (!result.TryAdd(entry.KeyHash, i))
            {
                throw new InvalidOperationException($"Duplicate demon content identity hash: {entry.ContentId}");
            }
        }

        return result;
    }
}
