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
    private WaveDatabase _waveDatabase;

    [Header("스폰 설정 (자동 할당됨)")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform goalTransform;
    public GameObject statusBarPrefab;

    [Header("정리용 부모 오브젝트")]
    public Transform monsterParent;

    private bool _isSpawningWave = false;

    #endregion

    #region 초기화

    /// <summary>
    /// MonsterSpawner를 초기화합니다.
    /// </summary>
    /// <param name="owner">소유 플레이어</param>
    /// <param name="grid">경로 탐색용 그리드</param>
    /// <param name="waveDatabase">웨이브 데이터베이스</param>
    /// <param name="spawnPoint">스폰 위치</param>
    /// <param name="goalTransform">목표 위치</param>
    public void Initialize(PlayerManager owner, AstarGrid grid, WaveDatabase waveDatabase, Transform spawnPoint, Transform goalTransform)
    {
        _playerManager = owner;
        _pathfinder = grid;
        _waveDatabase = waveDatabase;
        this.spawnPoint = spawnPoint;
        this.goalTransform = goalTransform;

        string ownerName = owner != null ? owner.name : "NULL";
        Debug.Log($"[MonsterSpawner] '{ownerName}' 초기화 완료. " +
                  $"AstarGrid: {(grid != null)}, " +
                  $"WaveDatabase: {(waveDatabase != null)}, " +
                  $"spawnPoint: {(spawnPoint != null)}, " +
                  $"goalTransform: {(goalTransform != null)}");

        if (monsterParent == null)
        {
            GameObject parentObject = new GameObject($"[{_playerManager.name} Monsters]");
            parentObject.transform.SetParent(transform.parent);
            monsterParent = parentObject.transform;
        }
    }

    #endregion

    #region Update (전투 상태 체크)

    void Update()
    {
        if (_playerManager == null || GameManagers.Instance == null) return;
        
        // 서버에서만 전투 상태 업데이트 (Networked 속성은 StateAuthority만 변경 가능)
        if (_playerManager.Object == null || !_playerManager.Object.HasStateAuthority) return;

        if (GameManagers.Instance.GetGameState() != GameManagers.GameState.Combat)
        {
            if (_playerManager.IsActivelyFighting)
            {
                _playerManager.SetFightingState(false);
                Debug.Log($"<color=yellow>[MonsterSpawner] Player {_playerManager.playerId}: 전투 상태 비전투 (GameState != Combat)</color>");
            }
            return;
        }

        int monsterCount = monsterParent != null ? monsterParent.childCount : -1;
        
        if (!_isSpawningWave && monsterCount == 0)
        {
            if (_playerManager.IsActivelyFighting)
            {
                _playerManager.SetFightingState(false);
                Debug.Log($"<color=green>[MonsterSpawner] Player {_playerManager.playerId}: 전투 종료! (monsterCount={monsterCount})</color>");
            }
        }
    }

    #endregion

    #region 웨이브 소환

    /// <summary>
    /// 특정 라운드의 웨이브를 소환합니다.
    /// </summary>
    /// <param name="round">라운드 번호</param>
    public void SpawnWave(int round)
    {
        if (_waveDatabase == null)
        {
            Debug.LogError("[MonsterSpawner] WaveDatabase가 설정되지 않았습니다!", this);
            return;
        }

        RoundWaveData waveData = _waveDatabase.GetWaveForRound(round);
        if (waveData == null)
        {
            Debug.LogError($"[MonsterSpawner] 라운드 {round}의 웨이브 데이터를 찾을 수 없습니다!", this);
            return;
        }

        StartCoroutine(SpawnAllMonstersCoroutine(round, waveData));
    }

    /// <summary>
    /// 기본 웨이브 → 대기 중인 보스 → 생존 보스 → 증강 몬스터 순으로 모두 간격 두고 소환
    /// </summary>
    IEnumerator SpawnAllMonstersCoroutine(int round, RoundWaveData waveData)
    {
        if (_pathfinder == null)
        {
            Debug.LogError("[MonsterSpawner] AstarGrid가 연결되지 않았습니다!", this);
            yield break;
        }

        _isSpawningWave = true;
        _playerManager.SetFightingState(true);

        int totalMonsters = waveData.GetTotalMonsterCount();
        
        // 자동 스케일링 정보 로그
        float autoHealthScale = _waveDatabase.GetHealthScaleForRound(round);
        float autoSpeedScale = _waveDatabase.GetSpeedScaleForRound(round);
        Debug.Log($"<color=cyan>[MonsterSpawner] 라운드 {round} 웨이브 시작: 총 {totalMonsters}마리 (자동 스케일링: 체력 ×{autoHealthScale:F2}, 속도 ×{autoSpeedScale:F2})</color>");

        // 1. 기본 웨이브 몬스터 소환 (WaveDatabase 기반 + 자동 스케일링)
        yield return StartCoroutine(SpawnBaseWaveFromDataCoroutine(round, waveData));

        // 2. 대기 중인 보스 증강 소환 (1회성, 상대의 증강에서 온 보스)
        yield return StartCoroutine(SpawnPendingBossesCoroutine());

        // 3. 이전 라운드에서 살아남은 보스 소환 (전체 유저 중 랜덤 타겟)
        yield return StartCoroutine(SpawnSurvivorBossesCoroutine());

        // 4. 상대의 일반 몬스터 소환 증강에 의한 추가 몬스터 소환
        yield return StartCoroutine(SpawnAugmentMonstersCoroutine());

        _isSpawningWave = false;
    }

    /// <summary>
    /// WaveDatabase의 RoundWaveData를 기반으로 몬스터를 소환합니다.
    /// </summary>
    /// <param name="round">현재 라운드 (자동 스케일링 계산용)</param>
    /// <param name="waveData">웨이브 데이터</param>
    IEnumerator SpawnBaseWaveFromDataCoroutine(int round, RoundWaveData waveData)
    {
        if (waveData.monsters == null || waveData.monsters.Count == 0)
        {
            Debug.LogWarning("[MonsterSpawner] 웨이브에 몬스터가 정의되지 않았습니다.");
            yield break;
        }

        // 자동 스케일링 배율 계산
        float autoHealthScale = _waveDatabase.GetHealthScaleForRound(round);
        float autoSpeedScale = _waveDatabase.GetSpeedScaleForRound(round);
        float autoDamageScale = _waveDatabase.GetDamageScaleForRound(round);

        foreach (var entry in waveData.monsters)
        {
            if (entry == null || entry.monsterData == null)
            {
                Debug.LogWarning("[MonsterSpawner] 웨이브 엔트리가 null이거나 MonsterData가 없습니다.");
                continue;
            }

            for (int i = 0; i < entry.count; i++)
            {
                var spawnTask = SpawnMonsterInternalAsync(entry.monsterData);
                yield return new WaitUntil(() => spawnTask.Status.IsCompleted());
                
                Monster monster = spawnTask.GetAwaiter().GetResult();
                
                if (monster != null)
                {
                    // 자동 스케일링 배율 적용 (1.0이 아닌 경우에만)
                    if (autoHealthScale != 1f || autoSpeedScale != 1f || autoDamageScale != 1f)
                    {
                        monster.ApplyBuff(autoHealthScale, autoSpeedScale, autoDamageScale);
                    }
                    
                    // 상대 증강에 의한 디버프 적용
                    ApplyOpponentDebuffs(monster);
                }

                yield return new WaitForSeconds(waveData.spawnInterval);
            }
        }
    }

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
            monster.ApplyBuff(healthMultiplier, speedMultiplier);
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
    public async void SpawnBossMonsterAsync(MonsterData bossData, int originPlayerId)
    {
        if (_pathfinder == null || bossData == null)
        {
            Debug.LogError("[MonsterSpawner] 보스 소환 실패: _pathfinder 또는 bossData가 null");
            return;
        }

        Monster monster = await SpawnMonsterInternalAsync(bossData);
        if (monster != null)
        {
            monster.SetAsBoss(true, originPlayerId);
            Debug.Log($"<color=red>[MonsterSpawner] 보스 '{monster.name}' 소환 완료! (OriginPlayer: {originPlayerId})</color>");
        }
    }

    /// <summary>
    /// 대기 중인 보스 증강을 코루틴으로 소환합니다. (1회성, 상대의 증강에서 온 보스)
    /// 모든 플레이어를 확인하여 이 플레이어를 타겟으로 한 보스만 추출하여 소환합니다.
    /// </summary>
    IEnumerator SpawnPendingBossesCoroutine()
    {
        // 모든 플레이어의 대기 중인 보스 증강 확인
        var allPlayers = GameManagers.Instance?.AllPlayers;
        if (allPlayers == null) yield break;

        foreach (var sourcePlayer in allPlayers)
        {
            if (sourcePlayer == null) continue;
            
            // 이 플레이어(타겟)에 해당하는 보스만 추출 (다른 플레이어의 데이터는 건드리지 않음)
            var pendingBosses = sourcePlayer.ExtractPendingBossesForTarget(_playerManager.playerId);
            foreach (var pending in pendingBosses)
            {
                if (pending.Augment?.bossMonsterData == null) continue;

                var spawnTask = SpawnMonsterInternalAsync(pending.Augment.bossMonsterData);
                yield return new WaitUntil(() => spawnTask.Status.IsCompleted());
                
                Monster monster = spawnTask.GetAwaiter().GetResult();
                if (monster != null)
                {
                    monster.SetAsBoss(true, sourcePlayer.playerId);
                    Debug.Log($"<color=red>[MonsterSpawner] 보스 '{monster.name}' 소환! (소환자: Player {sourcePlayer.playerId} → 타겟: Player {_playerManager.playerId})</color>");
                }

                yield return new WaitForSeconds(0.5f);
            }
        }
    }

    /// <summary>
    /// 이전 라운드에서 살아남은 보스들을 소환합니다. (전체 유저 중 랜덤 타겟)
    /// 타겟은 GameManagers에서 라운드 시작 전에 AssignTargetsToSurvivors()로 미리 할당됩니다.
    /// </summary>
    IEnumerator SpawnSurvivorBossesCoroutine()
    {
        if (SurvivorBossManager.Instance == null)
        {
            yield break;
        }

        // 이 플레이어를 타겟으로 하는 생존 보스만 추출 (다른 플레이어의 데이터는 건드리지 않음)
        var pendingBosses = SurvivorBossManager.Instance.ExtractBossesForTarget(_playerManager.playerId);

        foreach (var bossData in pendingBosses)
        {
            if (bossData.BossData == null) continue;

            var spawnTask = SpawnMonsterInternalAsync(bossData.BossData);
            yield return new WaitUntil(() => spawnTask.Status.IsCompleted());
            
            Monster monster = spawnTask.GetAwaiter().GetResult();
            if (monster != null)
            {
                monster.SetAsBoss(true, bossData.OriginPlayerId);
                monster.SetCurrentHP(bossData.RemainingHP, bossData.MaxHP);
                Debug.Log($"<color=red>[MonsterSpawner] 생존 보스 재소환! Player {_playerManager.playerId}에게 침공. HP: {bossData.RemainingHP:F0}/{bossData.MaxHP:F0}</color>");
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    /// <summary>
    /// 상대의 일반 몬스터 소환 증강에 따라 추가 몬스터를 소환합니다.
    /// 매 라운드 이 플레이어의 상대(opponentManager)의 증강 목록을 확인합니다.
    /// </summary>
    IEnumerator SpawnAugmentMonstersCoroutine()
    {
        if (_playerManager.opponentManager == null) yield break;

        // 상대(opponentManager)가 등록한 일반 몬스터 소환 증강들을 가져옴
        var augments = _playerManager.opponentManager.GetActiveMonsterSummonAugments();

        foreach (var augment in augments)
        {
            if (augment.monsterSpawnEntries == null || augment.monsterSpawnEntries.Count == 0) continue;

            int totalSpawned = 0;
            
            // 각 MonsterSpawnEntry의 MonsterData를 count만큼 소환
            foreach (var entry in augment.monsterSpawnEntries)
            {
                if (entry == null || entry.monsterData == null) continue;
                
                int spawnCount = Mathf.Max(0, entry.count);
                for (int i = 0; i < spawnCount; i++)
                {
                    var spawnTask = SpawnMonsterInternalAsync(entry.monsterData);
                    yield return new WaitUntil(() => spawnTask.Status.IsCompleted());
                    spawnTask.GetAwaiter().GetResult();
                    totalSpawned++;
                    yield return new WaitForSeconds(0.5f);
                }
            }

            Debug.Log($"<color=orange>[MonsterSpawner] 증강 '{augment.augmentName}'에 의해 Player {_playerManager.playerId}에게 추가 몬스터 {totalSpawned}마리 소환</color>");
        }
    }

    /// <summary>
    /// 내부 몬스터 스폰 로직 (비동기). MonsterData에서 프리팹을 로드하여 소환합니다.
    /// </summary>
    /// <param name="monsterData">소환할 몬스터 데이터</param>
    /// <returns>생성된 Monster 컴포넌트</returns>
    private async UniTask<Monster> SpawnMonsterInternalAsync(MonsterData monsterData)
    {
        if (_pathfinder == null || monsterData == null) return null;
        
        // MonsterData에서 프리팹 Addressable 키를 가져와 로드
        if (string.IsNullOrEmpty(monsterData.monsterPrefab))
        {
            Debug.LogError($"[MonsterSpawner] '{monsterData.monsterName}'의 monsterPrefab 주소가 설정되지 않았습니다!", monsterData);
            return null;
        }
        
        GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(monsterData.monsterPrefab);
        if (prefab == null)
        {
            Debug.LogError($"[MonsterSpawner] '{monsterData.monsterName}'의 프리팹 로드 실패! (주소: {monsterData.monsterPrefab})", monsterData);
            return null;
        }

        Vector3 spawnPos = spawnPoint.position;
        float groundOffset = GetGroundMonsterHeightOffset(prefab);
        spawnPos.y += groundOffset;

        GameObject monsterGO = null;
        var runner = _playerManager != null ? _playerManager.Runner : null;
        if (runner != null && _playerManager.Object.HasStateAuthority && prefab.TryGetComponent<NetworkObject>(out var netPrefab))
        {
            var spawned = runner.Spawn(netPrefab, spawnPos, Quaternion.identity, PlayerRef.None);
            if (spawned == null)
            {
                Debug.LogError($"Runner.Spawn 실패: {prefab.name}", this);
                return null;
            }
            monsterGO = spawned.gameObject;
            if (monsterParent != null)
            {
                monsterGO.transform.SetParent(monsterParent, true);
            }
        }
        else
        {
            monsterGO = Instantiate(prefab, spawnPos, Quaternion.identity, monsterParent);
        }

        Monster monster = monsterGO.GetComponent<Monster>();
        if (monster == null) return null;

        // 지상 몬스터의 경우 높이 조정 (Collider bounds 기반)
        if (monsterData.monsterType != MonsterType.Flying)
        {
            Collider col = monsterGO.GetComponent<Collider>();
            if (col != null)
            {
                Vector3 pos = monsterGO.transform.position;
                pos.y = spawnPoint.position.y + col.bounds.extents.y;
                monsterGO.transform.position = pos;
            }
        }

        // StatusBarPrefab 설정
        monster.statusBarPrefab = this.statusBarPrefab;

        // BuffManager가 없으면 자동 추가 (디버프 시스템 지원)
        if (monsterGO.GetComponent<BuffManager>() == null)
        {
            monsterGO.AddComponent<BuffManager>();
        }

        // 몬스터 초기화
        monster.Initialize(_playerManager, this.goalTransform, monsterData, _pathfinder);

        // 클라이언트에도 초기화 데이터 전송 (RPC)
        if (_playerManager.Object != null)
        {
            monster.RPC_InitializeOnClient(
                _playerManager.Object.Id,
                monsterData != null ? monsterData.name : ""
            );
        }

        // 경로 설정
        Vector3 clampedSpawn = _pathfinder.ClampToGrid(spawnPoint.position);
        Vector3 clampedGoal = _pathfinder.ClampToGrid(goalTransform.position);
        Vector2Int startPos = _pathfinder.WorldToCell(clampedSpawn);
        Vector2Int endPos = _pathfinder.WorldToCell(clampedGoal);

        if (_pathfinder.FindPath(startPos, endPos))
        {
            List<AstarNode> path = _pathfinder.FinalPath;
            monster.StartFollowingPath(path);
        }
        else
        {
            Debug.LogWarning($"{monsterGO.name}을(를) 위한 경로를 찾지 못했습니다.");
            Destroy(monsterGO);
            return null;
        }

        return monster;
    }

    /// <summary>
    /// 전투 페이즈 종료 시 필드에 남은 몬스터를 정리합니다.
    /// - 일반 몬스터: 즉시 제거 (유저 HP 차감 없음)
    /// - 보스 몬스터: 생존 등록 후 제거 (다음 라운드에 재배치, HP 차감 없음)
    /// </summary>
    public void OnCombatPhaseEnded()
    {
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
        
        Debug.Log($"<color=yellow>[MonsterSpawner] 전투 종료 정리: {monstersToRemove.Count}마리 처리 (Player {_playerManager?.playerId})</color>");
    }

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
