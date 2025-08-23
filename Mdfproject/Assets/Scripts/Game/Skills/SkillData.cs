// Assets/Scripts/Game/Skills/SkillData.cs
using UnityEngine;

/// <summary>
/// 스킬의 모든 정적 데이터(정보)를 담고 있는 ScriptableObject입니다.
/// 이 에셋을 통해 게임의 다양한 스킬을 정의하고 관리할 수 있습니다.
/// </summary>
[CreateAssetMenu(fileName = "New SkillData", menuName = "Game/Skill Data")]
public class SkillData : ScriptableObject
{
    [Header("기본 정보")]
    [Tooltip("스킬의 이름입니다. UI에 표시됩니다.")]
    public string skillName;

    [Tooltip("스킬에 대한 설명입니다. UI에 표시됩니다.")]
    [TextArea(3, 5)] // 인스펙터에서 여러 줄로 입력할 수 있도록 합니다.
    public string description;

    [Tooltip("스킬을 나타내는 아이콘 이미지입니다.")]
    public Sprite icon;

    [Header("사용 방식")]
    [Tooltip("스킬을 사용하는 데 필요한 마나의 양입니다.")]
    public int manaCost;

    [Tooltip("스킬의 발동 방식입니다. (Automatic: 자동 발동, Manual: 수동 발동)")]
    public SkillActivationType activationType;

    [Header("로직 연결")]
    [Tooltip("실제 스킬의 효과를 구현한 로직 컴포넌트가 붙어있는 '프리팹'을 연결해야 합니다.")]
    public GameObject skillLogicPrefab;
}