// Assets/Scripts/Game/Units/UnitData.cs
using UnityEngine;

public enum UnitType { Melee, Ranged }

[CreateAssetMenu(fileName = "New UnitData", menuName = "Game/Unit Data")]
public class UnitData : ScriptableObject
{
    [Header("기본 정보")]
    public string unitName;
    public GameObject unitPrefab;
    public Sprite unitIcon;
    public int cost;
    public UnitType unitType;

    [Header("공격 스탯 (1성 기준)")]
    public float baseAttackDamage;      // 기본 공격력
    public float attackSpeed;           // 공격 속도 (초당 공격 횟수)
    public float attackRange;           // 공격 범위
    public DamageType damageType;       // 공격 속성 (물리/마법)

    [Header("방어 스탯 (1성 기준)")]
    public float baseHealth;            // 기본 체력
    public float defense;               // 방어력 (물리 데미지 감소)
    public float magicResistance;       // 마법 저항력 (마법 데미지 % 감소)

    [Header("특수 능력")]
    [Tooltip("이 유닛이 동시에 막을 수 있는 지상 몬스터의 수. 원거리 유닛은 0으로 설정하세요.")]
    public int blockCount;

    [Header("마나 & 스킬")]
    public int maxMana;
    public SkillData skillData; 
}