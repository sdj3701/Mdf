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
                Debug.Log($"<color=yellow>[MonsterSpawner] Player {_playerManager.playerId}: 전투 상태 비전투 (GameState != Battle)</color>");
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
    /// 증강 몬스터 없이 기본 웨이브만 소환합니다. (상대 없는 플레이어의 수비 시퀀스용)
    /// AI가 순서대로 자동 소환합니다.
    /// </summary>
    /// <param name="round">라운드 번호</param>
    public void SpawnWaveWithoutAugments(int round)
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

        StartCoroutine(SpawnBaseWaveOnlyCoroutine(round, waveData));
    }

    /// <summary>
    /// 기본 웨이브만 소환하는 코루틴 (증강 몬스터, 보스 제외)
    /// </summary>
    IEnumerator SpawnBaseWaveOnlyCoroutine(int round, RoundWaveData waveData)
    {
        if (_pathfinder == null)
        {
            Debug.LogError("[MonsterSpawner] AstarGrid가 연결되지 않았습니다!", this);
            yield break;
        }

        _isSpawningWave = true;
        _playerManager.SetFightingState(true);

        int totalMonsters = waveData.GetTotalMonsterCount();
        Debug.Log($"<color=gray>[MonsterSpawner] 라운드 {round} 기본 웨이브만 소환 (상대 없음): 총 {totalMonsters}마리</color>");

        // 기본 웨이브 몬스터만 소환 (증강, 보스 제외)
        yield return StartCoroutine(SpawnBaseWaveFromDataCoroutine(round, waveData));

        _isSpawningWave = false;
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

        // 2. 이전 라운드에서 살아남은 보스 소환 (전체 유저 중 랜덤 타겟)
        yield return StartCoroutine(SpawnSurvivorBossesCoroutine());

        // 3. 상대의 일반 몬스터 소환 증강에 의한 추가 몬스터 소환
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
    /// 생존 보스들을 비동기로 소환합니다. (외부 호출용)
    /// 이 플레이어를 타겟으로 하는 생존 보스 중 이번 턴에 침공하지 않은 보스만 소환합니다.
    /// 전투 시퀀스 시작 시 수비자가 호출합니다.
    /// </summary>
    public async UniTask SpawnSurvivorBossesAsync()
    {
        if (SurvivorBossManager.Instance == null) return;
        
        // 이 플레이어를 타겟으로 하는 생존 보스 중 이번 턴에 침공하지 않은 보스만 추출
        var pendingBosses = SurvivorBossManager.Instance.ExtractBossesForBattleSequence(_playerManager.playerId);
        
        if (pendingBosses.Count == 0) return;
        
        Debug.Log($"<color=cyan>[MonsterSpawner] Player {_playerManager.playerId}: 생존 보스 {pendingBosses.Count}마리 소환 시작</color>");
        
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
                Debug.Log($"<color=red>[MonsterSpawner] 생존 보스 재소환! Player {_playerManager.playerId}에게 침공. 위치: {spawnPos}, HP: {bossData.RemainingHP:F0}/{bossData.MaxHP:F0}, ID: {bossData.BossUniqueId}</color>");
            }
            
            await UniTask.Delay(500); // 0.5초 간격
        }
    }

    /// <summary>
    /// 이전 라운드에서 살아남은 보스들을 소환합니다. (전체 유저 중 랜덤 타겟)
    /// 타겟은 GameManagers에서 라운드 시작 전에 AssignTargetsToSurvivors()로 미리 할당됩니다.
    /// 턴당 1회 침공 제한: 이미 이번 턴에 침공한 보스는 제외됩니다.
    /// </summary>
    IEnumerator SpawnSurvivorBossesCoroutine()
    {
        if (SurvivorBossManager.Instance == null)
        {
            yield break;
        }

        // 이 플레이어를 타겟으로 하는 생존 보스 중 이번 턴에 침공하지 않은 보스만 추출
        var pendingBosses = SurvivorBossManager.Instance.ExtractBossesForBattleSequence(_playerManager.playerId);

        foreach (var bossData in pendingBosses)
        {
            if (bossData.BossData == null) continue;

            // 아우터 그리드 랜덤 위치에서 소환
            Vector3 spawnPos = GetRandomOuterGridPosition();
            
            // SpawnMonsterAtPositionAsync 사용 (경로 설정 포함)
            var spawnTask = SpawnMonsterAtPositionAsync(
                bossData.BossData,
                spawnPos,
                _playerManager.fieldManager,
                true, // isBoss
                bossData.BossUniqueId,
                bossData.OriginPlayerId
            );
            yield return new WaitUntil(() => spawnTask.Status.IsCompleted());
            
            Monster monster = spawnTask.GetAwaiter().GetResult();
            if (monster != null)
            {
                // 이전 라운드 HP 유지
                monster.SetCurrentHP(bossData.RemainingHP, bossData.MaxHP);
                Debug.Log($"<color=red>[MonsterSpawner] 생존 보스 재소환! Player {_playerManager.playerId}에게 침공. 위치: {spawnPos}, HP: {bossData.RemainingHP:F0}/{bossData.MaxHP:F0}, ID: {bossData.BossUniqueId}</color>");
            }

            yield return new WaitForSeconds(0.5f);
        }
    }
    
    /// <summary>
    /// 아우터 그리드 영역(배치 불가, 스폰 가능)에서 랜덤 위치를 반환합니다.
    /// </summary>
    private Vector3 GetRandomOuterGridPosition()
    {
        var field = _playerManager?.fieldManager;
        if (field == null)
        {
            // 폴백: 기본 스폰 포인트
            return _playerManager?.spawnPoint?.position ?? Vector3.zero;
        }
        
        int margin = field.OuterGridMargin;
        Vector2Int gridSize = field.gridSize;
        Vector3 gridOrigin = field.gridOrigin;
        float cellSize = field.cellSize;
        
        // 아우터 그리드 영역 정의 (그리드 바깥쪽 셀들)
        // 4개 구역: 위쪽, 아래쪽, 왼쪽, 오른쪽
        List<Vector2Int> outerCells = new List<Vector2Int>();
        
        // 위쪽 (y = gridSize.y ~ gridSize.y + margin - 1)
        for (int y = gridSize.y; y < gridSize.y + margin; y++)
        {
            for (int x = -margin; x < gridSize.x + margin; x++)
            {
                outerCells.Add(new Vector2Int(x, y));
            }
        }
        
        // 아래쪽 (y = -margin ~ -1)
        for (int y = -margin; y < 0; y++)
        {
            for (int x = -margin; x < gridSize.x + margin; x++)
            {
                outerCells.Add(new Vector2Int(x, y));
            }
        }
        
        // 왼쪽 (x = -margin ~ -1, 중간 y만)
        for (int y = 0; y < gridSize.y; y++)
        {
            for (int x = -margin; x < 0; x++)
            {
                outerCells.Add(new Vector2Int(x, y));
            }
        }
        
        // 오른쪽 (x = gridSize.x ~ gridSize.x + margin - 1, 중간 y만)
        for (int y = 0; y < gridSize.y; y++)
        {
            for (int x = gridSize.x; x < gridSize.x + margin; x++)
            {
                outerCells.Add(new Vector2Int(x, y));
            }
        }
        
        if (outerCells.Count == 0)
        {
            // 폴백: 기본 스폰 포인트
            return _playerManager?.spawnPoint?.position ?? Vector3.zero;
        }
        
        // 랜덤 셀 선택
        Vector2Int randomCell = outerCells[Random.Range(0, outerCells.Count)];
        
        // 월드 좌표로 변환 (셀 중심)
        float worldX = gridOrigin.x + (randomCell.x + 0.5f) * cellSize;
        float worldZ = gridOrigin.z + (randomCell.y + 0.5f) * cellSize;
        
        return new Vector3(worldX, gridOrigin.y, worldZ);
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

        // 지상 몬스터의 경우 높이 조정 (BoxCollider 밑면이 스폰포인트에 닿도록)
        if (monsterData.monsterType != MonsterType.Flying)
        {
            BoxCollider boxCol = monsterGO.GetComponent<BoxCollider>();
            if (boxCol != null)
            {
                // BoxCollider의 로컬 밑면 오프셋: center.y - size.y/2
                // 밑면을 스폰포인트에 맞추려면 이 값을 빼줘야 함
                float localBottomY = boxCol.center.y - boxCol.size.y * 0.5f;
                Vector3 pos = monsterGO.transform.position;
                pos.y = spawnPoint.position.y - localBottomY * monsterGO.transform.localScale.y;
                monsterGO.transform.position = pos;
            }
            else
            {
                // BoxCollider가 없으면 일반 Collider 사용 (폴백)
                Collider col = monsterGO.GetComponent<Collider>();
                if (col != null)
                {
                    Vector3 pos = monsterGO.transform.position;
                    pos.y = spawnPoint.position.y + col.bounds.extents.y;
                    monsterGO.transform.position = pos;
                }
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

    #region AI 자동 소환 (AttackMonsterPool 사용)
    
    /// <summary>
    /// AI 공격자가 AttackMonsterPool에서 순차적으로 몬스터를 자동 소환합니다.
    /// </summary>
    /// <param name="targetFieldManager">소환할 대상 필드 (수비자 필드)</param>
    public async UniTask StartAutoSpawnFromPool(FieldManager targetFieldManager)
    {
        if (_playerManager == null || targetFieldManager == null)
        {
            Debug.LogError("[MonsterSpawner] AI 자동 소환 실패: PlayerManager 또는 targetFieldManager가 null");
            return;
        }

        var pool = _playerManager.AttackMonsterPool;
        if (pool == null || pool.Count == 0)
        {
            Debug.LogWarning("[MonsterSpawner] AI 자동 소환: 몬스터 풀이 비어있음");
            return;
        }

        Debug.Log($"<color=orange>[MonsterSpawner] AI 자동 소환 시작: {pool.Count}종류의 몬스터</color>");

        // 스폰 포인트 위치 (수비자 필드의 스폰 포인트)
        Vector3 spawnPosition = targetFieldManager.playerManager?.spawnPoint?.position ?? 
                                targetFieldManager.gridOrigin;

        // 풀에 있는 모든 몬스터를 순차적으로 소환
        foreach (var entry in pool)
        {
            while (!entry.IsEmpty)
            {
                // 몬스터 소환 (보스 플래그 포함)
                await SpawnMonsterAtPositionAsync(
                    entry.MonsterData, 
                    spawnPosition, 
                    targetFieldManager,
                    entry.IsBoss,
                    entry.BossUniqueId,
                    entry.OriginPlayerId
                );
                
                // 풀에서 직접 소비 (Find 로직 우회하여 무한루프 방지)
                entry.TryConsume();
                GameEvents.TriggerMonsterPoolChanged(_playerManager.playerId, pool);
                
                // 소환 간격
                await UniTask.Delay(300); // 0.3초 간격
            }
        }

        Debug.Log($"<color=orange>[MonsterSpawner] AI 자동 소환 완료</color>");
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
        if (monsterData == null || targetFieldManager == null)
        {
            Debug.LogError("[MonsterSpawner] SpawnMonsterAtPositionAsync 실패: monsterData 또는 targetFieldManager가 null");
            return null;
        }

        // 프리팹 로드
        if (string.IsNullOrEmpty(monsterData.monsterPrefab))
        {
            Debug.LogError($"[MonsterSpawner] '{monsterData.monsterName}'의 monsterPrefab 주소가 설정되지 않았습니다!", monsterData);
            return null;
        }

        GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(monsterData.monsterPrefab);
        if (prefab == null)
        {
            Debug.LogError($"[MonsterSpawner] '{monsterData.monsterName}'의 프리팹 로드 실패!", monsterData);
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
        var runner = _playerManager?.Runner;
        if (runner != null && _playerManager.Object.HasStateAuthority && prefab.TryGetComponent<NetworkObject>(out var netPrefab))
        {
            var spawned = runner.Spawn(netPrefab, adjustedSpawnPos, Quaternion.identity, PlayerRef.None);
            if (spawned == null)
            {
                Debug.LogError($"Runner.Spawn 실패: {prefab.name}", this);
                return null;
            }
            monsterGO = spawned.gameObject;
            if (targetMonsterParent != null)
            {
                monsterGO.transform.SetParent(targetMonsterParent, true);
            }
        }
        else
        {
            monsterGO = Instantiate(prefab, adjustedSpawnPos, Quaternion.identity, targetMonsterParent);
        }

        Monster monster = monsterGO.GetComponent<Monster>();
        if (monster == null) return null;

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
            Debug.LogError("[MonsterSpawner] 대상 필드의 AstarGrid 또는 goalTransform이 null");
            Destroy(monsterGO);
            return null;
        }

        // 몬스터 초기화 (상대 필드 목표 사용)
        monster.Initialize(targetFieldManager.playerManager, targetGoal, monsterData, targetGrid);
        
        // 보스 플래그 설정
        if (isBoss)
        {
            int actualOriginId = originPlayerId >= 0 ? originPlayerId : _playerManager.playerId;
            monster.SetAsBoss(true, actualOriginId, bossUniqueId);
            Debug.Log($"<color=red>[MonsterSpawner] 보스 소환! '{monsterData.monsterName}' (ID:{bossUniqueId}, Origin: Player {actualOriginId})</color>");
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
        Vector2Int startPos = targetGrid.WorldToCell(targetGrid.ClampToGrid(spawnPosition));
        Vector3 clampedGoal = targetGrid.ClampToGrid(targetGoal.position);
        Vector2Int endPos = targetGrid.WorldToCell(clampedGoal);

        if (targetGrid.FindPath(startPos, endPos))
        {
            monster.StartFollowingPath(targetGrid.FinalPath);
            
            // 버서커 모드가 활성화되어 있으면 신규 소환 몬스터에도 적용
            if (GameManagers.Instance != null && GameManagers.Instance.IsBerserkModeActive)
            {
                monster.ApplyBerserkMode();
                Debug.Log($"<color=red>[MonsterSpawner] 신규 소환 몬스터 '{monsterData.monsterName}'에 버서커 모드 적용!</color>");
            }
        }
        else
        {
            Debug.LogWarning($"[MonsterSpawner] 수동 소환 몬스터 경로 찾기 실패: {startPos} → {endPos}");
            Destroy(monsterGO);
            return null;
        }

        string bossTag = isBoss ? " [BOSS]" : "";
        Debug.Log($"<color=green>[MonsterSpawner] 수동 소환: {monsterData.monsterName}{bossTag} at {spawnPosition}, 경로 시작: {startPos}</color>");
        return monster;
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
