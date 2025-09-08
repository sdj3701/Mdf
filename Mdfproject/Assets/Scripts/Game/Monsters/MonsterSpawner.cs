// Assets/Scripts/Game/Monsters/MonsterSpawner.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

        if (GameManagers.Instance.GetGameState() != GameManagers.GameState.Combat)
        {
            if (playerManager.IsActivelyFighting)
            {
                playerManager.SetFightingState(false);
            }
            return;
        }

        if (!isSpawningWave && monsterParent.childCount == 0)
        {
            if (playerManager.IsActivelyFighting)
            {
                playerManager.SetFightingState(false);
            }
        }
    }

    public void SpawnWave(int round)
    {
        int monsterCount = 5 + round;
        StartCoroutine(SpawnMonsterCoroutine(monsterCount));
    }

    IEnumerator SpawnMonsterCoroutine(int count)
    {
        if (pathfinder == null || monsterPrefab == null)
        {
            Debug.LogError("MonsterSpawner에 AstarGrid 또는 monsterPrefab이 연결되지 않았습니다!", this);
            yield break;
        }

        isSpawningWave = true;
        playerManager.SetFightingState(true);

        Monster monsterComponentInPrefab = monsterPrefab.GetComponent<Monster>();
        if (monsterComponentInPrefab == null || monsterComponentInPrefab.monsterData == null)
        {
            Debug.LogError($"'{monsterPrefab.name}' 프리팹에 Monster 컴포넌트나 MonsterData가 없습니다!", monsterPrefab);
            isSpawningWave = false;
            playerManager.SetFightingState(false);
            yield break;
        }
        MonsterData dataToSpawn = monsterComponentInPrefab.monsterData;
        
        for (int i = 0; i < count; i++)
        {
            if (spawnPoint == null || goalTransform == null)
            {
                 Debug.LogError("스폰 포인트 또는 목표 지점이 할당되지 않았습니다!", this);
                 isSpawningWave = false;
                 playerManager.SetFightingState(false);
                 yield break;
            }
            
            GameObject monsterGO = Instantiate(monsterPrefab, spawnPoint.position, Quaternion.identity, monsterParent);
            Monster monster = monsterGO.GetComponent<Monster>();

            if (monster != null)
            {
                if (statusBarPrefab != null)
                {
                    GameObject statusBarGO = Instantiate(statusBarPrefab, monsterGO.transform);
                    monster.SetStatusBar(statusBarGO.GetComponent<StatusBarUI>());
                }

                // [수정] 몬스터에게 올바른 AstarGrid 인스턴스를 직접 전달합니다.
                monster.Initialize(this.playerManager, this.goalTransform, dataToSpawn, this.pathfinder);
                ApplyOpponentDebuffs(monster);

                Vector2Int startPos = new Vector2Int(Mathf.RoundToInt(spawnPoint.position.x), Mathf.RoundToInt(spawnPoint.position.y));
                Vector2Int endPos = new Vector2Int(Mathf.RoundToInt(goalTransform.position.x), Mathf.RoundToInt(goalTransform.position.y));
                
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
        
        isSpawningWave = false;
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
        
        GameObject monsterGO = Instantiate(monsterPrefabToSpawn, spawnPoint.position, Quaternion.identity, monsterParent);
        Monster monster = monsterGO.GetComponent<Monster>();

        if (monster != null)
        {
            if (statusBarPrefab != null)
            {
                GameObject statusBarGO = Instantiate(statusBarPrefab, monsterGO.transform);
                monster.SetStatusBar(statusBarGO.GetComponent<StatusBarUI>());
            }
            // [수정] 몬스터에게 올바른 AstarGrid 인스턴스를 직접 전달합니다.
            monster.Initialize(this.playerManager, this.goalTransform, dataToSpawn, this.pathfinder);
            
            // [수정] startPos의 y좌표가 goalTransform을 잘못 참조하던 버그를 수정합니다.
            Vector2Int startPos = new Vector2Int(Mathf.RoundToInt(spawnPoint.position.x), Mathf.RoundToInt(spawnPoint.position.y));
            Vector2Int endPos = new Vector2Int(Mathf.RoundToInt(goalTransform.position.x), Mathf.RoundToInt(goalTransform.position.y));

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
}
