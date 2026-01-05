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
    SpawnMonsterOnEnemyField
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

[CreateAssetMenu(fileName = "New AugmentData", menuName = "Game/Augment Data")]
public class AugmentData : ScriptableObject
{
    [Header("기본 정보")]
    public string augmentName;
    [TextArea] public string description;
    public Sprite icon;
    
    // ✅ [추가] 증강 등급 변수
    [Header("등급 정보")]
    public AugmentTier tier;

    [Header("효과 정보")]
    public TargetType targetType;
    public EffectType effectType;

    [Header("구체적인 수치")]
    public float value; 

    #region 몬스터 소환 설정
    [Header("몬스터 소환 설정")]
    [Tooltip("체크 시 보스 모드: 1회 소환, 살아남으면 다음 라운드 전체 유저 중 랜덤 침공")]
    public bool isBossSummon;
    
    [Tooltip("보스 모드: 소환할 보스 데이터 (1마리)")]
    public MonsterData bossMonsterData;
    
    [Tooltip("일반 모드: 매 라운드 소환할 몬스터와 수량 목록")]
    public List<MonsterSpawnEntry> monsterSpawnEntries;
    #endregion
}