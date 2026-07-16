using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class PlaceUnitCommand : ICommand, IAsyncCommand
{
    public int PlayerId { get; set; }
    public UnitData UnitData { get; private set; }
    public Vector3Int Position { get; private set; }

    public PlaceUnitCommand(int playerId, UnitData unitData, Vector3Int position)
    {
        PlayerId = playerId;
        UnitData = unitData;
        Position = position;
    }

    public void Execute()
    {
        ExecuteAsync(CancellationToken.None).Forget();
    }

    public async UniTask<CommandExecutionResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        GameManagers gm = GameManagers.Instance;
        if (gm == null || gm.Runner == null || !gm.Runner.IsServer)
        {
            return CommandExecutionResult.Failed("game_managers_not_authoritative");
        }

        PlayerManager player = gm.GetPlayer(PlayerId);
        if (player == null || player.fieldManager == null)
        {
            return CommandExecutionResult.Failed("player_or_field_missing");
        }
        if (gm.currentState != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning)
        {
            return CommandExecutionResult.Failed("command_requires_stable_prepare_phase");
        }

        cancellationToken.ThrowIfCancellationRequested();
        FieldManager.UnitPlacementResult result = await player.fieldManager.TryCreateUnitAtAsync(
            UnitData,
            Position,
            1,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Succeeded &&
            (gm != GameManagers.Instance || gm.currentState != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning))
        {
            player.fieldManager.TryRollbackPlacedUnit(result.Unit, "PlaceUnitPhaseChanged");
            return CommandExecutionResult.Failed("phase_changed_during_unit_spawn");
        }
        return result.Succeeded
            ? CommandExecutionResult.Completed()
            : CommandExecutionResult.Failed(result.FailureReason);
    }
}
