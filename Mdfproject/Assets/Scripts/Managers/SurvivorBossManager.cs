// Assets/Scripts/Managers/SurvivorBossManager.cs

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Fusion;

/// <summary>
/// 생존한 보스의 정보를 저장하는 데이터 구조
/// </summary>
[System.Serializable]
public struct SurvivorBossData
{
    public GameObject BossPrefab;
    public float RemainingHP;
    public float MaxHP;
    public int OriginPlayerId; // 보스를 소환한 원래 플레이어
}

/// <summary>
/// 다음 라운드에 소환될 생존 보스들을 관리합니다.
/// 보스가 상대에게 죽지 않고 목표 지점에 도착하면, 다음 라운드에 전체 유저 중 랜덤하게 침공합니다.
/// </summary>
public class SurvivorBossManager : MonoBehaviour
{
    public static SurvivorBossManager Instance { get; private set; }
    
    // 다음 라운드에 소환될 생존 보스들
    private List<SurvivorBossData> _pendingSurvivorBosses = new List<SurvivorBossData>();
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    /// <summary>
    /// 보스가 목표 지점에 도달했을 때 호출됩니다. 
    /// 체력을 저장하고 다음 라운드에 전체 유저 중 랜덤하게 소환 예약합니다.
    /// </summary>
    public void RegisterSurvivorBoss(GameObject bossPrefab, float remainingHP, float maxHP, int originPlayerId)
    {
        var data = new SurvivorBossData
        {
            BossPrefab = bossPrefab,
            RemainingHP = remainingHP,
            MaxHP = maxHP,
            OriginPlayerId = originPlayerId
        };
        
        _pendingSurvivorBosses.Add(data);
        Debug.Log($"<color=red>[SurvivorBossManager] 보스 생존 등록! HP: {remainingHP:F0}/{maxHP:F0}, 다음 라운드에 전체 유저 중 랜덤 침공 예정</color>");
    }
    
    /// <summary>
    /// 다음 라운드에 소환할 생존 보스 목록을 가져옵니다.
    /// 각 보스마다 전체 유저 중 랜덤하게 타겟을 선정합니다.
    /// </summary>
    /// <returns>타겟 플레이어 ID와 보스 데이터 쌍</returns>
    public List<(int targetPlayerId, SurvivorBossData bossData)> GetPendingBossesWithTargets()
    {
        var result = new List<(int, SurvivorBossData)>();
        var allPlayers = GameManagers.Instance?.AllPlayers.ToList();
        
        if (allPlayers == null || allPlayers.Count == 0)
        {
            _pendingSurvivorBosses.Clear();
            return result;
        }
        
        foreach (var bossData in _pendingSurvivorBosses)
        {
            // 전체 유저 중 랜덤하게 타겟 선정 (보스를 소환한 플레이어 포함)
            int randomIndex = Random.Range(0, allPlayers.Count);
            int targetPlayerId = allPlayers[randomIndex].playerId;
            
            result.Add((targetPlayerId, bossData));
            Debug.Log($"<color=orange>[SurvivorBossManager] 생존 보스 타겟 선정: Player {targetPlayerId}</color>");
        }
        
        _pendingSurvivorBosses.Clear();
        return result;
    }
    
    /// <summary>
    /// 보류 중인 보스가 있는지 확인합니다.
    /// </summary>
    public bool HasPendingBosses()
    {
        return _pendingSurvivorBosses.Count > 0;
    }
    
    /// <summary>
    /// 보스가 사망했을 때 호출됩니다. (정보 로깅용)
    /// </summary>
    public void OnBossDied(string bossName)
    {
        Debug.Log($"<color=green>[SurvivorBossManager] 보스 '{bossName}'가 처치됨! 더 이상 소환되지 않습니다.</color>");
    }
}
