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
    public GameObject monsterPrefab; // 기본 웨이브용 몬스터 프리팹

    [Header("정리용 부모 오브젝트")]
    [Tooltip("생성된 몬스터들이 이 오브젝트의 자식으로 들어갑니다.")]
    public Transform monsterParent;

    [Header("참조")]
    public AstarGrid pathfinder;

    void Awake()
    {
        if (monsterParent == null)
        {
            GameObject parentObject = new GameObject($"[{playerManager.name} Monsters]");
            parentObject.transform.SetParent(transform.parent);
            monsterParent = parentObject.transform;
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

        // ✅ [수정] 프리팹에서 MonsterData를 미리 한 번만 가져옵니다.
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
            
            GameObject monsterGO = Instantiate(monsterPrefab, spawnPoint.position, Quaternion.identity, monsterParent);
            Monster monster = monsterGO.GetComponent<Monster>();

            if (monster != null)
            {
                // ✅ [수정] 세 번째 인자로 MonsterData를 전달합니다.
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
        
        // ✅ [수정] 특정 몬스터 프리팹에서도 MonsterData를 가져옵니다.
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
            // ✅ [수정] 세 번째 인자로 MonsterData를 전달합니다.
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