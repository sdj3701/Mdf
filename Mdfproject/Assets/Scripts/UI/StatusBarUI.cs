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

    private void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
    }

    private void Awake()
    {
        isUnit = GetComponentInParent<Unit>() != null;

        if (GameManagers.Instance != null)
        {
            isCombatPhase = GameManagers.Instance.GetGameState() == GameManagers.GameState.Combat;
        }

        IHealth healthComponent = GetComponentInParent<IHealth>();
        if (healthComponent != null)
        {
            if (healthBarImage != null)
            {
                // 부모가 Unit인지 Monster인지 확인하여 체력 바 색상을 설정합니다.
                if (isUnit)
                {
                    healthBarImage.color = unitHealthColor;
                }
                else if (GetComponentInParent<Monster>() != null)
                {
                    healthBarImage.color = monsterHealthColor;
                }
            }
            
            healthComponent.OnHealthChanged += UpdateHealth;
            UpdateHealth(healthComponent.CurrentHealth, healthComponent.MaxHealth);
        }

        IMana manaComponent = GetComponentInParent<IMana>();
        // 마나 컴포넌트가 있고, 최대 마나가 0보다 큰 경우에만 마나 바를 활성화하고 이벤트를 구독합니다.
        if (manaBarImage != null)
        {
            // 유닛이 아닌 경우(몬스터, 벽 등) 전투 중에만 마나바가 보일 수 있습니다.
            bool canShowManaBar = isUnit || isCombatPhase;
            bool shouldShowManaBar = canShowManaBar && manaComponent != null && manaComponent.MaxMana > 0;

            manaBarImage.gameObject.SetActive(shouldShowManaBar);
            if (manaBarBackgroundImage != null)
            {
                manaBarBackgroundImage.gameObject.SetActive(shouldShowManaBar);
            }

            if (shouldShowManaBar)
            {
                manaComponent.OnManaChanged += UpdateMana;
                UpdateMana(manaComponent.CurrentMana, manaComponent.MaxMana);
            }
        }
    }

    private void Start()
    {
        // 캔버스를 찾아 월드 카메라를 자동으로 설정합니다.
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
        {
            canvas.worldCamera = Camera.main;
        }

        // 타입에 맞는 오프셋과 스케일을 적용합니다.
        if (isUnit)
        {
            transform.localPosition += unitPositionOffset;
            transform.localScale = unitScale;
        }
        else // 몬스터 또는 벽
        {
            transform.localPosition += monsterPositionOffset;
            transform.localScale = monsterScale;
        }
    }
    
    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        isCombatPhase = (newState == GameManagers.GameState.Combat);
        // 유닛의 경우, 게임 상태가 변경될 때 UI 표시 여부를 다시 계산해야 합니다.
        if (isUnit)
        {
            IHealth healthComponent = GetComponentInParent<IHealth>();
            if (healthComponent != null)
            {
                UpdateHealth(healthComponent.CurrentHealth, healthComponent.MaxHealth);
            }
        }
    }

    private void UpdateHealth(float current, float max)
    {
        if (healthBarImage == null) return;

        bool shouldShow;
        if (isUnit)
        {
            // 유닛: 전투 단계일 때만 UI를 표시합니다.
            shouldShow = isCombatPhase;
        }
        else
        {
            // 몬스터와 벽: 전투 중이고, 피해를 입었을 때만 UI를 표시합니다.
            shouldShow = isCombatPhase && (current > 0 && current < max);
        }

        healthBarImage.gameObject.SetActive(shouldShow);
        if (healthBarBackgroundImage != null)
        {
            healthBarBackgroundImage.gameObject.SetActive(shouldShow);
        }

        if (shouldShow)
        {
            healthBarImage.fillAmount = current / max;
        }
    }

    private void UpdateMana(float current, float max)
    {
        if (manaBarImage == null) return;
        
        // UpdateMana는 Awake에서 마나 바가 활성화된 경우에만 호출됩니다.
        // max가 0일 수 없다고 가정합니다 (Awake에서 확인했기 때문).
        if (max > 0)
        {
            manaBarImage.fillAmount = current / max;
        }
    }
}
