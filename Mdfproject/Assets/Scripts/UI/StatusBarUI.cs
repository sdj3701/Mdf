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

    // ✅ [핵심 수정] 실행 순서 문제를 해결하기 위해 모든 로직을 Awake()에서 Start()로 옮겼습니다.
    private void Start()
    {
        isUnit = GetComponentInParent<Unit>() != null;

        if (GameManagers.Instance != null)
        {
            isCombatPhase = GameManagers.Instance.GetGameState() == GameManagers.GameState.Combat;
        }

        // 체력 바 설정
        IHealth healthComponent = GetComponentInParent<IHealth>();
        if (healthComponent != null)
        {
            if (healthBarImage != null)
            {
                if (isUnit) healthBarImage.color = unitHealthColor;
                else if (GetComponentInParent<Monster>() != null) healthBarImage.color = monsterHealthColor;
            }
            
            healthComponent.OnHealthChanged += UpdateHealth;
            UpdateHealth(healthComponent.CurrentHealth, healthComponent.MaxHealth);
        }

        // 마나 바 설정 (이제 Unit이 maxMana를 정확히 설정한 후에 실행됩니다)
        IMana manaComponent = GetComponentInParent<IMana>();
        if (manaBarImage != null && manaComponent != null && manaComponent.MaxMana > 0)
        {
            manaBarImage.gameObject.SetActive(true);
            if (manaBarBackgroundImage != null)
            {
                manaBarBackgroundImage.gameObject.SetActive(true);
            }

            manaComponent.OnManaChanged += UpdateMana;
            UpdateMana(manaComponent.CurrentMana, manaComponent.MaxMana);
        }
        else if (manaBarImage != null)
        {
            manaBarImage.gameObject.SetActive(false);
            if (manaBarBackgroundImage != null)
            {
                manaBarBackgroundImage.gameObject.SetActive(false);
            }
        }

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
    }
    
    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        isCombatPhase = (newState == GameManagers.GameState.Combat);
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
            shouldShow = isCombatPhase;
        }
        else
        {
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
        
        bool shouldShow = isUnit || (isCombatPhase && !isUnit);
        
        manaBarImage.gameObject.SetActive(shouldShow);
        if (manaBarBackgroundImage != null)
        {
            manaBarBackgroundImage.gameObject.SetActive(shouldShow);
        }
        
        if (shouldShow && max > 0)
        {
            manaBarImage.fillAmount = current / max;
        }
    }
}