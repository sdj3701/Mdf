// Assets/Scripts/Game/Monsters/MonsterSpawner.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class MonsterSpawner : MonoBehaviour
{
    private PlayerManager playerManager;
    private AstarGrid pathfinder;

    // ✅ [수정] 모든 public 참조를 private으로 변경
    [Header("스폰 설정 (자동 할당됨)")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform goalTransform;
    [SerializeField] private GameObject monsterPrefab;
    public GameObject statusBarPrefab;

    [Header("정리용 부모 오브젝트")]
    public Transform monsterParent;

    private bool isSpawningWave = false;

    // ✅ [수정된 최종 로직] 필요한 모든 참조를 전달받습니다.
   public void Initialize(PlayerManager owner, AstarGrid grid, GameObject monsterPrefab, Transform spawnPoint, Transform goalTransform)
    {
        this.playerManager = owner;
        this.pathfinder = grid;
        this.monsterPrefab = monsterPrefab;
        this.spawnPoint = spawnPoint;
        this.goalTransform = goalTransform;

        // ✅ [진단 코드] 최종적으로 할당된 참조들이 null인지 확인
        string ownerName = owner != null ? owner.name : "NULL";
        Debug.Log($"MonsterSpawner for '{ownerName}' 초기화 완료. " +
                  $"AstarGrid: {(grid != null)}, " +
                  $"monsterPrefab: {(monsterPrefab != null)}, " +
                  $"spawnPoint: {(spawnPoint != null)}, " +
                  $"goalTransform: {(goalTransform != null)}");

        if (monsterParent == null)
        {
            GameObject parentObject = new GameObject($"[{playerManager.name} Monsters]");
            parentObject.transform.SetParent(transform.parent);
            monsterParent = parentObject.transform;
        }
    }

    // ... (이하 나머지 코드는 이전과 동일) ...
    void Update()
    {
        if (playerManager == null || GameManagers.Instance == null) return;
        
        // 서버에서만 전투 상태 업데이트 (Networked 속성은 StateAuthority만 변경 가능)
        if (playerManager.Object == null || !playerManager.Object.HasStateAuthority) return;

        if (GameManagers.Instance.GetGameState() != GameManagers.GameState.Combat)
        {
            if (playerManager.IsActivelyFighting)
            {
                playerManager.SetFightingState(false);
                Debug.Log($"<color=yellow>[MonsterSpawner] Player {playerManager.playerId}: 전투 상태 비전투 (GameState != Combat)</color>");
            }
            return;
        }

        // 디버그: 현재 몬스터 상태 확인
        int monsterCount = monsterParent != null ? monsterParent.childCount : -1;
        
        if (!isSpawningWave && monsterCount == 0)
        {
            if (playerManager.IsActivelyFighting)
            {
                playerManager.SetFightingState(false);
                Debug.Log($"<color=green>[MonsterSpawner] Player {playerManager.playerId}: 전투 종료! (monsterCount={monsterCount}, isSpawningWave={isSpawningWave})</color>");
            }
        }
    }

    public void SpawnWave(int round)
    {
        int monsterCount = 5 + round;
        StartCoroutine(SpawnAllMonstersCoroutine(round, monsterCount));
    }

    /// <summary>
    /// 기본 웨이브 → 대기 중인 보스 → 생존 보스 → 증강 몬스터 순으로 모두 간격 두고 소환
    /// </summary>
    IEnumerator SpawnAllMonstersCoroutine(int round, int baseCount)
    {
        if (pathfinder == null || monsterPrefab == null)
        {
            Debug.LogError("MonsterSpawner에 AstarGrid 또는 monsterPrefab이 연결되지 않았습니다!", this);
            yield break;
        }

        isSpawningWave = true;
        playerManager.SetFightingState(true);

        // 1. 기본 웨이브 몬스터 소환
        yield return StartCoroutine(SpawnBaseWaveCoroutine(baseCount));

        // 2. 대기 중인 보스 증강 소환 (1회성, 상대의 증강에서 온 보스)
        yield return StartCoroutine(SpawnPendingBossesCoroutine());

        // 3. 이전 라운드에서 살아남은 보스 소환 (전체 유저 중 랜덤 타겟)
        yield return StartCoroutine(SpawnSurvivorBossesCoroutine());

        // 4. 상대의 일반 몬스터 소환 증강에 의한 추가 몬스터 소환
        yield return StartCoroutine(SpawnAugmentMonstersCoroutine());

        isSpawningWave = false;
    }

    /// <summary>
    /// 기본 웨이브 몬스터 소환 (간격: 0.5초)
    /// </summary>
    IEnumerator SpawnBaseWaveCoroutine(int count)
    {
        Monster monsterComponentInPrefab = monsterPrefab.GetComponent<Monster>();
        if (monsterComponentInPrefab == null || monsterComponentInPrefab.monsterData == null)
        {
            Debug.LogError($"'{monsterPrefab.name}' 프리팹에 Monster 컴포넌트나 MonsterData가 없습니다!", monsterPrefab);
            yield break;
        }
        MonsterData dataToSpawn = monsterComponentInPrefab.monsterData;

        for (int i = 0; i < count; i++)
        {
            if (spawnPoint == null || goalTransform == null)
            {
                 Debug.LogError("스폰 포인트 또는 목표 지점이 할당되지 않았습니다!", this);
                 yield break;
            }

            Vector3 spawnPos = spawnPoint.position;
            float groundOffset = GetGroundMonsterHeightOffset(monsterPrefab);
            spawnPos.y += groundOffset;

            GameObject monsterGO = null;
            var runner = playerManager != null ? playerManager.Runner : null;
            if (runner != null && playerManager.Object.HasStateAuthority && monsterPrefab.TryGetComponent<NetworkObject>(out var netPrefab))
            {
                var spawned = runner.Spawn(netPrefab, spawnPos, Quaternion.identity, PlayerRef.None);
                if (spawned == null)
                {
                    Debug.LogError($"Runner.Spawn 실패: {monsterPrefab.name}", this);
                    yield break;
                }
                monsterGO = spawned.gameObject;
                if (monsterParent != null)
                {
                    monsterGO.transform.SetParent(monsterParent, true);
                }
            }
            else
            {
                monsterGO = Instantiate(monsterPrefab, spawnPos, Quaternion.identity, monsterParent);
            }
            Monster monster = monsterGO.GetComponent<Monster>();

            if (monster != null)
            {
                // StatusBarPrefab을 Monster에 설정
                monster.statusBarPrefab = this.statusBarPrefab;

                // 서버에서 몬스터 초기화
                monster.Initialize(this.playerManager, this.goalTransform, dataToSpawn, this.pathfinder);
                ApplyOpponentDebuffs(monster);
                
                // 클라이언트에도 초기화 데이터 전송 (RPC)
                if (playerManager.Object != null)
                {
                    monster.RPC_InitializeOnClient(
                        playerManager.Object.Id,
                        dataToSpawn != null ? dataToSpawn.name : ""
                    );
                }

                // [3D Migration] 경계 밖 스폰/목표가 유령셀을 만들지 않도록, 그리드로 클램프 후 셀 변환
                Vector3 clampedSpawn = pathfinder.ClampToGrid(spawnPoint.position);
                Vector3 clampedGoal = pathfinder.ClampToGrid(goalTransform.position);
                Vector2Int startPos = pathfinder.WorldToCell(clampedSpawn);
                Vector2Int endPos = pathfinder.WorldToCell(clampedGoal);

                if (pathfinder.FindPath(startPos, endPos))
                {
                    List<AstarNode> path = pathfinder.FinalPath;
                    monster.StartFollowingPath(path);
                }
                else
                {
                    Debug.LogWarning($"{monsterGO.name}을(를) 위한 경로를 찾지 못했습니다.");
                    Destroy(monsterGO);
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    private void ApplyOpponentDebuffs(Monster monster)
    {
        if (playerManager.opponentManager == null) return;

        float healthMultiplier = 1f;
        float speedMultiplier = 1f;

        foreach (var augment in playerManager.opponentManager.chosenAugments)
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

    public void SpawnSpecificMonster(GameObject monsterPrefabToSpawn)
    {
        if (pathfinder == null || monsterPrefabToSpawn == null)
        {
            Debug.LogError("MonsterSpawner에 AstarGrid 또는 특정 몬스터 프리팹이 없습니다!", this);
            return;
        }

        Monster monsterComponentInPrefab = monsterPrefabToSpawn.GetComponent<Monster>();
        if (monsterComponentInPrefab == null || monsterComponentInPrefab.monsterData == null)
        {
            Debug.LogError($"'{monsterPrefabToSpawn.name}' 프리팹에 Monster 컴포넌트나 MonsterData가 없습니다!", monsterPrefabToSpawn);
            return;
        }
        MonsterData dataToSpawn = monsterComponentInPrefab.monsterData;

        Debug.Log($"<color=red>보스 몬스터 소환!</color> {dataToSpawn.monsterName} at Player {playerManager.playerId}'s field");

        Vector3 spawnPos = spawnPoint.position;
        float groundOffset = GetGroundMonsterHeightOffset(monsterPrefabToSpawn);
        spawnPos.y += groundOffset;

        GameObject monsterGO = null;
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null && playerManager.Object.HasStateAuthority && monsterPrefabToSpawn.TryGetComponent<NetworkObject>(out var netPrefab))
        {
            var spawned = runner.Spawn(netPrefab, spawnPos, Quaternion.identity, PlayerRef.None);
            if (spawned == null)
            {
                Debug.LogError($"Runner.Spawn 실패: {monsterPrefabToSpawn.name}", this);
                return;
            }
            monsterGO = spawned.gameObject;
            if (monsterParent != null)
            {
                monsterGO.transform.SetParent(monsterParent, true);
            }
        }
        else
        {
            monsterGO = Instantiate(monsterPrefabToSpawn, spawnPos, Quaternion.identity, monsterParent);
        }
        Monster monster = monsterGO.GetComponent<Monster>();

        if (monster != null)
        {
            // StatusBarPrefab을 Monster에 설정
            monster.statusBarPrefab = this.statusBarPrefab;
            
            // 서버에서 몬스터 초기화
            monster.Initialize(this.playerManager, this.goalTransform, dataToSpawn, this.pathfinder);
            
            // 클라이언트에도 초기화 데이터 전송 (RPC)
            if (playerManager.Object != null)
            {
                monster.RPC_InitializeOnClient(
                    playerManager.Object.Id,
                    dataToSpawn != null ? dataToSpawn.name : ""
                );
            }

            // [3D Migration] position.y → position.z
            Vector2Int startPos = new Vector2Int(Mathf.FloorToInt(spawnPoint.position.x), Mathf.FloorToInt(spawnPoint.position.z));
            Vector2Int endPos = new Vector2Int(Mathf.FloorToInt(goalTransform.position.x), Mathf.FloorToInt(goalTransform.position.z));

            if (pathfinder.FindPath(startPos, endPos))
            {
                List<AstarNode> path = pathfinder.FinalPath;
                monster.StartFollowingPath(path);
            }
            else
            {
                Debug.LogWarning($"{monsterGO.name}을(를) 위한 경로를 찾지 못했습니다.");
                Destroy(monsterGO);
            }
        }
    }

    private float GetGroundMonsterHeightOffset(GameObject prefab)
    {
        if (prefab == null) return 0f;

        Monster monsterComponent = prefab.GetComponent<Monster>();
        if (monsterComponent == null || monsterComponent.monsterData == null) return 0f;

        if (monsterComponent.monsterData.monsterType == MonsterType.Flying) return 0f;

        return prefab.transform.localScale.y * 0.5f;
    }

    #region 보스 및 증강 몬스터 소환

    /// <summary>
    /// 보스 몬스터를 소환합니다. (1회성, 보스 플래그 설정)
    /// </summary>
    /// <param name="bossPrefab">소환할 보스 프리팹</param>
    /// <param name="originPlayerId">보스를 소환한 플레이어 ID (생존 시 추적용)</param>
    public void SpawnBossMonster(GameObject bossPrefab, int originPlayerId)
    {
        if (pathfinder == null || bossPrefab == null)
        {
            Debug.LogError("[MonsterSpawner] 보스 소환 실패: pathfinder 또는 bossPrefab이 null");
            return;
        }

        Monster monster = SpawnMonsterInternal(bossPrefab);
        if (monster != null)
        {
            monster.SetAsBoss(true, originPlayerId, bossPrefab);
            Debug.Log($"<color=red>[MonsterSpawner] 보스 '{monster.name}' 소환 완료! (OriginPlayer: {originPlayerId})</color>");
        }
    }

    /// <summary>
    /// 대기 중인 보스 증강을 코루틴으로 소환합니다. (1회성, 상대의 증강에서 온 보스)
    /// 모든 플레이어를 확인하여 이 플레이어를 타겟으로 한 보스를 소환합니다.
    /// </summary>
    IEnumerator SpawnPendingBossesCoroutine()
    {
        // 모든 플레이어의 대기 중인 보스 증강 확인
        var allPlayers = GameManagers.Instance?.AllPlayers;
        if (allPlayers == null) yield break;

        foreach (var sourcePlayer in allPlayers)
        {
            if (sourcePlayer == null) continue;
            
            var pendingBosses = sourcePlayer.GetAndClearPendingBossAugments();
            foreach (var pending in pendingBosses)
            {
                // 이 플레이어가 타겟인 경우에만 소환
                if (pending.TargetPlayerId != playerManager.playerId) continue;
                if (pending.Augment?.bossPrefab == null) continue;

                Monster monster = SpawnMonsterInternal(pending.Augment.bossPrefab);
                if (monster != null)
                {
                    monster.SetAsBoss(true, sourcePlayer.playerId, pending.Augment.bossPrefab);
                    Debug.Log($"<color=red>[MonsterSpawner] 보스 '{monster.name}' 소환! (소환자: Player {sourcePlayer.playerId} → 타겟: Player {playerManager.playerId})</color>");
                }

                yield return new WaitForSeconds(0.5f);
            }
        }
    }

    /// <summary>
    /// 이전 라운드에서 살아남은 보스들을 소환합니다. (전체 유저 중 랜덤 타겟)
    /// </summary>
    IEnumerator SpawnSurvivorBossesCoroutine()
    {
        if (SurvivorBossManager.Instance == null || !SurvivorBossManager.Instance.HasPendingBosses())
        {
            yield break;
        }

        var pendingBosses = SurvivorBossManager.Instance.GetPendingBossesWithTargets();

        foreach (var (targetPlayerId, bossData) in pendingBosses)
        {
            // 이 플레이어가 타겟인 경우에만 소환
            if (targetPlayerId != playerManager.playerId) continue;

            Monster monster = SpawnMonsterInternal(bossData.BossPrefab);
            if (monster != null)
            {
                monster.SetAsBoss(true, bossData.OriginPlayerId, bossData.BossPrefab);
                monster.SetCurrentHP(bossData.RemainingHP, bossData.MaxHP);
                Debug.Log($"<color=red>[MonsterSpawner] 생존 보스 재소환! Player {targetPlayerId}에게 침공. HP: {bossData.RemainingHP:F0}/{bossData.MaxHP:F0}</color>");
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
        if (playerManager.opponentManager == null) yield break;

        // 상대(opponentManager)가 등록한 일반 몬스터 소환 증강들을 가져옴
        var augments = playerManager.opponentManager.GetActiveMonsterSummonAugments();

        foreach (var augment in augments)
        {
            if (augment.monsterSpawnEntries == null || augment.monsterSpawnEntries.Count == 0) continue;

            int totalSpawned = 0;
            
            // 각 MonsterSpawnEntry의 프리팹을 count만큼 소환
            foreach (var entry in augment.monsterSpawnEntries)
            {
                if (entry == null || entry.prefab == null) continue;
                
                int spawnCount = Mathf.Max(0, entry.count);
                for (int i = 0; i < spawnCount; i++)
                {
                    SpawnMonsterInternal(entry.prefab);
                    totalSpawned++;
                    yield return new WaitForSeconds(0.5f);
                }
            }

            Debug.Log($"<color=orange>[MonsterSpawner] 증강 '{augment.augmentName}'에 의해 Player {playerManager.playerId}에게 추가 몬스터 {totalSpawned}마리 소환</color>");
        }
    }

    /// <summary>
    /// 내부 몬스터 스폰 로직 (공통화). 기존 SpawnSpecificMonster 로직 재사용.
    /// </summary>
    /// <param name="prefab">소환할 몬스터 프리팹</param>
    /// <param name="overrideHP">잔여 HP 오버라이드 (생존 보스용, -1이면 무시)</param>
    /// <param name="overrideMaxHP">최대 HP 오버라이드 (생존 보스용, -1이면 무시)</param>
    /// <returns>생성된 Monster 컴포넌트</returns>
    private Monster SpawnMonsterInternal(GameObject prefab, float overrideHP = -1f, float overrideMaxHP = -1f)
    {
        if (pathfinder == null || prefab == null) return null;

        Monster monsterComponentInPrefab = prefab.GetComponent<Monster>();
        if (monsterComponentInPrefab == null || monsterComponentInPrefab.monsterData == null)
        {
            Debug.LogError($"'{prefab.name}' 프리팹에 Monster 컴포넌트나 MonsterData가 없습니다!", prefab);
            return null;
        }
        MonsterData dataToSpawn = monsterComponentInPrefab.monsterData;

        Vector3 spawnPos = spawnPoint.position;
        float groundOffset = GetGroundMonsterHeightOffset(prefab);
        spawnPos.y += groundOffset;

        GameObject monsterGO = null;
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null && playerManager.Object.HasStateAuthority && prefab.TryGetComponent<NetworkObject>(out var netPrefab))
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

        // StatusBarPrefab 설정
        monster.statusBarPrefab = this.statusBarPrefab;

        // 몬스터 초기화
        monster.Initialize(this.playerManager, this.goalTransform, dataToSpawn, this.pathfinder);
        ApplyOpponentDebuffs(monster);

        // 클라이언트에도 초기화 데이터 전송 (RPC)
        if (playerManager.Object != null)
        {
            monster.RPC_InitializeOnClient(
                playerManager.Object.Id,
                dataToSpawn != null ? dataToSpawn.name : ""
            );
        }

        // 경로 설정
        Vector3 clampedSpawn = pathfinder.ClampToGrid(spawnPoint.position);
        Vector3 clampedGoal = pathfinder.ClampToGrid(goalTransform.position);
        Vector2Int startPos = pathfinder.WorldToCell(clampedSpawn);
        Vector2Int endPos = pathfinder.WorldToCell(clampedGoal);

        if (pathfinder.FindPath(startPos, endPos))
        {
            List<AstarNode> path = pathfinder.FinalPath;
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

    #endregion
}
