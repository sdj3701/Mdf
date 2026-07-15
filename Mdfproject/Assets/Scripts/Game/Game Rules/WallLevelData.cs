using System;
using UnityEngine;

[CreateAssetMenu(fileName = "WallLevel_New", menuName = "Game/Walls/Wall Level")]
public sealed class WallLevelData : ScriptableObject, IStableContentIdentity
{
    [SerializeField, Tooltip("Immutable gameplay identity. Do not change after release.")]
    private string contentId;

    public string ContentId => StableDataKeyUtility.NormalizeContentId(contentId);
    public int ContentIdHash => StableDataKeyUtility.StableContentIdHash(contentId);

    [SerializeField, Min(1)] private int level = 1;
    [SerializeField, Min(1f)] private float maxHealth = 200f;
    [SerializeField, Min(0)] private int upgradeCostFromPreviousLevel;
    [SerializeField, Min(0f)] private float defense = 5f;
    [SerializeField, Min(0f)] private float magicResistance;
    [SerializeField] private Mesh visualMesh;
    [SerializeField] private Material[] visualMaterials = Array.Empty<Material>();

    public int Level => level;
    public float MaxHealth => maxHealth;
    public int UpgradeCostFromPreviousLevel => upgradeCostFromPreviousLevel;
    public float Defense => defense;
    public float MagicResistance => magicResistance;
    public Mesh VisualMesh => visualMesh;
    public Material[] VisualMaterials => visualMaterials;

#if UNITY_EDITOR
    public void ConfigureForEditor(
        int configuredLevel,
        float configuredMaxHealth,
        int configuredUpgradeCost,
        float configuredDefense,
        float configuredMagicResistance,
        Mesh configuredMesh,
        Material[] configuredMaterials)
    {
        level = Mathf.Max(1, configuredLevel);
        maxHealth = Mathf.Max(1f, configuredMaxHealth);
        upgradeCostFromPreviousLevel = Mathf.Max(0, configuredUpgradeCost);
        defense = Mathf.Max(0f, configuredDefense);
        magicResistance = Mathf.Max(0f, configuredMagicResistance);
        visualMesh = configuredMesh;
        visualMaterials = configuredMaterials ?? Array.Empty<Material>();
    }
#endif
}
