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
    /// 유닛 데이터베이스 로딩이 완료되면 끝나는 작업을 반환합니다.
    /// </summary>
    public UniTask WaitUntilDatabaseLoaded() => databaseLoadTask.Task.AsUniTask();

    // [변경됨] 반환 타입이 List<ShopItem>으로 변경되었습니다.
    /// <summary>
    /// 현재 상점 아이템을 반환합니다. 비어 있고 DB가 준비되었다면 무료 리롤로 채웁니다.
    /// </summary>
    public List<ShopItem> GetCurrentShopItems()
    {
        if (currentShopItems.Count == 0 && IsDatabaseLoaded)
        {
            Reroll(isFree: true);
        }
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
    /// 유닛 데이터 로딩을 보장하고, 상점 아이템 리롤을 실행합니다.
    /// 이 함수를 호출하면 currentShopItems가 채워집니다 (Count > 0).
    /// </summary>
    public async UniTask EnsureShopRerolledAsync()
    {
        // 1. 유닛 데이터(Addressables) 로드가 완료될 때까지 기다림
        // Reroll 함수 내부에서 IsDatabaseLoaded를 체크하지만, 비동기로 외부에서 기다려주어 확실하게 보장합니다.
        await WaitUntilDatabaseLoaded(); 
        
        // 2. 데이터 로딩이 완료되면 Reroll을 호출하여 currentShopItems를 채움
        //    * Reroll() 함수 내부에 currentShopItems.Add(...) 로직이 이미 구현되어 있습니다.
        
        // 상점 아이템이 0개일 때만 리롤을 수행하여 채웁니다. (새 라운드 시작 등)
        if (currentShopItems.Count == 0)
        {
            Reroll(isFree: true); // 처음 상점을 채우는 것이므로 무료 리롤로 처리합니다.
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
