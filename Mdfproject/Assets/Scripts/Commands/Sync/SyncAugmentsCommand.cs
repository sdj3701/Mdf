// Assets/Scripts/Commands/Sync/SyncAugmentsCommand.cs

using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 서버에서 생성한 증강체 목록을 클라이언트에 동기화하는 커맨드입니다.
/// 동기화 완료 후 로컬 플레이어인 경우 증강 UI 이벤트를 트리거합니다.
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

    private async UniTask<PlayerManager> WaitForPlayerAsync(GameManagers gm)
    {
        const float timeoutSeconds = 6f;
        float waited = 0f;

        while (waited < timeoutSeconds)
        {
            var player = gm.GetPlayer(PlayerId);
            if (player != null)
            {
                return player;
            }

            await UniTask.Delay(100);
            waited += 0.1f;
        }

        return null;
    }

    public async void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null)
        {
            // Debug.LogError("[SyncAugmentsCommand] GameManagers.Instance is null.");
            return;
        }

        var player = await WaitForPlayerAsync(gm);
        if (player == null)
        {
            // Debug.LogWarning($"[SyncAugmentsCommand] Player {PlayerId} not ready. Sync skipped.");
            return;
        }

        if (player.augmentManager == null)
        {
            player.augmentManager = player.GetComponentInChildren<AugmentManager>(true);
        }

        if (player.augmentManager == null)
        {
            // Debug.LogWarning($"[SyncAugmentsCommand] Player {PlayerId} augmentManager is null. Sync skipped.");
            return;
        }

        if (player.augmentManager.playerManager == null)
        {
            player.augmentManager.playerManager = player;
        }

        bool isServer = gm.Object != null && gm.Object.HasStateAuthority;
        if (!isServer)
        {
            await player.augmentManager.SetPresentedAugmentsByNamesAsync(AugmentNames);
            // Debug.Log($"<color=magenta>[SyncAugmentsCommand] Player {PlayerId}: {AugmentNames.Length}개 증강체 동기화 완료</color>");
        }

        bool isLocalByAuthority = player.Object != null && player.Object.IsValid && player.Object.HasInputAuthority;
        if (isLocalByAuthority && gm.localPlayer != player)
        {
            gm.localPlayer = player;
        }

        bool isLocalPlayer = isLocalByAuthority;
        if (!isLocalPlayer && gm.localPlayer != null)
        {
            try
            {
                isLocalPlayer = gm.localPlayer.playerId == PlayerId;
            }
            catch (System.InvalidOperationException)
            {
                isLocalPlayer = false;
            }
        }

        if (!isLocalPlayer)
        {
            return;
        }

        var presentedAugments = player.augmentManager.GetPresentedAugments();
        GameEvents.TriggerAugmentPhaseStart(player, presentedAugments);
        // Debug.Log($"<color=cyan>[SyncAugmentsCommand] Player {PlayerId} augment UI event triggered. choices={presentedAugments?.Count ?? 0}</color>");
    }
}
