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
            if (!IsReadyForNetworkAccess)
            {
                yield break;
            }

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

    public bool TryGetBattleOpponentSnapshot(int playerId, out int opponentId)
    {
        if (Object != null && !Object.HasStateAuthority &&
            TryReadNetworkedBattleOpponentSnapshot(playerId, out opponentId))
        {
            _battleOpponents[playerId] = opponentId;
            return true;
        }

        if (_battleOpponents.TryGetValue(playerId, out opponentId))
        {
            return true;
        }

        return TryReadNetworkedBattleOpponentSnapshot(playerId, out opponentId);
    }

    public bool TryGetBattleRoleSnapshot(int playerId, out bool isActivelyFighting, out bool isAttacker)
    {
        isActivelyFighting = false;
        isAttacker = false;

        if (playerId < 0 || (currentState != GameState.Battle1 && currentState != GameState.Battle2))
        {
            return false;
        }

        if (TryGetBattleOpponentSnapshot(playerId, out int opponentId) &&
            opponentId >= 0 &&
            TryGetMatchFirstAttackerSnapshot(playerId, out int firstAttackerId) &&
            firstAttackerId >= 0)
        {
            isActivelyFighting = true;
            bool isFirstAttacker = playerId == firstAttackerId;
            isAttacker = currentState == GameState.Battle1 ? isFirstAttacker : !isFirstAttacker;
            return true;
        }

        var player = GetPlayer(playerId);
        if (!IsPlayerReadable(player))
        {
            return false;
        }

        try
        {
            isActivelyFighting = player.IsActivelyFighting;
            isAttacker = player.IsAttackerInCurrentBattle;
            return true;
        }
        catch (InvalidOperationException)
        {
            isActivelyFighting = false;
            isAttacker = false;
            return false;
        }
    }

    private bool TryGetMatchFirstAttackerSnapshot(int playerId, out int firstAttackerId)
    {
        if (Object != null && !Object.HasStateAuthority &&
            TryReadNetworkedMatchFirstAttackerSnapshot(playerId, out firstAttackerId))
        {
            _matchFirstAttacker[playerId] = firstAttackerId;
            return true;
        }

        if (_matchFirstAttacker.TryGetValue(playerId, out firstAttackerId))
        {
            return true;
        }

        return TryReadNetworkedMatchFirstAttackerSnapshot(playerId, out firstAttackerId);
    }

    private static int EncodeBattleSnapshotId(int id)
    {
        return id < -1 ? 0 : id + 2;
    }

    private static bool TryDecodeBattleSnapshotId(int value, out int id)
    {
        if (value == 0)
        {
            id = -1;
            return false;
        }

        id = value - 2;
        return true;
    }

    private void PublishBattleSnapshotMap()
    {
        if (Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        int capacity = Mathf.Min(MAX_PLAYERS, Mathf.Min(BattleOpponentSnapshotIds.Length, BattleFirstAttackerSnapshotIds.Length));
        for (int i = 0; i < capacity; i++)
        {
            BattleOpponentSnapshotIds.Set(i, 0);
            BattleFirstAttackerSnapshotIds.Set(i, 0);
        }

        foreach (var kv in _battleOpponents)
        {
            if (kv.Key >= 0 && kv.Key < capacity)
            {
                BattleOpponentSnapshotIds.Set(kv.Key, EncodeBattleSnapshotId(kv.Value));
            }
        }

        foreach (var kv in _matchFirstAttacker)
        {
            if (kv.Key >= 0 && kv.Key < capacity)
            {
                BattleFirstAttackerSnapshotIds.Set(kv.Key, EncodeBattleSnapshotId(kv.Value));
            }
        }
    }

    private bool HasBattleSnapshotWriteAuthority()
    {
        return Object != null && Object.HasStateAuthority;
    }

    private bool TryReadNetworkedBattleOpponentSnapshot(int playerId, out int opponentId)
    {
        opponentId = -1;
        if (playerId < 0 || playerId >= BattleOpponentSnapshotIds.Length)
        {
            return false;
        }

        return TryDecodeBattleSnapshotId(BattleOpponentSnapshotIds.Get(playerId), out opponentId);
    }

    private bool TryReadNetworkedMatchFirstAttackerSnapshot(int playerId, out int firstAttackerId)
    {
        firstAttackerId = -1;
        if (playerId < 0 || playerId >= BattleFirstAttackerSnapshotIds.Length)
        {
            return false;
        }

        return TryDecodeBattleSnapshotId(BattleFirstAttackerSnapshotIds.Get(playerId), out firstAttackerId);
    }

    private Dictionary<int, int> BuildBattleOpponentSnapshotMap()
    {
        var result = new Dictionary<int, int>(_battleOpponents);
        int capacity = Mathf.Min(MAX_PLAYERS, BattleOpponentSnapshotIds.Length);
        for (int i = 0; i < capacity; i++)
        {
            if (!result.ContainsKey(i) && TryReadNetworkedBattleOpponentSnapshot(i, out int opponentId))
            {
                result[i] = opponentId;
            }
        }

        return result;
    }

    private Dictionary<int, int> BuildMatchFirstAttackerSnapshotMap()
    {
        var result = new Dictionary<int, int>(_matchFirstAttacker);
        int capacity = Mathf.Min(MAX_PLAYERS, BattleFirstAttackerSnapshotIds.Length);
        for (int i = 0; i < capacity; i++)
        {
            if (!result.ContainsKey(i) && TryReadNetworkedMatchFirstAttackerSnapshot(i, out int firstAttackerId))
            {
                result[i] = firstAttackerId;
            }
        }

        return result;
    }

    private void CacheNetworkedBattleSnapshotMap()
    {
        int opponentCapacity = Mathf.Min(MAX_PLAYERS, BattleOpponentSnapshotIds.Length);
        for (int i = 0; i < opponentCapacity; i++)
        {
            if (TryReadNetworkedBattleOpponentSnapshot(i, out int opponentId))
            {
                _battleOpponents[i] = opponentId;
            }
        }

        int firstAttackerCapacity = Mathf.Min(MAX_PLAYERS, BattleFirstAttackerSnapshotIds.Length);
        for (int i = 0; i < firstAttackerCapacity; i++)
        {
            if (TryReadNetworkedMatchFirstAttackerSnapshot(i, out int firstAttackerId))
            {
                _matchFirstAttacker[i] = firstAttackerId;
            }
        }
    }

    private void RecordBattleStartSnapshotFromRpc(int playerId, bool isAttacker, int opponentId)
    {
        if (playerId < 0)
        {
            return;
        }

        _battleOpponents[playerId] = opponentId;
        if (opponentId >= 0 && !_battleOpponents.ContainsKey(opponentId))
        {
            _battleOpponents[opponentId] = playerId;
        }

        int firstAttackerId = -1;
        if (opponentId >= 0)
        {
            firstAttackerId = currentState == GameState.Battle2
                ? (isAttacker ? opponentId : playerId)
                : (isAttacker ? playerId : opponentId);
        }

        _matchFirstAttacker[playerId] = firstAttackerId;
        if (opponentId >= 0)
        {
            _matchFirstAttacker[opponentId] = firstAttackerId;
        }

        if (firstAttackerId >= 0 && Object != null && Object.HasStateAuthority)
        {
            FirstAttackerPlayerId = firstAttackerId;
        }

        PublishBattleSnapshotMap();
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

    /// <summary>
    /// Rebinds presentation-only references after Fusion has rebuilt the player objects.
    /// Durable gameplay identity remains playerId; no networked state is written here.
    /// </summary>
    private void RebindLocalPresentationAfterPlayerRegistryChanged(string context)
    {
        RelinkLocalPlayer();
        CameraManager.Instance?.RebindAfterPlayerRegistryChanged();
        OnPlayersDataReady?.Invoke();
        Debug.Log($"[MigrationRestore] Local player presentation registry rebound ({context}).");
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

        var allRunnerPlayers = FindObjectsOfType<PlayerManager>(true)
            .Where(player => player != null)
            .Where(player => player.Runner == Runner)
            .Where(player => player.Object != null && player.Object.IsValid)
            .Where(player => player.playerId >= 0)
            .ToList();

        var runnerPlayers = allRunnerPlayers
            .GroupBy(player => player.playerId)
            .Select(group => group
                .OrderByDescending(ScoreMigrationPlayerCandidate)
                .First())
            .ToList();

        if (runnerPlayers.Count == 0)
        {
            Debug.LogWarning($"[복원] NetworkPlayers 재구성 스킵 ({context}) - runnerPlayers=0, staleCleared={staleCleared}");
            return;
        }

        var selectedInstanceIds = new HashSet<int>(runnerPlayers.Select(player => player.GetInstanceID()));
        foreach (var duplicate in allRunnerPlayers.Where(player => !selectedInstanceIds.Contains(player.GetInstanceID())))
        {
            DespawnDuplicatePlayerManagerAfterMigration(duplicate, context);
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

    private static int ScoreMigrationPlayerCandidate(PlayerManager player)
    {
        if (player == null || player.Object == null || !player.Object.IsValid)
        {
            return int.MinValue;
        }

        int score = 0;
        if (player.Object.HasStateAuthority)
        {
            score += 1000;
        }

        if (player.IsRuntimeReady(out _))
        {
            score += 80;
        }

        if (player.TryGetShopSnapshot(out var shopKeys, out _, out _, out int shopRevision, out _)
            && shopRevision > 0
            && shopKeys != null
            && shopKeys.Length > 0)
        {
            score += 80;
        }

        if (player.fieldManager != null)
        {
            score += 20;
            player.fieldManager.RebuildWallMapsAfterMigration("GameManagers.ScoreMigrationPlayerCandidate", false, out _);
            if (player.fieldManager.IsWallMapReady)
            {
                score += 40;
            }
        }

        return score;
    }

    private void DespawnDuplicatePlayerManagerAfterMigration(PlayerManager duplicate, string context)
    {
        if (duplicate == null)
        {
            return;
        }

        int playerId = -1;
        try
        {
            playerId = duplicate.playerId;
        }
        catch
        {
        }

        var networkObject = duplicate.Object;
        try
        {
            if (Runner != null && Runner.IsServer && networkObject != null && networkObject.IsValid)
            {
                Runner.Despawn(networkObject);
                Debug.Log($"[MigrationRestore] Duplicate PlayerManager despawn queued ({context}) P{playerId} name={duplicate.name}");
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MigrationRestore] Duplicate PlayerManager despawn failed ({context}) P{playerId}: {e.Message}");
        }

        if (duplicate.gameObject != null)
        {
            Destroy(duplicate.gameObject);
            Debug.Log($"[MigrationRestore] Duplicate PlayerManager GameObject destroyed ({context}) P{playerId} name={duplicate.name}");
        }
    }

    public void CaptureBattleSnapshotForMigration(out string battleOpponentsSnapshot, out string matchFirstAttackerSnapshot, out int firstAttackerPlayerId)
    {
        battleOpponentsSnapshot = SerializeIntMap(BuildBattleOpponentSnapshotMap());
        matchFirstAttackerSnapshot = SerializeIntMap(BuildMatchFirstAttackerSnapshotMap());
        firstAttackerPlayerId = FirstAttackerPlayerId;
    }

    public string CaptureBattleActiveSnapshot()
    {
        var parts = new List<string>();
        foreach (var player in AllPlayers)
        {
            if (!TryGetPlayerIdSafe(player, out int playerId) || playerId < 0)
            {
                continue;
            }

            int opponentId = TryGetBattleOpponentSnapshot(playerId, out int opponent) ? opponent : -1;
            int matchFirstAttackerId = TryGetMatchFirstAttackerSnapshot(playerId, out int firstAttacker) ? firstAttacker : -1;
            bool isFighting = false;
            bool isAttacker = false;
            try
            {
                isFighting = player.IsActivelyFighting;
                isAttacker = player.IsAttackerInCurrentBattle;
            }
            catch (InvalidOperationException)
            {
            }

            parts.Add($"p={playerId};opp={opponentId};first={matchFirstAttackerId};fighting={isFighting};attacker={isAttacker}");
        }

        return string.Join("|", parts.OrderBy(part => part));
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

        if (firstAttackerPlayerId >= 0 && HasBattleSnapshotWriteAuthority())
        {
            FirstAttackerPlayerId = firstAttackerPlayerId;
        }

        PublishBattleSnapshotMap();
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
        bool hasWriteAuthority = HasBattleSnapshotWriteAuthority();
        if (_battleOpponents.Count > 0 && _matchFirstAttacker.Count > 0)
        {
            if (hasWriteAuthority)
            {
                PublishBattleSnapshotMap();
            }
            return;
        }

        if (!hasWriteAuthority)
        {
            CacheNetworkedBattleSnapshotMap();
            return;
        }

        var alivePlayers = AllPlayers.Where(p => p != null && p.GetHealth() > 0).ToList();
        if (alivePlayers.Count == 0)
        {
            _battleOpponents.Clear();
            _matchFirstAttacker.Clear();
            PublishBattleSnapshotMap();
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
            PublishBattleSnapshotMap();

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
        PublishBattleSnapshotMap();
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
