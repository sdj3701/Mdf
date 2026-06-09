// Assets/Scripts/UI/AugmentUIController.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AugmentUIController : MonoBehaviour
{
    private enum UiLifecycleState
    {
        Hidden,
        Loading,
        DataBinding,
        Visible,
        Closing
    }

    [Header("슬롯 설정")]
    public AugmentSlot[] augmentSlots;

    [Header("슬롯 컨테이너 설정")]
    public GameObject slotsContainer;
    public GameObject rerollButtonObject;

    // 이벤트를 통해 전달받은 데이터를 임시로 저장할 변수들
    private PlayerManager localPlayer;
    private List<AugmentData> currentChoices;
    public event Action<bool> OnContentVisibilityChanged;
    private UiLifecycleState _uiState = UiLifecycleState.Hidden;
    private CanvasGroup _rootCanvasGroup;
    private string _lastAugmentTriggerKey = string.Empty;
    private float _lastAugmentTriggerRealtime = -10f;
    private int _presentationVersion;

    private static string SafePlayerId(PlayerManager player)
    {
        if (player == null || player.Object == null || !player.Object.IsValid)
        {
            return "null";
        }

        try
        {
            return player.playerId.ToString();
        }
        catch (System.InvalidOperationException)
        {
            return "?";
        }
    }

    void Awake()
    {
        // [수정] Awake에서 구독하여 GameObject 비활성화 시에도 이벤트를 수신
        GameEvents.OnAugmentPhaseStart += HandleAugmentPhaseStart;
        BuildDebugGUI.LogClient("[AugmentUI] Awake: subscribed OnAugmentPhaseStart");
        EnsureRootCanvasGroup();
        SetPanelRootVisibility(false);
        // Debug.Log($"<color=lime>[AugmentUIController] Awake: OnAugmentPhaseStart 이벤트 구독 완료</color>");
    }

    private void OnDisable()
    {
        SetContentVisibility(false);
        SetPanelRootVisibility(false);
        _uiState = UiLifecycleState.Hidden;
    }

    void OnDestroy()
    {
        // [수정] 오브젝트가 파괴될 때만 구독 해제
        GameEvents.OnAugmentPhaseStart -= HandleAugmentPhaseStart;
    }

    /// <summary>
    /// OnAugmentPhaseStart 이벤트가 발생했을 때 호출되는 핸들러입니다.
    /// </summary>
    private async void HandleAugmentPhaseStart(PlayerManager player, List<AugmentData> choices)
    {        
        // 이 UI는 로컬 플레이어의 것만 처리합니다.
        var localPlayer = GameManagers.Instance?.localPlayer;
        BuildDebugGUI.LogClient($"[AugmentUI] Event received local={SafePlayerId(localPlayer)} target={SafePlayerId(player)} choices={choices?.Count ?? 0}");
        
        if (localPlayer != player)
        {
            BuildDebugGUI.LogClient("[AugmentUI] Event ignored: target is not local player.");
            return;
        }

        if (GamePrepareUIToolkitController.TryShowAugmentsFromLegacy(player, choices))
        {
            InitializeAndHide();
            BuildDebugGUI.LogClient("[AugmentUI] Routed to UI Toolkit panel.");
            return;
        }

        this.localPlayer = player;
        this.currentChoices = choices;
        _presentationVersion++;
        int version = _presentationVersion;

        string triggerKey = BuildAugmentTriggerKey(player, choices);
        float now = Time.unscaledTime;
        if (triggerKey == _lastAugmentTriggerKey && now - _lastAugmentTriggerRealtime < 1.5f)
        {
            BuildDebugGUI.LogClient($"[AugmentUI] Duplicate trigger ignored key={triggerKey}");
            return;
        }

        _lastAugmentTriggerKey = triggerKey;
        _lastAugmentTriggerRealtime = now;
        _uiState = UiLifecycleState.Loading;
        SetContentVisibility(false);
        SetPanelRootVisibility(false);

        // [핵심 수정] UIPool.activeObject와 동기화되도록 GetUIElement를 통해 활성화
        // 직접 SetActive(true)를 호출하면 activeObject가 설정되지 않아
        // 이후 ReturnUIElement가 동작하지 않는 문제 발생
        if (!gameObject.activeSelf)
        {
            BuildDebugGUI.LogClient("[AugmentUI] Panel inactive -> requesting UI_Pnl_Augment");
            if (UIManagers.Instance == null)
            {
                BuildDebugGUI.LogClient("[AugmentUI] UIManagers.Instance is null. Abort trigger.");
                return;
            }

            await UIManagers.Instance.GetUIElement("UI_Pnl_Augment");
        }

        if (version != _presentationVersion)
        {
            return;
        }

        _uiState = UiLifecycleState.DataBinding;
        SetAugmentChoices(choices ?? new List<AugmentData>());

        bool hasChoices = choices != null && choices.Count > 0;
        SetContentVisibility(hasChoices);
        SetPanelRootVisibility(hasChoices);
        _uiState = hasChoices ? UiLifecycleState.Visible : UiLifecycleState.Hidden;
        BuildDebugGUI.LogClient($"[AugmentUI] Panel state={_uiState} choices={choices?.Count ?? 0}");
    }

    /// <summary>
    /// 전달받은 증강 리스트를 사용하여 UI 슬롯을 설정합니다.
    /// </summary>
    public void SetAugmentChoices(List<AugmentData> choices)
    {
        for (int i = 0; i < augmentSlots.Length; i++)
        {
            if (i < choices.Count)
            {
                AugmentData data = choices[i];
                string displayName = GamePrepareUIToolkitController.FormatAugmentDisplayName(data.augmentName);
                augmentSlots[i].Display(data, displayName);

                // 리스너 중복 추가를 방지하기 위해 항상 먼저 제거합니다.
                augmentSlots[i].selectButton.onClick.RemoveAllListeners();
                
                // 루프 변수 'i'를 새로운 지역 변수에 복사해야 클로저 문제를 피할 수 있습니다.
                int choiceIndex = i; 
                
                // 버튼 클릭 시 OnAugmentButtonClicked 메서드가 호출되도록 리스너를 추가합니다.
                augmentSlots[i].selectButton.onClick.AddListener(() => OnAugmentButtonClicked(choiceIndex));
            }
            else
            {
                // 표시할 증강이 부족하면 슬롯을 비활성화합니다.
                augmentSlots[i].Display(null);
            }
        }
    }

    /// <summary>
    /// 플레이어가 증강 버튼을 클릭했을 때 호출됩니다.
    /// </summary>
    private void OnAugmentButtonClicked(int index)
    {
        // Debug.Log("<color=yellow>OnAugmentButtonClicked 호출</color>");
        // [핵심 변경점]
        // 이제 이벤트를 직접 발생시키는 대신, SelectAugmentCommand를 생성하여 실행합니다.
        // 이를 통해 플레이어의 행동과 AI의 행동이 동일한 로직을 타게 됩니다.
        if (localPlayer != null && currentChoices != null && index < currentChoices.Count)
        {
            var command = new SelectAugmentCommand(localPlayer.playerId, index);
            GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
            _uiState = UiLifecycleState.Closing;
            SetContentVisibility(false);
            SetPanelRootVisibility(false);
            
            // 증강 선택 후 UI 숨김 - UIPool 상태 동기화를 위해 ReturnUIElement 사용
            // 직접 SetActive(false)를 호출하면 UIPool.activeObject가 불일치하여 
            // 다음 라운드에서 ReturnUIElement가 동작하지 않는 문제 발생
            UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
        }
        else
        {
            // Debug.LogError($"증강 선택 처리 중 오류 발생: LocalPlayer: {localPlayer}, Choices: {currentChoices}, Index: {index}");
        }
    }

    /// <summary>
    /// 콘텐츠의 표시/숨김을 설정합니다.
    /// </summary>
    public void SetContentVisibility(bool isVisible)
    {
        if (slotsContainer != null) slotsContainer.SetActive(isVisible);
        if (rerollButtonObject != null) rerollButtonObject.SetActive(isVisible);
        OnContentVisibilityChanged?.Invoke(isVisible);
    }

    /// <summary>
    /// 콘텐츠가 현재 표시 중인지 확인합니다.
    /// </summary>
    public bool IsContentVisible()
    {
        return slotsContainer != null && slotsContainer.activeSelf;
    }

    /// <summary>
    /// GameManagers에서 호출. 초기화 후 UI를 숨깁니다.
    /// </summary>
    public void InitializeAndHide()
    {
        EnsureRootCanvasGroup();
        _uiState = UiLifecycleState.Hidden;
        SetContentVisibility(false);
        SetPanelRootVisibility(false);
    }

    private void EnsureRootCanvasGroup()
    {
        if (_rootCanvasGroup == null)
        {
            _rootCanvasGroup = GetComponent<CanvasGroup>();
            if (_rootCanvasGroup == null)
            {
                _rootCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }
    }

    private void SetPanelRootVisibility(bool isVisible)
    {
        EnsureRootCanvasGroup();
        _rootCanvasGroup.alpha = isVisible ? 1f : 0f;
        _rootCanvasGroup.interactable = isVisible;
        _rootCanvasGroup.blocksRaycasts = isVisible;
    }

    private static string BuildAugmentTriggerKey(PlayerManager player, List<AugmentData> choices)
    {
        int playerId = -1;
        if (player != null && player.Object != null && player.Object.IsValid)
        {
            try
            {
                playerId = player.playerId;
            }
            catch (InvalidOperationException)
            {
                playerId = -1;
            }
        }
        int round = -1;
        if (GameManagers.Instance != null)
        {
            try
            {
                round = GameManagers.Instance.currentRound;
            }
            catch (InvalidOperationException)
            {
                round = -1;
            }
        }
        string choiceSig = choices == null
            ? "none"
            : string.Join(",", choices.Select(choice => choice != null ? choice.augmentName : "null"));
        return $"r={round}|p={playerId}|choices={choiceSig}";
    }
}
