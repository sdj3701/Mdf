using System;
using UnityEngine;

public enum KingBuffStat
{
    AttackDamage,
    AttackSpeed,
    Range,
    Defense,
    MagicResistance,
    MaxHealth
}

public enum KingBuffModifierMode
{
    Flat,
    Percent
}

[Serializable]
public struct KingBuffModifier
{
    public KingBuffStat stat;
    public KingBuffModifierMode mode;
    public float value;
}

[CreateAssetMenu(fileName = "KingBuff_New", menuName = "Game/King/Buff Data")]
public class KingBuffData : ScriptableObject, IStableContentIdentity
{
    [SerializeField, Tooltip("Immutable gameplay identity. Do not change after release.")]
    private string contentId;

    public string ContentId => StableDataKeyUtility.NormalizeContentId(contentId);
    public int ContentIdHash => StableDataKeyUtility.StableContentIdHash(contentId);

    public string buffName;
    [TextArea(2, 5)] public string description;
    public KingBuffModifier[] modifiers = Array.Empty<KingBuffModifier>();

    public float GetModifier(KingBuffStat stat, KingBuffModifierMode mode)
    {
        float total = 0f;
        KingBuffModifier[] source = modifiers;
        if (source == null)
        {
            return total;
        }

        for (int i = 0; i < source.Length; i++)
        {
            KingBuffModifier modifier = source[i];
            if (modifier.stat == stat && modifier.mode == mode)
            {
                total += modifier.value;
            }
        }

        return total;
    }
}
