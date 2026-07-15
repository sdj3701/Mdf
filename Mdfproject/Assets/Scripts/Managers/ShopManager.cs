// Assets/Scripts/Managers/ShopManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

    public UniTask<bool> ApplySnapshotFromNetworkAsync(string context, bool triggerRefreshedEvent = true)
    {
        return ApplySnapshotFromNetworkAsync(context, triggerRefreshedEvent, CancellationToken.None);
    }

    public async UniTask<bool> ApplySnapshotFromNetworkAtOrAfterRevisionAsync(
        int expectedRevision,
        int expectedRound,
        string context,
        bool triggerRefreshedEvent,
        CancellationToken cancellationToken)
    {
        if (playerManager == null)
        {
            LogShopTrace("ApplySnapshotAtRevision:ABORT_NO_PLAYER", $"context={context}");
            return false;
        }

        const float timeoutSeconds = 12f;
        float waited = 0f;
        while (waited < timeoutSeconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (playerManager.TryGetShopSnapshot(
                    out _,
                    out _,
                    out _,
                    out int currentRevision,
                    out int currentRound)
                && IsSnapshotAtOrAfter(
                    currentRevision,
                    currentRound,
                    expectedRevision,
                    expectedRound))
            {
                // ApplySnapshotFromNetworkAsync reads the snapshot again. If authority advances it
                // between these calls, the later snapshot is applied; an old RPC/command can never
                // roll the local cache back to its names/stars payload.
                return await ApplySnapshotFromNetworkAsync(
                    context,
                    triggerRefreshedEvent,
                    cancellationToken);
            }

            await UniTask.Delay(100, cancellationToken: cancellationToken);
            waited += 0.1f;
        }

        LogShopTrace(
            "ApplySnapshotAtRevision:TIMEOUT",
            $"context={context},expectedRevision={expectedRevision},expectedRound={expectedRound}");
        return false;
    }

    private static bool IsSnapshotAtOrAfter(
        int candidateRevision,
        int candidateRound,
        int expectedRevision,
        int expectedRound)
    {
        if (candidateRevision <= 0)
        {
            return false;
        }

        // Revision-less legacy messages may only request the currently replicated snapshot.
        if (expectedRevision <= 0)
        {
            return true;
        }

        if (candidateRevision == expectedRevision)
        {
            return expectedRound <= 0 || candidateRound >= expectedRound;
        }

        const long revisionRange = int.MaxValue - 1L;
        long forwardDistance =
            (candidateRevision - (long)expectedRevision + revisionRange) % revisionRange;
        return forwardDistance > 0 && forwardDistance <= revisionRange / 2L;
    }

    public async UniTask<bool> ApplySnapshotFromNetworkAsync(
        string context,
        bool triggerRefreshedEvent,
        CancellationToken cancellationToken)
    {
        LogShopTrace("ApplySnapshotFromNetworkAsync:ENTER", $"context={context}");
        if (playerManager == null)
        {
            LogShopTrace("ApplySnapshotFromNetworkAsync:ABORT_NO_PLAYER", $"context={context}");
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await WaitUntilDatabaseLoaded().AttachExternalCancellation(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!playerManager.TryGetShopSnapshot(out string[] unitKeys, out int[] starLevels, out bool[] soldFlags, out int revision, out int round))
        {
            LogShopTrace("ApplySnapshotFromNetworkAsync:NO_SNAPSHOT", $"context={context}");
            return false;
        }

        // Revision equality alone is insufficient after host migration: the non-networked cache
        // may be stale even though the replicated revision was restored. Skip only on exact data.
        if (_lastAppliedSnapshotRevision == revision
            && DoesRuntimeShopExactlyMatch(unitKeys, starLevels, soldFlags))
        {
            LogShopTrace("ApplySnapshotFromNetworkAsync:SKIP_DUPLICATE_REV", $"context={context},revision={revision}");
            return true;
        }

        int count = Mathf.Min(unitKeys.Length, starLevels.Length);
        var restoredItems = new List<ShopItem>(count);
        var restoredSoldFlags = new bool[_isSlotSold.Length];
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string unitKey = unitKeys[i];
            if (string.IsNullOrEmpty(unitKey))
            {
                LogShopTrace("ApplySnapshotFromNetworkAsync:INVALID_EMPTY_KEY", $"context={context},slot={i}");
                return false;
            }

            UnitData unitData = LoadManager.Instance?.GetUnitData(unitKey);
            if (unitData == null)
            {
                unitData = await AssetLoader.LoadAssetAsync<UnitData>(unitKey, _addressableAssets);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (unitData == null)
            {
                LogShopTrace("ApplySnapshotFromNetworkAsync:UNRESOLVED_KEY", $"context={context},slot={i},key={unitKey}");
                return false;
            }

            int star = Mathf.Max(1, starLevels[i]);
            restoredItems.Add(new ShopItem(unitData, star));
            if (i < soldFlags.Length && i < restoredSoldFlags.Length)
            {
                restoredSoldFlags[i] = soldFlags[i];
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        currentShopItems.Clear();
        currentShopItems.AddRange(restoredItems);
        System.Array.Copy(restoredSoldFlags, _isSlotSold, restoredSoldFlags.Length);
        _lastAppliedSnapshotRevision = revision;
        LogShopTrace("ApplySnapshotFromNetworkAsync:APPLIED", $"context={context},revision={revision},round={round},count={currentShopItems.Count}");

        if (triggerRefreshedEvent)
        {
            GameEvents.TriggerShopRefreshed(playerManager);
        }

        return currentShopItems.Count == count;
    }

    private bool DoesRuntimeShopExactlyMatch(
        string[] unitKeys,
        int[] starLevels,
        bool[] soldFlags)
    {
        if (unitKeys == null || starLevels == null || soldFlags == null
            || unitKeys.Length != starLevels.Length
            || unitKeys.Length != soldFlags.Length
            || currentShopItems == null
            || currentShopItems.Count != unitKeys.Length)
        {
            return false;
        }

        for (int i = 0; i < unitKeys.Length; i++)
        {
            ShopItem item = currentShopItems[i];
            string runtimeKey = StableDataKeyUtility.NormalizeKey(
                item.UnitData != null ? item.UnitData.name : string.Empty);
            if (!string.Equals(runtimeKey, unitKeys[i], System.StringComparison.Ordinal)
                || item.StarLevel != starLevels[i]
                || IsSlotSold(i) != soldFlags[i])
            {
                return false;
            }
        }

        for (int i = unitKeys.Length; i < _isSlotSold.Length; i++)
        {
            if (_isSlotSold[i])
            {
                return false;
            }
        }

        return true;
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
    /// Projects a purchase that was already committed by State Authority into this peer's
    /// non-networked runtime cache. This never spends gold, spawns a unit, or publishes a
    /// snapshot; it is safe to call repeatedly from the authoritative success notification.
    /// </summary>
    public bool ApplyAuthoritativePurchaseNotification(int slotIndex)
    {
        int itemCount = currentShopItems != null ? currentShopItems.Count : 0;
        if (slotIndex < 0 || slotIndex >= itemCount || slotIndex >= _isSlotSold.Length)
        {
            return false;
        }

        _isSlotSold[slotIndex] = true;
        return true;
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
