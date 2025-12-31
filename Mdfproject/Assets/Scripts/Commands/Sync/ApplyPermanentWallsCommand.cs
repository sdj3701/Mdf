// Assets/Scripts/Commands/Sync/ApplyPermanentWallsCommand.cs

using UnityEngine;

/// <summary>
/// 서버에서 적용된 영구 벽 위치를 클라이언트에 동기화하는 커맨드
/// </summary>
public class ApplyPermanentWallsCommand : ICommand
{
    public int PlayerId { get; set; }
    public int[] FlatPositions { get; private set; }

    public ApplyPermanentWallsCommand(int playerId, int[] flatPositions)
    {
        PlayerId = playerId;
        FlatPositions = flatPositions ?? System.Array.Empty<int>();
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null) return;

        var player = gm.GetPlayer(PlayerId);
        if (player?.fieldManager != null)
        {
            player.fieldManager.ApplyPermanentWallsFromServer(FlatPositions);
            Debug.Log($"<color=cyan>[ApplyPermanentWallsCommand] Player {PlayerId}: {FlatPositions.Length}개 영구 벽 적용</color>");
        }
    }
}
