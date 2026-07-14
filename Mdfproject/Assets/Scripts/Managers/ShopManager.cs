// Assets/Scripts/Managers/ShopManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using MDF.Runtime.Assets;

public class ShopManager : MonoBehaviour
{
    private readonly AddressableAssetOwner _addressableAssets = new AddressableAssetOwner();
    public PlayerManager playerManager;
    private List<UnitData> allUnitDatabase = new List<UnitData>();
    [SerializeField] private int rerollCost = 2;

    // [변경됨] 이제 UnitData가 아닌 ShopItem 리스트를 관리합니다.
    private List<ShopItem> currentShopItems = new List<ShopItem>();
    private bool[] _isSlotSold = new bool[5];
    private int _lastAppliedSnapshotRevision = -1;
    
    public bool IsDatabaseLoaded { get; private set; } = false;
    private UniTaskCompletionSource<bool> databaseLoadTask = new UniTaskCompletionSource<bool>();
    private static int _shopTraceSeq;

    private string BuildShopTraceOwner()
    {
        if (playerManager == null)
        {
            return "player=null";
        }

        var runner = playerManager.Runner;
        bool hasObject = playerManager.Object != null;
        bool hasAuthority = hasObject && playerManager.Object.HasStateAuthority;
        string playerIdLabel = TryGetSafePlayerId(out int safePlayerId) ? safePlayerId.ToString() : "unspawned";
        string runnerSummary = runner == null
            ? "runner=null"
            : $"runner={runner.name},running={runner.IsRunning},server={runner.IsServer}";

        return $"player={playerIdLabel},name={playerManager.name},hasObject={hasObject},stateAuth={hasAuthority},{runnerSummary}";
    }

    private bool IsRunningClientPeerWithoutAuthority()
    {
        var runner = playerManager != null ? playerManager.Runner : null;
        return runner != null && runner.IsRunning && !runner.IsServer;
    }

    private bool TryGetSafePlayerId(out int playerId)
    {
        playerId = -1;
        if (playerManager == null || playerManager.Object == null || !playerManager.Object.IsValid)
        {
            return false;
        }

        try
        {
            playerId = playerManager.playerId;
            return true;
        }
        catch (System.InvalidOperationException)
        {
            return false;
        }
    }

    [System.Diagnostics.Conditional("MDF_SHOP_TRACE")]
    private void LogShopTrace(string step, string extra = null)
    {
        GameManagers gm = GameManagers.Instance;
        string gmState = gm == null ? "gmState=NoGameManagers" : $"gmState={gm.GetGameState()}";
        string suffix = string.IsNullOrEmpty(extra) ? string.Empty : $" | {extra}";
        // Debug.Log(
            // $"[SHOP-TRACE #{++_shopTraceSeq}] {step} | {BuildShopTraceOwner()} | " +
            // $"dbLoaded={IsDatabaseLoaded} dbCount={allUnitDatabase.Count} shopCount={currentShopItems.Count} {gmState}{suffix}");
    }

    /// <summary>
    /// Unity 생명주기 진입점으로, LoadManager를 통해 UnitData 로딩을 시작합니다.
    /// </summary>
    void Start()
    {
        LogShopTrace("Start:InitializeBegin");
        InitializeFromLoadManager();
    }

    /// <summary>
    /// LoadManager 준비 완료를 기다린 뒤 모든 UnitData를 로컬 데이터베이스에 캐시합니다.
    /// </summary>
    private async void InitializeFromLoadManager()
    {
        LogShopTrace("InitializeFromLoadManager:ENTER", $"hasLoadManager={LoadManager.Instance != null}");
        try
        {
            await Cysharp.Threading.Tasks.UniTask.WaitUntil(() => LoadManager.Instance != null);
            LogShopTrace("InitializeFromLoadManager:LoadManagerResolved");

            await LoadManager.Instance.WaitUntilReady();
            allUnitDatabase = LoadManager.Instance.GetAllUnitData().ToList();
            IsDatabaseLoaded = true;
            databaseLoadTask.TrySetResult(true);

            LogShopTrace("InitializeFromLoadManager:COMPLETE", $"loadedUnitCount={allUnitDatabase.Count}");
        }
        catch (System.Exception ex)
        {
            databaseLoadTask.TrySetException(ex);
            // Debug.LogError($"[ShopManager] InitializeFromLoadManager 예외: {ex}");
            LogShopTrace("InitializeFromLoadManager:EXCEPTION", $"error={ex.Message}");
        }
    }

    /// <summary>
    /// 서버에서 전송받은 상점 아이템 데이터로 로컬 상점을 업데이트합니다.
    /// 데이터베이스 로딩이 완료될 때까지 대기합니다.
    /// </summary>
    public async UniTask SetShopItemsFromServerAsync(string[] unitDataNames, int[] starLevels)
    {
        int nameCount = unitDataNames?.Length ?? 0;
        int starCount = starLevels?.Length ?? 0;
        LogShopTrace("SetShopItemsFromServerAsync:ENTER", $"incomingNames={nameCount},incomingStars={starCount}");

        // 데이터베이스 로딩 완료 대기
        await WaitUntilDatabaseLoaded();
        LogShopTrace("SetShopItemsFromServerAsync:DB_READY");
        
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
                // Debug.LogWarning($"[ShopManager] 유닛 데이터를 찾을 수 없음: {unitDataNames[i]} (slot={i})");
            }
        }

        string snapshot = string.Join(", ", currentShopItems.Select(item =>
            item.UnitData != null ? $"{item.UnitData.name}*{item.StarLevel}" : "null"));
        LogShopTrace("SetShopItemsFromServerAsync:APPLIED", $"resolved={currentShopItems.Count},items=[{snapshot}]");

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

    public bool IsSlotSold(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < _isSlotSold.Length && _isSlotSold[slotIndex];
    }

    public bool[] GetSoldSlotSnapshot()
    {
        var copy = new bool[_isSlotSold.Length];
        System.Array.Copy(_isSlotSold, copy, _isSlotSold.Length);
        return copy;
    }

    public int GetShopSlotCount()
    {
        return currentShopItems != null ? currentShopItems.Count : 0;
    }

    public int GetSoldSlotCount()
    {
        int count = 0;
        int limit = Mathf.Min(GetShopSlotCount(), _isSlotSold != null ? _isSlotSold.Length : 0);
        for (int i = 0; i < limit; i++)
        {
            if (_isSlotSold[i])
            {
                count++;
            }
        }

        return count;
    }

    public int GetUnsoldSlotCount()
    {
        return Mathf.Max(0, GetShopSlotCount() - GetSoldSlotCount());
    }

    private void PublishNetworkShopSnapshotIfAuthority(string context)
    {
        if (playerManager == null || playerManager.Object == null || !playerManager.Object.IsValid || !playerManager.Object.HasStateAuthority)
        {
            return;
        }

        playerManager.PublishShopSnapshot(currentShopItems, _isSlotSold, context);

        if (playerManager.Runner != null && playerManager.Runner.IsRunning && playerManager.Runner.IsServer)
        {
            string playerIdLabel = TryGetSafePlayerId(out int safePlayerId) ? safePlayerId.ToString() : "unspawned";
            HostMigrationHandler.Instance?.TryPushHostMigrationSnapshot(
                playerManager.Runner,
                $"ShopSnapshot:{context}:P{playerIdLabel}");
        }
    }

    public async UniTask<bool> ApplySnapshotFromNetworkAsync(string context, bool triggerRefreshedEvent = true)
    {
        LogShopTrace("ApplySnapshotFromNetworkAsync:ENTER", $"context={context}");
        if (playerManager == null)
        {
            LogShopTrace("ApplySnapshotFromNetworkAsync:ABORT_NO_PLAYER", $"context={context}");
            return false;
        }

        await WaitUntilDatabaseLoaded();

        if (!playerManager.TryGetShopSnapshot(out string[] unitKeys, out int[] starLevels, out bool[] soldFlags, out int revision, out int round))
        {
            LogShopTrace("ApplySnapshotFromNetworkAsync:NO_SNAPSHOT", $"context={context}");
            return false;
        }

        // 이미 같은 리비전을 적용했고 상점 데이터가 살아있다면 중복 적용을 생략한다.
        if (_lastAppliedSnapshotRevision == revision && currentShopItems.Count > 0)
        {
            LogShopTrace("ApplySnapshotFromNetworkAsync:SKIP_DUPLICATE_REV", $"context={context},revision={revision}");
            return true;
        }

        currentShopItems.Clear();
        for (int i = 0; i < _isSlotSold.Length; i++)
        {
            _isSlotSold[i] = false;
        }

        int count = Mathf.Min(unitKeys.Length, starLevels.Length);
        for (int i = 0; i < count; i++)
        {
            string unitKey = unitKeys[i];
            if (string.IsNullOrEmpty(unitKey))
            {
                continue;
            }

            UnitData unitData = LoadManager.Instance?.GetUnitData(unitKey);
            if (unitData == null)
            {
                unitData = await AssetLoader.LoadAssetAsync<UnitData>(unitKey, _addressableAssets);
            }

            if (unitData == null)
            {
                continue;
            }

            int star = Mathf.Max(1, starLevels[i]);
            currentShopItems.Add(new ShopItem(unitData, star));
            if (i < soldFlags.Length)
            {
                _isSlotSold[i] = soldFlags[i];
            }
        }

        _lastAppliedSnapshotRevision = revision;
        LogShopTrace("ApplySnapshotFromNetworkAsync:APPLIED", $"context={context},revision={revision},round={round},count={currentShopItems.Count}");

        if (triggerRefreshedEvent)
        {
            GameEvents.TriggerShopRefreshed(playerManager);
        }

        return currentShopItems.Count > 0;
    }

    /// <summary>
    /// 상점 리롤에 필요한 골드 비용을 반환합니다.
    /// </summary>
    public int GetRerollCost() => rerollCost;

    private void OnDestroy()
    {
        _addressableAssets.Dispose();
    }

    // [핵심 로직] Reroll 메서드가 성급 확률을 계산하도록 완전히 변경됩니다.
    /// <summary>
    /// 새로운 상점 아이템 세트를 생성합니다(필요 시 무료). DB 준비 상태와 플레이어 골드를 검증합니다.
    /// </summary>
    /// <param name="isFree">true이면 골드를 차감하지 않습니다.</param>
    public void Reroll(bool isFree = false)
    {
        int goldBefore = playerManager != null ? playerManager.GetGold() : -1;
        LogShopTrace("Reroll:ENTER", $"isFree={isFree},goldBefore={goldBefore}");

        if (IsRunningClientPeerWithoutAuthority())
        {
            LogShopTrace("Reroll:ABORT_NON_AUTHORITY_PEER", $"isFree={isFree}");
            return;
        }

        if (!IsDatabaseLoaded)
        {
            // Debug.LogWarning("유닛 데이터베이스가 아직 로드되지 않아 리롤할 수 없습니다.");
            LogShopTrace("Reroll:ABORT_DB_NOT_READY");
            return;
        }

        if (!isFree && !playerManager.SpendGold(rerollCost))
        {
            string playerIdLabel = TryGetSafePlayerId(out int safePlayerId) ? safePlayerId.ToString() : "unspawned";
            // Debug.LogWarning($"Player {playerIdLabel}: 골드가 부족하여 리롤할 수 없습니다.");
            LogShopTrace("Reroll:ABORT_NOT_ENOUGH_GOLD", $"gold={playerManager.GetGold()},cost={rerollCost}");
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

        int goldAfter = playerManager != null ? playerManager.GetGold() : -1;
        string snapshot = string.Join(", ", currentShopItems.Select(item =>
            item.UnitData != null ? $"{item.UnitData.name}*{item.StarLevel}" : "null"));
        LogShopTrace("Reroll:SUCCESS", $"goldAfter={goldAfter},items=[{snapshot}]");
        PublishNetworkShopSnapshotIfAuthority("ShopManager.Reroll");

        GameEvents.TriggerShopRefreshed(playerManager);
    }

    /// <summary>
    /// 유닛 데이터 로딩을 보장하고, 상점 아이템이 있는지 확인합니다.
    /// 서버에서는 리롤 후 동기화, 클라이언트는 서버 데이터 도착을 대기합니다.
    /// </summary>
    public async UniTask EnsureShopRerolledAsync()
    {
        LogShopTrace("EnsureShopRerolledAsync:ENTER");
        await WaitUntilDatabaseLoaded();
        LogShopTrace("EnsureShopRerolledAsync:DB_READY");
        
        // 상점 아이템이 이미 있으면 대기 없이 반환
        if (currentShopItems.Count > 0)
        {
            LogShopTrace("EnsureShopRerolledAsync:ALREADY_READY");
            return;
        }
        
        // 서버 데이터 도착을 최대 5초간 대기
        float waited = 0f;
        int lastLoggedSecond = -1;
        while (currentShopItems.Count == 0 && waited < 5f)
        {
            await UniTask.Delay(100);
            waited += 0.1f;

            int waitedSecond = Mathf.FloorToInt(waited);
            if (waitedSecond > lastLoggedSecond)
            {
                lastLoggedSecond = waitedSecond;
                LogShopTrace("EnsureShopRerolledAsync:WAITING", $"waited={waited:F1}s");
            }
        }
        
        if (currentShopItems.Count == 0)
        {
            // UnityEngine.Debug.LogWarning("[ShopManager] 상점 아이템 대기 타임아웃");
            LogShopTrace("EnsureShopRerolledAsync:TIMEOUT", $"waited={waited:F1}s");
        }
        else
        {
            LogShopTrace("EnsureShopRerolledAsync:SUCCESS", $"waited={waited:F1}s");
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
            PublishNetworkShopSnapshotIfAuthority("ShopManager.MarkSlotAsPurchased");
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
