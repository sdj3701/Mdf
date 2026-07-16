public static class StableDataKeyUtility
{
    /// <summary>
    /// Canonicalizes an authored content id. Content ids are deliberately independent from
    /// ScriptableObject names and user-facing display text so those values can be renamed safely.
    /// </summary>
    public static string NormalizeContentId(string contentId)
    {
        return string.IsNullOrWhiteSpace(contentId)
            ? string.Empty
            : contentId.Trim().ToLowerInvariant();
    }

    public static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return key.Replace("(Clone)", string.Empty).Trim();
    }

    public static int StableHash(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        unchecked
        {
            uint hash = 2166136261u;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619u;
            }

            return (int)hash;
        }
    }

    public static int StableKeyHash(string key)
    {
        return StableHash(NormalizeKey(key));
    }

    public static int StableContentIdHash(string contentId)
    {
        return StableHash(NormalizeContentId(contentId));
    }
}
