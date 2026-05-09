// Assets/Scripts/Managers/SurvivorBossManager.cs

using System;
using System.Collections.Generic;
using System.Globalization;
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
    public string BossDataKey;
    public float RemainingHP;
    public float MaxHP;
    public int OriginPlayerId; // 보스를 소환한 원래 플레이어
    public int BossUniqueId;   // 보스 고유 ID (턴당 1회 침공 추적용)
}

/// <summary>
/// 다음 라운드에 소환될 생존 보스들을 관리합니다.
/// 보스가 상대에게 죽지 않고 목표 지점에 도착하면, 다음 라운드에 전체 유저 중 랜덤하게 침공합니다.
/// 턴당 1회 침공 제한: 전투시퀀스1 또는 전투시퀀스2 중 한 번만 등장.
/// </summary>
public class SurvivorBossManager : MonoBehaviour
{
    public static SurvivorBossManager Instance { get; private set; }
    
    // 다음 라운드에 소환될 생존 보스들 (타겟 할당 전)
    private List<SurvivorBossData> _pendingSurvivorBosses = new List<SurvivorBossData>();
    
    // 타겟별로 할당된 생존 보스 (타겟 플레이어 ID -> 보스 목록)
    private Dictionary<int, List<SurvivorBossData>> _bossTargetAssignments = new Dictionary<int, List<SurvivorBossData>>();
    
    // 현재 턴에서 침공한 보스 고유 ID 목록 (턴당 1회 침공 제한용)
    private HashSet<int> _bossesInvadedThisTurn = new HashSet<int>();
    
    // 보스 고유 ID 카운터
    private int _nextBossUniqueId = 1;
    
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

    private static bool IsRunningClientPeerWithoutAuthority()
    {
        var gm = GameManagers.Instance;
        var runner = gm != null ? gm.Runner : null;
        return runner != null && runner.IsRunning && !runner.IsServer;
    }
    
    #region 보스 등록 및 타겟 할당
    
    /// <summary>
    /// 보스가 목표 지점에 도달했을 때 호출됩니다. 
    /// 체력을 저장하고 다음 라운드에 전체 유저 중 랜덤하게 소환 예약합니다.
    /// </summary>
    public void RegisterSurvivorBoss(MonsterData bossData, float remainingHP, float maxHP, int originPlayerId, int bossUniqueId = -1)
    {
        if (IsRunningClientPeerWithoutAuthority())
        {
            Debug.LogWarning("[SurvivorBossManager] RegisterSurvivorBoss ignored on client peer.");
            return;
        }

        // 기존 보스가 생존한 경우 기존 ID 유지, 아니면 새 ID 발급
        int uniqueId = bossUniqueId > 0 ? bossUniqueId : _nextBossUniqueId++;
        
        var data = new SurvivorBossData
        {
            BossData = bossData,
            BossDataKey = BuildBossDataKey(bossData),
            RemainingHP = remainingHP,
            MaxHP = maxHP,
            OriginPlayerId = originPlayerId,
            BossUniqueId = uniqueId
        };
        
        _pendingSurvivorBosses.Add(data);
        SyncStateToClients("RegisterSurvivorBoss");
        Debug.Log($"<color=red>[SurvivorBossManager] 보스 생존 등록! ID:{uniqueId}, HP: {remainingHP:F0}/{maxHP:F0}, 다음 라운드에 전체 유저 중 랜덤 침공 예정 (대기 보스: {_pendingSurvivorBosses.Count}마리)</color>");
    }
    
    /// <summary>
    /// 라운드 시작 시 호출: 모든 생존 보스에게 랜덤 타겟 할당
    /// 이 메서드는 GameManagers에서 전투 시작 전에 한 번만 호출해야 합니다.
    /// </summary>
    public void AssignTargetsToSurvivors()
    {
        if (IsRunningClientPeerWithoutAuthority())
        {
            Debug.LogWarning("[SurvivorBossManager] AssignTargetsToSurvivors ignored on client peer.");
            return;
        }

        if (_pendingSurvivorBosses.Count == 0) return;
        
        var allPlayers = GameManagers.Instance?.AllPlayers.ToList();
        if (allPlayers == null || allPlayers.Count == 0)
        {
            _pendingSurvivorBosses.Clear();
            SyncStateToClients("AssignTargetsToSurvivors.NoPlayers");
            return;
        }
        
        foreach (var bossData in _pendingSurvivorBosses)
        {
            int randomIndex = UnityEngine.Random.Range(0, allPlayers.Count);
            int targetPlayerId = allPlayers[randomIndex].playerId;
            
            if (!_bossTargetAssignments.ContainsKey(targetPlayerId))
            {
                _bossTargetAssignments[targetPlayerId] = new List<SurvivorBossData>();
            }
            _bossTargetAssignments[targetPlayerId].Add(bossData);
            
            Debug.Log($"<color=orange>[SurvivorBossManager] 생존 보스 타겟 할당: Player {targetPlayerId} (ID:{bossData.BossUniqueId}, HP: {bossData.RemainingHP:F0}/{bossData.MaxHP:F0})</color>");
        }
        
        Debug.Log($"<color=cyan>[SurvivorBossManager] 총 {_pendingSurvivorBosses.Count}마리 생존 보스 타겟 할당 완료</color>");
        _pendingSurvivorBosses.Clear();
        SyncStateToClients("AssignTargetsToSurvivors");
    }
    
    #endregion
    
    #region 보스 추출 (전투시퀀스용)
    
    /// <summary>
    /// 특정 플레이어를 타겟으로 하는 생존 보스 중 이번 턴에 아직 침공하지 않은 보스만 추출합니다.
    /// 추출된 보스는 침공 상태로 마킹되고, 다음 전투시퀀스에서는 추출되지 않습니다.
    /// </summary>
    /// <param name="targetPlayerId">타겟 플레이어 ID</param>
    /// <returns>침공할 보스 목록</returns>
    public List<SurvivorBossData> ExtractBossesForBattleSequence(int targetPlayerId)
    {
        if (IsRunningClientPeerWithoutAuthority())
        {
            Debug.LogWarning("[SurvivorBossManager] ExtractBossesForBattleSequence ignored on client peer.");
            return new List<SurvivorBossData>();
        }

        if (!_bossTargetAssignments.TryGetValue(targetPlayerId, out var bossList))
        {
            return new List<SurvivorBossData>();
        }
        
        // 이번 턴에 아직 침공하지 않은 보스만 필터링
        var availableBosses = bossList
            .Where(b => !_bossesInvadedThisTurn.Contains(b.BossUniqueId))
            .ToList();
        
        if (availableBosses.Count == 0)
        {
            return new List<SurvivorBossData>();
        }
        
        // 침공한 보스들을 마킹 (이번 턴 다른 전투시퀀스에서 제외)
        foreach (var boss in availableBosses)
        {
            _bossesInvadedThisTurn.Add(boss.BossUniqueId);
        }
        
        // 할당 목록에서 침공한 보스 제거 (다음 턴에 다시 등록됨)
        bossList.RemoveAll(b => availableBosses.Any(ab => ab.BossUniqueId == b.BossUniqueId));
        if (bossList.Count == 0)
        {
            _bossTargetAssignments.Remove(targetPlayerId);
        }
        
        Debug.Log($"<color=cyan>[SurvivorBossManager] Player {targetPlayerId}에게 {availableBosses.Count}마리 생존 보스 침공 (이번 턴 침공 보스: {_bossesInvadedThisTurn.Count}마리)</color>");
        SyncStateToClients("ExtractBossesForBattleSequence");
        return availableBosses;
    }
    
    /// <summary>
    /// 특정 플레이어를 타겟으로 하는 생존 보스만 추출합니다. (기존 호환용)
    /// 추출된 보스는 할당 목록에서 제거됩니다.
    /// </summary>
    [System.Obsolete("Use ExtractBossesForBattleSequence() for turn-based invasion limit support")]
    public List<SurvivorBossData> ExtractBossesForTarget(int targetPlayerId)
    {
        if (IsRunningClientPeerWithoutAuthority())
        {
            Debug.LogWarning("[SurvivorBossManager] ExtractBossesForTarget ignored on client peer.");
            return new List<SurvivorBossData>();
        }

        if (!_bossTargetAssignments.TryGetValue(targetPlayerId, out var bossList))
        {
            return new List<SurvivorBossData>();
        }
        
        var result = new List<SurvivorBossData>(bossList);
        _bossTargetAssignments.Remove(targetPlayerId);
        
        Debug.Log($"<color=cyan>[SurvivorBossManager] Player {targetPlayerId}에게 {result.Count}마리 생존 보스 추출</color>");
        return result;
    }
    
    #endregion
    
    #region 턴당 1회 침공 제한 관리
    
    /// <summary>
    /// 턴 종료 시 호출: 침공 상태를 리셋합니다.
    /// 다음 턴에서 보스들이 다시 침공할 수 있도록 합니다.
    /// </summary>
    public void ResetTurnInvasionState()
    {
        if (_bossesInvadedThisTurn.Count > 0)
        {
            Debug.Log($"<color=yellow>[SurvivorBossManager] 턴 종료: {_bossesInvadedThisTurn.Count}마리 보스 침공 상태 리셋</color>");
        }
        _bossesInvadedThisTurn.Clear();
        SyncStateToClients("ResetTurnInvasionState");
    }
    
    /// <summary>
    /// 특정 보스가 이번 턴에 이미 침공했는지 확인합니다.
    /// </summary>
    public bool HasBossInvadedThisTurn(int bossUniqueId)
    {
        return _bossesInvadedThisTurn.Contains(bossUniqueId);
    }
    
    /// <summary>
    /// 다음 보스 고유 ID를 발급합니다. (첫 소환 시 사용)
    /// </summary>
    public int GetNextBossUniqueId()
    {
        if (IsRunningClientPeerWithoutAuthority())
        {
            Debug.LogWarning("[SurvivorBossManager] GetNextBossUniqueId ignored on client peer.");
            return -1;
        }

        return _nextBossUniqueId++;
    }
    
    #endregion
    
    #region 상태 확인
    
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
    /// 특정 플레이어에게 할당된 보스 중 이번 턴에 침공 가능한 보스가 있는지 확인합니다.
    /// </summary>
    public bool HasAvailableBossesForPlayer(int targetPlayerId)
    {
        if (!_bossTargetAssignments.TryGetValue(targetPlayerId, out var bossList))
        {
            return false;
        }
        return bossList.Any(b => !_bossesInvadedThisTurn.Contains(b.BossUniqueId));
    }

    public void CaptureStableSnapshot(
        out string pendingSnapshot,
        out string assignmentSnapshot,
        out int pendingCount,
        out int assignmentCount)
    {
        pendingCount = _pendingSurvivorBosses.Count;
        pendingSnapshot = string.Join("|", _pendingSurvivorBosses
            .Select(BuildBossSnapshotPart)
            .OrderBy(part => part));

        var assignmentParts = new List<string>();
        foreach (var kv in _bossTargetAssignments.OrderBy(kv => kv.Key))
        {
            foreach (var boss in kv.Value ?? Enumerable.Empty<SurvivorBossData>())
            {
                bool invaded = _bossesInvadedThisTurn.Contains(boss.BossUniqueId);
                assignmentParts.Add($"target={kv.Key};invaded={invaded};{BuildBossSnapshotPart(boss)}");
            }
        }

        assignmentCount = assignmentParts.Count;
        assignmentSnapshot = string.Join("|", assignmentParts.OrderBy(part => part));
    }

    public void CaptureNetworkSyncPayload(
        out string[] pendingParts,
        out int[] assignmentTargets,
        out string[] assignmentParts,
        out int[] invadedBossIds,
        out int nextBossUniqueId)
    {
        pendingParts = _pendingSurvivorBosses
            .Select(BuildBossNetworkPart)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        var targetList = new List<int>();
        var partList = new List<string>();
        foreach (var kv in _bossTargetAssignments.OrderBy(kv => kv.Key))
        {
            foreach (var boss in kv.Value ?? Enumerable.Empty<SurvivorBossData>())
            {
                targetList.Add(kv.Key);
                partList.Add(BuildBossNetworkPart(boss));
            }
        }

        assignmentTargets = targetList.ToArray();
        assignmentParts = partList.ToArray();
        invadedBossIds = _bossesInvadedThisTurn.OrderBy(id => id).ToArray();
        nextBossUniqueId = _nextBossUniqueId;
    }

    public void CaptureReplicatedSnapshotRows(
        out string[] dataKeys,
        out int[] states,
        out int[] bossIds,
        out int[] originPlayerIds,
        out int[] targetPlayerIds,
        out int[] hpBuckets,
        out int[] maxHpBuckets,
        out int[] invadedFlags,
        out int nextBossUniqueId)
    {
        var dataKeyList = new List<string>();
        var stateList = new List<int>();
        var bossIdList = new List<int>();
        var originList = new List<int>();
        var targetList = new List<int>();
        var hpList = new List<int>();
        var maxHpList = new List<int>();
        var invadedList = new List<int>();

        foreach (var boss in _pendingSurvivorBosses.OrderBy(boss => boss.BossUniqueId))
        {
            AppendReplicatedSnapshotRow(
                boss,
                state: 1,
                targetPlayerId: -1,
                invaded: false,
                dataKeyList,
                stateList,
                bossIdList,
                originList,
                targetList,
                hpList,
                maxHpList,
                invadedList);
        }

        foreach (var kv in _bossTargetAssignments.OrderBy(kv => kv.Key))
        {
            foreach (var boss in (kv.Value ?? Enumerable.Empty<SurvivorBossData>()).OrderBy(boss => boss.BossUniqueId))
            {
                AppendReplicatedSnapshotRow(
                    boss,
                    state: 2,
                    targetPlayerId: kv.Key,
                    invaded: _bossesInvadedThisTurn.Contains(boss.BossUniqueId),
                    dataKeyList,
                    stateList,
                    bossIdList,
                    originList,
                    targetList,
                    hpList,
                    maxHpList,
                    invadedList);
            }
        }

        dataKeys = dataKeyList.ToArray();
        states = stateList.ToArray();
        bossIds = bossIdList.ToArray();
        originPlayerIds = originList.ToArray();
        targetPlayerIds = targetList.ToArray();
        hpBuckets = hpList.ToArray();
        maxHpBuckets = maxHpList.ToArray();
        invadedFlags = invadedList.ToArray();
        nextBossUniqueId = _nextBossUniqueId;
    }

    public void ApplyNetworkSyncFromAuthority(
        string[] pendingParts,
        int[] assignmentTargets,
        string[] assignmentParts,
        int[] invadedBossIds,
        int nextBossUniqueId)
    {
        _pendingSurvivorBosses.Clear();
        _bossTargetAssignments.Clear();
        _bossesInvadedThisTurn.Clear();

        foreach (var part in pendingParts ?? Array.Empty<string>())
        {
            if (TryParseBossNetworkPart(part, out var boss))
            {
                _pendingSurvivorBosses.Add(boss);
            }
        }

        int assignmentCount = Mathf.Min(assignmentTargets?.Length ?? 0, assignmentParts?.Length ?? 0);
        for (int i = 0; i < assignmentCount; i++)
        {
            if (!TryParseBossNetworkPart(assignmentParts[i], out var boss))
            {
                continue;
            }

            int targetPlayerId = assignmentTargets[i];
            if (!_bossTargetAssignments.TryGetValue(targetPlayerId, out var bosses))
            {
                bosses = new List<SurvivorBossData>();
                _bossTargetAssignments[targetPlayerId] = bosses;
            }
            bosses.Add(boss);
        }

        foreach (int bossId in invadedBossIds ?? Array.Empty<int>())
        {
            if (bossId > 0)
            {
                _bossesInvadedThisTurn.Add(bossId);
            }
        }

        if (nextBossUniqueId > 0)
        {
            _nextBossUniqueId = Mathf.Max(_nextBossUniqueId, nextBossUniqueId);
        }
    }

    private static string BuildBossSnapshotPart(SurvivorBossData boss)
    {
        string dataKey = !string.IsNullOrWhiteSpace(boss.BossDataKey)
            ? boss.BossDataKey
            : BuildBossDataKey(boss.BossData);
        int hpBucket = BuildHpBucket(boss.RemainingHP, boss.MaxHP);
        int maxHpBucket = Mathf.Max(0, Mathf.RoundToInt(boss.MaxHP / 10f));
        return $"id={boss.BossUniqueId};origin={boss.OriginPlayerId};type={dataKey};hpBucket={hpBucket};maxHpBucket={maxHpBucket}";
    }

    private static void AppendReplicatedSnapshotRow(
        SurvivorBossData boss,
        int state,
        int targetPlayerId,
        bool invaded,
        List<string> dataKeys,
        List<int> states,
        List<int> bossIds,
        List<int> originPlayerIds,
        List<int> targetPlayerIds,
        List<int> hpBuckets,
        List<int> maxHpBuckets,
        List<int> invadedFlags)
    {
        string dataKey = !string.IsNullOrWhiteSpace(boss.BossDataKey)
            ? boss.BossDataKey
            : BuildBossDataKey(boss.BossData);
        dataKeys.Add(dataKey);
        states.Add(state);
        bossIds.Add(boss.BossUniqueId);
        originPlayerIds.Add(boss.OriginPlayerId);
        targetPlayerIds.Add(targetPlayerId);
        hpBuckets.Add(BuildHpBucket(boss.RemainingHP, boss.MaxHP));
        maxHpBuckets.Add(Mathf.Max(0, Mathf.RoundToInt(boss.MaxHP / 10f)));
        invadedFlags.Add(invaded ? 1 : 0);
    }

    private static string BuildBossNetworkPart(SurvivorBossData boss)
    {
        string dataKey = !string.IsNullOrWhiteSpace(boss.BossDataKey)
            ? boss.BossDataKey
            : BuildBossDataKey(boss.BossData);
        return string.Join(";",
            $"id={boss.BossUniqueId}",
            $"origin={boss.OriginPlayerId}",
            $"type={dataKey}",
            $"remaining={boss.RemainingHP.ToString("R", CultureInfo.InvariantCulture)}",
            $"max={boss.MaxHP.ToString("R", CultureInfo.InvariantCulture)}");
    }

    private static bool TryParseBossNetworkPart(string part, out SurvivorBossData boss)
    {
        boss = default;
        if (string.IsNullOrWhiteSpace(part))
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in part.Split(';'))
        {
            int split = token.IndexOf('=');
            if (split <= 0 || split >= token.Length - 1)
            {
                continue;
            }
            values[token.Substring(0, split)] = token.Substring(split + 1);
        }

        if (!TryGetInt(values, "id", out int bossId) || bossId <= 0)
        {
            return false;
        }

        TryGetInt(values, "origin", out int originPlayerId);
        TryGetFloat(values, "remaining", out float remainingHp);
        TryGetFloat(values, "max", out float maxHp);
        values.TryGetValue("type", out string dataKey);

        boss = new SurvivorBossData
        {
            BossData = ResolveMonsterDataByName(dataKey),
            BossDataKey = string.IsNullOrWhiteSpace(dataKey) ? "null" : dataKey,
            RemainingHP = remainingHp,
            MaxHP = maxHp,
            OriginPlayerId = originPlayerId,
            BossUniqueId = bossId
        };
        return true;
    }

    private static bool TryGetInt(Dictionary<string, string> values, string key, out int value)
    {
        value = 0;
        return values != null
            && values.TryGetValue(key, out string text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetFloat(Dictionary<string, string> values, string key, out float value)
    {
        value = 0f;
        return values != null
            && values.TryGetValue(key, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string BuildBossDataKey(MonsterData data)
    {
        if (data == null)
        {
            return "null";
        }

        return !string.IsNullOrWhiteSpace(data.monsterName) ? data.monsterName : data.name;
    }

    private static MonsterData ResolveMonsterDataByName(string monsterDataName)
    {
        if (string.IsNullOrWhiteSpace(monsterDataName) || monsterDataName == "null")
        {
            return null;
        }

        foreach (var data in Resources.FindObjectsOfTypeAll<MonsterData>())
        {
            if (MatchesMonsterData(data, monsterDataName))
            {
                return data;
            }
        }

        foreach (var manager in Resources.FindObjectsOfTypeAll<AugmentManager>())
        {
            var data = manager != null ? manager.FindMonsterDataByName(monsterDataName) : null;
            if (data != null)
            {
                return data;
            }
        }

        return null;
    }

    private static bool MatchesMonsterData(MonsterData data, string monsterDataName)
    {
        if (data == null || string.IsNullOrWhiteSpace(monsterDataName))
        {
            return false;
        }

        return string.Equals(data.name, monsterDataName, StringComparison.Ordinal)
            || string.Equals(data.monsterName, monsterDataName, StringComparison.Ordinal)
            || string.Equals(data.monsterPrefab, monsterDataName, StringComparison.Ordinal);
    }

    private void SyncStateToClients(string reason)
    {
        GameManagers.Instance?.SyncSurvivorBossStateToClientsIfAuthoritative(reason);
    }

    private static int BuildHpBucket(float currentHp, float maxHp)
    {
        if (maxHp <= 0f)
        {
            return Mathf.Max(0, Mathf.RoundToInt(currentHp / 10f));
        }

        float ratio = Mathf.Clamp01(currentHp / maxHp);
        return Mathf.Clamp(Mathf.FloorToInt(ratio * 10f), 0, 10);
    }
    
    /// <summary>
    /// 보스가 사망했을 때 호출됩니다. (정보 로깅용)
    /// </summary>
    public void OnBossDied(string bossName)
    {
        Debug.Log($"<color=green>[SurvivorBossManager] 보스 '{bossName}'가 처치됨! 더 이상 소환되지 않습니다.</color>");
    }
    
    #endregion
    
    #region Legacy/Obsolete
    
    /// <summary>
    /// 다음 라운드에 소환할 생존 보스 목록을 가져옵니다.
    /// 각 보스마다 전체 유저 중 랜덤하게 타겟을 선정합니다.
    /// </summary>
    [System.Obsolete("Use AssignTargetsToSurvivors() + ExtractBossesForBattleSequence() instead for proper multi-player support")]
    public List<(int targetPlayerId, SurvivorBossData bossData)> GetPendingBossesWithTargets()
    {
        var result = new List<(int, SurvivorBossData)>();
        if (IsRunningClientPeerWithoutAuthority())
        {
            Debug.LogWarning("[SurvivorBossManager] GetPendingBossesWithTargets ignored on client peer.");
            return result;
        }

        var allPlayers = GameManagers.Instance?.AllPlayers.ToList();
        
        if (allPlayers == null || allPlayers.Count == 0)
        {
            _pendingSurvivorBosses.Clear();
            return result;
        }
        
        foreach (var bossData in _pendingSurvivorBosses)
        {
            // 전체 유저 중 랜덤하게 타겟 선정 (보스를 소환한 플레이어 포함)
            int randomIndex = UnityEngine.Random.Range(0, allPlayers.Count);
            int targetPlayerId = allPlayers[randomIndex].playerId;
            
            result.Add((targetPlayerId, bossData));
            Debug.Log($"<color=orange>[SurvivorBossManager] 생존 보스 타겟 선정: Player {targetPlayerId}</color>");
        }
        
        _pendingSurvivorBosses.Clear();
        return result;
    }
    
    #endregion
}

