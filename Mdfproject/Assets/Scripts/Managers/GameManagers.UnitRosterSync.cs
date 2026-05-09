using System.Linq;
using Fusion;

public partial class GameManagers
{
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ReconcilePlayerUnitRosterCompact(int ownerPlayerId, int[] flatRoster)
    {
        if (Object != null && Object.HasStateAuthority)
        {
            return;
        }

        var player = AllPlayers.FirstOrDefault(candidate =>
            candidate != null &&
            candidate.playerId == ownerPlayerId);
        if (player == null)
        {
            return;
        }

        player.ApplyCompactUnitRosterFromAuthority(flatRoster);
    }
}
