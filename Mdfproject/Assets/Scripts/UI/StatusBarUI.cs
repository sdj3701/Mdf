using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using MDF.Runtime.Assets;

public class StatusBarUI : MonoBehaviour
{
    private AddressableAssetOwner _addressableAssets = new AddressableAssetOwner();

    private static readonly List<StatusBarUI> ActiveStatusBars = new List<StatusBarUI>(64);
    private static readonly Vector3[] ButtonWorldCorners = new Vector3[4];

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
    
    public void ResetForReuse(bool initializeImmediately = true)
    {
        skillInitializationVersion++;
        _addressableAssets?.Dispose();
        _addressableAssets = new AddressableAssetOwner();
        UnbindVitalComponents();
        unitComponent = GetComponentInParent<Unit>();
        isUnit = unitComponent != null;
        isInitialized = false;
        isCombatPhase = false;
        lastSkillRequestFrame = -1;
        
        ResetBarFillValues();
        SetHealthBarVisibility(false);
        SetManaBarVisibility(false);
        if (skillButton != null)
        {
            skillButton.onClick.RemoveAllListeners();
            skillButton.gameObject.SetActive(false);
        }
        if (skillIconImage != null)
        {
            skillIconImage.sprite = null;
        }
        if (graphicRaycaster != null)
        {
            graphicRaycaster.enabled = false;
        }
        
        if (initializeImmediately && GameManagers.Instance != null)
        {
            Initialize();
            if (unitComponent != null && unitComponent.Data != null)
            {
                InitializeSkillButton(unitComponent).Forget();
            }
        }
    }
    private Unit unitComponent;
    private GraphicRaycaster graphicRaycaster;
    private Canvas cachedCanvas;
    private Camera cachedCamera;
    private int lastSkillRequestFrame = -1;
    private int skillInitializationVersion;

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
        if (_addressableAssets == null || _addressableAssets.IsDisposed)
        {
            _addressableAssets = new AddressableAssetOwner();
        }

        if (!ActiveStatusBars.Contains(this))
        {
            ActiveStatusBars.Add(this);
        }

        GameEvents.OnGameManagersReady += Initialize;
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
        Initialize();

        if (unitComponent != null && unitComponent.Data != null)
        {
            InitializeSkillButton(unitComponent).Forget();
        }
    }

    private void OnDisable()
    {
        skillInitializationVersion++;
        _addressableAssets?.Dispose();
        GameEvents.OnGameManagersReady -= Initialize;
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;

        UnbindVitalComponents();
        isInitialized = false;
        ActiveStatusBars.Remove(this);
    }

    private void Update()
    {
        if (MdfInput.PrimaryPointerWasReleasedThisFrame() && IsPointerOverSkillButton(MdfInput.PointerPosition))
        {
            RequestSkillActivation(unitComponent);
        }
    }

    private void Initialize()
    {
        isCombatPhase = (GameManagers.Instance != null) 
            ? (GameManagers.Instance.GetGameState() == GameManagers.GameState.Battle1 || GameManagers.Instance.GetGameState() == GameManagers.GameState.Battle2)
            : false;

        UnbindVitalComponents();
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

        ResetBarFillValues();
        SetHealthBarVisibility(false);
        SetManaBarVisibility(false);
        if (skillButton != null)
        {
            skillButton.gameObject.SetActive(false);
        }

        isInitialized = true;
        UpdateAllUIVisibility();
    }

    private void UnbindVitalComponents()
    {
        if (healthComponent != null)
        {
            healthComponent.OnHealthChanged -= UpdateHealth;
            healthComponent = null;
        }

        if (manaComponent != null)
        {
            manaComponent.OnManaChanged -= UpdateMana;
            manaComponent = null;
        }
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

    private void ResetBarFillValues()
    {
        if (healthBarImage != null) healthBarImage.fillAmount = 1f;
        if (manaBarImage != null) manaBarImage.fillAmount = 0f;
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
            // 몬스터: 피해를 받아서 current < max일 때만 표시
            // max가 0 이하이면 아직 초기화되지 않은 상태이므로 숨김
            shouldShow = isCombatPhase && max > 0 && current > 0 && current < max;
        }

        SetHealthBarVisibility(shouldShow);

        if (max > 0)
        {
            healthBarImage.fillAmount = Mathf.Clamp01(current / max);
        }
        else
        {
            healthBarImage.fillAmount = 1f;
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
            // [핵심 수정] 로컬 플레이어가 소유한 유닛에만 스킬 버튼 표시
            if (isManualSkill && max > 0 && unitComponent.IsLocalPlayerOwned)
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
        int initializationVersion = ++skillInitializationVersion;
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
            currentSkill = await AssetLoader.LoadAssetAsync<SkillData>(skillKey, _addressableAssets);
        }

        if (this == null ||
            !isActiveAndEnabled ||
            initializationVersion != skillInitializationVersion ||
            unitComponent != owner)
        {
            return;
        }

        if (currentSkill != null && currentSkill.activationType == SkillActivationType.Manual)
        {
            if (graphicRaycaster != null) graphicRaycaster.enabled = true;
            
            if (skillIconImage != null && currentSkill.icon != null)
            {
                skillIconImage.sprite = currentSkill.icon;
            }
            skillButton.onClick.RemoveAllListeners();
            // [수정] 직접 호출 대신 커맨드 패턴으로 네트워크 동기화
            skillButton.onClick.AddListener(() => RequestSkillActivation(owner));
        }
        else
        {
            if (graphicRaycaster != null) graphicRaycaster.enabled = false;
            skillButton.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 스킬 활성화를 커맨드 패턴으로 서버에 요청합니다.
    /// </summary>
    private void RequestSkillActivation(Unit owner)
    {
        if (lastSkillRequestFrame == Time.frameCount)
        {
            return;
        }

        lastSkillRequestFrame = Time.frameCount;

        if (owner == null || owner.Object == null) return;
        if (owner.Owner == null) return;
        
        var gm = GameManagers.Instance;
        if (gm == null || gm.CommandProcessor == null) return;
        
        uint networkId = owner.Object.Id.Raw;
        var command = new ActivateSkillCommand(owner.Owner.playerId, networkId);
        gm.CommandProcessor.RequestCommandExecution(command);
    }

    public static bool IsPointerOverActiveSkillButton(Vector2 screenPosition)
    {
        for (int i = ActiveStatusBars.Count - 1; i >= 0; i--)
        {
            var statusBar = ActiveStatusBars[i];
            if (statusBar == null)
            {
                ActiveStatusBars.RemoveAt(i);
                continue;
            }

            if (statusBar.isActiveAndEnabled && statusBar.IsPointerOverSkillButton(screenPosition))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPointerOverSkillButton(Vector2 screenPosition)
    {
        if (skillButton == null || !skillButton.gameObject.activeInHierarchy || !skillButton.interactable)
        {
            return false;
        }

        var buttonRect = skillButton.transform as RectTransform;
        if (buttonRect == null)
        {
            return false;
        }

        Camera eventCamera = null;
        Canvas buttonCanvas = skillButton.GetComponentInParent<Canvas>();
        if (buttonCanvas != null && buttonCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            eventCamera = buttonCanvas.worldCamera != null ? buttonCanvas.worldCamera : ResolveCamera();
        }

        if (RectTransformUtility.RectangleContainsScreenPoint(buttonRect, screenPosition, eventCamera))
        {
            return true;
        }

        if (eventCamera != null)
        {
            return false;
        }

        buttonRect.GetWorldCorners(ButtonWorldCorners);
        float minX = Mathf.Min(ButtonWorldCorners[0].x, ButtonWorldCorners[2].x);
        float maxX = Mathf.Max(ButtonWorldCorners[0].x, ButtonWorldCorners[2].x);
        float minY = Mathf.Min(ButtonWorldCorners[0].y, ButtonWorldCorners[2].y);
        float maxY = Mathf.Max(ButtonWorldCorners[0].y, ButtonWorldCorners[2].y);
        return screenPosition.x >= minX && screenPosition.x <= maxX &&
               screenPosition.y >= minY && screenPosition.y <= maxY;
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
