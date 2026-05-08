using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

public partial class GameManagers
{
    public IEnumerable<PlayerManager> AllPlayers
    {
        get
        {
            if (NetworkPlayers.Length == 0) yield break;
            foreach (var playerNO in NetworkPlayers)
            {
                if (playerNO == null || !playerNO.IsValid)
                {
                    continue;
                }

                if (playerNO.TryGetComponent<PlayerManager>(out var playerManager)
                    && playerManager != null
                    && playerManager.Object != null
                    && playerManager.Object.IsValid
                    && (Runner == null || playerManager.Runner == Runner))
                {
                    yield return playerManager;
                }
            }
        }
    }

    private static bool IsPlayerReadable(PlayerManager player)
    {
        if (player == null || player.Object == null || !player.Object.IsValid)
        {
            return false;
        }

        var gm = Instance;
        if (gm != null && gm.Runner != null && player.Runner != null && player.Runner != gm.Runner)
        {
            return false;
        }

        return true;
    }

    private static bool TryGetPlayerIdSafe(PlayerManager player, out int playerId)
    {
        playerId = -1;
        if (!IsPlayerReadable(player))
        {
            return false;
        }

        try
        {
            playerId = player.playerId;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// 특정 플레이어의 현재 전투 상대 ID를 반환합니다. (-1이면 상대 없음)
    /// </summary>
    public int GetBattleOpponent(int playerId)
    {
        if (_battleOpponents.TryGetValue(playerId, out int oppId))
        {
            return oppId;
        }

        var player = GetPlayer(playerId);
        if (player?.opponentManager != null)
        {
            int fallbackOpp = player.opponentManager.playerId;
            _battleOpponents[playerId] = fallbackOpp;
            if (!_battleOpponents.ContainsKey(fallbackOpp))
            {
                _battleOpponents[fallbackOpp] = playerId;
            }
            // Debug.LogWarning($"[GetBattleOpponent] 딕셔너리 누락으로 opponentManager 폴백 사용: P{playerId} -> P{fallbackOpp}");
            return fallbackOpp;
        }

        return -1;
    }

    public PlayerManager GetPlayer(int id)
    {
        foreach (var player in AllPlayers)
        {
            if (player == null || player.Object == null || !player.Object.IsValid)
            {
                continue;
            }

            try
            {
                if (player.playerId == id)
                {
                    return player;
                }
            }
            catch (InvalidOperationException)
            {
                // Host Migration 중 Spawned 전 객체는 건너뛴다.
            }
        }

        return null;
    }

    private bool IsRpcSourceAuthorizedForPlayer(PlayerManager player, PlayerRef source)
    {
        if (!IsPlayerReadable(player) || source == PlayerRef.None)
        {
            return false;
        }

        return player.Object.InputAuthority == source;
    }

    private bool TryGetBattleDefenderField(PlayerManager attacker, out FieldManager defenderField)
    {
        defenderField = null;
        if (!TryGetPlayerIdSafe(attacker, out int attackerId))
        {
            return false;
        }

        int defenderId = GetBattleOpponent(attackerId);
        if (defenderId < 0)
        {
            return false;
        }

        var defender = GetPlayer(defenderId);
        if (defender?.fieldManager == null)
        {
            return false;
        }

        defenderField = defender.fieldManager;
        return true;
    }

    private static bool IsFiniteVector3(Vector3 value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z);
    }

    private static bool IsWithinFieldOuterBounds(FieldManager field, Vector3 worldPosition)
    {
        if (field == null || !IsFiniteVector3(worldPosition))
        {
            return false;
        }

        Vector3 totalOrigin = field.TotalGridOrigin;
        Vector2Int totalGridSize = field.TotalGridSize;
        float cellSize = field.cellSize;
        float epsilon = Mathf.Max(0.01f, cellSize * 0.05f);

        float minX = totalOrigin.x - epsilon;
        float minZ = totalOrigin.z - epsilon;
        float maxX = totalOrigin.x + (totalGridSize.x * cellSize) + epsilon;
        float maxZ = totalOrigin.z + (totalGridSize.y * cellSize) + epsilon;

        return worldPosition.x >= minX &&
               worldPosition.x <= maxX &&
               worldPosition.z >= minZ &&
               worldPosition.z <= maxZ;
    }

    /// <summary>
    /// Host Migration 후 로컬 플레이어 참조를 다시 연결합니다.
    /// </summary>
    private void RelinkLocalPlayer()
    {
        // Debug.Log($"[GameManagers] RelinkLocalPlayer 시작 - AllPlayers 수: {AllPlayers.Count()}");

        PlayerRef localRef = Runner != null ? Runner.LocalPlayer : PlayerRef.None;

        // 방법 1: AllPlayers에서 Runner.LocalPlayer 기준으로 우선 탐색
        localPlayer = AllPlayers.FirstOrDefault(p =>
            p != null &&
            p.Object != null &&
            p.Object.IsValid &&
            ((localRef != PlayerRef.None && p.Object.InputAuthority == localRef) || p.Object.HasInputAuthority));

        // 방법 2: AllPlayers에 없으면 FindObjectsOfType으로 폴백
        if (localPlayer == null)
        {
            // Debug.Log("[GameManagers] AllPlayers에서 못 찾음, FindObjectsOfType 시도...");
            var allPlayerManagers = FindObjectsOfType<PlayerManager>();
            // Debug.Log($"[GameManagers] 발견된 PlayerManager 수: {allPlayerManagers.Length}");

            foreach (var pm in allPlayerManagers)
            {
                // Debug.Log($"  - {pm.name}: Object={pm.Object != null}, HasInputAuthority={pm.Object?.HasInputAuthority}");
                if (pm != null &&
                    pm.Object != null &&
                    pm.Object.IsValid &&
                    pm.Runner == Runner &&
                    ((localRef != PlayerRef.None && pm.Object.InputAuthority == localRef) || pm.Object.HasInputAuthority))
                {
                    localPlayer = pm;
                    break;
                }
            }
        }

        if (localPlayer != null)
        {
            // Debug.Log($"[GameManagers] 로컬 플레이어 재연결 성공: Player {localPlayer.playerId}");

            // opponentManager 재연결 (2인 게임의 경우)
            var allPlayersList = AllPlayers.ToList();

            // AllPlayers가 비어있으면 FindObjectsOfType 사용
            if (allPlayersList.Count == 0)
            {
                allPlayersList = FindObjectsOfType<PlayerManager>()
                    .Where(p => p != null && p.Runner == Runner && p.Object != null && p.Object.IsValid)
                    .ToList();
            }

            if (allPlayersList.Count == 2)
            {
                var opponent = allPlayersList.FirstOrDefault(p => p != localPlayer);
                if (opponent != null)
                {
                    localPlayer.opponentManager = opponent;
                    opponent.opponentManager = localPlayer;
                    // Debug.Log($"[GameManagers] opponentManager 재연결: Player {opponent.playerId}");
                }
            }
        }
        else
        {
            // Debug.LogWarning("[GameManagers] 로컬 플레이어를 찾을 수 없습니다!");
        }
    }

    private void RebuildNetworkPlayersAfterMigration(string context)
    {
        if (Runner == null || Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        int staleCleared = 0;
        for (int i = 0; i < NetworkPlayers.Length; i++)
        {
            var stale = NetworkPlayers[i];
            if (stale != null && stale.IsValid)
            {
                staleCleared++;
            }

            NetworkPlayers.Set(i, null);
        }

        var playersById = new Dictionary<int, PlayerManager>();
        int unreadableId = 0;
        int negativeId = 0;

        foreach (var player in FindObjectsOfType<PlayerManager>(true))
        {
            if (player == null || player.Runner != Runner || player.Object == null || !player.Object.IsValid)
            {
                continue;
            }

            if (!TryGetPlayerIdSafe(player, out int playerId))
            {
                unreadableId++;
                continue;
            }

            if (playerId < 0)
            {
                negativeId++;
                continue;
            }

            if (!playersById.TryGetValue(playerId, out var existing)
                || (player.Object.HasStateAuthority && (existing.Object == null || !existing.Object.HasStateAuthority)))
            {
                playersById[playerId] = player;
            }
        }

        var runnerPlayers = playersById
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .ToList();

        if (runnerPlayers.Count == 0)
        {
            Debug.LogWarning($"[복원] NetworkPlayers 재구성 스킵 ({context}) - runnerPlayers=0, staleCleared={staleCleared}, unreadableId={unreadableId}, negativeId={negativeId}");
            return;
        }

        int assigned = 0;
        int outOfRange = 0;
        var usedSlots = new HashSet<int>();

        foreach (var player in runnerPlayers)
        {
            if (player == null || player.Object == null || !player.Object.IsValid)
            {
                continue;
            }

            if (!TryGetPlayerIdSafe(player, out int preferredSlot))
            {
                continue;
            }

            int slot = -1;

            if (preferredSlot >= 0 && preferredSlot < NetworkPlayers.Length && !usedSlots.Contains(preferredSlot))
            {
                slot = preferredSlot;
            }
            else
            {
                if (preferredSlot < 0 || preferredSlot >= NetworkPlayers.Length)
                {
                    outOfRange++;
                }

                for (int i = 0; i < NetworkPlayers.Length; i++)
                {
                    if (!usedSlots.Contains(i))
                    {
                        slot = i;
                        break;
                    }
                }
            }

            if (slot < 0)
            {
                continue;
            }

            NetworkPlayers.Set(slot, player.Object);
            usedSlots.Add(slot);
            assigned++;
        }

        int expected = runnerPlayers.Count;
        int dropped = expected - assigned;
        if (assigned != expected)
        {
            Debug.LogError($"[복원] NetworkPlayers 재구성 무결성 실패 ({context}) assigned={assigned}, expected={expected}, dropped={dropped}, outOfRange={outOfRange}, staleCleared={staleCleared}, capacity={NetworkPlayers.Length}");
            return;
        }

        Debug.Log($"[복원] NetworkPlayers 재구성 완료 ({context}) assigned={assigned}, expected={expected}, outOfRange={outOfRange}, staleCleared={staleCleared}, capacity={NetworkPlayers.Length}");
    }

    public void CaptureBattleSnapshotForMigration(out string battleOpponentsSnapshot, out string matchFirstAttackerSnapshot, out int firstAttackerPlayerId)
    {
        battleOpponentsSnapshot = SerializeIntMap(_battleOpponents);
        matchFirstAttackerSnapshot = SerializeIntMap(_matchFirstAttacker);
        firstAttackerPlayerId = FirstAttackerPlayerId;
    }

    public bool TryRestoreBattleSnapshotForMigration(
        string battleOpponentsSnapshot,
        string matchFirstAttackerSnapshot,
        int firstAttackerPlayerId,
        string context)
    {
        var restoredOpponents = DeserializeIntMap(battleOpponentsSnapshot);
        if (restoredOpponents.Count == 0)
        {
            return false;
        }

        var restoredFirstAttackers = DeserializeIntMap(matchFirstAttackerSnapshot);

        _battleOpponents.Clear();
        foreach (var kv in restoredOpponents)
        {
            if (kv.Key < 0)
            {
                continue;
            }

            _battleOpponents[kv.Key] = kv.Value;
        }

        _matchFirstAttacker.Clear();
        foreach (var kv in restoredFirstAttackers)
        {
            if (kv.Key < 0 || !_battleOpponents.ContainsKey(kv.Key))
            {
                continue;
            }

            _matchFirstAttacker[kv.Key] = kv.Value;
        }

        if (_matchFirstAttacker.Count == 0 && firstAttackerPlayerId >= 0)
        {
            foreach (var key in _battleOpponents.Keys)
            {
                _matchFirstAttacker[key] = firstAttackerPlayerId;
            }
        }

        if (firstAttackerPlayerId >= 0)
        {
            FirstAttackerPlayerId = firstAttackerPlayerId;
        }

        Debug.Log($"[복원/매칭] 캐시 스냅샷 복원 완료 ({context}) opponents={_battleOpponents.Count}, firstAttackers={_matchFirstAttacker.Count}, firstAttackerId={FirstAttackerPlayerId}");
        return _battleOpponents.Count > 0;
    }

    private static string SerializeIntMap(Dictionary<int, int> source)
    {
        if (source == null || source.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(";", source
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    private static Dictionary<int, int> DeserializeIntMap(string snapshot)
    {
        var result = new Dictionary<int, int>();
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            return result;
        }

        var pairs = snapshot.Split(';');
        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair))
            {
                continue;
            }

            var tokens = pair.Split(':');
            if (tokens.Length != 2)
            {
                continue;
            }

            if (!int.TryParse(tokens[0], out int key) || !int.TryParse(tokens[1], out int value))
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    private void EnsureBattleMappingAfterMigration()
    {
        if (_battleOpponents.Count > 0 && _matchFirstAttacker.Count > 0)
        {
            return;
        }

        var alivePlayers = AllPlayers
            .Where(p => p != null && p.GetHealth() > 0)
            .Where(p => TryGetPlayerIdSafe(p, out int playerId) && playerId >= 0)
            .ToList();
        if (alivePlayers.Count == 0)
        {
            return;
        }

        _battleOpponents.Clear();
        _matchFirstAttacker.Clear();

        if (alivePlayers.Count == 2)
        {
            var a = alivePlayers[0];
            var b = alivePlayers[1];
            if (!TryGetPlayerIdSafe(a, out int aId) || !TryGetPlayerIdSafe(b, out int bId))
            {
                return;
            }

            _battleOpponents[aId] = bId;
            _battleOpponents[bId] = aId;

            int firstAttacker = ResolveFirstAttackerForResumePair(a, b);
            _matchFirstAttacker[aId] = firstAttacker;
            _matchFirstAttacker[bId] = firstAttacker;
            FirstAttackerPlayerId = firstAttacker;

            Debug.LogWarning($"[복원/매칭] 2인 폴백 재구성 완료: P{aId}↔P{bId}, 선공자=P{firstAttacker}, state={currentState}");
            return;
        }

        var processed = new HashSet<int>();
        foreach (var player in alivePlayers)
        {
            if (!TryGetPlayerIdSafe(player, out int playerId) || processed.Contains(playerId))
            {
                continue;
            }

            var opponent = player.opponentManager;
            if (opponent != null && alivePlayers.Contains(opponent) && TryGetPlayerIdSafe(opponent, out int opponentId))
            {
                _battleOpponents[playerId] = opponentId;
                _battleOpponents[opponentId] = playerId;

                int firstAttacker = ResolveFirstAttackerForResumePair(player, opponent);
                _matchFirstAttacker[playerId] = firstAttacker;
                _matchFirstAttacker[opponentId] = firstAttacker;

                processed.Add(playerId);
                processed.Add(opponentId);
            }
            else
            {
                _battleOpponents[playerId] = -1;
                _matchFirstAttacker[playerId] = -1;
                processed.Add(playerId);
            }
        }

        Debug.LogWarning($"[복원/매칭] opponentManager 기반 재구성 완료: {string.Join(", ", _battleOpponents.Select(kv => $"P{kv.Key}↔P{kv.Value}"))}");
    }

    private int ResolveFirstAttackerForResumePair(PlayerManager a, PlayerManager b)
    {
        if (!TryGetPlayerIdSafe(a, out int aId) || !TryGetPlayerIdSafe(b, out int bId))
        {
            return -1;
        }

        if (currentState == GameState.Battle1)
        {
            if (a.IsAttackerInCurrentBattle && !b.IsAttackerInCurrentBattle) return aId;
            if (b.IsAttackerInCurrentBattle && !a.IsAttackerInCurrentBattle) return bId;
        }
        else if (currentState == GameState.Battle2)
        {
            // Battle2는 Battle1의 공수 반대이므로, 현재 수비자가 Battle1 선공자
            if (!a.IsAttackerInCurrentBattle && b.IsAttackerInCurrentBattle) return aId;
            if (!b.IsAttackerInCurrentBattle && a.IsAttackerInCurrentBattle) return bId;
        }

        if (FirstAttackerPlayerId == aId || FirstAttackerPlayerId == bId)
        {
            return FirstAttackerPlayerId;
        }

        return aId;
    }
}
