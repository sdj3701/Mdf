// Assets/Scripts/Commands/Sync/SyncAugmentsCommand.cs

using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 서버에서 생성한 증강체 목록을 클라이언트에 동기화하는 커맨드
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
        var gm = GameManagers.Instance;
        if (gm == null) return;

        // 서버는 이미 증강체 목록을 가지고 있으므로 무시
        if (gm.Object != null && gm.Object.HasStateAuthority) return;

        var player = gm.GetPlayer(PlayerId);
        if (player?.augmentManager != null)
        {
            // 증강 데이터가 Addressables에서 로드될 때까지 대기
            await player.augmentManager.WaitUntilAugmentDataLoaded();
            player.augmentManager.SetPresentedAugmentsByNames(AugmentNames);
            Debug.Log($"<color=magenta>[SyncAugmentsCommand] Player {PlayerId}: {AugmentNames.Length}개 증강체 동기화 완료</color>");
        }
    }
}
