// Assets/Scripts/Game/Units/UnitData.cs
using UnityEngine;

/// <summary>
/// 유닛의 타입을 정의합니다 (근접/원거리).
/// </summary>
public enum UnitType { Melee, Ranged }

/// <summary>
/// 유닛의 모든 정적 데이터(정보)를 담고 있는 ScriptableObject입니다.
/// 이 에셋 하나가 한 종류의 유닛(예: "궁수", "검사")의 모든 성급 정보를 포함합니다.
/// </summary>
[CreateAssetMenu(fileName = "New UnitData", menuName = "Game/Unit Data")]
public class UnitData : ScriptableObject
{
    [Header("공통 정보")]
    [Tooltip("UI와 게임 내에서 표시될 유닛의 이름입니다.")]
    public string unitName;

    [Tooltip("상점과 UI 등에서 사용될 유닛의 아이콘입니다.")]
    public Sprite unitIcon;

    [Tooltip("상점에서 이 유닛을 구매하는 데 필요한 골드입니다.")]
    public int cost;

    [Tooltip("근접(Melee) 유닛인지 원거리(Ranged) 유닛인지 설정합니다.")]
    public UnitType unitType;

    [Header("스탯 (1성 기준)")]
    [Tooltip("1성일 때의 기본 체력입니다. 2, 3성은 이 값을 기반으로 배율이 적용됩니다.")]
    public float baseHealth;

    [Tooltip("1성일 때의 기본 공격력입니다. 2, 3성은 이 값을 기반으로 배율이 적용됩니다.")]
    public float baseAttackDamage;

    [Tooltip("초당 공격 횟수입니다. 일반적으로 성급이 올라도 변하지 않습니다.")]
    public float attackSpeed;

    [Tooltip("공격 사거리입니다.")]
    public float attackRange;

    [Tooltip("공격의 속성(물리/마법)입니다.")]
    public DamageType damageType;

    [Tooltip("물리 방어력입니다.")]
    public float defense;

    [Tooltip("마법 저항력입니다.")]
    public float magicResistance;

    [Header("특수 능력")]
    [Tooltip("이 유닛이 동시에 저지할 수 있는 지상 몬스터의 수입니다. 원거리 유닛은 0으로 설정하세요.")]
    public int blockCount;

    [Tooltip("스킬을 사용하는 데 필요한 최대 마나량입니다.")]
    public int maxMana;

    [Header("성급별 변화 요소")]
    [Tooltip("유닛의 외형을 결정하는 프리팹입니다. Element 0은 1성, 1은 2성, 2는 3성에 해당합니다.")]
    public GameObject[] prefabsByStarLevel = new GameObject[3];

    [Tooltip("유닛이 사용하는 스킬입니다. Element 0은 1성, 1은 2성, 2는 3성에 해당합니다. 성급이 올라도 스킬이 같다면 같은 스킬 데이터를 넣어주세요.")]
    public SkillData[] skillsByStarLevel = new SkillData[3];

    [Header("원거리 유닛 설정")]
    [Tooltip("원거리 유닛이 발사할 투사체 프리팹입니다. Element 0은 1성, 1은 2성, 2는 3성에 해당합니다. 투사체가 같다면 같은 프리팹을 넣어주세요.")]
    public GameObject[] projectilePrefabsByStarLevel = new GameObject[3];
}