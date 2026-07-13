using Fusion;
using UnityEngine;

public static class BattleCommandValidator
{
    public static bool IsBattlePhase(GameManagers gm)
    {
        return gm != null &&
               !gm.IsSequenceTransitioning &&
               (gm.currentState == GameManagers.GameState.Battle1 ||
                gm.currentState == GameManagers.GameState.Battle2);
    }

    public static bool IsFiniteTargetPosition(Vector3 position)
    {
        return float.IsFinite(position.x) &&
               float.IsFinite(position.y) &&
               float.IsFinite(position.z);
    }

    public static bool ResolveActorPlayer(
        GameManagers gm,
        int playerId,
        CommandType commandType,
        out PlayerManager actor,
        out BattleCommandResult result,
        CommandExecutionScope scope = CommandExecutionScope.ClientRequest,
        string source = null)
    {
        actor = null;
        if (gm == null)
        {
            result = BattleCommandResult.Rejected(commandType, playerId, "game_managers_missing", null, -1, scope, source);
            return false;
        }

        if (!IsBattlePhase(gm))
        {
            result = BattleCommandResult.Rejected(commandType, playerId, "command_requires_active_battle_phase", null, -1, scope, source);
            return false;
        }

        actor = gm.GetPlayer(playerId);
        if (actor == null)
        {
            result = BattleCommandResult.Rejected(commandType, playerId, "actor_player_missing", null, -1, scope, source);
            return false;
        }

        result = BattleCommandResult.Accepted(commandType, playerId, "actor_resolved", -1, scope, source);
        return true;
    }

    public static bool ResolveOpponent(
        GameManagers gm,
        PlayerManager actor,
        int expectedOpponentId,
        CommandType commandType,
        out PlayerManager opponent,
        out BattleCommandResult result,
        CommandExecutionScope scope = CommandExecutionScope.ClientRequest,
        string source = null)
    {
        opponent = null;
        int actorPlayerId = SafePlayerId(actor);
        if (gm == null)
        {
            result = BattleCommandResult.Rejected(commandType, actorPlayerId, "game_managers_missing", null, expectedOpponentId, scope, source);
            return false;
        }

        if (actor == null || actorPlayerId < 0)
        {
            result = BattleCommandResult.Rejected(commandType, actorPlayerId, "actor_player_missing", null, expectedOpponentId, scope, source);
            return false;
        }

        int resolvedOpponentId;
        bool hasSnapshot = gm.TryGetBattleOpponentSnapshot(actorPlayerId, out resolvedOpponentId);
        if (!hasSnapshot && CanUseAuthorityOpponentFallback(gm, scope))
        {
            resolvedOpponentId = gm.GetBattleOpponent(actorPlayerId);
            hasSnapshot = resolvedOpponentId >= 0;
        }

        if (!hasSnapshot)
        {
            result = BattleCommandResult.Rejected(commandType, actorPlayerId, "battle_opponent_snapshot_missing", null, expectedOpponentId, scope, source);
            return false;
        }

        if (resolvedOpponentId < 0)
        {
            result = BattleCommandResult.Rejected(commandType, actorPlayerId, "battle_opponent_missing", null, expectedOpponentId, scope, source);
            return false;
        }

        if (expectedOpponentId >= 0 && resolvedOpponentId != expectedOpponentId)
        {
            result = BattleCommandResult.Rejected(commandType, actorPlayerId, "battle_opponent_mismatch", null, expectedOpponentId, scope, source);
            return false;
        }

        opponent = gm.GetPlayer(resolvedOpponentId);
        if (opponent == null)
        {
            result = BattleCommandResult.Rejected(commandType, actorPlayerId, "opponent_player_missing", null, resolvedOpponentId, scope, source);
            return false;
        }

        result = BattleCommandResult.Accepted(commandType, actorPlayerId, "opponent_resolved", resolvedOpponentId, scope, source);
        return true;
    }

    public static bool IsCurrentBattleAttacker(PlayerManager actor)
    {
        return actor != null && actor.IsActivelyFighting && actor.IsAttackerInCurrentBattle;
    }

    public static bool IsCurrentBattleDefender(PlayerManager actor)
    {
        return actor != null && actor.IsActivelyFighting && !actor.IsAttackerInCurrentBattle;
    }

    public static bool IsServerAiOrTestAuthority(PlayerManager actor, CommandExecutionScope scope)
    {
        if (actor == null || scope != CommandExecutionScope.ServerAuthorityOnly)
        {
            return false;
        }

        var gm = GameManagers.Instance;
        if (gm == null || gm.Object == null || !gm.Object.HasStateAuthority)
        {
            return false;
        }

        int playerId = SafePlayerId(actor);
        if (playerId >= 0 && ComponentRegistry.Has<AIPlayerController>(playerId.ToString()))
        {
            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return MPTestCommandLine.IsEnabled;
#else
        return false;
#endif
    }

    public static bool IsAuthorizedClientSource(PlayerManager actor, RpcInfo info)
    {
        return IsAuthorizedClientSource(actor, info.Source);
    }

    public static bool IsAuthorizedClientSource(PlayerManager actor, PlayerRef source)
    {
        return actor != null &&
               actor.Object != null &&
               actor.Object.IsValid &&
               source != PlayerRef.None &&
               actor.Object.InputAuthority == source;
    }

    public static bool IsInsideBattleSpawnZone(FieldManager defenderField, Vector3 position)
    {
        return IsWithinTotalFieldBounds(defenderField, position) &&
               !IsWithinInnerGridBounds(defenderField, position);
    }

    public static bool TryResolveExactBattleSpawnPosition(
        FieldManager defenderField,
        Vector3 requestedPosition,
        out Vector2Int navigationCell,
        out Vector3 exactWorldPosition,
        out string errorCode)
    {
        navigationCell = default;
        exactWorldPosition = default;
        errorCode = null;

        if (defenderField == null)
        {
            errorCode = "defender_field_missing";
            return false;
        }

        if (!IsFiniteTargetPosition(requestedPosition))
        {
            errorCode = "spawn_position_not_finite";
            return false;
        }

        float cellSize = Mathf.Max(Mathf.Epsilon, defenderField.cellSize);
        Vector3 totalOrigin = defenderField.TotalGridOrigin;
        navigationCell = new Vector2Int(
            Mathf.FloorToInt((requestedPosition.x - totalOrigin.x) / cellSize),
            Mathf.FloorToInt((requestedPosition.z - totalOrigin.z) / cellSize));
        if (!defenderField.IsValidNavigationCell(navigationCell))
        {
            errorCode = "spawn_cell_outside_navigation_grid";
            return false;
        }

        if (defenderField.TryNavigationCellToInnerCell(navigationCell, out Vector3Int innerCell))
        {
            if (defenderField.HasWallAt(innerCell))
            {
                errorCode = "spawn_cell_blocked_by_wall";
                return false;
            }

            if (defenderField.IsUnitAt(innerCell))
            {
                errorCode = "spawn_cell_occupied";
                return false;
            }
        }

        if (IsLivingMonsterInNavigationCell(defenderField, navigationCell))
        {
            errorCode = "spawn_cell_occupied";
            return false;
        }

        if (!IsInsideBattleSpawnZone(defenderField, requestedPosition))
        {
            errorCode = "spawn_position_outside_battle_spawn_zone";
            return false;
        }

        exactWorldPosition = defenderField.NavigationCellToWorld(navigationCell);
        exactWorldPosition.y = requestedPosition.y;
        return true;
    }

    public static bool TryValidateBattleSpawnPath(
        FieldManager defenderField,
        Vector3 exactWorldPosition,
        MonsterData monsterData,
        out string errorCode)
    {
        errorCode = null;
        AstarGrid grid = defenderField?.playerManager?.astarGrid;
        Transform goal = defenderField?.playerManager?.goalTransform;
        if (grid == null || goal == null)
        {
            errorCode = "spawn_path_context_missing";
            return false;
        }

        Vector2Int start = defenderField.WorldToNavigationCell(exactWorldPosition);
        Vector2Int end = defenderField.WorldToNavigationCell(goal.position);
        bool ignoreBreakableWalls = monsterData != null &&
                                    (monsterData.traits & MonsterTraits.Destroyer) != 0;
        if (!grid.FindPath(start, end, ignoreWalls: false, ignoreBreakableWalls: ignoreBreakableWalls) ||
            grid.FinalPath == null ||
            grid.FinalPath.Count == 0)
        {
            errorCode = "spawn_cell_path_unavailable";
            return false;
        }

        return true;
    }

    public static bool IsInsideScrollTargetDomain(MagicScrollData scroll, FieldManager defenderField, Vector3 position)
    {
        return scroll != null &&
               scroll.skillData != null &&
               IsWithinTotalFieldBounds(defenderField, position);
    }

    private static bool IsWithinTotalFieldBounds(FieldManager field, Vector3 position)
    {
        if (field == null || !IsFiniteTargetPosition(position))
        {
            return false;
        }

        Vector3 origin = field.TotalGridOrigin;
        Vector2Int size = field.TotalGridSize;
        float cellSize = field.cellSize;
        float epsilon = Mathf.Max(0.01f, cellSize * 0.05f);
        float minX = origin.x - epsilon;
        float minZ = origin.z - epsilon;
        float maxX = origin.x + size.x * cellSize + epsilon;
        float maxZ = origin.z + size.y * cellSize + epsilon;

        return position.x >= minX &&
               position.x <= maxX &&
               position.z >= minZ &&
               position.z <= maxZ;
    }

    private static bool IsWithinInnerGridBounds(FieldManager field, Vector3 position)
    {
        if (field == null || !IsFiniteTargetPosition(position))
        {
            return false;
        }

        Vector3 origin = field.gridOrigin;
        Vector2Int size = field.gridSize;
        float cellSize = field.cellSize;
        float epsilon = Mathf.Max(0.01f, cellSize * 0.05f);
        float minX = origin.x - epsilon;
        float minZ = origin.z - epsilon;
        float maxX = origin.x + size.x * cellSize + epsilon;
        float maxZ = origin.z + size.y * cellSize + epsilon;

        return position.x >= minX &&
               position.x <= maxX &&
               position.z >= minZ &&
               position.z <= maxZ;
    }

    private static bool CanUseAuthorityOpponentFallback(GameManagers gm, CommandExecutionScope scope)
    {
        return scope == CommandExecutionScope.ServerAuthorityOnly &&
               gm != null &&
               gm.Object != null &&
               gm.Object.HasStateAuthority;
    }

    private static int SafePlayerId(PlayerManager player)
    {
        if (player == null)
        {
            return -1;
        }

        try
        {
            return player.playerId;
        }
        catch (System.InvalidOperationException)
        {
            return -1;
        }
    }

    private static bool IsLivingMonsterInNavigationCell(FieldManager defenderField, Vector2Int navigationCell)
    {
        Transform monsterParent = defenderField?.playerManager?.monsterSpawner?.monsterParent;
        if (monsterParent == null)
        {
            return false;
        }

        foreach (Transform child in monsterParent)
        {
            if (child == null || !child.gameObject.activeInHierarchy ||
                !child.TryGetComponent(out Monster monster) ||
                monster.CurrentHealth <= 0f)
            {
                continue;
            }

            if (monster.Object != null && !monster.Object.IsValid)
            {
                continue;
            }

            if (defenderField.WorldToNavigationCell(child.position) == navigationCell)
            {
                return true;
            }
        }

        return false;
    }
}
