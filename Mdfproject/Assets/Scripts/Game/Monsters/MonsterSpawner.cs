// Assets/Scripts/Game/Monsters/MonsterSpawner.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MonsterSpawner : MonoBehaviour
{
    public PlayerManager playerManager;

    [Header("스폰 설정")]
    public Transform spawnPoint;
    public Transform goalTransform;
    [Tooltip("스폰할 몬스터의 프리팹입니다. 이 프리팹에는 Monster 컴포넌트와 MonsterData 에셋이 연결되어 있어야 합니다.")]
    public GameObject monsterPrefab;

    [Header("정리용 부모 오브젝트")]
    [Tooltip("생성된 몬스터들이 이 오브젝트의 자식으로 들어갑니다.")]
    public Transform monsterParent;

    [Header("참조")]
    public AstarGrid pathfinder;

    // ✅ [추가] 현재 몬스터를 스폰하는 중인지 여부를 추적하는 변수입니다.
    private bool isSpawningWave = false;

    void Awake()
    {
        if (monsterParent == null)
        {
            GameObject parentObject = new GameObject($"[{playerManager.name} Monsters]");
            parentObject.transform.SetParent(transform.parent);
            monsterParent = parentObject.transform;
        }
    }

    // ✅ [추가] Update 메서드를 추가하여 전투 상태를 실시간으로 확인합니다.
    void Update()
    {
        if (playerManager == null || GameManagers.Instance == null) return;

        // Combat 단계가 아니면 무조건 싸우는 상태가 아닙니다.
        if (GameManagers.Instance.GetGameState() != GameManagers.GameState.Combat)
        {
            // 전투가 끝났거나 준비 단계일 때 상태를 확실히 false로 설정합니다.
            if (playerManager.IsActivelyFighting)
            {
                playerManager.SetFightingState(false);
            }
            return;
        }

        // Combat 단계일 때, 플레이어의 전투가 끝났는지 확인합니다.
        // 조건: 1. 웨이브 스폰이 끝났고, 2. 필드 위에 몬스터가 한 마리도 없다.
        if (!isSpawningWave && monsterParent.childCount == 0)
        {
            // 전투가 끝났으므로 상태를 false로 변경합니다.
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

        // ✅ [추가] 스폰을 시작했으므로 상태를 true로 설정합니다.
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
                monster.Initialize(this.playerManager, this.goalTransform, dataToSpawn);
                ApplyOpponentDebuffs(monster);

                Vector2Int startPos = new Vector2Int(Mathf.RoundToInt(spawnPoint.position.x), Mathf.RoundToInt(spawnPoint.position.y));
                Vector2Int endPos = new Vector2Int(Mathf.RoundToInt(goalTransform.position.x), Mathf.RoundToInt(goalTransform.position.y));
                List<AstarNode> path = pathfinder.FindPath(startPos, endPos);

                if (path != null && path.Count > 0)
                {
                    monster.StartFollowingPath(path);
                }
                else
                {
                    Debug.LogWarning($"{monsterGO.name}을(를) 위한 경로를 찾지 못했습니다.");
                }
            }
            
            yield return new WaitForSeconds(0.5f);
        }
        
        // ✅ [추가] 모든 몬스터의 스폰이 끝났으므로 상태를 false로 변경합니다.
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
            monster.Initialize(this.playerManager, this.goalTransform, dataToSpawn);
            
            Vector2Int startPos = new Vector2Int(Mathf.RoundToInt(spawnPoint.position.x), Mathf.RoundToInt(spawnPoint.position.y));
            Vector2Int endPos = new Vector2Int(Mathf.RoundToInt(goalTransform.position.x), Mathf.RoundToInt(goalTransform.position.y));
            List<AstarNode> path = pathfinder.FindPath(startPos, endPos);

            if (path != null && path.Count > 0)
            {
                monster.StartFollowingPath(path);
            }
            else
            {
                Debug.LogWarning($"{monsterGO.name}을(를) 위한 경로를 찾지 못했습니다.");
            }
        }
    }
}