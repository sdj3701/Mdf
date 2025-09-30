// Assets/Scripts/UI/RankingUIController.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class RankingUIController : MonoBehaviour
{
    [Header("UI Parent Containers")]
    [SerializeField] private Transform leftSideContainer;
    [SerializeField] private Transform rightSideContainer;

    private List<PlayerRankSlot> allSlots = new List<PlayerRankSlot>();
    private List<PlayerManager> allPlayers = new List<PlayerManager>();

    // [추가] 초기화가 완료되었는지 확인하기 위한 플래그
    private bool isInitialized = false;

    // [수정] Start()에서는 아무것도 하지 않습니다.
    void Start()
    {
        // 초기화 로직은 Update에서 처리합니다.
    }

    void Update()
    {
        // 초기화가 되지 않았다면 매 프레임 시도합니다.
        if (!isInitialized)
        {
            InitializePlayersAndSlots();
        }
        
        // 초기화가 완료된 후에만 정렬 및 표시 로직을 실행합니다.
        if (isInitialized)
        {
            SortAndDisplayPlayers();
        }
    }

    /// <summary>
    /// 씬에서 모든 PlayerManager와 PlayerRankSlot을 찾아 초기화합니다.
    /// </summary>
    private void InitializePlayersAndSlots()
    {
        // GameManagers가 준비될 때까지 기다립니다.
        if (GameManagers.Instance == null)
        {
            return; // 아직 준비되지 않았으므로 다음 프레임에 다시 시도합니다.
        }

        // [수정] FindObjectsOfType 대신, GameManagers가 관리하는 플레이어 리스트를 직접 사용합니다.
        // 이것이 네트워크 환경에서 훨씬 더 안정적입니다.
        allPlayers = GameManagers.Instance.players.Where(p => p != null).ToList();

        // GameManagers는 준비되었지만 플레이어 객체들이 아직 리스트에 추가되지 않았을 수 있습니다.
        if (allPlayers.Count == 0)
        {
            return; // 아직 플레이어가 없으므로 다음 프레임에 다시 시도합니다.
        }

        // 자식 오브젝트에서 모든 슬롯을 찾아 리스트에 추가합니다.
        allSlots = GetComponentsInChildren<PlayerRankSlot>(true).ToList();

        // 모든 슬롯을 일단 비활성화합니다.
        foreach (var slot in allSlots)
        {
            slot.gameObject.SetActive(false);
        }

        // 초기화가 성공적으로 완료되었음을 표시합니다.
        isInitialized = true;
        Debug.Log("RankingUIController 초기화 완료.");
    }

    /// <summary>
    /// 플레이어를 정렬하고, 모든 슬롯을 올바른 위치에 재배치하며 UI를 업데이트합니다.
    /// </summary>
    private void SortAndDisplayPlayers()
    {
        // [수정] isInitialized 플래그가 true일 때만 호출되므로, 중복 확인을 제거해도 안전합니다.
        // if (allPlayers.Count == 0 || allSlots.Count == 0) return;

        var sortedPlayers = allPlayers
            .OrderByDescending(p => p.GetHealth())
            .ThenBy(p => p.playerId)
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

                // UpdateUI는 Initialize 내부에서 호출될 필요가 없으므로 여기로 이동하거나,
                // PlayerRankSlot의 Update에서 처리하도록 둘 수 있습니다. 여기서는 명시적으로 호출합니다.
                currentSlot.UpdateUI();
            }
            else
            {
                currentSlot.gameObject.SetActive(false);
            }
        }
    }
}