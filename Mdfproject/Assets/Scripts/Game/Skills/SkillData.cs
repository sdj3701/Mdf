// Assets/Scripts/Game/Skills/SkillData.cs (수정된 버전)
using UnityEngine;
using System.Collections.Generic; // List 사용을 위해 추가

[CreateAssetMenu(fileName = "New SkillData", menuName = "Game/Skill Data")]
public class SkillData : ScriptableObject
{
    [Header("기본 정보")]
    [SerializeField, Tooltip("Immutable gameplay identity. Never change this after the content ships.")]
    private string contentId;
    public string skillName;
    [TextArea(3, 5)]
    public string description;
    public Sprite icon;

    [Header("사용 방식")]
    public int manaCost;
    public float range; // 스킬의 유효 사거리
    public SkillActivationType activationType;

    [Header("AI Strategic Use")]
    [Tooltip("Allows an otherwise automatic skill to be selected by AI/HumanBot policy as a strategic command.")]
    public bool canAiUseStrategically;
    [Tooltip("Minimum score required before AI/HumanBot policy emits ActivateSkillCommand for this skill.")]
    public float aiMinSkillValue = 100f;

    // [변경됨] skillLogicPrefab 대신 아래 두 개로 대체
    [Header("스킬 로직 (조립)")]
    [Tooltip("스킬의 대상을 어떻게 찾을지 결정합니다.")]
    public TargetingStrategy targetingStrategy;

    [Tooltip("대상에게 적용할 효과들의 목록입니다. 순서대로 적용됩니다.")]
    public List<SkillEffect> effects;
    
    [Header("시각 효과 (선택)")]
    [Tooltip("스킬 발동 시 클라이언트에서 생성할 이펙트 프리팹 (네트워크 객체 X)")]
    public GameObject vfxPrefab;

    public string ContentId => StableDataKeyUtility.NormalizeContentId(contentId);
    public int ContentIdHash => StableDataKeyUtility.StableContentIdHash(contentId);
}
