// Assets/Scripts/Game/Augments/AugmentData.cs
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
    SpawnBossOnEnemyField
}

[CreateAssetMenu(fileName = "New AugmentData", menuName = "Game/Augment Data")]
public class AugmentData : ScriptableObject
{
    [Header("기본 정보")]
    public string augmentName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("효과 정보")]
    public TargetType targetType;
    public EffectType effectType;

    [Header("구체적인 수치")]
    public float value; // 효과 수치 (예: 공격력 20% 증가시 0.2f)
    public GameObject prefabToSpawn; // 소환할 특수 몬스터 프리팹
}