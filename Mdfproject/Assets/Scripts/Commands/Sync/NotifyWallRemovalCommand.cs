// Assets/Scripts/Commands/Sync/NotifyWallRemovalCommand.cs

using UnityEngine;

/// <summary>
/// 벽 제거 성공을 클라이언트에 알리는 커맨드
/// </summary>
public class NotifyWallRemovalCommand : ICommand
{
    public int PlayerId { get; set; }
    public int X { get; private set; }
    public int Y { get; private set; }

    public NotifyWallRemovalCommand(int playerId, int x, int y)
    {
        PlayerId = playerId;
        X = x;
        Y = y;
    }

    public void Execute()
    {
        var pos = new Vector3Int(X, Y, 0);
        GameEvents.TriggerWallRemovalSucceeded(PlayerId, pos);
        Debug.Log($"<color=green>[NotifyWallRemovalCommand] Player {PlayerId}: 벽 제거 성공 알림 {pos}</color>");
    }
}
