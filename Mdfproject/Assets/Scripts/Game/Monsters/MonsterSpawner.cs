using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MonsterSpawner : MonoBehaviour
{
    public PlayerManager playerManager;

    [Header("스폰 설정")]
    public Transform spawnPoint;
    public GameObject monsterPrefab;
    // TODO: 라운드별 웨이브 정보를 담을 ScriptableObject 필요

    public void SpawnWave(int round)
    {
        int monsterCount = 5 + round; // 예시: 라운드마다 1마리씩 증가
        StartCoroutine(SpawnMonsterCoroutine(monsterCount));
    }

    IEnumerator SpawnMonsterCoroutine(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (spawnPoint == null)
            {
                 Debug.LogError("스폰 포인트가 할당되지 않았습니다!", this);
                 yield break;
            }
            GameObject monsterGO = Instantiate(monsterPrefab, spawnPoint.position, Quaternion.identity);
            Monster monster = monsterGO.GetComponent<Monster>();

            if (monster != null)
            {
                // 상대방이 선택한 디버프 증강 효과 적용
                ApplyOpponentDebuffs(monster);
            }
            
            yield return new WaitForSeconds(0.5f); // 스폰 간격
        }
    }

    private void ApplyOpponentDebuffs(Monster monster)
    {
        if (playerManager.opponentManager == null) return;

        float healthMultiplier = 1f;
        float speedMultiplier = 1f;

        // 상대방이 선택한 증강 중, 나에게 적용되는(TargetType.Opponent) 효과들을 찾습니다.
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
        if (spawnPoint == null)
        {
             Debug.LogError("스폰 포인트가 할당되지 않았습니다!", this);
             return;
        }
        Debug.Log($"<color=red>보스 몬스터 소환!</color> {monsterPrefabToSpawn.name} at Player {playerManager.playerId}'s field");
        Instantiate(monsterPrefabToSpawn, spawnPoint.position, Quaternion.identity);
    }
}