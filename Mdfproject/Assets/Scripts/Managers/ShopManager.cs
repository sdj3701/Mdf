// Assets/Scripts/Managers/ShopManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;

public class ShopManager : MonoBehaviour
{
    public PlayerManager playerManager;
    private List<UnitData> allUnitDatabase = new List<UnitData>();
    [SerializeField] private int rerollCost = 2;

    // [변경됨] 이제 UnitData가 아닌 ShopItem 리스트를 관리합니다.
    private List<ShopItem> currentShopItems = new List<ShopItem>();
    private bool[] _isSlotSold = new bool[5];
    
    public bool IsDatabaseLoaded { get; private set; } = false;
    private UniTaskCompletionSource<bool> databaseLoadTask = new UniTaskCompletionSource<bool>();

    /// <summary>
    /// Unity 생명주기 진입점으로, LoadManager를 통해 UnitData 로딩을 시작합니다.
    /// </summary>
    void Start()
    {
        InitializeFromLoadManager();
    }

    /// <summary>
    /// LoadManager 준비 완료를 기다린 뒤 모든 UnitData를 로컬 데이터베이스에 캐시합니다.
    /// </summary>
    private async void InitializeFromLoadManager()
    {
        await Cysharp.Threading.Tasks.UniTask.WaitUntil(() => LoadManager.Instance != null);
        await LoadManager.Instance.WaitUntilReady();
        allUnitDatabase = LoadManager.Instance.GetAllUnitData().ToList();
        IsDatabaseLoaded = true;
        databaseLoadTask.TrySetResult(true);
    }

    /// <summary>
    /// 서버에서 전송받은 상점 아이템 데이터로 로컬 상점을 업데이트합니다.
    /// 데이터베이스 로딩이 완료될 때까지 대기합니다.
    /// </summary>
    public async UniTask SetShopItemsFromServerAsync(string[] unitDataNames, int[] starLevels)
    {
        // 데이터베이스 로딩 완료 대기
        await WaitUntilDatabaseLoaded();
        
        currentShopItems.Clear();
        for (int i = 0; i < _isSlotSold.Length; i++)
        {
            _isSlotSold[i] = false;
        }

        for (int i = 0; i < unitDataNames.Length && i < starLevels.Length; i++)
        {
            var unitData = LoadManager.Instance?.GetUnitData(unitDataNames[i]);
            if (unitData != null)
            {
                currentShopItems.Add(new ShopItem(unitData, starLevels[i]));
            }
            else
            {
                Debug.LogWarning($"[ShopManager] 유닛 데이터를 찾을 수 없음: {unitDataNames[i]}");
            }
        }
        GameEvents.TriggerShopRefreshed(playerManager);
    }

    /// <summary>
    /// 유닛 데이터베이스 로딩이 완료되면 끝나는 작업을 반환합니다.
    /// </summary>
    public UniTask WaitUntilDatabaseLoaded() => databaseLoadTask.Task.AsUniTask();

    // [변경됨] 반환 타입이 List<ShopItem>으로 변경되었습니다.
    /// <summary>
    /// 현재 상점 아이템을 반환합니다.
    /// </summary>
    public List<ShopItem> GetCurrentShopItems()
    {
        // 자동 리롤 제거 - 서버에서 RPC로 동기화해야 함
        return currentShopItems;
    }
    /// <summary>
    /// 상점 리롤에 필요한 골드 비용을 반환합니다.
    /// </summary>
    public int GetRerollCost() => rerollCost;

    // [핵심 로직] Reroll 메서드가 성급 확률을 계산하도록 완전히 변경됩니다.
    /// <summary>
    /// 새로운 상점 아이템 세트를 생성합니다(필요 시 무료). DB 준비 상태와 플레이어 골드를 검증합니다.
    /// </summary>
    /// <param name="isFree">true이면 골드를 차감하지 않습니다.</param>
    public void Reroll(bool isFree = false)
    {
        if (!IsDatabaseLoaded)
        {
            Debug.LogWarning("유닛 데이터베이스가 아직 로드되지 않아 리롤할 수 없습니다.");
            return;
        }

        if (!isFree && !playerManager.SpendGold(rerollCost))
        {
            Debug.LogWarning($"Player {playerManager.playerId}: 골드가 부족하여 리롤할 수 없습니다.");
            return;
        }

        currentShopItems.Clear();
        for (int i = 0; i < _isSlotSold.Length; i++)
        {
            _isSlotSold[i] = false;
        }

        for (int i = 0; i < 5; i++) // 5개의 슬롯을 채웁니다.
        {
            if (allUnitDatabase.Count > 0)
            {
                // 1. 먼저 어떤 유닛이 나올지 랜덤으로 선택합니다.
                UnitData randomUnitData = allUnitDatabase[Random.Range(0, allUnitDatabase.Count)];

                // 2. 해당 유닛의 성급을 확률에 따라 결정합니다.
                int starLevel;
                float roll = Random.value; // 0.0 ~ 1.0 사이의 랜덤 값

                if (roll < 0.7f) // 70% 확률
                {
                    starLevel = 1;
                }
                else // 30% 확률
                {
                    starLevel = 2;
                }

                // 3. 결정된 유닛과 성급으로 ShopItem을 만들어 리스트에 추가합니다.
                currentShopItems.Add(new ShopItem(randomUnitData, starLevel));
            }
        }
        GameEvents.TriggerShopRefreshed(playerManager);
    }

    /// <summary>
    /// 유닛 데이터 로딩을 보장하고, 상점 아이템이 있는지 확인합니다.
    /// 서버에서는 리롤 후 동기화, 클라이언트는 서버 데이터 도착을 대기합니다.
    /// </summary>
    public async UniTask EnsureShopRerolledAsync()
    {
        await WaitUntilDatabaseLoaded();
        
        // 상점 아이템이 이미 있으면 대기 없이 반환
        if (currentShopItems.Count > 0) return;
        
        // 서버 데이터 도착을 최대 5초간 대기
        float waited = 0f;
        while (currentShopItems.Count == 0 && waited < 5f)
        {
            await UniTask.Delay(100);
            waited += 0.1f;
        }
        
        if (currentShopItems.Count == 0)
        {
            UnityEngine.Debug.LogWarning("[ShopManager] 상점 아이템 대기 타임아웃");
        }
    }

    /// <summary>
    /// 지정한 상점 슬롯을 구매 처리하여 재선택을 방지합니다.
    /// </summary>
    /// <param name="slotIndex">구매 처리할 슬롯의 인덱스입니다.</param>
    public void MarkSlotAsPurchased(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < _isSlotSold.Length)
        {
            _isSlotSold[slotIndex] = true;
        }
    }

    /// <summary>
    /// 구매되지 않은 슬롯만 슬롯 인덱스→아이템 형태의 사전으로 반환합니다.
    /// </summary>
    public Dictionary<int, ShopItem> GetAvailableShopItems()
    {
        var availableItems = new Dictionary<int, ShopItem>();
        for (int i = 0; i < currentShopItems.Count; i++)
        {
            if (!_isSlotSold[i])
            {
                availableItems.Add(i, currentShopItems[i]);
            }
        }
        return availableItems;
    }
}
