// Assets/Scripts/Game/Monsters/MonsterSpawner.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Linq;
using MDF.Runtime.Assets;

public readonly struct MonsterPrewarmReport
{
    public string Context { get; }
    public int RequestedPrefabCount { get; }
    public int CompletedPrefabCount { get; }
    public int RequestedInstanceCount { get; }
    public int CreatedInstanceCount { get; }
    public int FailedPrefabCount { get; }
    public string FailureSummary { get; }
    public string SkippedReason { get; }

    public bool Succeeded => FailedPrefabCount == 0;
    public bool WasAttempted => RequestedPrefabCount > 0;

    public MonsterPrewarmReport(
        string context,
        int requestedPrefabCount,
        int completedPrefabCount,
        int requestedInstanceCount,
        int createdInstanceCount,
        int failedPrefabCount,
        string failureSummary,
        string skippedReason = null)
    {
        Context = context ?? string.Empty;
        RequestedPrefabCount = Mathf.Max(0, requestedPrefabCount);
        CompletedPrefabCount = Mathf.Max(0, completedPrefabCount);
        RequestedInstanceCount = Mathf.Max(0, requestedInstanceCount);
        CreatedInstanceCount = Mathf.Max(0, createdInstanceCount);
        FailedPrefabCount = Mathf.Max(0, failedPrefabCount);
        FailureSummary = failureSummary ?? string.Empty;
        SkippedReason = skippedReason ?? string.Empty;
    }

    public static MonsterPrewarmReport Skipped(string context, string reason)
    {
        return new MonsterPrewarmReport(context, 0, 0, 0, 0, 0, string.Empty, reason);
    }

    public static MonsterPrewarmReport Failed(string context, int requestedPrefabCount, string reason)
    {
        int failures = Mathf.Max(1, requestedPrefabCount);
        return new MonsterPrewarmReport(
            context,
            Mathf.Max(0, requestedPrefabCount),
            0,
            0,
            0,
            failures,
            reason);
    }

    public static MonsterPrewarmReport Combine(string context, params MonsterPrewarmReport[] reports)
    {
        if (reports == null || reports.Length == 0)
        {
            return Skipped(context, "no reports");
        }

        int requestedPrefabs = 0;
        int completedPrefabs = 0;
        int requestedInstances = 0;
        int createdInstances = 0;
        int failures = 0;
        var failureParts = new List<string>();
        foreach (MonsterPrewarmReport report in reports)
        {
            requestedPrefabs += report.RequestedPrefabCount;
            completedPrefabs += report.CompletedPrefabCount;
            requestedInstances += report.RequestedInstanceCount;
            createdInstances += report.CreatedInstanceCount;
            failures += report.FailedPrefabCount;
            if (!string.IsNullOrWhiteSpace(report.FailureSummary))
            {
                failureParts.Add(report.FailureSummary);
            }
        }

        return new MonsterPrewarmReport(
            context,
            requestedPrefabs,
            completedPrefabs,
            requestedInstances,
            createdInstances,
            failures,
            string.Join(" | ", failureParts));
    }

    public override string ToString()
    {
        string skipped = string.IsNullOrWhiteSpace(SkippedReason) ? string.Empty : $", skipped={SkippedReason}";
        string failures = string.IsNullOrWhiteSpace(FailureSummary) ? string.Empty : $", errors={FailureSummary}";
        return $"context={Context}, prefabs={CompletedPrefabCount}/{RequestedPrefabCount}, " +
               $"requestedInstances={RequestedInstanceCount}, createdInstances={CreatedInstanceCount}, " +
               $"failedPrefabs={FailedPrefabCount}{skipped}{failures}";
    }
}

public class MonsterSpawner : MonoBehaviour
{
    private AddressableAssetOwner _addressableAssets = new AddressableAssetOwner();

    #region 필드 및 참조

    private PlayerManager _playerManager;
    private AstarGrid _pathfinder;
    private float _lastRuntimeResolveLogTime;
    private static int _spawnTraceSeq;
    private const int SpawnSeparationSearchRadius = 2;
    private const float SpawnSeparationDistanceFactor = 0.55f;

    [Header("스폰 설정 (자동 할당됨)")]
    [SerializeField] private Transform goalTransform;
    public GameObject statusBarPrefab;

    [Header("정리용 부모 오브젝트")]
    public Transform monsterParent;

    [Header("Monster Prewarm")]
    [SerializeField] private bool enableMonsterPrewarm = true;
    [Tooltip("Soft upper bound per normal monster prefab. The network pool may impose a lower hard cap.")]
    [SerializeField, Min(1)] private int maxNormalPrewarmCountPerMonsterPrefab = 32;
    [Tooltip("Soft upper bound per base-wave monster prefab.")]
    [SerializeField, Min(1)] private int maxWavePrewarmCountPerMonsterPrefab = 24;
    [SerializeField, Min(0)] private int prewarmCountPerBossPrefab = 2;
    
    private readonly HashSet<string> _activeAutoSpawnKeys = new HashSet<string>();
    private readonly HashSet<string> _completedAutoSpawnKeys = new HashSet<string>();
    private int _battleGeneration;
    private CancellationTokenSource _battleCancellation;
    private GameManagers.GameState _battleGenerationState;

    // _isSpawningWave 제거됨 (CS0414 - 사용되지 않음)

    #endregion

    #region 초기화

    /// <summary>Subscribes battle lifecycle cancellation.</summary>
    private void OnEnable()
    {
        if (_addressableAssets == null || _addressableAssets.IsDisposed)
        {
            _addressableAssets = new AddressableAssetOwner();
        }
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        RotateBattleGeneration(GameManagers.GameState.GameOver, createCancellation: false);
        _activeAutoSpawnKeys.Clear();
        _addressableAssets?.Dispose();
    }

    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        bool isBattle = IsBattleState(newState);
        RotateBattleGeneration(newState, isBattle);
        if (!isBattle)
        {
            _activeAutoSpawnKeys.Clear();
        }
    }

    private void RotateBattleGeneration(GameManagers.GameState state, bool createCancellation)
    {
        _battleGeneration++;
        _battleGenerationState = state;
        if (_battleCancellation != null)
        {
            _battleCancellation.Cancel();
            _battleCancellation.Dispose();
            _battleCancellation = null;
        }

        if (createCancellation)
        {
            _battleCancellation = new CancellationTokenSource();
        }
    }

    private static bool IsBattleState(GameManagers.GameState state)
    {
        return state == GameManagers.GameState.Battle1 || state == GameManagers.GameState.Battle2;
    }

    public int CaptureBattleGeneration()
    {
        GameManagers gm = GameManagers.Instance;
        if (gm == null || !IsBattleState(gm.currentState))
        {
            return -1;
        }

        if (_battleCancellation == null || _battleGenerationState != gm.currentState)
        {
            RotateBattleGeneration(gm.currentState, createCancellation: true);
        }

        return _battleGeneration;
    }

    public bool IsBattleGenerationCurrent(int generation)
    {
        GameManagers gm = GameManagers.Instance;
        return generation >= 0 &&
               generation == _battleGeneration &&
               _battleCancellation != null &&
               !_battleCancellation.IsCancellationRequested &&
               gm != null &&
               gm.currentState == _battleGenerationState &&
               IsBattleState(gm.currentState);
    }

    public CancellationToken GetBattleCancellationToken(int generation)
    {
        return IsBattleGenerationCurrent(generation) && _battleCancellation != null
            ? _battleCancellation.Token
            : new CancellationToken(canceled: true);
    }

    /// <summary>Initializes the owner, pathfinder, and goal references.</summary>
    public void Initialize(PlayerManager owner, AstarGrid grid, Transform goalTransform)
    {
        _playerManager = owner;
        _pathfinder = grid;
        this.goalTransform = goalTransform;

        string ownerName = owner != null ? owner.name : "NULL";
        // Debug.Log($"[MonsterSpawner] '{ownerName}' 초기화 완료. " +
                  // $"AstarGrid: {(grid != null)}, " +
                  // $"goalTransform: {(goalTransform != null)}");

        if (monsterParent == null)
        {
            GameObject parentObject = new GameObject($"[{_playerManager.name} Monsters]");
            parentObject.transform.SetParent(transform.parent);
            monsterParent = parentObject.transform;
        }
    }

    public bool EnsureRuntimeReferencesForMigration(string context, bool verboseFailure = true)
    {
        return EnsureRuntimeReferences(context, verboseFailure);
    }

    public bool IsRuntimeReady(out string reason)
    {
        if (_playerManager == null)
        {
            reason = "owner=null";
            return false;
        }

        if (_pathfinder == null)
        {
            reason = "pathfinder=null";
            return false;
        }

        if (goalTransform == null)
        {
            reason = "goalTransform=null";
            return false;
        }

        reason = null;
        return true;
    }

    private bool EnsureRuntimeReferences(string context, bool verboseFailure)
    {
        if (_playerManager == null)
        {
            _playerManager = GetComponentInParent<PlayerManager>();
        }

        if (_playerManager != null)
        {
            if (_pathfinder == null)
            {
                _pathfinder = _playerManager.astarGrid != null ? _playerManager.astarGrid : GetComponentInChildren<AstarGrid>(true);
            }

            if (goalTransform == null)
            {
                goalTransform = _playerManager.goalTransform;
            }

        }

        bool ready = IsRuntimeReady(out string notReadyReason);
        if (!ready && verboseFailure && Time.unscaledTime - _lastRuntimeResolveLogTime > 0.5f)
        {
            _lastRuntimeResolveLogTime = Time.unscaledTime;
            // Debug.LogWarning($"[MonsterSpawner] 참조 복구 실패 ({context}) reason={notReadyReason} {DescribeRuntimeState()}");
        }

        return ready;
    }

    private string DescribeRuntimeState()
    {
        string ownerState = _playerManager == null
            ? "owner=null"
            : $"owner=Player({GetPlayerIdForLog(_playerManager)}, name={_playerManager.name})";
        string pathState = _pathfinder == null ? "pathfinder=null" : $"pathfinder={_pathfinder.name}";
        string goalState = goalTransform == null ? "goal=null" : $"goal={goalTransform.name}";
        return $"{ownerState}, {pathState}, {goalState}";
    }

    private string DescribeRunnerState(NetworkRunner runner)
    {
        if (runner == null)
        {
            return "runner=null";
        }

        return $"runner={runner.name},running={runner.IsRunning},mode={runner.GameMode},isServer={runner.IsServer}";
    }

    private string DescribePlayerState(PlayerManager player, string label)
    {
        if (player == null)
        {
            return $"{label}=null";
        }

        bool hasObject = player.Object != null;
        bool objectValid = hasObject && player.Object.IsValid;
        bool hasAuthority = objectValid && player.Object.HasStateAuthority;
        string gridName = player.astarGrid != null ? player.astarGrid.name : "null";
        string goalName = player.goalTransform != null ? player.goalTransform.name : "null";
        string fieldName = player.fieldManager != null ? player.fieldManager.name : "null";

        return $"{label}=Player(id={GetPlayerIdForLog(player)},name={player.name},active={player.isActiveAndEnabled}," +
               $"hasObject={hasObject},objectValid={objectValid},stateAuth={hasAuthority}," +
               $"grid={gridName},goal={goalName},field={fieldName})";
    }

    private string DescribeFieldState(FieldManager field, string label)
    {
        if (field == null)
        {
            return $"{label}=null";
        }

        string ownerId = field.playerManager != null ? GetPlayerIdForLog(field.playerManager) : "null";
        string groundName = field.ground3D != null ? field.ground3D.name : "null";
        return $"{label}=Field(name={field.name},active={field.isActiveAndEnabled},owner={ownerId},ground={groundName})";
    }

    private static string GetPlayerIdForLog(PlayerManager player)
    {
        if (player == null)
        {
            return "null";
        }

        var playerObject = player.Object;
        if (playerObject == null || !playerObject.IsValid)
        {
            return "unspawned";
        }

        return player.playerId.ToString();
    }

    private string BuildAllPlayersSnapshot()
    {
        var players = UnityEngine.Object.FindObjectsOfType<PlayerManager>(true);
        if (players == null || players.Length == 0)
        {
            return "allPlayers=0";
        }

        var chunks = new List<string>(players.Length);
        for (int i = 0; i < players.Length; i++)
        {
            chunks.Add($"[{i}] {DescribePlayerState(players[i], "player")}");
        }

        return $"allPlayers={players.Length} {string.Join(" || ", chunks)}";
    }

    private string BuildAllFieldsSnapshot()
    {
        var fields = UnityEngine.Object.FindObjectsOfType<FieldManager>(true);
        if (fields == null || fields.Length == 0)
        {
            return "allFields=0";
        }

        var chunks = new List<string>(fields.Length);
        for (int i = 0; i < fields.Length; i++)
        {
            chunks.Add($"[{i}] {DescribeFieldState(fields[i], "field")}");
        }

        return $"allFields={fields.Length} {string.Join(" || ", chunks)}";
    }

    [System.Diagnostics.Conditional("MDF_SPAWN_TRACE")]
    private void LogSpawnTrace(string step, MonsterData monsterData, Vector3 spawnPosition, FieldManager targetFieldManager, string extra = null)
    {
        PlayerManager targetPlayer = targetFieldManager != null ? targetFieldManager.playerManager : null;
        string monsterName = monsterData != null ? monsterData.monsterName : "null";
        string suffix = string.IsNullOrEmpty(extra) ? string.Empty : $" | {extra}";

        // Debug.Log(
            // $"[SPAWN-TRACE #{++_spawnTraceSeq}] {step} | monster={monsterName} spawn={spawnPosition} | " +
            // $"{DescribePlayerState(_playerManager, "attacker")} | {DescribeFieldState(targetFieldManager, "targetField")} | " +
            // $"{DescribePlayerState(targetPlayer, "defender")} | {DescribeRunnerState(_playerManager?.Runner)}{suffix}");
    }

    private struct MonsterPrewarmRequest
    {
        public MonsterData MonsterData;
        public int TargetFreeCount;

        public MonsterPrewarmRequest(MonsterData monsterData, int targetFreeCount)
        {
            MonsterData = monsterData;
            TargetFreeCount = targetFreeCount;
        }
    }

    public async UniTask<MonsterPrewarmReport> PrewarmAttackMonsterPoolAsync(
        IEnumerable<MonsterPoolEntry> pool,
        string context = null,
        int activePlayerCount = 0,
        int maximumBlackMagic = 0,
        RoundWaveData concurrentWaveData = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context = context ?? "AttackMonsterPool";
        if (!enableMonsterPrewarm || pool == null)
        {
            return MonsterPrewarmReport.Skipped(context, !enableMonsterPrewarm ? "disabled" : "pool missing");
        }

        var entries = pool.Where(entry => entry?.MonsterData != null && !entry.IsEmpty).ToList();
        ResolveAttackDemandInputs(ref activePlayerCount, ref maximumBlackMagic);
        var requests = new Dictionary<string, MonsterPrewarmRequest>();
        foreach (var entry in entries)
        {
            int availableCount = Mathf.Max(1, Mathf.Max(entry.RemainingCount, entry.MaxCount));
            int concurrentWaveCount = entry.IsBoss
                ? 0
                : ResolveWaveCountForPrefab(concurrentWaveData, entry.MonsterData.monsterPrefab);
            int targetCount = entry.IsBoss
                ? Mathf.Min(prewarmCountPerBossPrefab, availableCount)
                : EstimateNormalPrewarmTarget(
                    activePlayerCount,
                    maximumBlackMagic,
                    Mathf.Max(1, entry.MonsterData.blackMagicCost),
                    maxNormalPrewarmCountPerMonsterPrefab,
                    concurrentWaveCount);
            AddPrewarmRequest(requests, entry.MonsterData, targetCount);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await PrewarmRequestsAsync(requests, context, cancellationToken);
    }

    public async UniTask<MonsterPrewarmReport> PrewarmWaveAsync(
        RoundWaveData waveData,
        string context = null,
        int activePlayerCount = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context = context ?? "Wave";
        if (!enableMonsterPrewarm || waveData?.monsters == null)
        {
            return MonsterPrewarmReport.Skipped(context, !enableMonsterPrewarm ? "disabled" : "wave missing");
        }

        int ignoredMaximum = 0;
        ResolveAttackDemandInputs(ref activePlayerCount, ref ignoredMaximum);
        int concurrentBattleSlots = ResolveConcurrentBattleSlots(activePlayerCount);
        var requests = new Dictionary<string, MonsterPrewarmRequest>();
        foreach (var entry in waveData.monsters)
        {
            if (entry?.monsterData == null || entry.count <= 0)
            {
                continue;
            }

            int totalWaveCount = ResolveWaveCountForPrefab(waveData, entry.monsterData.monsterPrefab);
            long concurrentWaveDemand = (long)Mathf.Max(1, totalWaveCount) * concurrentBattleSlots;
            int targetCount = concurrentWaveDemand >= maxWavePrewarmCountPerMonsterPrefab
                ? maxWavePrewarmCountPerMonsterPrefab
                : Mathf.Max(1, (int)concurrentWaveDemand);
            AddPrewarmRequest(requests, entry.monsterData, targetCount);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await PrewarmRequestsAsync(requests, context, cancellationToken);
    }

    public async UniTask<MonsterPrewarmReport> PrewarmMonsterDataAsync(
        MonsterData monsterData,
        bool isBoss,
        int requestedCount,
        string context = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int configuredLimit = isBoss ? prewarmCountPerBossPrefab : maxNormalPrewarmCountPerMonsterPrefab;
        int targetCount = requestedCount > 0
            ? Mathf.Min(configuredLimit, requestedCount)
            : configuredLimit;

        var requests = new Dictionary<string, MonsterPrewarmRequest>();
        AddPrewarmRequest(requests, monsterData, targetCount);
        return await PrewarmRequestsAsync(requests, context ?? "SingleMonster", cancellationToken);
    }

    public async UniTask<MonsterPrewarmReport> PrewarmMonsterDataSetAsync(
        IEnumerable<MonsterData> monsterDataSet,
        bool isBoss,
        int requestedCount,
        string context = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!enableMonsterPrewarm || monsterDataSet == null)
        {
            return MonsterPrewarmReport.Skipped(
                context ?? "MonsterSet",
                !enableMonsterPrewarm ? "disabled" : "data set missing");
        }

        int configuredLimit = isBoss ? prewarmCountPerBossPrefab : maxNormalPrewarmCountPerMonsterPrefab;
        int targetCount = requestedCount > 0
            ? Mathf.Min(configuredLimit, requestedCount)
            : configuredLimit;
        var requests = new Dictionary<string, MonsterPrewarmRequest>();
        foreach (MonsterData monsterData in monsterDataSet)
        {
            AddPrewarmRequest(requests, monsterData, targetCount);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await PrewarmRequestsAsync(requests, context ?? "MonsterSet", cancellationToken);
    }

    public static int EstimateNormalPrewarmTarget(
        int activePlayerCount,
        int maximumBlackMagic,
        int blackMagicCost,
        int perPrefabCap,
        int waveCountPerBattleForPrefab = 0)
    {
        int boundedCap = Mathf.Max(1, perPrefabCap);
        int concurrentAttackers = ResolveConcurrentBattleSlots(activePlayerCount);
        int summonsPerAttacker = Mathf.Max(
            1,
            Mathf.CeilToInt(Mathf.Max(0, maximumBlackMagic) / (float)Mathf.Max(1, blackMagicCost)));
        long estimatedDemand = (long)concurrentAttackers *
                               (summonsPerAttacker + Mathf.Max(0, waveCountPerBattleForPrefab));
        return estimatedDemand >= boundedCap ? boundedCap : Mathf.Max(1, (int)estimatedDemand);
    }

    private static int ResolveConcurrentBattleSlots(int activePlayerCount)
    {
        return Mathf.Max(1, (Mathf.Max(1, activePlayerCount) + 1) / 2);
    }

    private static int ResolveWaveCountForPrefab(RoundWaveData waveData, string monsterPrefabKey)
    {
        if (waveData?.monsters == null || string.IsNullOrWhiteSpace(monsterPrefabKey))
        {
            return 0;
        }

        long total = 0;
        foreach (WaveMonsterEntry entry in waveData.monsters)
        {
            if (entry?.monsterData == null ||
                !string.Equals(entry.monsterData.monsterPrefab, monsterPrefabKey, System.StringComparison.Ordinal))
            {
                continue;
            }

            total += Mathf.Max(0, entry.count);
            if (total >= int.MaxValue)
            {
                return int.MaxValue;
            }
        }

        return (int)total;
    }

    private void ResolveAttackDemandInputs(ref int activePlayerCount, ref int maximumBlackMagic)
    {
        GameManagers gameManagers = GameManagers.Instance;
        if (gameManagers != null)
        {
            List<PlayerManager> activePlayers = gameManagers.AllPlayers
                .Where(player => player != null && player.GetHealth() > 0)
                .ToList();
            if (activePlayerCount <= 0)
            {
                activePlayerCount = activePlayers.Count;
            }

            if (maximumBlackMagic <= 0)
            {
                int round = Mathf.Max(1, gameManagers.currentRound);
                maximumBlackMagic = activePlayers
                    .Select(player => player.GetProjectedBlackMagicMaximumForRound(round))
                    .DefaultIfEmpty(0)
                    .Max();
            }
        }

        activePlayerCount = Mathf.Max(1, activePlayerCount);
        if (maximumBlackMagic <= 0 && _playerManager != null)
        {
            int round = GameManagers.Instance != null ? Mathf.Max(1, GameManagers.Instance.currentRound) : 1;
            maximumBlackMagic = _playerManager.GetProjectedBlackMagicMaximumForRound(round);
        }
    }

    private static void AddPrewarmRequest(Dictionary<string, MonsterPrewarmRequest> requests, MonsterData monsterData, int targetCount)
    {
        if (requests == null || monsterData == null || targetCount <= 0 || string.IsNullOrEmpty(monsterData.monsterPrefab))
        {
            return;
        }

        string key = monsterData.monsterPrefab;
        if (requests.TryGetValue(key, out var existing) && existing.TargetFreeCount >= targetCount)
        {
            return;
        }

        requests[key] = new MonsterPrewarmRequest(monsterData, targetCount);
    }

    private async UniTask<MonsterPrewarmReport> PrewarmRequestsAsync(
        Dictionary<string, MonsterPrewarmRequest> requests,
        string context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!enableMonsterPrewarm || requests == null || requests.Count == 0)
        {
            return MonsterPrewarmReport.Skipped(context, !enableMonsterPrewarm ? "disabled" : "no requests");
        }

        if (_playerManager == null)
        {
            _playerManager = GetComponentInParent<PlayerManager>();
        }

        var runner = _playerManager?.Runner;
        if (runner == null || !runner.IsRunning)
        {
            MonsterPrewarmReport failure = MonsterPrewarmReport.Failed(context, requests.Count, "runner unavailable");
            Debug.LogWarning($"[MonsterSpawner] Monster prewarm failed. {failure}");
            return failure;
        }

        var provider = runner.GetComponent<PooledNetworkObjectProvider>();
        if (provider == null)
        {
            MonsterPrewarmReport failure = MonsterPrewarmReport.Failed(context, requests.Count, "network pool provider unavailable");
            Debug.LogWarning($"[MonsterSpawner] Monster prewarm failed. {failure}");
            return failure;
        }

        int createdTotal = 0;
        int completedPrefabs = 0;
        int requestedInstances = requests.Values.Sum(request => Mathf.Max(0, request.TargetFreeCount));
        int failedPrefabs = 0;
        var failures = new List<string>();
        foreach (var request in requests.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.MonsterData == null || request.TargetFreeCount <= 0)
            {
                continue;
            }

            GameObject prefab;
            try
            {
                prefab = await AssetLoader.LoadAssetAsync<GameObject>(request.MonsterData.monsterPrefab, _addressableAssets);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception exception)
            {
                failedPrefabs++;
                failures.Add($"{request.MonsterData.monsterPrefab}: load exception ({exception.Message})");
                continue;
            }

            if (prefab == null)
            {
                failedPrefabs++;
                failures.Add($"{request.MonsterData.monsterPrefab}: prefab load returned null");
                continue;
            }

            if (!prefab.TryGetComponent<NetworkObject>(out var netPrefab))
            {
                failedPrefabs++;
                failures.Add($"{request.MonsterData.monsterPrefab}: NetworkObject missing");
                continue;
            }

            try
            {
                await FirstSpawnPresentationPrewarmer.WarmPrefabAsync(
                    prefab,
                    $"monster:{request.MonsterData.monsterPrefab}",
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception exception)
            {
                // Presentation warmup is an optimization. A graphics-driver/editor failure must
                // not prevent the authoritative pool from being prepared or the phase advancing.
                Debug.LogWarning(
                    $"[MonsterSpawner] Presentation prewarm skipped for " +
                    $"'{request.MonsterData.monsterPrefab}': {exception.Message}");
                failedPrefabs++;
                failures.Add($"{request.MonsterData.monsterPrefab}: presentation ({exception.Message})");
            }

            if (this == null || !isActiveAndEnabled || runner == null || !runner.IsRunning || provider == null)
            {
                failedPrefabs++;
                failures.Add($"{request.MonsterData.monsterPrefab}: lifecycle ended");
                break;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                createdTotal += await provider.PrewarmPrefabAsync(
                    runner,
                    netPrefab,
                    request.TargetFreeCount,
                    registerForMonsterTrimming: true,
                    cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                completedPrefabs++;
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception exception)
            {
                failedPrefabs++;
                failures.Add($"{request.MonsterData.monsterPrefab}: pool ({exception.Message})");
            }
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        GameManagers gameManagers = GameManagers.Instance;
        if (gameManagers != null && gameManagers.currentState == GameManagers.GameState.Prepare)
        {
            try
            {
                // Register and satisfy this Prepare's reserve floors before trimming. Otherwise
                // an unused catalog entry would be destroyed to its high-water target and then
                // immediately recreated to the same projected battle target every round.
                await provider.TrimMonsterPoolsForGenerationAsync(
                    Mathf.Max(1, gameManagers.currentRound),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception exception)
            {
                // Retention trimming is local memory hygiene. A trim failure must not prevent
                // presentation/network pool readiness or delay the authoritative Prepare flow.
                Debug.LogWarning($"[MonsterSpawner] Monster pool trim skipped: {exception.Message}");
            }
        }

        var report = new MonsterPrewarmReport(
            context,
            requests.Count,
            completedPrefabs,
            requestedInstances,
            createdTotal,
            failedPrefabs,
            string.Join(" | ", failures));
        if (createdTotal > 0)
        {
            Debug.Log($"[MonsterSpawner] Monster prewarm complete. owner={GetPlayerIdForLog(_playerManager)}, {report}");
        }

        if (!report.Succeeded)
        {
            Debug.LogWarning($"[MonsterSpawner] Monster prewarm completed with failures. owner={GetPlayerIdForLog(_playerManager)}, {report}");
        }

        return report;
    }

    public bool IsAutoSpawnRunningForKey(string battleBootstrapKey)
    {
        if (string.IsNullOrWhiteSpace(battleBootstrapKey))
        {
            return false;
        }

        return _activeAutoSpawnKeys.Contains(battleBootstrapKey);
    }

    private bool TryBeginAutoSpawnForKey(string battleBootstrapKey, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(battleBootstrapKey))
        {
            return true;
        }

        if (_activeAutoSpawnKeys.Contains(battleBootstrapKey))
        {
            reason = "alreadyRunning";
            return false;
        }

        if (_completedAutoSpawnKeys.Contains(battleBootstrapKey))
        {
            reason = "alreadyCompleted";
            return false;
        }

        _activeAutoSpawnKeys.Add(battleBootstrapKey);
        reason = "acquired";
        return true;
    }

    private void EndAutoSpawnForKey(string battleBootstrapKey, bool completed)
    {
        if (string.IsNullOrWhiteSpace(battleBootstrapKey))
        {
            return;
        }

        _activeAutoSpawnKeys.Remove(battleBootstrapKey);
        if (completed)
        {
            _completedAutoSpawnKeys.Add(battleBootstrapKey);
        }
    }

    #endregion

    #region Update (전투 상태 체크)

    void Update()
    {
        if (_playerManager == null || GameManagers.Instance == null) return;
        
        // 서버에서만 전투 상태 업데이트 (Networked 속성은 StateAuthority만 변경 가능)
        if (_playerManager.Object == null || !_playerManager.Object.HasStateAuthority) return;

        var currentState = GameManagers.Instance.GetGameState();
        bool isInBattle = currentState == GameManagers.GameState.Battle1 || currentState == GameManagers.GameState.Battle2;
        
        // 비전투 상태일 때만 상태 리셋 (Prepare, Setup 등)
        // 전투 중 상태 업데이트는 GameManagers.FixedUpdateNetwork()에서 처리
        // (공격자-수비자 페어 동기화를 위해 중앙에서 관리)
        if (!isInBattle)
        {
            if (_playerManager.IsActivelyFighting)
            {
                _playerManager.SetFightingState(false);
                // Debug.Log($"<color=yellow>[MonsterSpawner] Player {_playerManager.playerId}: 전투 상태 비전투 (GameState != Battle)</color>");
            }
        }
        // 전투 중 개별 판정은 GameManagers.IsPlayerBattleFinished()에서 수행
        // → 공격자 풀 + 수비자 필드 몬스터를 함께 확인하고, 상대와 함께 상태 변경
    }

    #endregion

    #region 웨이브 소환

    /// <summary>
    /// 특정 라운드의 웨이브를 소환합니다.
    /// </summary>
    /// <param name="round">라운드 번호</param>


    /// <summary>
    /// 증강 몬스터 없이 기본 웨이브만 소환합니다. (상대 없는 플레이어의 수비 시퀀스용)
    /// AI가 순서대로 자동 소환합니다.
    /// </summary>
    /// <param name="round">라운드 번호</param>


    /// <summary>
    /// 기본 웨이브만 소환하는 코루틴 (증강 몬스터, 보스 제외)
    /// </summary>


    /// <summary>
    /// 기본 웨이브 → 대기 중인 보스 → 생존 보스 → 증강 몬스터 순으로 모두 간격 두고 소환
    /// </summary>


    /// <summary>
    /// WaveDatabase의 RoundWaveData를 기반으로 몬스터를 소환합니다.
    /// </summary>
    /// <param name="round">현재 라운드 (자동 스케일링 계산용)</param>
    /// <param name="waveData">웨이브 데이터</param>


    #endregion

    #region 디버프 적용

    private void ApplyOpponentDebuffs(Monster monster)
    {
        if (_playerManager.opponentManager == null) return;

        float healthMultiplier = 1f;
        float speedMultiplier = 1f;

        foreach (var augment in _playerManager.opponentManager.chosenAugments)
        {
            if (augment == null)
            {
                continue;
            }

            for (int effectIndex = 0; effectIndex < augment.EffectCount; effectIndex++)
            {
                AugmentEffectData effect = augment.GetEffect(effectIndex);
                if (effect == null || effect.targetType != TargetType.Opponent)
                {
                    continue;
                }

                switch(effect.effectType)
                {
                    case EffectType.IncreaseEnemyHealth:
                        healthMultiplier += effect.value;
                        break;
                    case EffectType.IncreaseEnemyMoveSpeed:
                        speedMultiplier += effect.value;
                        break;
                }
            }
        }

        if(healthMultiplier > 1f || speedMultiplier > 1f)
        {
            monster.ApplyAugmentBuffs(healthMultiplier, speedMultiplier, 1f);
        }
    }

    /// <summary>
    /// 공격팀(소환자)의 몬스터 버프 증강체 효과를 적용합니다.
    /// SpawnMonsterAtPositionAsync에서 호출됩니다.
    /// </summary>
    private void ApplyAttackerAugmentBuffs(Monster monster)
    {
        if (_playerManager == null) return;

        float healthMultiplier = 1f;
        float speedMultiplier = 1f;
        float damageMultiplier = 1f;

        foreach (var augment in _playerManager.chosenAugments)
        {
            if (augment == null)
            {
                continue;
            }

            for (int effectIndex = 0; effectIndex < augment.EffectCount; effectIndex++)
            {
                AugmentEffectData effect = augment.GetEffect(effectIndex);
                if (effect == null)
                {
                    continue;
                }

                if (effect.effectType == EffectType.StrengthenMonsterType
                    && effect.strengthenedMonsterData != null
                    && effect.strengthenedMonsterData == monster.Data)
                {
                    healthMultiplier += Mathf.Max(0f, effect.monsterHealthBonusPercent);
                    speedMultiplier += Mathf.Max(0f, effect.monsterMoveSpeedBonusPercent);
                    damageMultiplier += Mathf.Max(0f, effect.monsterDamageBonusPercent);
                    continue;
                }

                // 상대 필드에 적용되는 몬스터 강화 증강체
                if (effect.targetType == TargetType.Opponent)
                {
                    switch(effect.effectType)
                    {
                        case EffectType.IncreaseEnemyHealth:
                            healthMultiplier += effect.value;
                            break;
                        case EffectType.IncreaseEnemyMoveSpeed:
                            speedMultiplier += effect.value;
                            break;
                        // 추가 가능한 효과들...
                    }
                }
            }
        }

        if (healthMultiplier > 1f || speedMultiplier > 1f || damageMultiplier > 1f)
        {
            monster.ApplyAugmentBuffs(healthMultiplier, speedMultiplier, damageMultiplier);
            // Debug.Log($"<color=cyan>[MonsterSpawner] 공격팀 증강체 [2단계] 적용: HP x{healthMultiplier:F2}, Speed x{speedMultiplier:F2}</color>");
        }
    }

    #endregion

    private float GetGroundMonsterHeightOffset(GameObject prefab, MonsterData monsterData)
    {
        if (prefab == null) return 0f;

        if (monsterData != null && monsterData.monsterType == MonsterType.Flying) return 0f;

        if (monsterData == null) return 0f;

        Collider col = prefab.GetComponent<Collider>();
        if (col != null)
        {
            float bottom = GetColliderBottomYInRootSpace(prefab.transform, col);
            return bottom < 0f ? -bottom : 0f;
        }

        return 0f;
    }

    private static float GetColliderBottomYInRootSpace(Transform root, Collider collider)
    {
        if (root == null || collider == null)
        {
            return 0f;
        }

        if (collider is BoxCollider box)
        {
            return RootLocalY(root, box.transform, box.center + Vector3.down * box.size.y * 0.5f);
        }

        if (collider is SphereCollider sphere)
        {
            return RootLocalY(root, sphere.transform, sphere.center + Vector3.down * sphere.radius);
        }

        if (collider is CapsuleCollider capsule)
        {
            float verticalHalfExtent = capsule.direction == 1
                ? Mathf.Max(capsule.radius, capsule.height * 0.5f)
                : capsule.radius;
            Vector3 localBottom = capsule.center;
            localBottom.y -= verticalHalfExtent;

            return RootLocalY(root, capsule.transform, localBottom);
        }

        return root.InverseTransformPoint(collider.bounds.min).y;
    }

    private static float RootLocalY(Transform root, Transform colliderTransform, Vector3 colliderLocalPoint)
    {
        return root.InverseTransformPoint(colliderTransform.TransformPoint(colliderLocalPoint)).y;
    }

    private static void SnapSpawnTransform(GameObject monsterGO, Vector3 position, Quaternion rotation)
    {
        if (monsterGO == null)
        {
            return;
        }

        monsterGO.transform.SetPositionAndRotation(position, rotation);

        var networkTransform = monsterGO.GetComponent<Fusion.NetworkTransform>();
        if (networkTransform != null && networkTransform.enabled)
        {
            networkTransform.Teleport(position, rotation);
        }
    }

    #region 보스 및 증강 몬스터 소환

    /// <summary>
    /// 보스 몬스터를 소환합니다. (1회성, 보스 플래그 설정)
    /// </summary>
    /// <param name="bossData">소환할 보스 데이터</param>
    /// <param name="originPlayerId">보스를 소환한 플레이어 ID (생존 시 추적용)</param>


    /// <summary>
    /// 생존 보스들을 비동기로 소환합니다. (외부 호출용)
    /// 이 플레이어를 타겟으로 하는 생존 보스 중 이번 턴에 침공하지 않은 보스만 소환합니다.
    /// 전투 시퀀스 시작 시 수비자가 호출합니다.
    /// </summary>
    public async UniTask SpawnSurvivorBossesAsync()
    {
        if (!EnsureRuntimeReferences("SpawnSurvivorBossesAsync", true))
        {
            return;
        }

        SurvivorBossManager survivorBossManager = SurvivorBossManager.Instance;
        if (survivorBossManager == null) return;
        int battleGeneration = CaptureBattleGeneration();
        if (battleGeneration < 0) return;
        if (_playerManager == null)
        {
            // Debug.LogError($"[MonsterSpawner] SpawnSurvivorBossesAsync 중단: playerManager null ({DescribeRuntimeState()})");
            return;
        }
        
        // 이 플레이어를 타겟으로 하는 생존 보스 중 이번 턴에 침공하지 않은 보스만 추출
        int targetPlayerId = _playerManager.playerId;
        var pendingBosses = survivorBossManager.ExtractBossesForBattleSequence(targetPlayerId, out int extractionLease);
        var unspawnedBosses = new List<SurvivorBossData>(pendingBosses);
        
        if (pendingBosses.Count == 0) return;
        try
        {
        
        // Debug.Log($"<color=cyan>[MonsterSpawner] Player {_playerManager.playerId}: 생존 보스 {pendingBosses.Count}마리 소환 시작</color>");
        
        foreach (var bossData in pendingBosses)
        {
            if (!IsBattleGenerationCurrent(battleGeneration)) return;
            if (bossData.BossData == null) continue;
            
            // 아우터 그리드 랜덤 위치에서 소환
            Vector3 spawnPos = GetRandomOuterGridPosition();
            
            // SpawnMonsterAtPositionAsync 사용 (경로 설정 포함)
            Monster monster = await SpawnMonsterAtPositionAsync(
                bossData.BossData,
                spawnPos,
                _playerManager.fieldManager,
                true, // isBoss
                bossData.BossUniqueId,
                bossData.OriginPlayerId,
                battleGeneration
            );

            if (!IsBattleGenerationCurrent(battleGeneration)) return;
            
            if (monster != null)
            {
                // 이전 라운드 HP 유지
                monster.SetCurrentHP(bossData.RemainingHP, bossData.MaxHP);
                unspawnedBosses.RemoveAll(boss => boss.BossUniqueId == bossData.BossUniqueId);
                // Debug.Log($"<color=red>[MonsterSpawner] 생존 보스 재소환! Player {_playerManager.playerId}에게 침공. 위치: {spawnPos}, HP: {bossData.RemainingHP:F0}/{bossData.MaxHP:F0}, ID: {bossData.BossUniqueId}</color>");
            }
            
            await UniTask.Delay(500); // 0.5초 간격
            if (!IsBattleGenerationCurrent(battleGeneration)) return;
        }
        }
        finally
        {
            if (unspawnedBosses.Count > 0 && survivorBossManager != null && SurvivorBossManager.Instance == survivorBossManager)
            {
                survivorBossManager.ReturnUnspawnedBossesForBattleSequence(
                    targetPlayerId,
                    extractionLease,
                    unspawnedBosses);
            }
        }
    }

    /// <summary>
    /// 이전 라운드에서 살아남은 보스들을 소환합니다. (전체 유저 중 랜덤 타겟)
    /// 타겟은 GameManagers에서 라운드 시작 전에 AssignTargetsToSurvivors()로 미리 할당됩니다.
    /// 턴당 1회 침공 제한: 이미 이번 턴에 침공한 보스는 제외됩니다.
    /// </summary>

    
    /// <summary>
    /// 아우터 그리드 영역(배치 불가, 스폰 가능)에서 랜덤 위치를 반환합니다.
    /// </summary>
    private Vector3 GetRandomOuterGridPosition()
    {
        EnsureRuntimeReferences("GetRandomOuterGridPosition", false);

        var field = _playerManager?.fieldManager;
        return field != null ? field.GetRandomOuterSpawnWorldPosition() : Vector3.zero;
    }

    public async UniTask SpawnBaseWaveFromFastestOuterDirectionAsync(int round, FieldManager targetFieldManager)
    {
        if (!EnsureRuntimeReferences("SpawnBaseWaveFromFastestOuterDirectionAsync", true))
        {
            return;
        }

        if (_playerManager == null || targetFieldManager == null)
        {
            return;
        }

        int battleGeneration = CaptureBattleGeneration();
        if (battleGeneration < 0)
        {
            return;
        }

        var waveDatabase = AddressablesManager.Instance?.WaveDatabase;
        var waveData = waveDatabase?.GetWaveForRound(round);
        if (waveData?.monsters == null || waveData.monsters.Count == 0)
        {
            return;
        }

        try
        {
            await PrewarmWaveAsync(
                waveData,
                $"SpawnBaseWaveFromFastestOuterDirectionAsync/R{round}",
                cancellationToken: GetBattleCancellationToken(battleGeneration));
        }
        catch (System.OperationCanceledException)
        {
            return;
        }
        if (!IsBattleGenerationCurrent(battleGeneration))
        {
            return;
        }

        Vector3 spawnPosition = GetFastestOuterDirectionSpawnPosition(targetFieldManager);
        int delayMs = Mathf.Max(100, Mathf.RoundToInt(Mathf.Max(0.1f, waveData.spawnInterval) * 1000f));

        foreach (var entry in waveData.monsters)
        {
            if (entry?.monsterData == null || entry.count <= 0)
            {
                continue;
            }

            for (int i = 0; i < entry.count; i++)
            {
                if (!IsBattleGenerationCurrent(battleGeneration))
                {
                    return;
                }

                await SpawnMonsterAtPositionAsync(
                    entry.monsterData,
                    spawnPosition,
                    targetFieldManager,
                    expectedBattleGeneration: battleGeneration);

                await UniTask.Delay(delayMs);
                if (!IsBattleGenerationCurrent(battleGeneration))
                {
                    return;
                }
            }
        }
    }

    private Vector3 GetFastestOuterDirectionSpawnPosition(FieldManager targetFieldManager)
    {
        var targetGrid = targetFieldManager?.playerManager?.astarGrid;
        var targetGoal = targetFieldManager?.playerManager?.goalTransform;
        Vector3 fallbackPosition = targetFieldManager != null
            ? targetFieldManager.GetFallbackOuterSpawnWorldPosition()
            : Vector3.zero;

        if (targetFieldManager == null || targetGrid == null || targetGoal == null)
        {
            return fallbackPosition;
        }

        var candidatesByDirection = targetFieldManager.GetOuterSpawnWorldPositionsByDirection(ResolveSpawnAreaLayerMask());

        Vector3 bestSpawnPosition = fallbackPosition;
        int shortestPathLength = int.MaxValue;
        bool foundPath = false;

        foreach (var pair in candidatesByDirection)
        {
            Vector3 directionBestPosition = Vector3.zero;
            int directionShortestPath = int.MaxValue;

            foreach (var candidate in pair.Value)
            {
                int pathLength = CalculatePathLength(targetFieldManager, targetGrid, targetGoal, candidate, false);
                if (pathLength > 0 && pathLength < directionShortestPath)
                {
                    directionShortestPath = pathLength;
                    directionBestPosition = candidate;
                }
            }

            if (directionShortestPath < shortestPathLength)
            {
                shortestPathLength = directionShortestPath;
                bestSpawnPosition = directionBestPosition;
                foundPath = true;
            }
        }

        if (foundPath)
        {
            return bestSpawnPosition;
        }

        foreach (var pair in candidatesByDirection)
        {
            if (pair.Value.Count > 0)
            {
                return pair.Value[pair.Value.Count / 2];
            }
        }

        return fallbackPosition;
    }

    private LayerMask ResolveSpawnAreaLayerMask()
    {
        var attackSequenceManager = _playerManager != null
            ? _playerManager.GetComponent<AttackSequenceManager>()
            : null;

        return attackSequenceManager != null
            ? attackSequenceManager.SpawnAreaLayer
            : default;
    }

    private Vector3 ResolveSeparatedSpawnPosition(
        Vector3 requestedPosition,
        FieldManager targetFieldManager,
        AstarGrid targetGrid,
        Transform targetGoal,
        MonsterData monsterData)
    {
        Transform targetMonsterParent = targetFieldManager?.playerManager?.monsterSpawner?.monsterParent;
        if (targetMonsterParent == null || targetFieldManager == null || targetGrid == null || targetGoal == null)
        {
            return requestedPosition;
        }

        float cellSize = Mathf.Max(0.25f, targetFieldManager.cellSize);
        float minSeparation = Mathf.Max(0.25f, cellSize * SpawnSeparationDistanceFactor);
        if (!HasLivingMonsterNear(targetMonsterParent, requestedPosition, minSeparation))
        {
            return requestedPosition;
        }

        foreach (Vector3 candidate in BuildSpawnSeparationCandidates(requestedPosition, targetFieldManager))
        {
            if (HasLivingMonsterNear(targetMonsterParent, candidate, minSeparation))
            {
                continue;
            }

            if (!HasValidSpawnPath(targetFieldManager, targetGrid, targetGoal, candidate, monsterData))
            {
                continue;
            }

            return candidate;
        }

        return requestedPosition;
    }

    private static IEnumerable<Vector3> BuildSpawnSeparationCandidates(Vector3 requestedPosition, FieldManager targetFieldManager)
    {
        if (targetFieldManager == null)
        {
            yield break;
        }

        Vector2Int center = targetFieldManager.WorldToNavigationCell(requestedPosition);
        var seen = new HashSet<Vector2Int> { center };
        for (int radius = 1; radius <= SpawnSeparationSearchRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius)
                    {
                        continue;
                    }

                    var cell = new Vector2Int(center.x + dx, center.y + dy);
                    if (!targetFieldManager.IsValidNavigationCell(cell) || !seen.Add(cell))
                    {
                        continue;
                    }

                    Vector3 candidate = targetFieldManager.NavigationCellToWorld(cell);
                    candidate.y = requestedPosition.y;
                    yield return candidate;
                }
            }
        }
    }

    private static bool HasLivingMonsterNear(Transform monsterParent, Vector3 position, float minDistance)
    {
        if (monsterParent == null)
        {
            return false;
        }

        float minDistanceSqr = minDistance * minDistance;
        foreach (Transform child in monsterParent)
        {
            if (child == null || !child.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!child.TryGetComponent<Monster>(out var monster))
            {
                continue;
            }

            if (monster.Object != null && !monster.Object.IsValid)
            {
                continue;
            }

            if (monster.CurrentHealth <= 0f)
            {
                continue;
            }

            if (FlatDistanceSqr(child.position, position) <= minDistanceSqr)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasValidSpawnPath(
        FieldManager targetFieldManager,
        AstarGrid targetGrid,
        Transform targetGoal,
        Vector3 spawnWorldPosition,
        MonsterData monsterData)
    {
        bool ignoreBreakableWalls = monsterData != null && (monsterData.traits & MonsterTraits.Destroyer) != 0;
        return CalculatePathLength(targetFieldManager, targetGrid, targetGoal, spawnWorldPosition, ignoreBreakableWalls) > 0;
    }

    private static float FlatDistanceSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private static int CalculatePathLength(
        FieldManager targetFieldManager,
        AstarGrid targetGrid,
        Transform targetGoal,
        Vector3 spawnWorldPosition,
        bool ignoreBreakableWalls)
    {
        if (targetFieldManager == null || targetGrid == null || targetGoal == null)
        {
            return -1;
        }

        Vector2Int startPos = targetFieldManager.WorldToNavigationCell(spawnWorldPosition);
        Vector2Int endPos = targetFieldManager.WorldToNavigationCell(targetGoal.position);

        if (!targetGrid.FindPath(startPos, endPos, ignoreWalls: false, ignoreBreakableWalls: ignoreBreakableWalls))
        {
            return -1;
        }

        return targetGrid.FinalPath?.Count ?? -1;
    }

    /// <summary>
    /// 상대의 일반 몬스터 소환 증강에 따라 추가 몬스터를 소환합니다.
    /// 매 라운드 이 플레이어의 상대(opponentManager)의 증강 목록을 확인합니다.
    /// </summary>


    /// <summary>
    /// 내부 몬스터 스폰 로직 (비동기). MonsterData에서 프리팹을 로드하여 소환합니다.
    /// </summary>
    /// <param name="monsterData">소환할 몬스터 데이터</param>
    /// <returns>생성된 Monster 컴포넌트</returns>


    #region AI 자동 소환 (AttackMonsterPool 사용)
    
    /// <summary>
    /// AI 공격자가 AttackMonsterPool에서 순차적으로 몬스터를 자동 소환합니다.
    /// </summary>
    /// <param name="targetFieldManager">소환할 대상 필드 (수비자 필드)</param>
    public async UniTask StartAutoSpawnFromPool(FieldManager targetFieldManager, string battleBootstrapKey = null)
    {
        bool keyAcquired = false;
        bool completed = false;
        try
        {
            if (!TryBeginAutoSpawnForKey(battleBootstrapKey, out string keyReason))
            {
                LogSpawnTrace(
                    "StartAutoSpawnFromPool:SKIP_DUPLICATE_KEY",
                    null,
                    targetFieldManager != null ? targetFieldManager.gridOrigin : Vector3.zero,
                    targetFieldManager,
                    $"key={battleBootstrapKey},reason={keyReason}");
                return;
            }
            keyAcquired = !string.IsNullOrWhiteSpace(battleBootstrapKey);

            if (!EnsureRuntimeReferences("StartAutoSpawnFromPool", true))
            {
                return;
            }

            if (_playerManager == null || targetFieldManager == null)
            {
                return;
            }

            int battleGeneration = CaptureBattleGeneration();
            if (battleGeneration < 0)
            {
                return;
            }

            var pool = _playerManager.AttackMonsterPool;
            if (pool == null || pool.Count == 0)
            {
                return;
            }

            try
            {
                await PrewarmAttackMonsterPoolAsync(
                    pool,
                    "StartAutoSpawnFromPool",
                    cancellationToken: GetBattleCancellationToken(battleGeneration));
            }
            catch (System.OperationCanceledException)
            {
                return;
            }
            if (!IsBattleGenerationCurrent(battleGeneration))
            {
                return;
            }

            // ★ AIAttackStrategy를 사용한 전략적 소환
            // spawnAreaLayerMask를 AttackSequenceManager에서 가져옴
            LayerMask spawnAreaLayer = default;
            var attackSeqMgr = _playerManager.GetComponent<AttackSequenceManager>();
            if (attackSeqMgr != null)
            {
                spawnAreaLayer = attackSeqMgr.SpawnAreaLayer;
            }

            var strategy = new AI.BehaviorTree.Nodes.Actions.AIAttackStrategy(
                targetFieldManager, _playerManager, spawnAreaLayer);
            var plan = strategy.BuildSpawnPlan(pool);

            // 계획에 따라 전략적 소환 실행
            await ExecuteSpawnPlanAsync(plan, targetFieldManager, battleGeneration);

            completed = IsBattleGenerationCurrent(battleGeneration);
        }
        finally
        {
            if (keyAcquired)
            {
                EndAutoSpawnForKey(battleBootstrapKey, completed);
            }
        }
    }

    /// <summary>
    /// AIAttackStrategy가 생성한 소환 계획을 페이즈별로 실행합니다.
    /// </summary>
    private async UniTask ExecuteSpawnPlanAsync(
        AI.BehaviorTree.Nodes.Actions.AISpawnPlan plan,
        FieldManager targetFieldManager,
        int battleGeneration)
    {
        if (plan == null || plan.Phases.Count == 0) return;

        var pool = _playerManager.AttackMonsterPool;

        foreach (var phase in plan.Phases)
        {
            if (!IsBattleGenerationCurrent(battleGeneration)) return;
            // 페이즈 시작 전 대기
            if (phase.DelayBeforePhase > 0)
            {
                await UniTask.Delay((int)(phase.DelayBeforePhase * 1000));
                if (!IsBattleGenerationCurrent(battleGeneration)) return;
            }
            if (!await WaitWhileMpTestGameFlowFrozen(battleGeneration)) return;

            foreach (var order in phase.Orders)
            {
                if (!await WaitWhileMpTestGameFlowFrozen(battleGeneration)) return;
                if (order.PoolEntry == null || order.PoolEntry.IsEmpty) continue;

                int spawnCount = Mathf.Min(order.Count, order.PoolEntry.RemainingCount);
                for (int i = 0; i < spawnCount; i++)
                {
                    if (!await WaitWhileMpTestGameFlowFrozen(battleGeneration)) return;
                    if (order.PoolEntry.IsEmpty) break;

                    int poolSlotIndex = pool.IndexOf(order.PoolEntry);
                    int defenderPlayerId = targetFieldManager?.playerManager != null
                        ? targetFieldManager.playerManager.playerId
                        : -1;
                    if (poolSlotIndex < 0 || defenderPlayerId < 0)
                    {
                        break;
                    }

                    var gm = GameManagers.Instance;
                    if (gm == null)
                    {
                        break;
                    }

                    var command = new BattleSpawnMonsterCommand(
                        _playerManager.playerId,
                        defenderPlayerId,
                        poolSlotIndex,
                        order.SpawnPosition,
                        1,
                        "server_ai_spawn_plan",
                        _playerManager.AttackMonsterPoolRevision,
                        _playerManager.BlackMagicRevision);

                    BattleCommandResult result = await gm.ExecuteBattleSpawnMonsterCommandAsync(
                        command,
                        CommandExecutionScope.ServerAuthorityOnly);
                    if (!IsBattleGenerationCurrent(battleGeneration)) return;
                    if (!result.Success)
                    {
                        break;
                    }

                    // 풀에서 직접 소비 (Find 로직 우회하여 무한루프 방지)
                    // Pool consumption is owned by BattleSpawnMonsterCommand.
                    // Pool UI sync is triggered by authoritative pool consumption.

                    // 소환 간격
                    await UniTask.Delay(300); // 0.3초 간격
                    if (!await WaitWhileMpTestGameFlowFrozen(battleGeneration)) return;
                }
            }
        }
    }

    private async UniTask<bool> WaitWhileMpTestGameFlowFrozen(int battleGeneration)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        while (MPTestCommandLine.IsGameFlowFrozen)
        {
            if (!IsBattleGenerationCurrent(battleGeneration)) return false;
            await UniTask.Yield();
        }
#else
        await UniTask.CompletedTask;
#endif
        return IsBattleGenerationCurrent(battleGeneration);
    }
    
    /// <summary>
    /// 공격자가 수비자 필드에 몬스터를 소환합니다.
    /// 기본 웨이브는 AttackMonsterPool에 포함되어 있으므로 별도 소환 불필요.
    /// AI: AttackMonsterPool에서 순차 자동 소환
    /// 유저: UI를 통해 AttackMonsterPool에서 수동 선택 소환
    /// </summary>
    /// <param name="round">현재 라운드</param>
    /// <param name="targetFieldManager">수비자 필드</param>
    /// <param name="isAI">AI 공격자 여부</param>
    public UniTask SpawnAllMonstersToTargetField(int round, FieldManager targetFieldManager, bool isAI, string battleBootstrapKey = null)
    {
        if (!EnsureRuntimeReferences("SpawnAllMonstersToTargetField", true))
        {
            return UniTask.CompletedTask;
        }

        if (_playerManager == null)
        {
            // Debug.LogError($"[MonsterSpawner] SpawnAllMonstersToTargetField 중단: playerManager null ({DescribeRuntimeState()})");
            return UniTask.CompletedTask;
        }

        if (targetFieldManager == null)
        {
            // Debug.LogError("[MonsterSpawner] SpawnAllMonstersToTargetField: targetFieldManager가 null입니다!");
            return UniTask.CompletedTask;
        }
        
        // AI attackers must go through AIPlayerController -> BattleDecisionPolicy
        // -> ServerAiCommandEmitter so real AI and HumanBot share decisions.
        if (isAI)
        {
            Debug.LogWarning("[MonsterSpawner] SpawnAllMonstersToTargetField AI bootstrap is disabled. Use BattleDecisionPolicy command emission instead.");
            return UniTask.CompletedTask;
        }

        // Human attackers use UI or HumanBot client command emission.
        return UniTask.CompletedTask;
    }
    
    #endregion

    #region 수동 몬스터 소환 (공격 시퀀스용)

    /// <summary>
    /// 지정 위치에 몬스터를 소환합니다. (공격 시퀀스에서 수동 소환용)
    /// </summary>
    /// <param name="monsterData">소환할 몬스터 데이터</param>
    /// <param name="spawnPosition">소환 위치 (월드 좌표)</param>
    /// <param name="targetFieldManager">대상 필드 매니저 (경로 설정용)</param>
    /// <param name="isBoss">보스 몬스터 여부</param>
    /// <param name="bossUniqueId">보스 고유 ID (생존 추적용)</param>
    /// <param name="originPlayerId">보스 소환자 플레이어 ID</param>
    public UniTask<Monster> SpawnMonsterAtExactPositionAsync(
        MonsterData monsterData,
        Vector3 spawnPosition,
        FieldManager targetFieldManager,
        bool isBoss = false,
        int bossUniqueId = -1,
        int originPlayerId = -1,
        int expectedBattleGeneration = -1)
    {
        return SpawnMonsterAtPositionAsync(
            monsterData,
            spawnPosition,
            targetFieldManager,
            isBoss,
            bossUniqueId,
            originPlayerId,
            expectedBattleGeneration,
            allowNearbyCellFallback: false);
    }

    public async UniTask<Monster> SpawnMonsterAtPositionAsync(
        MonsterData monsterData, 
        Vector3 spawnPosition, 
        FieldManager targetFieldManager,
        bool isBoss = false,
        int bossUniqueId = -1,
        int originPlayerId = -1,
        int expectedBattleGeneration = -1,
        bool allowNearbyCellFallback = true)
    {
        LogSpawnTrace(
            "SpawnMonsterAtPositionAsync:ENTER",
            monsterData,
            spawnPosition,
            targetFieldManager,
            $"isBoss={isBoss},bossUniqueId={bossUniqueId},originPlayerId={originPlayerId}");

        if (!EnsureRuntimeReferences("SpawnMonsterAtPositionAsync", true))
        {
            LogSpawnTrace("SpawnMonsterAtPositionAsync:ABORT_RUNTIME_REF", monsterData, spawnPosition, targetFieldManager);
            return null;
        }

        if (monsterData == null || targetFieldManager == null)
        {
            // Debug.LogError("[MonsterSpawner] SpawnMonsterAtPositionAsync 실패: monsterData 또는 targetFieldManager가 null");
            LogSpawnTrace("SpawnMonsterAtPositionAsync:ABORT_INVALID_INPUT", monsterData, spawnPosition, targetFieldManager);
            return null;
        }

        var targetGrid = targetFieldManager.playerManager?.astarGrid;
        var targetGoal = targetFieldManager.playerManager?.goalTransform;
        if (targetGrid == null || targetGoal == null)
        {
            // Debug.LogError("[MonsterSpawner] 대상 필드의 AstarGrid 또는 goalTransform이 null");
            LogSpawnTrace(
                "SpawnMonsterAtPositionAsync:ABORT_TARGET_GRID_OR_GOAL_NULL",
                monsterData,
                spawnPosition,
                targetFieldManager,
                $"targetGrid={(targetGrid != null ? targetGrid.name : "null")},targetGoal={(targetGoal != null ? targetGoal.name : "null")}");
            return null;
        }

        Vector2Int exactSpawnCell = default;
        if (!allowNearbyCellFallback)
        {
            bool exactCellValid = BattleCommandValidator.TryResolveExactBattleSpawnPosition(
                targetFieldManager,
                spawnPosition,
                out exactSpawnCell,
                out Vector3 exactSpawnPosition,
                out string exactSpawnReason);
            if (exactCellValid)
            {
                exactCellValid = BattleCommandValidator.TryValidateBattleSpawnPath(
                    targetFieldManager,
                    exactSpawnPosition,
                    monsterData,
                    out exactSpawnReason);
            }

            if (!exactCellValid)
            {
                LogSpawnTrace(
                    "SpawnMonsterAtPositionAsync:ABORT_EXACT_SPAWN_CELL",
                    monsterData,
                    spawnPosition,
                    targetFieldManager,
                    $"reason={exactSpawnReason}");
                return null;
            }

            spawnPosition = exactSpawnPosition;
        }
        else
        {
            Vector3 separatedSpawnPosition = ResolveSeparatedSpawnPosition(
                spawnPosition,
                targetFieldManager,
                targetGrid,
                targetGoal,
                monsterData);
            if ((separatedSpawnPosition - spawnPosition).sqrMagnitude > 0.0001f)
            {
                LogSpawnTrace(
                    "SpawnMonsterAtPositionAsync:SEPARATED_STACKED_SPAWN",
                    monsterData,
                    separatedSpawnPosition,
                    targetFieldManager,
                    $"requested=({spawnPosition.x:F2},{spawnPosition.y:F2},{spawnPosition.z:F2})");
                spawnPosition = separatedSpawnPosition;
            }
        }

        // 프리팹 로드
        if (string.IsNullOrEmpty(monsterData.monsterPrefab))
        {
            // Debug.LogError($"[MonsterSpawner] '{monsterData.monsterName}'의 monsterPrefab 주소가 설정되지 않았습니다!", monsterData);
            LogSpawnTrace("SpawnMonsterAtPositionAsync:ABORT_EMPTY_PREFAB_KEY", monsterData, spawnPosition, targetFieldManager);
            return null;
        }

        GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(monsterData.monsterPrefab, _addressableAssets);
        if (expectedBattleGeneration >= 0 && !IsBattleGenerationCurrent(expectedBattleGeneration))
        {
            LogSpawnTrace("SpawnMonsterAtPositionAsync:ABORT_STALE_BATTLE_GENERATION", monsterData, spawnPosition, targetFieldManager);
            return null;
        }

        if (prefab == null)
        {
            // Debug.LogError($"[MonsterSpawner] '{monsterData.monsterName}'의 프리팹 로드 실패!", monsterData);
            LogSpawnTrace("SpawnMonsterAtPositionAsync:ABORT_PREFAB_LOAD_FAIL", monsterData, spawnPosition, targetFieldManager, $"prefabKey={monsterData.monsterPrefab}");
            return null;
        }

        if (!allowNearbyCellFallback)
        {
            bool exactCellStillValid = BattleCommandValidator.TryResolveExactBattleSpawnPosition(
                targetFieldManager,
                spawnPosition,
                out Vector2Int currentExactCell,
                out Vector3 currentExactPosition,
                out string exactSpawnReason);
            if (exactCellStillValid && currentExactCell != exactSpawnCell)
            {
                exactCellStillValid = false;
                exactSpawnReason = "spawn_cell_changed_during_asset_load";
            }

            if (exactCellStillValid)
            {
                exactCellStillValid = BattleCommandValidator.TryValidateBattleSpawnPath(
                    targetFieldManager,
                    currentExactPosition,
                    monsterData,
                    out exactSpawnReason);
            }

            if (!exactCellStillValid)
            {
                LogSpawnTrace(
                    "SpawnMonsterAtPositionAsync:ABORT_EXACT_SPAWN_CELL_AFTER_LOAD",
                    monsterData,
                    spawnPosition,
                    targetFieldManager,
                    $"reason={exactSpawnReason}");
                return null;
            }

            spawnPosition = currentExactPosition;
        }

        // 지상 몬스터 높이 조정
        Vector3 adjustedSpawnPos = spawnPosition;
        if (monsterData.monsterType != MonsterType.Flying)
        {
            float groundOffset = GetGroundMonsterHeightOffset(prefab, monsterData);
            adjustedSpawnPos.y += groundOffset;
        }
        
        // 수비자(대상 필드)의 monsterParent 사용
        Transform targetMonsterParent = targetFieldManager?.playerManager?.monsterSpawner?.monsterParent;
        
        // 몬스터 생성
        GameObject monsterGO = null;
        NetworkObject spawnedNetworkObject = null;
        var runner = _playerManager?.Runner;
        if (runner != null && _playerManager.Object != null && _playerManager.Object.HasStateAuthority && prefab.TryGetComponent<NetworkObject>(out var netPrefab))
        {
            LogSpawnTrace("SpawnMonsterAtPositionAsync:SPAWN_NETWORK", monsterData, adjustedSpawnPos, targetFieldManager, $"prefab={prefab.name}");
            Quaternion spawnRotation = Quaternion.identity;
            var spawned = runner.Spawn(netPrefab, adjustedSpawnPos, spawnRotation, PlayerRef.None);
            if (spawned == null)
            {
                // Debug.LogError($"Runner.Spawn 실패: {prefab.name}", this);
                LogSpawnTrace("SpawnMonsterAtPositionAsync:ABORT_RUNNER_SPAWN_FAIL", monsterData, adjustedSpawnPos, targetFieldManager, $"prefab={prefab.name}");
                return null;
            }
            spawnedNetworkObject = spawned;
            monsterGO = spawned.gameObject;
            if (targetMonsterParent != null)
            {
                monsterGO.transform.SetParent(targetMonsterParent, true);
            }

            SnapSpawnTransform(monsterGO, adjustedSpawnPos, spawnRotation);
        }
        else if (runner != null && runner.IsRunning)
        {
            LogSpawnTrace("SpawnMonsterAtPositionAsync:ABORT_NETWORK_PREFAB_REQUIRED", monsterData, adjustedSpawnPos, targetFieldManager, $"prefab={prefab.name}");
            return null;
        }
        else
        {
            LogSpawnTrace("SpawnMonsterAtPositionAsync:SPAWN_LOCAL_INSTANTIATE", monsterData, adjustedSpawnPos, targetFieldManager, $"prefab={prefab.name}");
            monsterGO = Instantiate(prefab, adjustedSpawnPos, Quaternion.identity, targetMonsterParent);
            SnapSpawnTransform(monsterGO, adjustedSpawnPos, Quaternion.identity);
        }

        if (expectedBattleGeneration >= 0 && !IsBattleGenerationCurrent(expectedBattleGeneration))
        {
            CleanupFailedSpawn(monsterGO, spawnedNetworkObject);
            LogSpawnTrace("SpawnMonsterAtPositionAsync:ABORT_STALE_BATTLE_AFTER_SPAWN", monsterData, adjustedSpawnPos, targetFieldManager);
            return null;
        }

        Monster monster = monsterGO.GetComponent<Monster>();
        if (monster == null)
        {
            CleanupFailedSpawn(monsterGO, spawnedNetworkObject);
            return null;
        }

        // StatusBarPrefab 설정
        monster.statusBarPrefab = this.statusBarPrefab;

        // BuffManager 자동 추가
        if (monsterGO.GetComponent<BuffManager>() == null)
        {
            monsterGO.AddComponent<BuffManager>();
        }

        // 몬스터 초기화 (상대 필드 목표 사용)
        monster.Initialize(targetFieldManager.playerManager, targetGoal, monsterData, targetGrid);
        
        // 공격팀(소환자)의 몬스터 버프 증강체 효과 적용
        ApplyAttackerAugmentBuffs(monster);
        
        // 보스 플래그 설정
        if (isBoss)
        {
            int actualOriginId = originPlayerId >= 0 ? originPlayerId : _playerManager.playerId;
            monster.SetAsBoss(true, actualOriginId, bossUniqueId);
            // Debug.Log($"<color=red>[MonsterSpawner] 보스 소환! '{monsterData.monsterName}' (ID:{bossUniqueId}, Origin: Player {actualOriginId})</color>");
        }

        // RPC로 클라이언트 동기화
        if (_playerManager.Object != null)
        {
            monster.RPC_InitializeOnClient(
                targetFieldManager.playerManager.Object.Id,
                monsterData.name
            );
        }

        // 경로 설정: 스폰 위치 → 목표까지 A* 경로
        // (그리드가 확장되어 스폰 위치도 그리드 안에 있음)
        Vector2Int startPos = targetFieldManager.WorldToNavigationCell(spawnPosition);
        Vector2Int endPos = targetFieldManager.WorldToNavigationCell(targetGoal.position);

        // 파괴자 특성 확인
        bool isDestroyer = monsterData != null && (monsterData.traits & MonsterTraits.Destroyer) != 0;
        
        if (targetGrid.FindPath(startPos, endPos, ignoreWalls: false, ignoreBreakableWalls: isDestroyer))
        {
            monster.StartFollowingPath(targetGrid.FinalPath);
            
            // 버서커 모드가 활성화되어 있으면 신규 소환 몬스터에도 적용
            if (GameManagers.Instance != null && GameManagers.Instance.IsBerserkModeActive)
            {
                monster.ApplyBerserkMode();
                // Debug.Log($"<color=red>[MonsterSpawner] 신규 소환 몬스터 '{monsterData.monsterName}'에 버서커 모드 적용!</color>");
            }
        }
        else
        {
            // Debug.LogWarning($"[MonsterSpawner] 수동 소환 몬스터 경로 찾기 실패: {startPos} → {endPos}");
            LogSpawnTrace("SpawnMonsterAtPositionAsync:ABORT_PATH_FAIL", monsterData, adjustedSpawnPos, targetFieldManager, $"start={startPos},end={endPos}");
            CleanupFailedSpawn(monsterGO, spawnedNetworkObject);
            return null;
        }

        if (expectedBattleGeneration >= 0 && !IsBattleGenerationCurrent(expectedBattleGeneration))
        {
            CleanupFailedSpawn(monsterGO, spawnedNetworkObject);
            LogSpawnTrace("SpawnMonsterAtPositionAsync:ABORT_STALE_BATTLE_BEFORE_COMMIT", monsterData, adjustedSpawnPos, targetFieldManager);
            return null;
        }

        string bossTag = isBoss ? " [BOSS]" : "";
        // Debug.Log($"<color=green>[MonsterSpawner] 수동 소환: {monsterData.monsterName}{bossTag} at {spawnPosition}, 경로 시작: {startPos}</color>");
        LogSpawnTrace("SpawnMonsterAtPositionAsync:SUCCESS", monsterData, adjustedSpawnPos, targetFieldManager, $"start={startPos},end={endPos},isBoss={isBoss}");
        return monster;
    }

    private void CleanupFailedSpawn(GameObject monsterGO, NetworkObject spawnedNetworkObject)
    {
        if (spawnedNetworkObject != null && spawnedNetworkObject.IsValid)
        {
            var runner = _playerManager?.Runner;
            if (runner != null && runner.IsRunning && _playerManager.Object != null && _playerManager.Object.HasStateAuthority)
            {
                runner.Despawn(spawnedNetworkObject);
                return;
            }
        }

        if (monsterGO != null)
        {
            Destroy(monsterGO);
        }
    }

    public void CleanupCanceledBattleSpawn(Monster monster)
    {
        if (monster == null)
        {
            return;
        }

        NetworkObject networkObject = monster.Object != null && monster.Object.IsValid
            ? monster.Object
            : monster.GetComponent<NetworkObject>();
        CleanupFailedSpawn(monster.gameObject, networkObject);
    }


    #endregion

    #region 상태 확인
    /// <summary>
    /// 필드에 활성화되어 있고 생존한 몬스터가 있는지 확인합니다.
    /// (비활성화된 몬스터나 골에 도달한 몬스터는 제외)
    /// </summary>
    public bool HasLivingMonsters()
    {
        if (monsterParent == null) return false;
        
        foreach (Transform child in monsterParent)
        {
            if (child == null) continue;
            
            // 비활성화된 몬스터는 건너뛰기 (골에 도달하거나 죽은 경우)
            if (!child.gameObject.activeInHierarchy) continue;
            
            if (!child.TryGetComponent<Monster>(out var monster)) continue;
            
            // NetworkObject가 유효한 상태인지 확인 (Despawn된 몬스터 건너뛰기)
            if (monster.Object == null || !monster.Object.IsValid) continue;
            
            // HP가 0보다 크면 생존 몬스터
            if (monster.NetworkedHP > 0)
            {
                return true;
            }
        }
        return false;
    }
    #endregion

    #region 전투 종료 처리
    /// <summary>
    /// 전투 페이즈 종료 시 필드에 남은 몬스터를 정리합니다.
    /// - 일반 몬스터: 즉시 제거 (유저 HP 차감 없음)
    /// - 보스 몬스터: 생존 등록 후 제거 (다음 라운드에 재배치, HP 차감 없음)
    /// </summary>
    public void OnCombatPhaseEnded()
    {
        _activeAutoSpawnKeys.Clear();
        _completedAutoSpawnKeys.Clear();

        if (monsterParent == null) return;
        
        var monstersToRemove = new List<Monster>();
        
        // 모든 자식 몬스터 수집
        foreach (Transform child in monsterParent)
        {
            if (child.TryGetComponent<Monster>(out var monster))
            {
                monstersToRemove.Add(monster);
            }
        }
        
        if (monstersToRemove.Count == 0) return;
        
        foreach (var monster in monstersToRemove)
        {
            if (monster == null) continue;
            
            if (monster.IsBoss())
            {
                // 보스: 현재 HP로 생존 등록 → 다음 라운드에 재배치됨
                monster.RegisterAsSurvivorAndRemove();
            }
            else
            {
                // 일반 몬스터: 유저 HP 차감 없이 제거
                monster.ForceRemoveWithoutPenalty();
            }
        }
        
        // Debug.Log($"<color=yellow>[MonsterSpawner] 전투 종료 정리: {monstersToRemove.Count}마리 처리 (Player {GetPlayerIdForLog(_playerManager)})</color>");
    }
    #endregion

    /// <summary>
    /// 필드의 모든 몬스터에 폭주 모드를 적용합니다.
    /// </summary>
    public void ApplyBerserkModeToAllMonsters()
    {
        if (monsterParent == null) return;
        
        foreach (Transform child in monsterParent)
        {
            if (child.TryGetComponent<Monster>(out var monster))
            {
                monster.ApplyBerserkMode();
            }
        }
    }

    #endregion
}
