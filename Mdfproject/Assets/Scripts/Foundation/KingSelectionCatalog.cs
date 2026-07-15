using System;
using System.Collections.Generic;

/// <summary>
/// Canonical, network-safe catalog for the kings that can be selected in the ready lobby.
/// Network state stores only <see cref="Entry.KeyHash"/>; asset keys are resolved through this allow-list.
/// Canonical hashes come from KingUnitData.contentId. Shipped asset-key hashes remain accepted
/// as read-only aliases so old PlayerPrefs and migration/network state can be upgraded in place.
/// </summary>
public static class KingSelectionCatalog
{
    public const string PlayerPrefsKey = "SelectedKingUnitKeyHash";
    public const string DefaultKey = "UnitData_King_Archer";

    public readonly struct Entry
    {
        public Entry(string contentId, string kingUnitKey, string baseUnitKey, string displayName, string iconKey)
        {
            ContentId = StableDataKeyUtility.NormalizeContentId(contentId);
            KingUnitKey = kingUnitKey;
            BaseUnitKey = baseUnitKey;
            DisplayName = displayName;
            IconKey = iconKey;
            KeyHash = StableDataKeyUtility.StableContentIdHash(ContentId);
            LegacyKeyHash = StableDataKeyUtility.StableKeyHash(kingUnitKey);
        }

        public string ContentId { get; }
        public string KingUnitKey { get; }
        public string BaseUnitKey { get; }
        public string DisplayName { get; }
        public string IconKey { get; }
        public int KeyHash { get; }
        public int LegacyKeyHash { get; }
    }

    private static readonly Entry[] CatalogEntries =
    {
        new Entry("king.unit.archer", "UnitData_King_Archer", "UnitData_Archer", "궁수", "Spr_Port_Archer"),
        new Entry("king.unit.cleric", "UnitData_King_Cleric", "UnitData_Cleric", "성직자", "Spr_Port_Cleric"),
        new Entry("king.unit.fighter", "UnitData_King_Fighter", "UnitData_Fighter", "전사", "Spr_Port_Fighter"),
        new Entry("king.unit.guardian", "UnitData_King_Guardian", "UnitData_Guardian", "수호자", "Spr_Port_Guardian"),
        new Entry("king.unit.mage", "UnitData_King_Mage", "UnitData_Mage", "마법사", "Spr_Port_Mage"),
        new Entry("king.unit.pyromancer", "UnitData_King_Pyromancer", "UnitData_Pyromancer", "화염술사", "Spr_Port_Mage"),
        new Entry("king.unit.warrior", "UnitData_King_Warrior", "UnitData_Warrior", "검사", "Spr_Port_Warrior")
    };

    private static readonly Dictionary<int, int> IndexByHash = BuildIndex();

    public static IReadOnlyList<Entry> Entries => CatalogEntries;
    public static int DefaultKeyHash => CatalogEntries[0].KeyHash;
    public static int DefaultLegacyKeyHash => CatalogEntries[0].LegacyKeyHash;

    public static bool IsAllowedHash(int keyHash)
    {
        return keyHash != 0 && IndexByHash.ContainsKey(keyHash);
    }

    public static bool IsValidHash(int keyHash)
    {
        return IsAllowedHash(keyHash);
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

    public static string GetAssetKey(int keyHash)
    {
        return TryGetByHash(keyHash, out Entry entry) ? entry.KingUnitKey : string.Empty;
    }

    private static Dictionary<int, int> BuildIndex()
    {
        var result = new Dictionary<int, int>(CatalogEntries.Length * 2);
        for (int i = 0; i < CatalogEntries.Length; i++)
        {
            Entry entry = CatalogEntries[i];
            if (string.IsNullOrEmpty(entry.ContentId) || entry.KeyHash == 0)
            {
                throw new InvalidOperationException($"King content identity cannot be empty or hash to zero: {entry.KingUnitKey}");
            }

            if (!result.TryAdd(entry.KeyHash, i))
            {
                throw new InvalidOperationException($"Duplicate king content identity hash: {entry.ContentId}");
            }

            if (entry.LegacyKeyHash == 0 || !result.TryAdd(entry.LegacyKeyHash, i))
            {
                throw new InvalidOperationException($"Duplicate or colliding legacy king selection hash: {entry.KingUnitKey}");
            }
        }

        return result;
    }
}
