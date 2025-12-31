// Assets/Scripts/UI/AugmentUIController.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public class AugmentUIController : MonoBehaviour
{
    [Header("슬롯 설정")]
    public AugmentSlot[] augmentSlots;

    [Header("슬롯 컨테이너 설정")]
    public GameObject slotsContainer;
    public GameObject rerollButtonObject;

    // 이벤트를 통해 전달받은 데이터를 임시로 저장할 변수들
    private PlayerManager localPlayer;
    private List<AugmentData> currentChoices;
    public event Action<bool> OnContentVisibilityChanged;

    void OnEnable()
    {
        // 증강 단계 시작 이벤트를 구독합니다.
        GameEvents.OnAugmentPhaseStart += HandleAugmentPhaseStart;
    }

    void OnDisable()
    {
        // 구독을 해지하여 메모리 누수를 방지합니다.
        GameEvents.OnAugmentPhaseStart -= HandleAugmentPhaseStart;
    }

    /// <summary>
    /// OnAugmentPhaseStart 이벤트가 발생했을 때 호출되는 핸들러입니다.
    /// </summary>
    private void HandleAugmentPhaseStart(PlayerManager player, List<AugmentData> choices)
    {
        // 이 UI는 로컬 플레이어의 것만 처리합니다.
        if (GameManagers.Instance.localPlayer != player) return;

        this.localPlayer = player;
        this.currentChoices = choices;

        // 증강 선택 UI 표시
        SetContentVisibility(true);
        SetAugmentChoices(choices);
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
                augmentSlots[i].Display(choices[i]);

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
        Debug.Log("<color=yellow>OnAugmentButtonClicked 호출</color>");
        // [핵심 변경점]
        // 이제 이벤트를 직접 발생시키는 대신, SelectAugmentCommand를 생성하여 실행합니다.
        // 이를 통해 플레이어의 행동과 AI의 행동이 동일한 로직을 타게 됩니다.
        if (localPlayer != null && currentChoices != null && index < currentChoices.Count)
        {
            var command = new SelectAugmentCommand(localPlayer.playerId, index);
            GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
            
            // 증강 선택 후 UI 숨김
            SetContentVisibility(false);
        }
        else
        {
            Debug.LogError($"증강 선택 처리 중 오류 발생: LocalPlayer: {localPlayer}, Choices: {currentChoices}, Index: {index}");
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
        SetContentVisibility(false);
    }
}
