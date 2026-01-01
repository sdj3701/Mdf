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
    
    [Tooltip("보스 모드: 소환할 보스 프리팹 (1마리)")]
    public GameObject bossPrefab;
    
    [Tooltip("일반 모드: 매 라운드 소환할 몬스터 프리팹 목록")]
    public List<GameObject> monsterPrefabs;
    
    [Tooltip("일반 모드: 각 프리팹 당 소환 수량")]
    public int monsterSpawnCount = 1;
    #endregion
}