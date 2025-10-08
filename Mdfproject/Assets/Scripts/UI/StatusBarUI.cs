// Assets/Scripts/UI/StatusBarUI.cs

using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
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
    private bool isInitialized = false; // 초기화 플래그 추가

    private IHealth healthComponent;
    private IMana manaComponent;
    private Unit unitComponent;
    private GraphicRaycaster graphicRaycaster;

    private void Awake()
    {
        // Start보다 먼저 호출되므로, Unit.cs에서 참조를 사용할 때 null이 되는 것을 방지합니다.
        unitComponent = GetComponentInParent<Unit>();
        isUnit = unitComponent != null;
        graphicRaycaster = GetComponent<GraphicRaycaster>();
    }

    private void Start()
    {
        // GameManagers가 아직 준비되지 않았을 수 있으므로, Start에서 직접 초기화를 시도합니다.
        // 이렇게 하면 이벤트 타이밍과 무관하게 초기화가 보장됩니다.
        if (!isInitialized)
        {
            Initialize();
        }
    }

    private void OnEnable()
    {
        GameEvents.OnGameManagersReady += Initialize;
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnGameManagersReady -= Initialize;
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;

        if (healthComponent != null) healthComponent.OnHealthChanged -= UpdateHealth;
        if (manaComponent != null) manaComponent.OnManaChanged -= UpdateMana;
    }

    private void Initialize()
    {
        if (isInitialized) return;
        
        // GameManagers가 준비되지 않았다면, 기본값으로 초기화를 진행합니다.
        isCombatPhase = (GameManagers.Instance != null) 
            ? GameManagers.Instance.GetGameState() == GameManagers.GameState.Combat 
            : false;

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

        manaComponent = GetComponentInParent<IMana>();
        if (manaComponent != null)
        {
            manaComponent.OnManaChanged += UpdateMana;
        }
        else
        {
            SetManaBarVisibility(false);
        }

        // 스킬 버튼 초기 상태 설정
        if (skillButton != null)
        {
            // 유닛이 아닌 경우(몬스터, 벽 등)는 항상 스킬 버튼을 끕니다.
            if (!isUnit)
            {
                skillButton.gameObject.SetActive(false);
            }
            // 유닛인 경우는 일단 끄고, InitializeSkillButton이 호출되면 다시 판단합니다.
            else
            {
                skillButton.gameObject.SetActive(false);
            }
        }

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
        {
            canvas.worldCamera = Camera.main;
        }

        // 위치 오프셋 적용
        if (isUnit)
        {
            transform.localPosition = unitPositionOffset; // +=에서 =로 변경 (중복 적용 방지)
            transform.localScale = unitScale;
        }
        else
        {
            transform.localPosition = monsterPositionOffset; // +=에서 =로 변경 (중복 적용 방지)
            transform.localScale = monsterScale;
        }

        // 초기 UI 상태 설정 - 기본적으로 모두 꺼진 상태로 시작
        SetHealthBarVisibility(false);
        SetManaBarVisibility(false);
        if (skillButton != null)
        {
            skillButton.gameObject.SetActive(false);
        }

        isInitialized = true;

        // 초기화 완료 후 현재 상태에 맞춰 UI 업데이트
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
            // 유닛: 전투 중에만 표시
            shouldShow = isCombatPhase;
        }
        else
        {
            // 몬스터, 벽: 전투 중이고, 피해를 입었을 때만 표시 (체력이 최대가 아닐 때)
            shouldShow = isCombatPhase && (current > 0 && current < max);
        }

        SetHealthBarVisibility(shouldShow);

        if (shouldShow)
        {
            healthBarImage.fillAmount = (max > 0) ? (current / max) : 0f;
        }
    }

    private void UpdateMana(float current, float max)
    {
        // 마나 바 처리 - 유닛만 마나 바를 가지며, 전투 중이고 최대 마나가 0보다 클 때만 표시
        if (manaBarImage != null)
        {
            bool shouldShowManaBar = isUnit && isCombatPhase && max > 0;
            SetManaBarVisibility(shouldShowManaBar);
            if (shouldShowManaBar)
            {
                manaBarImage.fillAmount = (max > 0) ? (current / max) : 0f;
            }
        }

        // 스킬 버튼 처리 - 유닛만 스킬 버튼을 가질 수 있음
        if (skillButton != null && isUnit && unitComponent != null)
        {
            // Unit이 이미 로드하고 저장해 둔 'currentSkillActivationType' 값을 직접 사용합니다.
            // 이렇게 하면 비동기 로드가 필요 없어집니다.
            bool isManualSkill = unitComponent.currentSkillActivationType == SkillActivationType.Manual;
            
            bool shouldShowButton = false;
            if (isManualSkill && max > 0) // 스킬이 있는 경우에만
            {
                bool isManaFull = current >= max;
                shouldShowButton = isCombatPhase && isManaFull;
            }
            skillButton.gameObject.SetActive(shouldShowButton);
        }
        else if (skillButton != null && !isUnit)
        {
            // 몬스터나 벽의 경우 스킬 버튼을 항상 끔
            skillButton.gameObject.SetActive(false);
        }
    }

    public async UniTask InitializeSkillButton(Unit owner)
    {
        if (owner == null || skillButton == null)
        {
            if (skillButton != null) skillButton.gameObject.SetActive(false);
            return;
        }

        this.unitComponent = owner;

        SkillData currentSkill = null;
        if (owner.Data != null && owner.Data.skillsByStarLevel != null && 
            owner.Data.skillsByStarLevel.Length >= owner.starLevel &&
            !string.IsNullOrEmpty(owner.Data.skillsByStarLevel[owner.starLevel - 1]))
        {
            // --- [핵심 수정 부분] ---
            string skillKey = owner.Data.skillsByStarLevel[owner.starLevel - 1];
            currentSkill = await AssetLoader.LoadAssetAsync<SkillData>(skillKey);
            // --- [수정 끝] ---
        }

        // 스킬이 있고 수동 스킬인 경우에만 버튼 설정
        if (currentSkill != null && currentSkill.activationType == SkillActivationType.Manual)
        {
            if (graphicRaycaster != null) graphicRaycaster.enabled = true;
            
            if (skillIconImage != null && currentSkill.icon != null)
            {
                skillIconImage.sprite = currentSkill.icon;
            }
            skillButton.onClick.RemoveAllListeners(); // 중복 방지
            skillButton.onClick.AddListener(owner.ActivateSkill);
            // 버튼은 UpdateMana에서 마나가 찰 때 활성화됩니다.
        }
        else
        {
            // 스킬이 없거나 자동 스킬인 경우
            if (graphicRaycaster != null) graphicRaycaster.enabled = false;
            skillButton.gameObject.SetActive(false);
        }
    }
}
