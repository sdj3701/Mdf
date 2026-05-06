// Assets/Scripts/Game/Monsters/MonsterSpawner.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Cysharp.Threading.Tasks;

public class MonsterSpawner : MonoBehaviour
{

    #region 필드 및 참조

    private PlayerManager _playerManager;
    private AstarGrid _pathfinder;
    private float _lastRuntimeResolveLogTime;
    private static int _spawnTraceSeq;

    [Header("스폰 설정 (자동 할당됨)")]
    [SerializeField] private Transform goalTransform;
    public GameObject statusBarPrefab;

    [Header("정리용 부모 오브젝트")]
    public Transform monsterParent;
    
    private readonly HashSet<string> _activeAutoSpawnKeys = new HashSet<string>();
    private readonly HashSet<string> _completedAutoSpawnKeys = new HashSet<string>();

    // _isSpawningWave 제거됨 (CS0414 - 사용되지 않음)

    #endregion

    #region 초기화

    /// <summary>
    /// MonsterSpawner를 초기화합니다.
    /// </summary>
    /// <param name="owner">소유 플레이어</param>
    /// <param name="grid">경로 탐색용 그리드</param>
    /// <param name="goalTransform">목표 위치</param>
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
            : $"owner=Player({_playerManager.playerId}, name={_playerManager.name})";
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
        bool hasAuthority = hasObject && player.Object.HasStateAuthority;
        string gridName = player.astarGrid != null ? player.astarGrid.name : "null";
        string goalName = player.goalTransform != null ? player.goalTransform.name : "null";
        string fieldName = player.fieldManager != null ? player.fieldManager.name : "null";

        return $"{label}=Player(id={player.playerId},name={player.name},active={player.isActiveAndEnabled}," +
               $"hasObject={hasObject},objectValid={objectValid},stateAuth={hasAuthority}," +
               $"grid={gridName},goal={goalName},field={fieldName})";
    }

    private string DescribeFieldState(FieldManager field, string label)
    {
        if (field == null)
        {
            return $"{label}=null";
        }

        string ownerId = field.playerManager != null ? field.playerManager.playerId.ToString() : "null";
        string groundName = field.ground3D != null ? field.ground3D.name : "null";
        return $"{label}=Field(name={field.name},active={field.isActiveAndEnabled},owner={ownerId},ground={groundName})";
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
            if (augment.targetType == TargetType.Opponent)
            {
                switch(augment.effectType)
                {
                    case EffectType.IncreaseEnemyHealth:
                        healthMultiplier += augment.value;
                        break;
                    case EffectType.IncreaseEnemyMoveSpeed:
                        speedMultiplier += augment.value;
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
            // 상대 필드에 적용되는 몬스터 강화 증강체
            if (augment.targetType == TargetType.Opponent)
            {
                switch(augment.effectType)
                {
                    case EffectType.IncreaseEnemyHealth:
                        healthMultiplier += augment.value;
                        break;
                    case EffectType.IncreaseEnemyMoveSpeed:
                        speedMultiplier += augment.value;
                        break;
                    // 추가 가능한 효과들...
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

    private float GetGroundMonsterHeightOffset(GameObject prefab)
    {
        if (prefab == null) return 0f;

        Monster monsterComponent = prefab.GetComponent<Monster>();
        if (monsterComponent == null || monsterComponent.Data == null) return 0f;

        // 비행 몬스터는 높이 오프셋 없음
        if (monsterComponent.Data.monsterType == MonsterType.Flying) return 0f;

        // Collider bounds를 사용하여 정확한 높이 계산
        Collider col = prefab.GetComponent<Collider>();
        if (col != null)
        {
            // bounds.extents.y는 collider 중심에서 바닥까지의 거리
            return col.bounds.extents.y;
        }

        // Collider가 없으면 localScale 기반 폴백
        return prefab.transform.localScale.y * 0.5f;
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

        if (SurvivorBossManager.Instance == null) return;
        if (_playerManager == null)
        {
            // Debug.LogError($"[MonsterSpawner] SpawnSurvivorBossesAsync 중단: playerManager null ({DescribeRuntimeState()})");
            return;
        }
        
        // 이 플레이어를 타겟으로 하는 생존 보스 중 이번 턴에 침공하지 않은 보스만 추출
        var pendingBosses = SurvivorBossManager.Instance.ExtractBossesForBattleSequence(_playerManager.playerId);
        
        if (pendingBosses.Count == 0) return;
        
        // Debug.Log($"<color=cyan>[MonsterSpawner] Player {_playerManager.playerId}: 생존 보스 {pendingBosses.Count}마리 소환 시작</color>");
        
        foreach (var bossData in pendingBosses)
        {
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
                bossData.OriginPlayerId
            );
            
            if (monster != null)
            {
                // 이전 라운드 HP 유지
                monster.SetCurrentHP(bossData.RemainingHP, bossData.MaxHP);
                // Debug.Log($"<color=red>[MonsterSpawner] 생존 보스 재소환! Player {_playerManager.playerId}에게 침공. 위치: {spawnPos}, HP: {bossData.RemainingHP:F0}/{bossData.MaxHP:F0}, ID: {bossData.BossUniqueId}</color>");
            }
            
            await UniTask.Delay(500); // 0.5초 간격
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

        var waveDatabase = AddressablesManager.Instance?.WaveDatabase;
        var waveData = waveDatabase?.GetWaveForRound(round);
        if (waveData?.monsters == null || waveData.monsters.Count == 0)
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
                await SpawnMonsterAtPositionAsync(
                    entry.monsterData,
                    spawnPosition,
                    targetFieldManager);

                await UniTask.Delay(delayMs);
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

            var pool = _playerManager.AttackMonsterPool;
            if (pool == null || pool.Count == 0)
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
            await ExecuteSpawnPlanAsync(plan, targetFieldManager);

            completed = true;
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
    private async UniTask ExecuteSpawnPlanAsync(AI.BehaviorTree.Nodes.Actions.AISpawnPlan plan, FieldManager targetFieldManager)
    {
        if (plan == null || plan.Phases.Count == 0) return;

        var pool = _playerManager.AttackMonsterPool;

        foreach (var phase in plan.Phases)
        {
            // 페이즈 시작 전 대기
            if (phase.DelayBeforePhase > 0)
            {
                await UniTask.Delay((int)(phase.DelayBeforePhase * 1000));
            }
            await WaitWhileMpTestGameFlowFrozen();

            foreach (var order in phase.Orders)
            {
                await WaitWhileMpTestGameFlowFrozen();
                if (order.PoolEntry == null || order.PoolEntry.IsEmpty) continue;

                int spawnCount = Mathf.Min(order.Count, order.PoolEntry.RemainingCount);
                for (int i = 0; i < spawnCount; i++)
                {
                    await WaitWhileMpTestGameFlowFrozen();
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
                        _playerManager.AttackMonsterPoolRevision);

                    BattleCommandResult result = await gm.ExecuteBattleSpawnMonsterCommandAsync(
                        command,
                        CommandExecutionScope.ServerAuthorityOnly);
                    if (!result.Success)
                    {
                        break;
                    }

                    // 풀에서 직접 소비 (Find 로직 우회하여 무한루프 방지)
                    // Pool consumption is owned by BattleSpawnMonsterCommand.
                    // Pool UI sync is triggered by authoritative pool consumption.

                    // 소환 간격
                    await UniTask.Delay(300); // 0.3초 간격
                    await WaitWhileMpTestGameFlowFrozen();
                }
            }
        }
    }

    private static async UniTask WaitWhileMpTestGameFlowFrozen()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        while (MPTestCommandLine.IsGameFlowFrozen)
        {
            await UniTask.Yield();
        }
#else
        await UniTask.CompletedTask;
#endif
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
    public async UniTask SpawnAllMonstersToTargetField(int round, FieldManager targetFieldManager, bool isAI, string battleBootstrapKey = null)
    {
        if (!EnsureRuntimeReferences("SpawnAllMonstersToTargetField", true))
        {
            return;
        }

        if (_playerManager == null)
        {
            // Debug.LogError($"[MonsterSpawner] SpawnAllMonstersToTargetField 중단: playerManager null ({DescribeRuntimeState()})");
            return;
        }

        if (targetFieldManager == null)
        {
            // Debug.LogError("[MonsterSpawner] SpawnAllMonstersToTargetField: targetFieldManager가 null입니다!");
            return;
        }
        
        // AI만 AttackMonsterPool에서 자동 소환
        // 유저는 UI를 통해 수동 소환 (기존 로직 유지)
        if (isAI)
        {
            // Debug.Log($"<color=cyan>[MonsterSpawner] AI 공격자: AttackMonsterPool 자동 소환 시작</color>");
            await StartAutoSpawnFromPool(targetFieldManager, battleBootstrapKey);
        }
        else
        {
            // 유저 공격자: AttackMonsterPool은 UI를 통해 수동 선택
            // 카메라/UI 처리는 RPC_NotifyBattleStart에서 각 클라이언트가 처리
            // Debug.Log($"<color=green>[MonsterSpawner] 유저 공격자: AttackMonsterPool 수동 소환 대기</color>");
        }
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
    public async UniTask<Monster> SpawnMonsterAtPositionAsync(
        MonsterData monsterData, 
        Vector3 spawnPosition, 
        FieldManager targetFieldManager,
        bool isBoss = false,
        int bossUniqueId = -1,
        int originPlayerId = -1)
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

        // 프리팹 로드
        if (string.IsNullOrEmpty(monsterData.monsterPrefab))
        {
            // Debug.LogError($"[MonsterSpawner] '{monsterData.monsterName}'의 monsterPrefab 주소가 설정되지 않았습니다!", monsterData);
            LogSpawnTrace("SpawnMonsterAtPositionAsync:ABORT_EMPTY_PREFAB_KEY", monsterData, spawnPosition, targetFieldManager);
            return null;
        }

        GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(monsterData.monsterPrefab);
        if (prefab == null)
        {
            // Debug.LogError($"[MonsterSpawner] '{monsterData.monsterName}'의 프리팹 로드 실패!", monsterData);
            LogSpawnTrace("SpawnMonsterAtPositionAsync:ABORT_PREFAB_LOAD_FAIL", monsterData, spawnPosition, targetFieldManager, $"prefabKey={monsterData.monsterPrefab}");
            return null;
        }

        // 지상 몬스터 높이 조정
        Vector3 adjustedSpawnPos = spawnPosition;
        if (monsterData.monsterType != MonsterType.Flying)
        {
            float groundOffset = GetGroundMonsterHeightOffset(prefab);
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
            var spawned = runner.Spawn(netPrefab, adjustedSpawnPos, Quaternion.identity, PlayerRef.None);
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

        // 상대 필드의 목표 지점을 사용하여 초기화
        var targetGrid = targetFieldManager.playerManager?.astarGrid;
        var targetGoal = targetFieldManager.playerManager?.goalTransform;
        
        if (targetGrid == null || targetGoal == null)
        {
            // Debug.LogError("[MonsterSpawner] 대상 필드의 AstarGrid 또는 goalTransform이 null");
            LogSpawnTrace(
                "SpawnMonsterAtPositionAsync:ABORT_TARGET_GRID_OR_GOAL_NULL",
                monsterData,
                adjustedSpawnPos,
                targetFieldManager,
                $"targetGrid={(targetGrid != null ? targetGrid.name : "null")},targetGoal={(targetGoal != null ? targetGoal.name : "null")}");
            // Debug.LogWarning($"[SPAWN-TRACE] {BuildAllPlayersSnapshot()}");
            // Debug.LogWarning($"[SPAWN-TRACE] {BuildAllFieldsSnapshot()}");
            CleanupFailedSpawn(monsterGO, spawnedNetworkObject);
            return null;
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
        
        // Debug.Log($"<color=yellow>[MonsterSpawner] 전투 종료 정리: {monstersToRemove.Count}마리 처리 (Player {_playerManager?.playerId})</color>");
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
