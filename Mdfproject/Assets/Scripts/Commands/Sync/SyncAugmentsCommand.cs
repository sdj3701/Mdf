// Assets/Scripts/Commands/Sync/SyncAugmentsCommand.cs

using UnityEngine;
using Cysharp.Threading.Tasks;
using Fusion;
using System.Linq;

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

    private static PlayerManager ResolveLocalPlayer(GameManagers gm)
    {
        if (gm == null)
        {
            return null;
        }

        if (gm.localPlayer != null && gm.localPlayer.Object != null && gm.localPlayer.Object.IsValid)
        {
            return gm.localPlayer;
        }

        var byInputAuthority = gm.AllPlayers.FirstOrDefault(p => p != null && p.Object != null && p.Object.IsValid && p.Object.HasInputAuthority);
        if (byInputAuthority != null)
        {
            return byInputAuthority;
        }

        if (gm.Runner == null || !gm.Runner.IsRunning)
        {
            return null;
        }

        PlayerRef localRef = gm.Runner.LocalPlayer;
        if (localRef == PlayerRef.None)
        {
            return null;
        }

        return gm.AllPlayers.FirstOrDefault(p => p != null && p.Object != null && p.Object.IsValid && p.Object.InputAuthority == localRef);
    }

    private static bool TryGetPlayerIdSafe(PlayerManager player, out int playerId)
    {
        playerId = -1;
        if (player == null || player.Object == null || !player.Object.IsValid)
        {
            return false;
        }

        try
        {
            playerId = player.playerId;
            return true;
        }
        catch (System.InvalidOperationException)
        {
            return false;
        }
    }

    private static string BuildLocalDebugSnapshot(GameManagers gm, PlayerManager targetPlayer, int targetPlayerId)
    {
        string localId = TryGetPlayerIdSafe(gm?.localPlayer, out int resolvedLocalId) ? resolvedLocalId.ToString() : "null";
        string targetInput = targetPlayer != null && targetPlayer.Object != null && targetPlayer.Object.IsValid
            ? targetPlayer.Object.InputAuthority.ToString()
            : "invalid";
        string runnerLocal = (gm != null && gm.Runner != null && gm.Runner.IsRunning)
            ? gm.Runner.LocalPlayer.ToString()
            : "None";
        return $"target={targetPlayerId}, local={localId}, targetInput={targetInput}, runnerLocal={runnerLocal}";
    }

    private async UniTask<bool> WaitForLocalMatchAsync(GameManagers gm, PlayerManager player)
    {
        const int maxAttempts = 20;
        int lastKnownLocalId = -1;

        for (int i = 0; i < maxAttempts; i++)
        {
            var resolvedLocal = ResolveLocalPlayer(gm);
            if (resolvedLocal != null && gm.localPlayer != resolvedLocal)
            {
                gm.localPlayer = resolvedLocal;
            }

            bool byAuthority = player.Object != null && player.Object.IsValid && player.Object.HasInputAuthority;
            bool byRunnerRef = gm.Runner != null &&
                               gm.Runner.IsRunning &&
                               gm.Runner.LocalPlayer != PlayerRef.None &&
                               player.Object != null &&
                               player.Object.IsValid &&
                               player.Object.InputAuthority == gm.Runner.LocalPlayer;

            bool byId = false;
            if (TryGetPlayerIdSafe(gm.localPlayer, out int localPlayerId))
            {
                lastKnownLocalId = localPlayerId;
                byId = localPlayerId == PlayerId;

                // 로컬 플레이어가 이미 다른 플레이어로 확정되었다면
                // 이 커맨드는 원격 플레이어 동기화이므로 UI 트리거를 기다리지 않는다.
                if (!byAuthority && !byRunnerRef && localPlayerId != PlayerId)
                {
                    Debug.Log($"[SyncAugmentsCommand] Skip remote augment UI sync. {BuildLocalDebugSnapshot(gm, player, PlayerId)}");
                    return false;
                }
            }

            if (byAuthority || byRunnerRef || byId)
            {
                return true;
            }

            await UniTask.Delay(100);
        }

        bool targetHasInputAuthority = player.Object != null && player.Object.IsValid && player.Object.HasInputAuthority;
        bool targetMatchesRunnerLocal = gm.Runner != null &&
                                        gm.Runner.IsRunning &&
                                        gm.Runner.LocalPlayer != PlayerRef.None &&
                                        player.Object != null &&
                                        player.Object.IsValid &&
                                        player.Object.InputAuthority == gm.Runner.LocalPlayer;
        bool shouldHaveMatched = targetHasInputAuthority || targetMatchesRunnerLocal || lastKnownLocalId == PlayerId;

        if (shouldHaveMatched)
        {
            Debug.LogWarning($"[SyncAugmentsCommand] Local player match timeout. {BuildLocalDebugSnapshot(gm, player, PlayerId)}");
        }
        else
        {
            Debug.Log($"[SyncAugmentsCommand] Skip non-local augment UI trigger after wait. {BuildLocalDebugSnapshot(gm, player, PlayerId)}");
        }

        return false;
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

        bool isLocalPlayer = await WaitForLocalMatchAsync(gm, player);
        if (!isLocalPlayer)
        {
            return;
        }

        var presentedAugments = player.augmentManager.GetPresentedAugments();
        Debug.Log($"[SyncAugmentsCommand] TriggerAugmentPhaseStart {BuildLocalDebugSnapshot(gm, player, PlayerId)}, choices={presentedAugments?.Count ?? 0}");
        GameEvents.TriggerAugmentPhaseStart(player, presentedAugments);
        // Debug.Log($"<color=cyan>[SyncAugmentsCommand] Player {PlayerId} augment UI event triggered. choices={presentedAugments?.Count ?? 0}</color>");
    }
}
