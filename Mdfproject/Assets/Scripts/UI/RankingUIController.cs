// Assets/Scripts/UI/RankingUIController.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// LeftSide와 RightSide 구조를 사용하여 플레이어 랭킹 UI를 관리하고 정렬하는 컨트롤러입니다.
/// </summary>
public class RankingUIController : MonoBehaviour
{
    [Header("UI Parent Containers")]
    [Tooltip("왼쪽에 위치할 슬롯들의 부모 Transform 입니다.")]
    [SerializeField] private Transform leftSideContainer;

    [Tooltip("오른쪽에 위치할 슬롯들의 부모 Transform 입니다.")]
    [SerializeField] private Transform rightSideContainer;

    // 모든 PlayerRankSlot 컴포넌트를 담을 리스트 (자동으로 찾아냄)
    private List<PlayerRankSlot> allSlots = new List<PlayerRankSlot>();
    // 모든 PlayerManager 컴포넌트를 담을 리스트 (자동으로 찾아냄)
    private List<PlayerManager> allPlayers = new List<PlayerManager>();

    void Start()
    {
        InitializePlayersAndSlots();
    }

    void Update()
    {
        SortAndDisplayPlayers();
    }

    /// <summary>
    /// 씬에서 모든 PlayerManager와 PlayerRankSlot을 찾아 초기화합니다.
    /// </summary>
    private void InitializePlayersAndSlots()
    {
        if (GameManagers.Instance == null)
        {
            Debug.LogError("GameManagers를 찾을 수 없습니다! 랭킹 UI가 작동하지 않습니다.");
            this.enabled = false;
            return;
        }

        allPlayers = FindObjectsOfType<PlayerManager>().ToList();
        allSlots = GetComponentsInChildren<PlayerRankSlot>(true).ToList();

        foreach (var slot in allSlots)
        {
            slot.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 플레이어를 정렬하고, 모든 슬롯을 올바른 위치에 재배치하며 UI를 업데이트합니다.
    /// </summary>
    private void SortAndDisplayPlayers()
    {
        if (allPlayers.Count == 0 || allSlots.Count == 0) return;

        // ✅ [수정] 2차 정렬 규칙(ThenBy)을 추가하여 체력이 같을 때 playerId 순으로 정렬합니다.
        var sortedPlayers = allPlayers
            .OrderByDescending(p => p.GetHealth()) // 1. 체력 내림차순
            .ThenBy(p => p.playerId)               // 2. 플레이어 ID 오름차순
            .ToList();

        int leftSideCount = Mathf.CeilToInt(sortedPlayers.Count / 2.0f);

        for (int i = 0; i < allSlots.Count; i++)
        {
            PlayerRankSlot currentSlot = allSlots[i];

            if (i < sortedPlayers.Count)
            {
                PlayerManager playerForThisSlot = sortedPlayers[i];
                currentSlot.Initialize(playerForThisSlot);
                
                Transform targetParent = (i < leftSideCount) ? leftSideContainer : rightSideContainer;
                currentSlot.transform.SetParent(targetParent, false);

                currentSlot.UpdateUI();
            }
            else
            {
                currentSlot.gameObject.SetActive(false);
            }
        }
    }
}