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
    public MonsterData BossData;
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
    
    // 다음 라운드에 소환될 생존 보스들 (타겟 할당 전)
    private List<SurvivorBossData> _pendingSurvivorBosses = new List<SurvivorBossData>();
    
    // 타겟별로 할당된 생존 보스 (타겟 플레이어 ID -> 보스 목록)
    private Dictionary<int, List<SurvivorBossData>> _bossTargetAssignments = new Dictionary<int, List<SurvivorBossData>>();
    
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
    public void RegisterSurvivorBoss(MonsterData bossData, float remainingHP, float maxHP, int originPlayerId)
    {
        var data = new SurvivorBossData
        {
            BossData = bossData,
            RemainingHP = remainingHP,
            MaxHP = maxHP,
            OriginPlayerId = originPlayerId
        };
        
        _pendingSurvivorBosses.Add(data);
        Debug.Log($"<color=red>[SurvivorBossManager] 보스 생존 등록! HP: {remainingHP:F0}/{maxHP:F0}, 다음 라운드에 전체 유저 중 랜덤 침공 예정 (대기 보스: {_pendingSurvivorBosses.Count}마리)</color>");
    }
    
    /// <summary>
    /// 라운드 시작 시 호출: 모든 생존 보스에게 랜덤 타겟 할당
    /// 이 메서드는 GameManagers에서 전투 시작 전에 한 번만 호출해야 합니다.
    /// </summary>
    public void AssignTargetsToSurvivors()
    {
        if (_pendingSurvivorBosses.Count == 0) return;
        
        var allPlayers = GameManagers.Instance?.AllPlayers.ToList();
        if (allPlayers == null || allPlayers.Count == 0)
        {
            _pendingSurvivorBosses.Clear();
            return;
        }
        
        foreach (var bossData in _pendingSurvivorBosses)
        {
            int randomIndex = Random.Range(0, allPlayers.Count);
            int targetPlayerId = allPlayers[randomIndex].playerId;
            
            if (!_bossTargetAssignments.ContainsKey(targetPlayerId))
            {
                _bossTargetAssignments[targetPlayerId] = new List<SurvivorBossData>();
            }
            _bossTargetAssignments[targetPlayerId].Add(bossData);
            
            Debug.Log($"<color=orange>[SurvivorBossManager] 생존 보스 타겟 할당: Player {targetPlayerId} (HP: {bossData.RemainingHP:F0}/{bossData.MaxHP:F0})</color>");
        }
        
        Debug.Log($"<color=cyan>[SurvivorBossManager] 총 {_pendingSurvivorBosses.Count}마리 생존 보스 타겟 할당 완료</color>");
        _pendingSurvivorBosses.Clear();
    }
    
    /// <summary>
    /// 특정 플레이어를 타겟으로 하는 생존 보스만 추출합니다.
    /// 추출된 보스는 할당 목록에서 제거됩니다.
    /// 각 플레이어의 MonsterSpawner에서 호출됩니다.
    /// </summary>
    public List<SurvivorBossData> ExtractBossesForTarget(int targetPlayerId)
    {
        if (!_bossTargetAssignments.TryGetValue(targetPlayerId, out var bossList))
        {
            return new List<SurvivorBossData>();
        }
        
        var result = new List<SurvivorBossData>(bossList);
        _bossTargetAssignments.Remove(targetPlayerId);
        
        Debug.Log($"<color=cyan>[SurvivorBossManager] Player {targetPlayerId}에게 {result.Count}마리 생존 보스 추출</color>");
        return result;
    }
    
    /// <summary>
    /// 다음 라운드에 소환할 생존 보스 목록을 가져옵니다.
    /// 각 보스마다 전체 유저 중 랜덤하게 타겟을 선정합니다.
    /// </summary>
    [System.Obsolete("Use AssignTargetsToSurvivors() + ExtractBossesForTarget() instead for proper multi-player support")]
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
    /// 보류 중인 보스가 있는지 확인합니다. (타겟 할당 전)
    /// </summary>
    public bool HasPendingBosses()
    {
        return _pendingSurvivorBosses.Count > 0;
    }
    
    /// <summary>
    /// 할당된 보스가 있는지 확인합니다. (타겟 할당 후)
    /// </summary>
    public bool HasAssignedBosses()
    {
        return _bossTargetAssignments.Count > 0;
    }
    
    /// <summary>
    /// 보스가 사망했을 때 호출됩니다. (정보 로깅용)
    /// </summary>
    public void OnBossDied(string bossName)
    {
        Debug.Log($"<color=green>[SurvivorBossManager] 보스 '{bossName}'가 처치됨! 더 이상 소환되지 않습니다.</color>");
    }
}

