using System;
using System.Collections.Generic;

/// <summary>
/// Canonical, network-safe catalog for the kings that can be selected in the ready lobby.
/// Network state stores only <see cref="Entry.KeyHash"/>; asset keys are resolved through this allow-list.
/// </summary>
public static class KingSelectionCatalog
{
    public const string PlayerPrefsKey = "SelectedKingUnitKeyHash";
    public const string DefaultKey = "UnitData_King_Archer";

    public readonly struct Entry
    {
        public Entry(string kingUnitKey, string baseUnitKey, string displayName, string iconKey)
        {
            KingUnitKey = kingUnitKey;
            BaseUnitKey = baseUnitKey;
            DisplayName = displayName;
            IconKey = iconKey;
            KeyHash = StableDataKeyUtility.StableKeyHash(kingUnitKey);
        }

        public string KingUnitKey { get; }
        public string BaseUnitKey { get; }
        public string DisplayName { get; }
        public string IconKey { get; }
        public int KeyHash { get; }
    }

    private static readonly Entry[] CatalogEntries =
    {
        new Entry("UnitData_King_Archer", "UnitData_Archer", "궁수", "Spr_Port_Archer"),
        new Entry("UnitData_King_Cleric", "UnitData_Cleric", "성직자", "Spr_Port_Cleric"),
        new Entry("UnitData_King_Fighter", "UnitData_Fighter", "전사", "Spr_Port_Fighter"),
        new Entry("UnitData_King_Guardian", "UnitData_Guardian", "수호자", "Spr_Port_Guardian"),
        new Entry("UnitData_King_Mage", "UnitData_Mage", "마법사", "Spr_Port_Mage"),
        new Entry("UnitData_King_Pyromancer", "UnitData_Pyromancer", "화염술사", "Spr_Port_Mage"),
        new Entry("UnitData_King_Warrior", "UnitData_Warrior", "검사", "Spr_Port_Warrior")
    };

    private static readonly Dictionary<int, int> IndexByHash = BuildIndex();

    public static IReadOnlyList<Entry> Entries => CatalogEntries;
    public static int DefaultKeyHash => StableDataKeyUtility.StableKeyHash(DefaultKey);

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
        return IsAllowedHash(keyHash) ? keyHash : DefaultKeyHash;
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
        var result = new Dictionary<int, int>(CatalogEntries.Length);
        for (int i = 0; i < CatalogEntries.Length; i++)
        {
            Entry entry = CatalogEntries[i];
            if (entry.KeyHash == 0)
            {
                throw new InvalidOperationException($"King selection key cannot hash to zero: {entry.KingUnitKey}");
            }

            if (!result.TryAdd(entry.KeyHash, i))
            {
                throw new InvalidOperationException($"Duplicate king selection hash: {entry.KingUnitKey}");
            }
        }

        return result;
    }
}
