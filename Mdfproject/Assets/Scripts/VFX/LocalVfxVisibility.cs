using Fusion;
using UnityEngine;

public enum LocalVfxVisibilityEventKind
{
    Projectile,
    BasicAttack
}

public static class LocalVfxVisibility
{
    public static bool Enabled { get; set; } = true;

    public static int ProjectileReceived { get; private set; }
    public static int ProjectilePlayed { get; private set; }
    public static int ProjectileSkipped { get; private set; }
    public static int BasicAttackReceived { get; private set; }
    public static int BasicAttackPlayed { get; private set; }
    public static int BasicAttackSkipped { get; private set; }

    public static bool ShouldPlay(NetworkObject attacker, NetworkObject target, LocalVfxVisibilityEventKind eventKind)
    {
        TrackReceived(eventKind);

        int eventFieldOwnerId = ResolveEventFieldOwnerPlayerId(attacker, target);
        int viewedPlayerId = ResolveViewedPlayerId();
        bool shouldPlay = !Enabled || ShouldPlayFieldOwner(viewedPlayerId, eventFieldOwnerId);

        if (shouldPlay)
        {
            TrackPlayed(eventKind);
        }
        else
        {
            TrackSkipped(eventKind);
        }

        return shouldPlay;
    }

    public static bool ShouldPlayFieldOwner(int viewedPlayerId, int eventFieldOwnerId)
    {
        return eventFieldOwnerId < 0 || viewedPlayerId < 0 || viewedPlayerId == eventFieldOwnerId;
    }

    public static int ResolveEventFieldOwnerPlayerId(Component attacker, Component target)
    {
        if (TryResolveFieldOwnerPlayerId(attacker, out int ownerId))
        {
            return ownerId;
        }

        return TryResolveFieldOwnerPlayerId(target, out ownerId) ? ownerId : -1;
    }

    public static int ResolveViewedPlayerId()
    {
        CameraManager cameraManager = CameraManager.Instance;
        if (TryGetPlayerId(cameraManager != null ? cameraManager.CurrentViewingField : null, out int viewedPlayerId))
        {
            return viewedPlayerId;
        }

        GameManagers gameManagers = GameManagers.Instance;
        return TryGetPlayerId(gameManagers != null ? gameManagers.localPlayer : null, out viewedPlayerId)
            ? viewedPlayerId
            : -1;
    }

    public static void ResetCountersForTests()
    {
        ProjectileReceived = 0;
        ProjectilePlayed = 0;
        ProjectileSkipped = 0;
        BasicAttackReceived = 0;
        BasicAttackPlayed = 0;
        BasicAttackSkipped = 0;
        Enabled = true;
    }

    private static bool TryResolveFieldOwnerPlayerId(Component component, out int ownerId)
    {
        ownerId = -1;
        if (component == null)
        {
            return false;
        }

        Unit unit = component.GetComponent<Unit>();
        if (unit != null)
        {
            ownerId = unit.OwnerPlayerIdForRoster;
            return ownerId >= 0;
        }

        Monster monster = component.GetComponent<Monster>();
        if (monster != null)
        {
            ownerId = monster.SnapshotOwnerPlayerId;
            return ownerId >= 0;
        }

        PlayerManager player = component.GetComponent<PlayerManager>();
        return TryGetPlayerId(player, out ownerId);
    }

    private static bool TryGetPlayerId(PlayerManager player, out int playerId)
    {
        playerId = -1;
        if (player == null)
        {
            return false;
        }

        try
        {
            playerId = player.playerId;
            return playerId >= 0;
        }
        catch (System.InvalidOperationException)
        {
            return false;
        }
    }

    private static void TrackReceived(LocalVfxVisibilityEventKind eventKind)
    {
        if (eventKind == LocalVfxVisibilityEventKind.Projectile)
        {
            ProjectileReceived++;
        }
        else
        {
            BasicAttackReceived++;
        }
    }

    private static void TrackPlayed(LocalVfxVisibilityEventKind eventKind)
    {
        if (eventKind == LocalVfxVisibilityEventKind.Projectile)
        {
            ProjectilePlayed++;
        }
        else
        {
            BasicAttackPlayed++;
        }
    }

    private static void TrackSkipped(LocalVfxVisibilityEventKind eventKind)
    {
        if (eventKind == LocalVfxVisibilityEventKind.Projectile)
        {
            ProjectileSkipped++;
        }
        else
        {
            BasicAttackSkipped++;
        }
    }
}
