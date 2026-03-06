// Assets/Scripts/Game/Augments/MagicScrollData.cs
using UnityEngine;

/// <summary>
/// 마법 스크롤 데이터. 기존 SkillData를 참조하여 스크롤 효과를 정의합니다.
/// 스크롤 사용 시 공격자는 몬스터 진영으로 취급되어 TargetingStrategy가 적절히 작동합니다.
/// </summary>
[CreateAssetMenu(fileName = "New MagicScrollData", menuName = "Game/Magic Scroll Data")]
public class MagicScrollData : ScriptableObject
{
    #region 기본 정보
    [Header("기본 정보")]
    [Tooltip("스크롤 이름")]
    public string scrollName;
    
    [TextArea(2, 4)]
    [Tooltip("스크롤 효과 설명")]
    public string description;
    
    [Tooltip("UI에 표시될 아이콘")]
    public Sprite icon;
    
    [Tooltip("증강 등급 (Silver, Gold, Prismatic)")]
    public AugmentTier tier;
    #endregion

    #region 스킬 참조
    [Header("스킬 참조")]
    [Tooltip("이 스크롤이 발동시킬 스킬 데이터")]
    public SkillData skillData;
    #endregion
}
