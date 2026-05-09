  // Assets/Scripts/UI/RankingUIController.cs
  using UnityEngine;
  using UnityEngine.UI;
  using System.Collections.Generic;
  using System.Linq;
  using Cysharp.Threading.Tasks;

 public class RankingUIController : MonoBehaviour
 {
     [Header("UI Parent Containers")]
     [SerializeField] private Transform leftSideContainer;
     [SerializeField] private Transform rightSideContainer;

     private List<PlayerRankSlot> allSlots = new List<PlayerRankSlot>();
     private List<PlayerManager> allPlayers = new List<PlayerManager>();

     // [추가] 초기화가 완료되었는지 확인하기 위한 플래그
     private bool isInitialized = false;
     private bool isInitializing = false;
     private float nextInitializeRetryTime = 0f;

     // 플레이어 상태 변경을 감지하기 위한 데이터 보관
     private List<int> lastPlayerHealths = new List<int>();

     private static bool IsPlayerReadable(PlayerManager player)
     {
         return player != null
             && player.Object != null
             && player.Object.IsValid;
     }

     private static bool TryGetHealthSafe(PlayerManager player, out int health)
     {
         health = 0;
         if (!IsPlayerReadable(player))
         {
             return false;
         }

         try
         {
             health = player.GetHealth();
             return true;
         }
         catch (System.InvalidOperationException)
         {
             return false;
         }
     }

     private static bool TryGetPlayerIdSafe(PlayerManager player, out int playerId)
     {
         playerId = int.MaxValue;
         if (!IsPlayerReadable(player))
         {
             return false;
         }

         try
         {
             playerId = player.playerId;
             return true;
         }
         catch (System.InvalidOperationException)
         {
             return false;
         }
     }

     void OnEnable()
     {
         GameManagers.OnPlayersDataReady += OnPlayersDataReady;
     }
   
     void OnDisable()
     {
         GameManagers.OnPlayersDataReady -= OnPlayersDataReady;
     }
   
     // 플레이어 데이터가 모두 준비되었을 때 호출되는 함수
     private void OnPlayersDataReady()
     {
         // 플레이어 데이터가 준비되었을 때 UI 업데이트
         if (!isInitialized)
         {
             InitializePlayersAndSlots();
         }
         else
         {
             // 이미 초기화되었다면 플레이어 데이터 변경만 반영
             SortAndDisplayPlayers();
         }
     }

     void Update()
     {
         // Host Migration 중이거나 Spawned 되지 않은 경우 네트워크 프로퍼티 접근 안함
         if (GameManagers.Instance == null || !GameManagers.Instance.IsReadyForNetworkAccess)
         {
             return;
         }
         
         // GameOver 상태이면 네트워크 프로퍼티 접근 안함 (씬 전환 대기 중)
         if (GameManagers.Instance.GetGameState() == GameManagers.GameState.GameOver)
         {
             return;
         }

         if (!isInitialized)
         {
             if (Time.unscaledTime >= nextInitializeRetryTime)
             {
                 nextInitializeRetryTime = Time.unscaledTime + 0.5f;
                 InitializePlayersAndSlots();
             }
             return;
         }
          
         if (isInitialized)
         {
             // 플레이어 수 또는 체력 상태가 변경되었을 때만 정렬
             if (HasPlayerStateChanged())
             {
                 SortAndDisplayPlayers();
             }
             
             // 전투 상태 등 실시간 변경사항 반영을 위해 매 프레임 UI 업데이트
             foreach (var slot in allSlots)
             {
                 if (slot != null && slot.gameObject.activeInHierarchy)
                 {
                     slot.UpdateUI();
                 }
             }
         }
     }

     // 플레이어 상태(수량 및 체력)가 변경되었는지 확인하는 함수
     private bool HasPlayerStateChanged()
     {
         if (allPlayers.Count != lastPlayerHealths.Count)
         {
             // 플레이어 수가 변경됨
             UpdateLastPlayerHealths();
             return true;
         }

         for (int i = 0; i < allPlayers.Count; i++)
         {
             if (TryGetHealthSafe(allPlayers[i], out int health) && lastPlayerHealths[i] != health)
             {
                 // 플레이어 체력이 변경됨
                 UpdateLastPlayerHealths();
                 return true;
             }
         }

         return false;
     }

     // 마지막 플레이어 체력 정보를 업데이트하는 함수
     private void UpdateLastPlayerHealths()
     {
         lastPlayerHealths.Clear();
         for (int i = 0; i < allPlayers.Count; i++)
         {
             lastPlayerHealths.Add(TryGetHealthSafe(allPlayers[i], out int health) ? health : 0);
         }
     }

     private async void InitializePlayersAndSlots()
     {
         if (isInitialized) return;
         if (isInitializing) return;
         isInitializing = true;
         if (GameManagers.Instance == null)
         {
             Debug.LogError("RankingUIController: GameManagers.Instance가 null입니다.");
             isInitializing = false;
             nextInitializeRetryTime = Time.unscaledTime + 0.5f;
             return;
         }
 
         Debug.Log("RankingUIController: InitializePlayersAndSlots() 호출됨.");
 
         // GameManagers에서 플레이어 리스트 가져오기
         var allGamePlayers = GameManagers.Instance.AllPlayers.ToList();
         
         // 유효한 플레이어만 필터링 (null이 아니고, playerId가 유효한 플레이어만)
         var validPlayers = allGamePlayers
             .Where(IsPlayerReadable)
             .ToList();
         
         Debug.Log($"GameManagers로부터 받은 플레이어 수: {allGamePlayers.Count}, 유효한 플레이어 수: {validPlayers.Count}");
 
         // GameManagers에서 singlePlayerModeCount 값을 가져와서 실제 플레이어 수만 사용
         int actualPlayerCount = GameManagers.Instance.singlePlayerModeCount;
         
         // 싱글플레이어 모드인 경우 singlePlayerModeCount가 0이면 기본값으로 사용
         if (GameManagers.Instance.Runner != null &&
             GameManagers.Instance.Runner.GameMode == Fusion.GameMode.Single)
         {
             // 싱글플레이어 모드에서 singlePlayerModeCount가 설정되지 않았으면 유효한 플레이어 수를 사용
             if (actualPlayerCount <= 0)
             {
                 actualPlayerCount = validPlayers.Count;
                 Debug.LogWarning($"[RankingUIController] 싱글플레이어 모드에서 singlePlayerModeCount가 0입니다. 유효한 플레이어 수({validPlayers.Count})를 사용합니다.");
             }
         }
         else
         {
             // 멀티플레이어 모드에서는 모든 유효한 플레이어를 사용
             actualPlayerCount = validPlayers.Count;
         }
         
         Debug.Log($"UI에 표시할 실제 플레이어 수: {actualPlayerCount}");
 
         // 실제 플레이어 수에 따라 리스트 구성
         allPlayers = validPlayers
             .Take(actualPlayerCount)
             .ToList();
 
 
         // 여전히 플레이어가 없으면 로그 출력하고 종료
         if (allPlayers.Count == 0)
         {
             Debug.LogWarning($"RankingUIController: UI를 초기화할 플레이어가 없습니다. allPlayers.Count: {allPlayers.Count}, validPlayers.Count: {validPlayers.Count}, allGamePlayers.Count: {allGamePlayers.Count}");
             isInitializing = false;
             nextInitializeRetryTime = Time.unscaledTime + 0.5f;
             return;
         }

         // 씬에 있는 모든 기존 슬롯들을 찾아서 제거
         PlayerRankSlot[] existingSlots = GetComponentsInChildren<PlayerRankSlot>(true);
         foreach (var slot in existingSlots)
         {
             if (slot != null)
                 DestroyImmediate(slot.gameObject);
         }
         allSlots.Clear();

         // 플레이어 수만큼 슬롯을 생성
         for (int i = 0; i < allPlayers.Count; i++)
         {
             GameObject slotGO = null;
             
             // AddressablesManager를 사용하여 프리팹 로드
             if (AddressablesManager.Instance != null)
             {
                 slotGO = await AddressablesManager.Instance.LoadObject("UI_Slot_PlayerRank", transform);
                 if (slotGO != null)
                 {
                     slotGO.name = $"PlayerRankSlot_{i}";
                 }
             }
             
             // AddressablesManager가 없거나 로드 실패 시 안전장치
             if (slotGO == null)
             {
                 // 안전장치: 프리팹이 없을 경우 RectTransform을 가진 GameObject를 생성해 UI에서 보이도록 합니다.
                 slotGO = new GameObject($"PlayerRankSlot_{i}", typeof(RectTransform));
                 slotGO.transform.SetParent(transform, false);
             }

             // Prefab에 이미 PlayerRankSlot이 붙어있을 수 있으니 확인 후 없으면 추가합니다.
             PlayerRankSlot slot = slotGO.GetComponent<PlayerRankSlot>();
             if (slot == null)
                 slot = slotGO.AddComponent<PlayerRankSlot>();

             // 안전: 인스턴스는 데이터 바인딩 전까지 비활성화 상태로 둡니다.
             slotGO.SetActive(false);

             allSlots.Add(slot);
         }

         isInitialized = true;
         Debug.Log($"RankingUIController 초기화 완료. 플레이어 수: {allPlayers.Count}");

         // 즉시 정렬 및 표시를 한 번 실행하여 Instantiate 직후 슬롯에 데이터가 바인딩되도록 보장합니다.
         SortAndDisplayPlayers();
         isInitializing = false;
     }

     /// <summary>
     /// 플레이어를 정렬하고, 모든 슬롯을 올바른 위치에 재배치하며 UI를 업데이트합니다.
     /// </summary>
     private void SortAndDisplayPlayers()
     {
         var sortedPlayers = allPlayers
             .OrderByDescending(p => TryGetHealthSafe(p, out int health) ? health : 0)
             .ThenBy(p => TryGetPlayerIdSafe(p, out int playerId) ? playerId : int.MaxValue)
             .ToList();

         for (int i = 0; i < allSlots.Count; i++)
         {
             PlayerRankSlot currentSlot = allSlots[i];

             if (i < sortedPlayers.Count)
             {
                 PlayerManager playerForThisSlot = sortedPlayers[i];

                 // Host(왼쪽) / Client(오른쪽) 기준으로 부모 컨테이너를 선택합니다.
                 Transform targetParent = IsHostPlayer(playerForThisSlot) ? leftSideContainer : rightSideContainer;
                 if (targetParent == null)
                 {
                     targetParent = transform;
                 }

                 // 부모가 비활성인 경우 강제로 활성화하여 자식 UI가 보이도록 합니다.
                 if (!targetParent.gameObject.activeInHierarchy)
                     targetParent.gameObject.SetActive(true);

                 // 먼저 부모에 배치하여 계층/레이아웃이 올바르게 설정되도록 합니다.
                 currentSlot.transform.SetParent(targetParent, false);

                 // 슬롯을 활성화(하이라키 상에서 활성화)하여 텍스트/레이아웃가 제대로 초기화되도록 보장합니다.
                 if (!currentSlot.gameObject.activeInHierarchy)
                     currentSlot.gameObject.SetActive(true);

                 // 그 다음 데이터 바인딩 및 UI 업데이트 순서
                 currentSlot.Initialize(playerForThisSlot);

                 // 레이아웃 강제 업데이트 (즉시 드로우 보장)
                 Canvas.ForceUpdateCanvases();
                 RectTransform rt = targetParent.GetComponent<RectTransform>();
                 if (rt != null)
                     LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

                 currentSlot.UpdateUI();
             }
             else
             {
                 currentSlot.gameObject.SetActive(false);
             }
         }
     }

     private bool IsHostPlayer(PlayerManager player)
     {
         return TryGetPlayerIdSafe(player, out int playerId) && playerId == 0;
     }
 }
