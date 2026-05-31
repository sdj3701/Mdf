public static class StableDataKeyUtility
{
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
}
