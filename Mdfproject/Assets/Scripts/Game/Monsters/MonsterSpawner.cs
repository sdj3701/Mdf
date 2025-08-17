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
    public GameObject monsterPrefab;

    [Header("정리용 부모 오브젝트")]
    [Tooltip("생성된 몬스터들이 이 오브젝트의 자식으로 들어갑니다. 비워두면 '[Monsters]' 이름으로 자동 생성됩니다.")]
    public Transform monsterParent;

    [Header("참조")]
    public AstarGrid pathfinder;

    // ✅ [추가] Awake 메서드에서 부모 오브젝트 자동 생성/설정
    void Awake()
    {
        if (monsterParent == null)
        {
            // 이 스포너와 같은 레벨에 있는 [Monsters] 오브젝트를 먼저 찾아봅니다.
            Transform foundParent = transform.parent.Find("[Monsters]");
            if (foundParent != null)
            {
                monsterParent = foundParent;
            }
            else
            {
                // 찾지 못하면 새로 생성합니다.
                GameObject parentObject = new GameObject("[Monsters]");
                // 생성된 부모 오브젝트도 이 스포너와 같은 부모 아래에 두어 정리합니다.
                parentObject.transform.SetParent(transform.parent);
                monsterParent = parentObject.transform;
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
        if (pathfinder == null)
        {
            Debug.LogError("MonsterSpawner에 AstarGrid가 연결되지 않았습니다!", this);
            yield break;
        }
        
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
                monster.Initialize(this.playerManager, this.goalTransform);
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
        if (pathfinder == null)
        {
            Debug.LogError("MonsterSpawner에 AstarGrid가 연결되지 않았습니다!", this);
            return;
        }

        if (spawnPoint == null || goalTransform == null)
        {
             Debug.LogError("스폰 포인트 또는 목표 지점이 할당되지 않았습니다!", this);
             return;
        }
        
        Debug.Log($"<color=red>보스 몬스터 소환!</color> {monsterPrefabToSpawn.name} at Player {playerManager.playerId}'s field");
        
        GameObject monsterGO = Instantiate(monsterPrefabToSpawn, spawnPoint.position, Quaternion.identity, monsterParent);
        Monster monster = monsterGO.GetComponent<Monster>();

        if (monster != null)
        {
            monster.Initialize(this.playerManager, this.goalTransform);
            
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