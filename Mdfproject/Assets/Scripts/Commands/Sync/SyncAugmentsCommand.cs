// Assets/Scripts/Commands/Sync/SyncAugmentsCommand.cs

using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 서버에서 생성한 증강체 목록을 클라이언트에 동기화하는 커맨드
/// 동기화 완료 후 UI 활성화 이벤트를 트리거합니다.
/// </summary>
public class SyncAugmentsCommand : ICommand
{
    public int PlayerId { get; set; }
    public string[] AugmentNames { get; private set; }

    public SyncAugmentsCommand(int playerId, string[] augmentNames)
    {
        PlayerId = playerId;
        AugmentNames = augmentNames ?? System.Array.Empty<string>();
    }

    public async void Execute()
    {
        Debug.Log($"<color=yellow>[흐름 5] SyncAugmentsCommand.Execute() 시작: PlayerId={PlayerId}, 증강 수={AugmentNames?.Length ?? 0}</color>");
        
        var gm = GameManagers.Instance;
        if (gm == null)
        {
            Debug.LogError("[흐름 5-ERROR] GameManagers.Instance가 null입니다!");
            return;
        }

        var player = gm.GetPlayer(PlayerId);
        if (player?.augmentManager == null)
        {
            Debug.LogError($"[흐름 5-ERROR] Player {PlayerId} 또는 augmentManager가 null입니다!");
            return;
        }

        // 서버는 이미 데이터가 있으므로 동기화 건너뛰기
        bool isServer = gm.Object != null && gm.Object.HasStateAuthority;
        Debug.Log($"<color=yellow>[흐름 6] SyncAugmentsCommand: isServer={isServer}</color>");
        
        if (!isServer)
        {
            Debug.Log($"<color=yellow>[흐름 6-A] 클라이언트: SetPresentedAugmentsByNamesAsync 호출</color>");
            await player.augmentManager.SetPresentedAugmentsByNamesAsync(AugmentNames);
            Debug.Log($"<color=magenta>[SyncAugmentsCommand] Player {PlayerId}: {AugmentNames.Length}개 증강체 동기화 완료</color>");
        }

        // 로컬 플레이어인 경우에만 UI 활성화 이벤트 트리거
        bool isLocalPlayer = gm.localPlayer != null && gm.localPlayer.playerId == PlayerId;
        Debug.Log($"<color=yellow>[흐름 7] SyncAugmentsCommand: localPlayer={gm.localPlayer?.playerId ?? -1}, PlayerId={PlayerId}, isLocalPlayer={isLocalPlayer}</color>");
        
        if (isLocalPlayer)
        {
            var presentedAugments = player.augmentManager.GetPresentedAugments();
            Debug.Log($"<color=yellow>[흐름 8] SyncAugmentsCommand: TriggerAugmentPhaseStart 호출 전, 증강 수={presentedAugments?.Count ?? 0}</color>");
            
            GameEvents.TriggerAugmentPhaseStart(player, presentedAugments);
            Debug.Log($"<color=cyan>[흐름 9] SyncAugmentsCommand: Player {PlayerId} 증강 UI 활성화 이벤트 트리거 완료!</color>");
        }
    }
}
