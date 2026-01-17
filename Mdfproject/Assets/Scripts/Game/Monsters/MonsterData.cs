// Assets/Scripts/Game/Monsters/MonsterData.cs
using UnityEngine;

[CreateAssetMenu(fileName = "New MonsterData", menuName = "Game/Monster Data")]
public class MonsterData : ScriptableObject
{
    [Header("기본 정보")]
    public string monsterName;
    public MonsterType monsterType; // 지상, 공중 구분

    [Tooltip("UI에서 사용될 몬스터 아이콘입니다.")]
    [AddressableKey(typeof(Sprite))]
    public string monsterIcon;

    [Header("프리팹")]
    [Tooltip("몬스터 프리팹 (Addressable)")]
    [AddressableKey(typeof(GameObject))]
    public string monsterPrefab;

    [Header("공격 스탯")]
    public float attackDamage;      // 공격력
    public float attackSpeed = 1f;  // 공격 속도 (초당 공격 횟수)
    public float attackRange = 1.5f;// 공격 범위 (유닛을 공격할 때 사용)
    public DamageType damageType;   // 공격 속성

    [Header("방어 스탯")]
    public int maxHealth;           // 최대 체력
    public float defense;           // 방어력
    public float magicResistance;   // 마법 저항력

    [Header("이동 스탯")]
    public float moveSpeed = 2f;    // 이동 속도

    [Header("특성")]
    [Tooltip("몬스터의 고유 특성입니다.")]
    public MonsterTraits traits = MonsterTraits.None;

    [Header("마나 & 스킬")]
    // ✅ [수정] 불필요해진 maxMana 필드를 완전히 삭제했습니다.
    // public int maxMana; 
    public SkillData skillData; 
}