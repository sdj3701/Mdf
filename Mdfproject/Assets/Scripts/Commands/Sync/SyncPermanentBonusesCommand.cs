// Assets/Scripts/Commands/Sync/SyncPermanentBonusesCommand.cs

using UnityEngine;

/// <summary>
/// 서버에서 적용된 영구 증강 보너스를 클라이언트에 동기화하는 커맨드
/// </summary>
public class SyncPermanentBonusesCommand : ICommand
{
    public int PlayerId { get; set; }
    public float AttackDamagePercent { get; private set; }
    public float AttackSpeedPercent { get; private set; }

    public SyncPermanentBonusesCommand(int playerId, float attackDamagePercent, float attackSpeedPercent)
    {
        PlayerId = playerId;
        AttackDamagePercent = attackDamagePercent;
        AttackSpeedPercent = attackSpeedPercent;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null) return;

        var player = gm.GetPlayer(PlayerId);
        if (player != null)
        {
            player.permanentAttackDamagePercent = AttackDamagePercent;
            player.permanentAttackSpeedPercent = AttackSpeedPercent;
            player.ApplyPermanentBonusesToUnitsOnField();
            Debug.Log($"<color=cyan>[SyncPermanentBonusesCommand] Player {PlayerId}: AttackDmg={AttackDamagePercent:P0}, AttackSpd={AttackSpeedPercent:P0}</color>");
        }
    }
}
