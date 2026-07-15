using System;
using System.Collections.Generic;

public enum MapThemeId
{
    Classic = 1,
    Arena = 2
}

/// <summary>
/// Stable allow-list for cosmetic field themes. Network and migration state store only the
/// integer id; rendering details stay on the field prefab and never affect grid simulation.
/// </summary>
public static class MapThemeCatalog
{
    public const string PlayerPrefsKey = "SelectedMapThemeId";

    public readonly struct Entry
    {
        public Entry(MapThemeId id, string contentId, string displayName)
        {
            Id = id;
            ContentId = contentId;
            DisplayName = displayName;
        }

        public MapThemeId Id { get; }
        public string ContentId { get; }
        public string DisplayName { get; }
    }

    private static readonly Entry[] CatalogEntries =
    {
        new Entry(MapThemeId.Classic, "map.theme.classic", "클래식"),
        new Entry(MapThemeId.Arena, "map.theme.arena", "아레나")
    };

    private static readonly Dictionary<int, int> IndexById = BuildIndex();

    public static IReadOnlyList<Entry> Entries => CatalogEntries;
    public static int DefaultId => (int)MapThemeId.Arena;

    public static bool IsAllowed(int themeId)
    {
        return IndexById.ContainsKey(themeId);
    }

    public static int NormalizeOrDefault(int themeId)
    {
        return IsAllowed(themeId) ? themeId : DefaultId;
    }

    public static bool TryGet(int themeId, out Entry entry)
    {
        if (IndexById.TryGetValue(themeId, out int index))
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
            if ((int)entry.Id <= 0 || string.IsNullOrWhiteSpace(entry.ContentId))
            {
                throw new InvalidOperationException("Map theme identity cannot be empty.");
            }

            if (!result.TryAdd((int)entry.Id, i))
            {
                throw new InvalidOperationException($"Duplicate map theme id: {(int)entry.Id}");
            }
        }

        return result;
    }
}
