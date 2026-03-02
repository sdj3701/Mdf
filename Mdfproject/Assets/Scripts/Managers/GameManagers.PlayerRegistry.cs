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

    /// <summary>
    /// Host Migration 후 로컬 플레이어 참조를 다시 연결합니다.
    /// </summary>
    private void RelinkLocalPlayer()
    {
        // Debug.Log($"[GameManagers] RelinkLocalPlayer 시작 - AllPlayers 수: {AllPlayers.Count()}");

        // 방법 1: AllPlayers에서 InputAuthority 가진 플레이어 찾기
        localPlayer = AllPlayers.FirstOrDefault(p =>
            p != null && p.Object != null && p.Object.HasInputAuthority);

        // 방법 2: AllPlayers에 없으면 FindObjectsOfType으로 폴백
        if (localPlayer == null)
        {
            // Debug.Log("[GameManagers] AllPlayers에서 못 찾음, FindObjectsOfType 시도...");
            var allPlayerManagers = FindObjectsOfType<PlayerManager>();
            // Debug.Log($"[GameManagers] 발견된 PlayerManager 수: {allPlayerManagers.Length}");

            foreach (var pm in allPlayerManagers)
            {
                // Debug.Log($"  - {pm.name}: Object={pm.Object != null}, HasInputAuthority={pm.Object?.HasInputAuthority}");
                if (pm != null && pm.Object != null && pm.Object.HasInputAuthority)
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
                allPlayersList = FindObjectsOfType<PlayerManager>().ToList();
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

        var runnerPlayers = FindObjectsOfType<PlayerManager>(true)
            .Where(player => player != null)
            .Where(player => player.Runner == Runner)
            .Where(player => player.Object != null && player.Object.IsValid)
            .Where(player => player.playerId >= 0)
            .GroupBy(player => player.playerId)
            .Select(group => group
                .OrderByDescending(player => player.Object.HasStateAuthority)
                .First())
            .ToList();

        if (runnerPlayers.Count == 0)
        {
            Debug.LogWarning($"[복원] NetworkPlayers 재구성 스킵 ({context}) - runnerPlayers=0, staleCleared={staleCleared}");
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

            int preferredSlot = player.playerId;
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

        var alivePlayers = AllPlayers.Where(p => p != null && p.GetHealth() > 0).ToList();
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
            _battleOpponents[a.playerId] = b.playerId;
            _battleOpponents[b.playerId] = a.playerId;

            int firstAttacker = ResolveFirstAttackerForResumePair(a, b);
            _matchFirstAttacker[a.playerId] = firstAttacker;
            _matchFirstAttacker[b.playerId] = firstAttacker;
            FirstAttackerPlayerId = firstAttacker;

            Debug.LogWarning($"[복원/매칭] 2인 폴백 재구성 완료: P{a.playerId}↔P{b.playerId}, 선공자=P{firstAttacker}, state={currentState}");
            return;
        }

        var processed = new HashSet<int>();
        foreach (var player in alivePlayers)
        {
            if (processed.Contains(player.playerId))
            {
                continue;
            }

            var opponent = player.opponentManager;
            if (opponent != null && alivePlayers.Contains(opponent))
            {
                _battleOpponents[player.playerId] = opponent.playerId;
                _battleOpponents[opponent.playerId] = player.playerId;

                int firstAttacker = ResolveFirstAttackerForResumePair(player, opponent);
                _matchFirstAttacker[player.playerId] = firstAttacker;
                _matchFirstAttacker[opponent.playerId] = firstAttacker;

                processed.Add(player.playerId);
                processed.Add(opponent.playerId);
            }
            else
            {
                _battleOpponents[player.playerId] = -1;
                _matchFirstAttacker[player.playerId] = -1;
                processed.Add(player.playerId);
            }
        }

        Debug.LogWarning($"[복원/매칭] opponentManager 기반 재구성 완료: {string.Join(", ", _battleOpponents.Select(kv => $"P{kv.Key}↔P{kv.Value}"))}");
    }

    private int ResolveFirstAttackerForResumePair(PlayerManager a, PlayerManager b)
    {
        if (currentState == GameState.Battle1)
        {
            if (a.IsAttackerInCurrentBattle && !b.IsAttackerInCurrentBattle) return a.playerId;
            if (b.IsAttackerInCurrentBattle && !a.IsAttackerInCurrentBattle) return b.playerId;
        }
        else if (currentState == GameState.Battle2)
        {
            // Battle2는 Battle1의 공수 반대이므로, 현재 수비자가 Battle1 선공자
            if (!a.IsAttackerInCurrentBattle && b.IsAttackerInCurrentBattle) return a.playerId;
            if (!b.IsAttackerInCurrentBattle && a.IsAttackerInCurrentBattle) return b.playerId;
        }

        if (FirstAttackerPlayerId == a.playerId || FirstAttackerPlayerId == b.playerId)
        {
            return FirstAttackerPlayerId;
        }

        return a.playerId;
    }
}
