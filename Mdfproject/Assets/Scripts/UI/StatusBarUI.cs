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
    
    private IHealth healthComponent;
    private IMana manaComponent;

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
        isUnit = GetComponentInParent<Unit>() != null;

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
        if (manaBarImage == null) return;
        
        // [핵심 변경] 이제 마나 UI를 보여줄지 여부를 여기서 최종 결정합니다.
        // 조건: 유닛이어야 하고, 전투 중이어야 하며, MaxMana가 0보다 커야 합니다 (즉, 스킬이 있어야 함).
        bool shouldShow = isUnit && isCombatPhase && max > 0;
        
        SetManaBarVisibility(shouldShow);
        
        if (shouldShow)
        {
            manaBarImage.fillAmount = current / max;
        }
    }
}