// Assets/Scripts/UI/StatusBarUI.cs

using UnityEngine;
using UnityEngine.UI;

public class StatusBarUI : MonoBehaviour
{
    [Header("컴포넌트")]
    [Tooltip("Fill Amount를 조절할 체력 바 이미지")]
    public Image healthBarImage;
    [Tooltip("체력 바의 배경 이미지")]
    public Image healthBarBackgroundImage;
    [Tooltip("Fill Amount를 조절할 마나 바 이미지")]
    public Image manaBarImage;
    [Tooltip("마나 바의 배경 이미지")]
    public Image manaBarBackgroundImage;

    [Header("스킬 버튼 UI")]
    [Tooltip("스킬 사용 버튼")]
    public Button skillButton;
    [Tooltip("스킬 아이콘을 표시할 이미지")]
    public Image skillIconImage;

    [Header("색상 설정")]
    [Tooltip("플레이어 유닛의 체력 바 색상")]
    public Color unitHealthColor = new Color(0.2f, 0.8f, 0.2f); // Green
    [Tooltip("몬스터의 체력 바 색상")]
    public Color monsterHealthColor = new Color(0.8f, 0.2f, 0.2f); // Red

    [Header("유닛 UI 설정")]
    [Tooltip("유닛에 부착될 때의 UI 위치 오프셋입니다.")]
    public Vector3 unitPositionOffset;
    [Tooltip("유닛에 부착될 때의 UI 스케일입니다.")]
    public Vector3 unitScale = Vector3.one;

    [Header("몬스터 & 벽 UI 설정")]
    [Tooltip("몬스터나 벽에 부착될 때의 UI 위치 오프셋입니다.")]
    public Vector3 monsterPositionOffset;
    [Tooltip("몬스터나 벽에 부착될 때의 UI 스케일입니다.")]
    public Vector3 monsterScale = Vector3.one;

    private bool isUnit = false;
    private bool isCombatPhase = false;

    private IHealth healthComponent;
    private IMana manaComponent;
    private Unit unitComponent;

    private void Awake()
    {
        // Start보다 먼저 호출되므로, Unit.cs에서 참조를 사용할 때 null이 되는 것을 방지합니다.
        unitComponent = GetComponentInParent<Unit>();
        isUnit = unitComponent != null;
    }

    private void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;

        if (healthComponent != null) healthComponent.OnHealthChanged -= UpdateHealth;
        if (manaComponent != null) manaComponent.OnManaChanged -= UpdateMana;
    }

    private void Start()
    {
        if (GameManagers.Instance != null)
        {
            isCombatPhase = GameManagers.Instance.GetGameState() == GameManagers.GameState.Combat;
        }

        // 체력 바 설정
        healthComponent = GetComponentInParent<IHealth>();
        if (healthComponent != null)
        {
            if (healthBarImage != null)
            {
                if (isUnit) healthBarImage.color = unitHealthColor;
                else if (GetComponentInParent<Monster>() != null) healthBarImage.color = monsterHealthColor;
            }
            healthComponent.OnHealthChanged += UpdateHealth;
        }

        // 마나 바 설정
        manaComponent = GetComponentInParent<IMana>();
        if (manaComponent != null)
        {
            // [핵심 변경] MaxMana 값과 상관없이 우선 이벤트를 구독합니다.
            manaComponent.OnManaChanged += UpdateMana;
        }
        else
        {
            SetManaBarVisibility(false);
        }

        // 유닛이 아닌 경우 (몬스터, 벽 등) 스킬 버튼을 확실히 비활성화합니다.
        if (!isUnit && skillButton != null)
        {
            skillButton.gameObject.SetActive(false);
        }

        // [변경] Unit.cs에서 스탯 초기화 후 직접 호출하도록 변경되었으므로 Start에서 호출하지 않습니다.
        //InitializeSkillButton();

        // 캔버스 및 위치/스케일 설정
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
        {
            canvas.worldCamera = Camera.main;
        }

        if (isUnit)
        {
            transform.localPosition += unitPositionOffset;
            transform.localScale = unitScale;
        }
        else
        {
            transform.localPosition += monsterPositionOffset;
            transform.localScale = monsterScale;
        }

        UpdateAllUIVisibility();
    }

    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        isCombatPhase = (newState == GameManagers.GameState.Combat);
        UpdateAllUIVisibility();
    }

    private void UpdateAllUIVisibility()
    {
        if (healthComponent != null)
        {
            UpdateHealth(healthComponent.CurrentHealth, healthComponent.MaxHealth);
        }
        if (manaComponent != null)
        {
            UpdateMana(manaComponent.CurrentMana, manaComponent.MaxMana);
        }
        else
        {
            // manaComponent가 아예 없는 경우 (예: DestructibleWall) 확실하게 꺼줍니다.
            SetManaBarVisibility(false);
        }
    }

    private void SetHealthBarVisibility(bool visible)
    {
        if (healthBarImage != null) healthBarImage.gameObject.SetActive(visible);
        if (healthBarBackgroundImage != null) healthBarBackgroundImage.gameObject.SetActive(visible);
    }

    private void SetManaBarVisibility(bool visible)
    {
        if (manaBarImage != null) manaBarImage.gameObject.SetActive(visible);
        if (manaBarBackgroundImage != null) manaBarBackgroundImage.gameObject.SetActive(visible);
    }

    private void UpdateHealth(float current, float max)
    {
        if (healthBarImage == null) return;

        bool shouldShow;
        if (isUnit)
        {
            shouldShow = isCombatPhase;
        }
        else
        {
            shouldShow = isCombatPhase && (current > 0 && current < max);
        }

        SetHealthBarVisibility(shouldShow);

        if (shouldShow)
        {
            healthBarImage.fillAmount = current / max;
        }
    }

    private void UpdateMana(float current, float max)
    {
        // 마나 바 처리
        if (manaBarImage != null)
        {
            bool shouldShowManaBar = isUnit && isCombatPhase && max > 0;
            SetManaBarVisibility(shouldShowManaBar);
            if (shouldShowManaBar)
            {
                manaBarImage.fillAmount = current / max;
            }
        }

        // 스킬 버튼 처리
        if (skillButton != null && unitComponent != null && unitComponent.Data != null)
        {
            SkillData currentSkill = (unitComponent.Data.skillsByStarLevel.Length >= unitComponent.starLevel)
                ? unitComponent.Data.skillsByStarLevel[unitComponent.starLevel - 1]
                : null;

            bool shouldShowButton = false;
            if (currentSkill != null && currentSkill.activationType == SkillActivationType.Manual)
            {
                bool isManaFull = max > 0 && current >= max;
                shouldShowButton = isCombatPhase && isManaFull;
            }
            skillButton.gameObject.SetActive(shouldShowButton);
        }
    }

    public void InitializeSkillButton(Unit owner)
    {
        // Unit에서 직접 owner를 전달받아 실행 순서에 대한 의존성을 제거합니다.
        if (owner == null || skillButton == null)
        {
            if (skillButton != null) skillButton.gameObject.SetActive(false);
            return;
        }

        // [수정] owner를 내부 unitComponent 참조에도 할당하여 일관성을 보장합니다.
        // 이렇게 하면 UpdateMana에서도 항상 정확한 Unit 인스턴스를 사용하게 됩니다.
        this.unitComponent = owner;

        SkillData currentSkill = null;
        if (owner.Data != null && owner.Data.skillsByStarLevel.Length >= owner.starLevel)
        {
            currentSkill = owner.Data.skillsByStarLevel[owner.starLevel - 1];
        }



        if (currentSkill != null && currentSkill.activationType == SkillActivationType.Manual)
        {
            if (skillIconImage != null && currentSkill.icon != null)
            {
                skillIconImage.sprite = currentSkill.icon;
            }
            skillButton.onClick.AddListener(owner.ActivateSkill);
        }
        else
        {
            skillButton.gameObject.SetActive(false);
        }
    }
}
