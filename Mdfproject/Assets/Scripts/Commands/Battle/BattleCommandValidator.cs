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
        return IsWithinTotalFieldBounds(defenderField, position);
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
}
