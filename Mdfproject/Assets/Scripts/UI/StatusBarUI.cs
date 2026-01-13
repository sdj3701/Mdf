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

    [Header("3D 월드 스페이스 정렬")]
    [Tooltip("World Space UI일 때 사용할 카메라. 비워두면 플레이어 카메라/Camera.main을 사용합니다.")]
    public Camera overrideCamera;

    // --- [삭제] ---
    // 카메라 회전 관련 로직과 변수는 UIBillboard.cs로 이전되었으므로 모두 삭제합니다.
    // public bool matchCameraPitch = true; 
    // private Transform mainCameraTransform;

    private bool isUnit = false;
    private bool isCombatPhase = false;
    private bool isInitialized = false;

    private IHealth healthComponent;
    private IMana manaComponent;
    private Unit unitComponent;
    private GraphicRaycaster graphicRaycaster;
    private Canvas cachedCanvas;
    private Camera cachedCamera;

    private void Awake()
    {
        unitComponent = GetComponentInParent<Unit>();
        isUnit = unitComponent != null;
        graphicRaycaster = GetComponent<GraphicRaycaster>();
        cachedCanvas = GetComponent<Canvas>();
    }

    private void Start()
    {
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
        
        isCombatPhase = (GameManagers.Instance != null) 
            ? (GameManagers.Instance.GetGameState() == GameManagers.GameState.Battle1 || GameManagers.Instance.GetGameState() == GameManagers.GameState.Battle2)
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

        if (skillButton != null)
        {
            if (!isUnit)
            {
                skillButton.gameObject.SetActive(false);
            }
            else
            {
                skillButton.gameObject.SetActive(false);
            }
        }

        if (cachedCanvas != null && cachedCanvas.renderMode == RenderMode.WorldSpace)
        {
            // Canvas가 이벤트를 올바르게 수신하기 위해 worldCamera 설정은 여전히 필요합니다.
            cachedCanvas.worldCamera = ResolveCamera();
        }

        if (isUnit)
        {
            transform.localPosition = unitPositionOffset;
            transform.localScale = unitScale;
        }
        else
        {
            transform.localPosition = monsterPositionOffset;
            transform.localScale = monsterScale;
        }

        SetHealthBarVisibility(false);
        SetManaBarVisibility(false);
        if (skillButton != null)
        {
            skillButton.gameObject.SetActive(false);
        }

        isInitialized = true;
        UpdateAllUIVisibility();
    }
    
    // --- [삭제] ---
    // LateUpdate() 함수를 완전히 삭제하여 UIBillboard.cs가 회전을 전담하도록 합니다.

    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        isCombatPhase = (newState == GameManagers.GameState.Battle1 || newState == GameManagers.GameState.Battle2);
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
            healthBarImage.fillAmount = (max > 0) ? (current / max) : 0f;
        }
    }

    private void UpdateMana(float current, float max)
    {
        if (manaBarImage != null)
        {
            bool shouldShowManaBar = isUnit && isCombatPhase && max > 0;
            SetManaBarVisibility(shouldShowManaBar);
            if (shouldShowManaBar)
            {
                manaBarImage.fillAmount = (max > 0) ? (current / max) : 0f;
            }
        }

        if (skillButton != null && isUnit && unitComponent != null)
        {
            bool isManualSkill = unitComponent.currentSkillActivationType == SkillActivationType.Manual;
            
            bool shouldShowButton = false;
            if (isManualSkill && max > 0)
            {
                bool isManaFull = current >= max;
                shouldShowButton = isCombatPhase && isManaFull;
            }
            skillButton.gameObject.SetActive(shouldShowButton);
        }
        else if (skillButton != null && !isUnit)
        {
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
            string skillKey = owner.Data.skillsByStarLevel[owner.starLevel - 1];
            currentSkill = await AssetLoader.LoadAssetAsync<SkillData>(skillKey);
        }

        if (currentSkill != null && currentSkill.activationType == SkillActivationType.Manual)
        {
            if (graphicRaycaster != null) graphicRaycaster.enabled = true;
            
            if (skillIconImage != null && currentSkill.icon != null)
            {
                skillIconImage.sprite = currentSkill.icon;
            }
            skillButton.onClick.RemoveAllListeners();
            skillButton.onClick.AddListener(owner.ActivateSkill);
        }
        else
        {
            if (graphicRaycaster != null) graphicRaycaster.enabled = false;
            skillButton.gameObject.SetActive(false);
        }
    }

    private Camera ResolveCamera()
    {
        if (overrideCamera != null) return overrideCamera;
        if (cachedCamera != null) return cachedCamera;

        cachedCamera = ComponentRegistry.Get<Camera>("Main Camera", false);
        if (cachedCamera == null)
        {
            cachedCamera = Camera.main;
        }
        if (cachedCamera == null)
        {
            cachedCamera = FindObjectOfType<Camera>();
        }
        return cachedCamera;
    }
}