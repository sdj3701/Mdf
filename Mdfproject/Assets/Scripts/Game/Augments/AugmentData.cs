using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum TargetType { Player, Opponent }
public enum EffectType { IncreaseUnitAttack, IncreaseUnitAttackSpeed, SpawnBoss, AddGold }

[CreateAssetMenu(fileName = "New AugmentData", menuName = "Game/Augment Data")]
public class AugmentData : ScriptableObject
{
    public string augmentName;
    [TextArea] public string description;
    public Sprite icon;

    public TargetType targetType;
    public EffectType effectType;
    public float value; // 효과 수치 (예: 공격력 20% 증가시 0.2)
    public GameObject prefabToSpawn; // 소환할 보스 몬스터 등
}