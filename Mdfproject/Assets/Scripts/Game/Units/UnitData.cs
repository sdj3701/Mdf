// Assets/Scripts/Game/Units/UnitData.cs

using UnityEngine;

/// <summary>
/// 유닛의 타입을 정의합니다 (근접/원거리).
/// </summary>
public enum UnitType { Melee, Ranged }

// [추가됨] 유닛의 마나 회복 방식을 정의하는 열거형입니다.
public enum ManaRegenType 
{
    OnAttack, // 공격 시 일정량 회복
    Passive   // 매초 일정량 자연 회복
}

/// <summary>
/// 공격 대상 타입을 정의합니다 (단일/스플래시).
/// </summary>
public enum AttackTargetType 
{ 
    Single,  // 단일 대상 공격
    Splash   // 다중 대상 공격 (범위)
}


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

    [Tooltip("상점과 UI 등에서 사용될 유닛의 아이콘입니다.")] [AddressableKey(typeof(Sprite))]
    public string unitIcon;

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

    [Header("공격 타입")]
    [Tooltip("단일 대상 공격인지 스플래시(범위) 공격인지 설정합니다.")]
    public AttackTargetType attackTargetType = AttackTargetType.Single;

    [Tooltip("원거리 유닛의 투사체 폭발 범위입니다. 스플래시 공격일 때만 사용됩니다. 근접 유닛은 저지 중인 모든 몬스터를 공격합니다.")]
    public float splashRadius = 0f;

    // [추가됨] 마나 회복 관련 설정
    [Header("마나 & 스킬")]
    [Tooltip("유닛의 마나 회복 방식을 선택합니다.")]
    public ManaRegenType manaRegenType = ManaRegenType.OnAttack;

    [Tooltip("마나 회복 방식이 'OnAttack'일 때, 공격마다 회복하는 마나의 양입니다.")]
    public float manaOnAttack = 15f;

    [Tooltip("마나 회복 방식이 'Passive'일 때, 초당 회복하는 마나의 양입니다.")]
    public float manaPerSecond = 5f;

    [Header("성급별 변화 요소")]
    [Tooltip("유닛의 외형을 결정하는 프리팹입니다. Element 0은 1성, 1은 2성, 2는 3성에 해당합니다.")]
    [AddressableKey(typeof(GameObject))]
    public string[] prefabsByStarLevel = new string[3];

    [Tooltip("유닛이 사용하는 스킬입니다. Element 0은 1성, 1은 2성, 2는 3성에 해당합니다. 성급이 올라도 스킬이 같다면 같은 스킬 데이터를 넣어주세요.")]
    [AddressableKey(typeof(SkillData))]
    public string[] skillsByStarLevel = new string[3];

    [Header("원거리 유닛 설정")]
    [Tooltip("원거리 유닛이 발사할 투사체 프리팹입니다. Element 0은 1성, 1은 2성, 2는 3성에 해당합니다. 투사체가 같다면 같은 프리팹을 넣어주세요.")]
    [AddressableKey(typeof(GameObject))]
    public string[] projectilePrefabsByStarLevel = new string[3];

    [Header("Basic Attack VFX")]
    [Tooltip("Slash VFX prefab and placement settings. Element 0 is 1-star, 1 is 2-star, and 2 is 3-star.")]
    public BasicAttackVfxConfig[] basicAttackVfxConfigsByStarLevel = new BasicAttackVfxConfig[3];

    [Tooltip("투사체 속도입니다. 0이면 투사체 프리팹의 기본 속도를 사용합니다.")]
    public float projectileSpeed = 0f;
    public BasicAttackVfxConfig GetBasicAttackVfxConfig(int starLevel)
    {
        int index = Mathf.Clamp(starLevel - 1, 0, 2);
        EnsureBasicAttackVfxConfigArray();

        var config = basicAttackVfxConfigsByStarLevel[index];
        if (config != null && config.HasPrefabKey)
        {
            return config;
        }

        return null;
    }

    public void EnsureBasicAttackVfxConfigArray()
    {
        if (basicAttackVfxConfigsByStarLevel == null || basicAttackVfxConfigsByStarLevel.Length != 3)
        {
            var resized = new BasicAttackVfxConfig[3];
            if (basicAttackVfxConfigsByStarLevel != null)
            {
                int count = Mathf.Min(3, basicAttackVfxConfigsByStarLevel.Length);
                for (int i = 0; i < count; i++)
                {
                    resized[i] = basicAttackVfxConfigsByStarLevel[i];
                }
            }

            basicAttackVfxConfigsByStarLevel = resized;
        }

        for (int i = 0; i < basicAttackVfxConfigsByStarLevel.Length; i++)
        {
            if (basicAttackVfxConfigsByStarLevel[i] == null)
            {
                basicAttackVfxConfigsByStarLevel[i] = BasicAttackVfxConfig.CreateDefault();
            }
        }
    }
}
