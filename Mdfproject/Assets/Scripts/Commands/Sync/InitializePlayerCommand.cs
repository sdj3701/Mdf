// Assets/Scripts/Commands/Sync/InitializePlayerCommand.cs

using UnityEngine;
using Fusion;
using Cysharp.Threading.Tasks;
using System.Linq;

/// <summary>
/// 플레이어 초기화를 클라이언트에 동기화하는 커맨드
/// 참고: Rpc_InitializePlayer는 복잡한 비동기 로직이므로 기존 RPC 방식 유지 권장
/// 이 커맨드는 향후 확장을 위해 예비로 남겨둠
/// </summary>
public class InitializePlayerCommand : ICommand
{
    public int PlayerId { get; set; }
    public NetworkId GridNetworkId { get; private set; }

    public InitializePlayerCommand(int playerId, NetworkId gridNetworkId)
    {
        PlayerId = playerId;
        GridNetworkId = gridNetworkId;
    }

    public void Execute()
    {
        // Rpc_InitializePlayer의 복잡한 비동기 로직으로 인해
        // 현재는 기존 RPC 방식을 유지하고, 이 커맨드는 향후 확장용으로 예비
        Debug.Log($"<color=yellow>[InitializePlayerCommand] Player {PlayerId} 초기화 (향후 확장용)</color>");
    }
}
