// Assets/Scripts/Game/Augments/AugmentData.cs

using System.Collections.Generic;
using UnityEngine;

// 증강 효과가 누구에게 적용될지 결정
public enum TargetType { Player, Opponent }

// 증강 효과의 종류를 구체적으로 정의
public enum EffectType 
{
    // 내 유닛/필드 강화
    IncreaseMyUnitAttack,
    IncreaseMyUnitAttackSpeed,
    AddGold,
    AddWallPlacementCount,
    // 상대 필드 약화 (몬스터 강화)
    IncreaseEnemyHealth,
    IncreaseEnemyMoveSpeed,
    SpawnMonsterOnEnemyField,
    // 마법 스크롤 획득
    GrantMagicScroll,
    // Append new values to preserve serialized enum values in existing assets.
    StrengthenMonsterType,
    IncreaseBlackMagicMaximum,
    GrantPermanentWallPlacementCount,
    StrengthenKing
}

// ✅ [추가] 증강의 등급을 정의하는 열거형
public enum AugmentTier { Silver, Gold, Prismatic }

/// <summary>
/// 일반 몬스터 소환 증강에서 사용할 몬스터 + 수량 쌍
/// </summary>
[System.Serializable]
public class MonsterSpawnEntry
{
    [Tooltip("소환할 몬스터 데이터")]
    public MonsterData monsterData;
    
    [Tooltip("소환 수량")]
    public int count = 1;
}

/// <summary>
/// One authored augment effect. AugmentData can compose several of these in a stable order.
/// Validation guarantees that only the payload required by effectType is used.
/// </summary>
[System.Serializable]
public sealed class AugmentEffectData
{
    public TargetType targetType;
    public EffectType effectType;
    public float value;

    [Header("Monster Summon")]
    public bool isBossSummon;
    public MonsterData bossMonsterData;
    public List<MonsterSpawnEntry> monsterSpawnEntries = new List<MonsterSpawnEntry>();

    [Header("Monster Strengthening")]
    public MonsterData strengthenedMonsterData;
    [Min(0f)] public float monsterHealthBonusPercent;
    [Min(0f)] public float monsterDamageBonusPercent;
    [Min(0f)] public float monsterMoveSpeedBonusPercent;

    [Header("King Strengthening")]
    [Min(0f)] public float kingDamageBonusPercent;
    [Min(0f)] public float kingAttackSpeedBonusPercent;
    [Min(0f)] public float kingSkillPowerBonusPercent;

    [Header("Magic Scroll")]
    public MagicScrollData magicScrollData;

    internal void CopyFromLegacy(AugmentData source)
    {
        targetType = source.targetType;
        effectType = source.effectType;
        value = source.value;
        isBossSummon = source.isBossSummon;
        bossMonsterData = source.bossMonsterData;
        monsterSpawnEntries = source.monsterSpawnEntries;
        strengthenedMonsterData = source.strengthenedMonsterData;
        monsterHealthBonusPercent = source.monsterHealthBonusPercent;
        monsterDamageBonusPercent = source.monsterDamageBonusPercent;
        monsterMoveSpeedBonusPercent = source.monsterMoveSpeedBonusPercent;
        kingDamageBonusPercent = source.kingDamageBonusPercent;
        kingAttackSpeedBonusPercent = source.kingAttackSpeedBonusPercent;
        kingSkillPowerBonusPercent = source.kingSkillPowerBonusPercent;
        magicScrollData = source.magicScrollData;
    }
}

[CreateAssetMenu(fileName = "New AugmentData", menuName = "Game/Augment Data")]
public class AugmentData : ScriptableObject, IStableContentIdentity
{
    [Header("기본 정보")]
    [SerializeField, Tooltip("Immutable gameplay identity. Never change this after the content ships.")]
    private string contentId;
    public string augmentName;
    [TextArea] public string description;
    public Sprite icon;
    
    // ✅ [추가] 증강 등급 변수
    [Header("등급 정보")]
    public AugmentTier tier;

    [Header("효과 정보")]
    [HideInInspector]
    public TargetType targetType;
    [HideInInspector]
    public EffectType effectType;

    [Header("구체적인 수치")]
    [HideInInspector]
    public float value; 

    [Header("Composed Effects (Canonical)")]
    [SerializeField, Tooltip("Ordered effects applied by State Authority. Empty lists use the legacy fields below during migration.")]
    private List<AugmentEffectData> effects = new List<AugmentEffectData>();

    [System.NonSerialized]
    private AugmentEffectData legacyEffectFallback;

    #region 몬스터 소환 설정
    [Header("몬스터 소환 설정")]
    [Tooltip("체크 시 보스 모드: 1회 소환, 살아남으면 다음 라운드 전체 유저 중 랜덤 침공")]
    [HideInInspector]
    public bool isBossSummon;
    
    [Tooltip("보스 모드: 소환할 보스 데이터 (1마리)")]
    [HideInInspector]
    public MonsterData bossMonsterData;
    
    [Tooltip("일반 모드: 매 라운드 소환할 몬스터와 수량 목록")]
    [HideInInspector]
    public List<MonsterSpawnEntry> monsterSpawnEntries;
    #endregion

    #region Monster strengthening
    [Header("Monster Strengthening")]
    [Tooltip("Only spawned monsters using this exact MonsterData receive the bonuses below.")]
    [HideInInspector]
    public MonsterData strengthenedMonsterData;

    [HideInInspector, Min(0f)] public float monsterHealthBonusPercent;
    [HideInInspector, Min(0f)] public float monsterDamageBonusPercent;
    [HideInInspector, Min(0f)] public float monsterMoveSpeedBonusPercent;
    #endregion

    #region King strengthening
    [Header("King Strengthening")]
    [HideInInspector, Min(0f)] public float kingDamageBonusPercent;
    [HideInInspector, Min(0f)] public float kingAttackSpeedBonusPercent;
    [HideInInspector, Min(0f)] public float kingSkillPowerBonusPercent;
    #endregion

    #region 마법 스크롤 설정
    [Header("마법 스크롤 설정")]
    [Tooltip("마법 스크롤 모드: 획득할 스크롤 데이터")]
    [HideInInspector]
    public MagicScrollData magicScrollData;
    #endregion

    public string ContentId => StableDataKeyUtility.NormalizeContentId(contentId);
    public int ContentIdHash => StableDataKeyUtility.StableContentIdHash(contentId);

    public IReadOnlyList<AugmentEffectData> AuthoredEffects =>
        effects != null ? effects : (IReadOnlyList<AugmentEffectData>)System.Array.Empty<AugmentEffectData>();

    public bool HasAuthoredEffects => effects != null && effects.Count > 0;

    /// <summary>
    /// Old assets expose their legacy fields as one virtual effect until the migration tool has
    /// populated the canonical list. Runtime behavior and snapshot identity therefore stay stable.
    /// </summary>
    public int EffectCount => HasAuthoredEffects ? effects.Count : 1;

    public AugmentEffectData GetEffect(int index)
    {
        if (HasAuthoredEffects)
        {
            return index >= 0 && index < effects.Count ? effects[index] : null;
        }

        if (index != 0)
        {
            return null;
        }

        if (legacyEffectFallback == null)
        {
            legacyEffectFallback = new AugmentEffectData();
        }

        legacyEffectFallback.CopyFromLegacy(this);
        return legacyEffectFallback;
    }

    public bool ContainsEffect(EffectType requestedType)
    {
        return TryGetFirstEffect(requestedType, out _);
    }

    public bool TryGetFirstEffect(EffectType requestedType, out AugmentEffectData effect)
    {
        for (int index = 0; index < EffectCount; index++)
        {
            AugmentEffectData candidate = GetEffect(index);
            if (candidate != null && candidate.effectType == requestedType)
            {
                effect = candidate;
                return true;
            }
        }

        effect = null;
        return false;
    }

    public bool TryGetBossMonster(out MonsterData monsterData)
    {
        for (int index = 0; index < EffectCount; index++)
        {
            AugmentEffectData effect = GetEffect(index);
            if (effect != null &&
                effect.effectType == EffectType.SpawnMonsterOnEnemyField &&
                effect.isBossSummon &&
                effect.bossMonsterData != null)
            {
                monsterData = effect.bossMonsterData;
                return true;
            }
        }

        monsterData = null;
        return false;
    }

    public bool TryGetMagicScroll(out MagicScrollData scrollData)
    {
        if (TryGetFirstEffect(EffectType.GrantMagicScroll, out AugmentEffectData effect) &&
            effect.magicScrollData != null)
        {
            scrollData = effect.magicScrollData;
            return true;
        }

        scrollData = null;
        return false;
    }
}
