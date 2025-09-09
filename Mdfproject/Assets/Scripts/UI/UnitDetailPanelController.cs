using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 유닛 상세 정보 패널의 UI 요소들을 관리하고,
/// 선택된 유닛의 데이터를 받아와 텍스트를 업데이트하는 클래스입니다.
/// </summary>
public class UnitDetailPanelController : MonoBehaviour
{
    [Header("UI Text 컴포넌트")]
    [SerializeField] private TextMeshProUGUI unitNameText;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI attackDamageText;
    [SerializeField] private TextMeshProUGUI defenseText;
    [SerializeField] private TextMeshProUGUI magicResistText;
    [SerializeField] private TextMeshProUGUI attackSpeedText;
    [SerializeField] private TextMeshProUGUI attackRangeText;
    [SerializeField] private TextMeshProUGUI attackTypeText;
    [SerializeField] private TextMeshProUGUI blockCountText;
    [SerializeField] private TextMeshProUGUI manaRegenText;
    [SerializeField] private TextMeshProUGUI skillDescriptionText;
    [SerializeField] private Toggle skillActivationToggle;

    private Unit currentUnit;

    /// <summary>
    /// 전달받은 유닛의 정보로 UI 패널의 내용을 채웁니다.
    /// </summary>
    /// <param name="unit">정보를 표시할 유닛</param>
    public void DisplayUnitInfo(Unit unit)
    {
        if (unit == null || unit.Data == null)
        {
            Debug.LogError("UnitDetailPanel에 유효하지 않은 유닛 데이터가 전달되었습니다.");
            gameObject.SetActive(false);
            return;
        }

        currentUnit = unit;

        // 기본 스탯 정보 업데이트
        unitNameText.text = unit.Data.unitName;
        healthText.text = $"{unit.CurrentHealth:F0}";
        attackDamageText.text = $"{unit.currentAttackDamage:F0}";
        defenseText.text = $"{unit.currentDefense:F0}";
        magicResistText.text = $"{unit.currentMagicResistance:F0}";

        // 부가 스탯 정보 업데이트
        attackSpeedText.text = $"{unit.currentAttackSpeed:F2}";
        attackRangeText.text = $"{unit.currentAttackRange:F1}";
        attackTypeText.text = ConvertDamageTypeToString(unit.Data.damageType);
        blockCountText.text = $"{unit.Data.blockCount}";
        manaRegenText.text = ConvertManaRegenTypeToString(unit.Data.manaRegenType);
        
        // 스킬 정보 업데이트
        UpdateSkillDescription(unit);
        UpdateSkillToggle(unit);

        // 필드에 유닛의 공격 및 스킬 범위 표시 요청
        if (GameManagers.Instance != null && GameManagers.Instance.localPlayer != null && GameManagers.Instance.localPlayer.fieldManager != null)
        {
            GameManagers.Instance.localPlayer.fieldManager.ShowRanges(unit);
        }
    }

    private void OnDisable()
    {
        // 패널이 비활성화될 때 범위 표시를 지웁니다.
        if (GameManagers.Instance != null && GameManagers.Instance.localPlayer != null && GameManagers.Instance.localPlayer.fieldManager != null)
        {
            GameManagers.Instance.localPlayer.fieldManager.ClearRanges();
        }

        if (skillActivationToggle != null)
        {
            skillActivationToggle.onValueChanged.RemoveAllListeners();
        }
        currentUnit = null;
    }

    private void UpdateSkillToggle(Unit unit)
    {
        if (skillActivationToggle == null) return;

        skillActivationToggle.onValueChanged.RemoveAllListeners();

        if (unit.Data.skillsByStarLevel.Length >= unit.starLevel &&
            unit.Data.skillsByStarLevel[unit.starLevel - 1] != null)
        {
            skillActivationToggle.gameObject.SetActive(true);
            skillActivationToggle.isOn = (unit.currentSkillActivationType == SkillActivationType.Automatic);
            skillActivationToggle.onValueChanged.AddListener(OnSkillActivationToggleChanged);
        }
        else
        {
            skillActivationToggle.gameObject.SetActive(false);
        }
    }

    private void OnSkillActivationToggleChanged(bool isAutomatic)
    {
        if (currentUnit != null)
        {
            currentUnit.currentSkillActivationType = isAutomatic ? SkillActivationType.Automatic : SkillActivationType.Manual;
        }
    }

    private void UpdateSkillDescription(Unit unit)
    {
        // Unit.cs의 DoesHaveSkill() 로직을 참고하여 스킬 존재 여부를 확인합니다.
        if (unit.Data.skillsByStarLevel.Length >= unit.starLevel && 
            unit.Data.skillsByStarLevel[unit.starLevel - 1] != null)
        {
            SkillData currentSkill = unit.Data.skillsByStarLevel[unit.starLevel - 1];
            skillDescriptionText.text = currentSkill.description;
        }
        else
        {
            skillDescriptionText.text = "특별한 능력이 없습니다.";
        }
    }

    private string ConvertDamageTypeToString(DamageType damageType)
    {
        switch (damageType)
        {
            case DamageType.Physical: return "PHY";
            case DamageType.Magic: return "MAG";
            default: return damageType.ToString();
        }
    }

    private string ConvertManaRegenTypeToString(ManaRegenType regenType)
    {
        switch (regenType)
        {
            case ManaRegenType.Passive: return "시간당 회복";
            case ManaRegenType.OnAttack: return "공격 시 회복";
            default: return "없음";
        }
    }
}
