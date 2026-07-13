using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;
using MDF.Runtime.Assets;
/// <summary>
/// 유닛 상세 정보 패널의 UI 요소들을 관리하고,
/// 선택된 유닛의 데이터를 받아와 텍스트를 업데이트하는 클래스입니다.
/// </summary>
public class UnitDetailPanelController : MonoBehaviour
{
    private AddressableAssetOwner _addressableAssets = new AddressableAssetOwner();

    private void OnEnable()
    {
        if (_addressableAssets == null || _addressableAssets.IsDisposed)
        {
            _addressableAssets = new AddressableAssetOwner();
        }
    }
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
    private PlayerManager currentKingOwner;
    private int displayRevision;

    /// <summary>
    /// 전달받은 유닛의 정보로 UI 패널의 내용을 채웁니다.
    /// </summary>
    /// <param name="unit">정보를 표시할 유닛</param>
    public async void DisplayUnitInfo(Unit unit)
    {
        int revision = ++displayRevision;
        currentKingOwner = null;
        if (unit == null || unit.IsDead || unit.Data == null)
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
        UpdateSkillToggle(unit);
        await UpdateSkillDescription(unit, revision);
        if (revision != displayRevision || currentUnit != unit)
        {
            return;
        }

        // 필드에 유닛의 공격 및 스킬 범위 표시 요청
        if (GameManagers.Instance != null && GameManagers.Instance.localPlayer != null && GameManagers.Instance.localPlayer.fieldManager != null)
        {
            GameManagers.Instance.localPlayer.fieldManager.ShowRanges(unit);
        }
    }

    /// <summary>
    /// Displays the selected King's replicated runtime combat values without exposing unit-only
    /// controls. A King has no independent health pool, so the HP value intentionally stays blank.
    /// </summary>
    public void DisplayKingInfo(PlayerManager kingOwner)
    {
        ++displayRevision;
        currentUnit = null;
        currentKingOwner = null;

        KingUnitData kingData = kingOwner != null ? kingOwner.SelectedKingData : null;
        UnitData baseData = kingData != null ? kingData.baseUnitData : null;
        if (kingOwner == null || baseData == null)
        {
            Debug.LogError("UnitDetailPanel received an invalid King runtime.");
            gameObject.SetActive(false);
            return;
        }

        currentKingOwner = kingOwner;
        unitNameText.text = $"{baseData.unitName} 국왕";
        healthText.text = string.Empty;
        attackDamageText.text = $"{kingOwner.CurrentKingAttackDamage:F0}";
        defenseText.text = $"{baseData.defense:F0}";
        magicResistText.text = $"{baseData.magicResistance:F0}";
        attackSpeedText.text = $"{kingOwner.CurrentKingAttackSpeed:F2}";
        attackRangeText.text = "ALL";
        attackTypeText.text = ConvertDamageTypeToString(baseData.damageType);
        blockCountText.text = $"{baseData.blockCount}";
        manaRegenText.text = ConvertManaRegenTypeToString(baseData.manaRegenType);
        skillDescriptionText.text = kingData.kingSkill != null
            && !string.IsNullOrWhiteSpace(kingData.kingSkill.description)
                ? kingData.kingSkill.description
                : "특별한 능력이 없습니다.";

        if (skillActivationToggle != null)
        {
            skillActivationToggle.onValueChanged.RemoveAllListeners();
            skillActivationToggle.gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        ++displayRevision;
        _addressableAssets?.Dispose();
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
        currentKingOwner = null;
    }

    private void UpdateSkillToggle(Unit unit)
    {
        if (skillActivationToggle == null) return;

        skillActivationToggle.onValueChanged.RemoveAllListeners();

        if (unit.Data.skillsByStarLevel.Length >= unit.starLevel &&
            unit.Data.skillsByStarLevel[unit.starLevel - 1] != null)
        {
            skillActivationToggle.gameObject.SetActive(true);
            skillActivationToggle.interactable = unit.IsLocalPlayerOwned;
            skillActivationToggle.SetIsOnWithoutNotify(unit.currentSkillActivationType == SkillActivationType.Automatic);
            skillActivationToggle.onValueChanged.AddListener(OnSkillActivationToggleChanged);
        }
        else
        {
            skillActivationToggle.gameObject.SetActive(false);
        }
    }

    private void OnSkillActivationToggleChanged(bool isAutomatic)
    {
        Unit unit = currentUnit;
        if (unit == null || !unit.IsLocalPlayerOwned)
        {
            return;
        }

        SkillActivationType requestedMode = isAutomatic
            ? SkillActivationType.Automatic
            : SkillActivationType.Manual;
        var gm = GameManagers.Instance;

        if (unit.Object == null || gm == null || gm.Runner == null || !gm.Runner.IsRunning)
        {
            unit.TrySetSkillActivationModeAuthoritative(requestedMode);
            skillActivationToggle.SetIsOnWithoutNotify(
                unit.currentSkillActivationType == SkillActivationType.Automatic);
            return;
        }

        int ownerPlayerId = unit.OwnerPlayerIdForRoster;
        if (ownerPlayerId < 0 || !unit.Object.IsValid)
        {
            skillActivationToggle.SetIsOnWithoutNotify(
                unit.currentSkillActivationType == SkillActivationType.Automatic);
            return;
        }

        if (gm.CommandProcessor == null)
        {
            skillActivationToggle.SetIsOnWithoutNotify(
                unit.currentSkillActivationType == SkillActivationType.Automatic);
            return;
        }

        gm.CommandProcessor.RequestCommandExecution(
            new SetSkillActivationModeCommand(ownerPlayerId, unit.Object.Id.Raw, requestedMode));
        RefreshSkillToggleFromAuthorityAsync(unit, requestedMode).Forget();
    }

    private async UniTaskVoid RefreshSkillToggleFromAuthorityAsync(
        Unit requestedUnit,
        SkillActivationType requestedMode)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            await UniTask.Delay(125, DelayType.Realtime);
            if (this == null || currentUnit != requestedUnit || skillActivationToggle == null)
            {
                return;
            }

            if (requestedUnit != null && requestedUnit.currentSkillActivationType == requestedMode)
            {
                break;
            }
        }

        if (requestedUnit != null && currentUnit == requestedUnit && skillActivationToggle != null)
        {
            skillActivationToggle.SetIsOnWithoutNotify(
                requestedUnit.currentSkillActivationType == SkillActivationType.Automatic);
        }
    }

    private async UniTask UpdateSkillDescription(Unit unit, int revision)
    {
        if (unit.Data.skillsByStarLevel.Length >= unit.starLevel)
        {
            // --- [핵심 수정 부분] ---
            string skillKey = unit.Data.skillsByStarLevel[unit.starLevel - 1];
            SkillData currentSkill = await AssetLoader.LoadAssetAsync<SkillData>(skillKey, _addressableAssets);
            if (revision != displayRevision || currentUnit != unit)
            {
                return;
            }
            // --- [수정 끝] ---

            if (currentSkill != null)
            {
                skillDescriptionText.text = currentSkill.description;
            }
            else
            {
                skillDescriptionText.text = "특별한 능력이 없습니다.";
            }
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
