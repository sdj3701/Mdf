// Assets/Scripts/Commands/PlayerActions/ActivateSkillCommand.cs

using UnityEngine;
using Fusion;

public class ActivateSkillCommand : ICommand
{
    public int PlayerId { get; set; }
    public uint UnitNetworkId { get; private set; }

    public ActivateSkillCommand(int playerId, uint unitNetworkId)
    {
        PlayerId = playerId;
        UnitNetworkId = unitNetworkId;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null || gm.Runner == null) return;
        
        // NetworkObject를 Raw ID로 찾기 (RegisterUnitAtCommand와 동일한 패턴)
        NetworkObject unitNO = null;
        foreach (var no in gm.Runner.GetAllNetworkObjects())
        {
            if (no != null && no.Id.Raw == UnitNetworkId)
            {
                unitNO = no;
                break;
            }
        }

        if (unitNO == null)
        {
            Debug.LogWarning($"[ActivateSkillCommand] Unit with NetworkId {UnitNetworkId} not found.");
            return;
        }

        var unit = unitNO.GetComponent<Unit>();
        if (unit != null)
        {
            // 서버에서 스킬 발동 (HasStateAuthorityOrNoNetwork 체크는 Unit 내부에서 수행)
            unit.ActivateSkill();
        }
    }
}

