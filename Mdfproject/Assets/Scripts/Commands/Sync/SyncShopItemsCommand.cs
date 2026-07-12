// Assets/Scripts/Commands/Sync/SyncShopItemsCommand.cs

using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

/// <summary>
/// 서버에서 생성한 상점 아이템을 클라이언트에 동기화하는 커맨드입니다.
/// </summary>
public class SyncShopItemsCommand : ICommand, IAsyncCommand
{
    public int PlayerId { get; set; }
    public string[] UnitDataNames { get; private set; }
    public int[] StarLevels { get; private set; }
    private static readonly Dictionary<string, float> RecentSyncPayloads = new Dictionary<string, float>();
    private const float SyncPayloadDedupWindowSeconds = 1.5f;

    private static void TraceClient(string message)
    {
        BuildDebugGUI.LogClient($"[SyncShop] {message}");
    }

    public SyncShopItemsCommand(int playerId, string[] unitDataNames, int[] starLevels)
    {
        PlayerId = playerId;
        UnitDataNames = unitDataNames ?? System.Array.Empty<string>();
        StarLevels = starLevels ?? System.Array.Empty<int>();
    }

    private async UniTask<PlayerManager> WaitForPlayerAsync(GameManagers gm, CancellationToken cancellationToken)
    {
        const float timeoutSeconds = 12f;
        float waited = 0f;

        while (waited < timeoutSeconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var player = gm.GetPlayer(PlayerId);
            if (player != null)
            {
                return player;
            }

            await UniTask.Delay(100, cancellationToken: cancellationToken);
            waited += 0.1f;
        }

        TraceClient($"WaitForPlayer timeout target={PlayerId}");
        return null;
    }

    public void Execute()
    {
        ExecuteAsync(CancellationToken.None).Forget();
    }

    public async UniTask<CommandExecutionResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ExecuteCoreAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return CommandExecutionResult.Completed();
        }
        catch (System.OperationCanceledException)
        {
            return CommandExecutionResult.Canceled();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SyncShopItemsCommand] Execution failed. target={PlayerId}, error={ex}");
            return CommandExecutionResult.Failed(ex.Message);
        }
    }

    private async UniTask ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var gm = GameManagers.Instance;
        if (gm == null) return;

        // 서버는 이미 원본 데이터를 가지고 있으므로 클라이언트에서만 적용
        if (gm.Object != null && gm.Object.HasStateAuthority) return;

        TraceClient($"Execute enter target={PlayerId}, items={UnitDataNames.Length}");
        bool uiReady = await gm.EnsureGameUIReadyForSyncCommands();
        cancellationToken.ThrowIfCancellationRequested();
        if (!uiReady)
        {
            Debug.LogWarning($"[SyncShopItemsCommand] UI readiness timeout before sync. target={PlayerId}");
            TraceClient($"UI readiness timeout before sync. target={PlayerId}");
        }
        else
        {
            TraceClient("UI readiness confirmed.");
        }

        var player = await WaitForPlayerAsync(gm, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (player == null)
        {
            // Debug.LogWarning($"[SyncShopItemsCommand] Player {PlayerId} not ready. Sync skipped.");
            TraceClient($"Player not ready timeout. target={PlayerId}");
            return;
        }

        if (player.shopManager == null)
        {
            player.shopManager = player.GetComponentInChildren<ShopManager>(true);
        }

        if (player.shopManager == null)
        {
            // Debug.LogWarning($"[SyncShopItemsCommand] Player {PlayerId} shopManager is null. Sync skipped.");
            TraceClient($"shopManager null. target={PlayerId}");
            return;
        }

        if (player.shopManager.playerManager == null)
        {
            player.shopManager.playerManager = player;
        }

        string payloadKey = BuildPayloadKey(gm);
        if (IsDuplicatePayload(payloadKey))
        {
            TraceClient($"Skip duplicate shop sync key={payloadKey}");
            return;
        }

        await player.shopManager.SetShopItemsFromServerAsync(UnitDataNames, StarLevels);
        cancellationToken.ThrowIfCancellationRequested();
        TraceClient($"SetShopItems applied target={PlayerId}, items={UnitDataNames.Length}");
        // Debug.Log($"<color=cyan>[SyncShopItemsCommand] Player {PlayerId}: {UnitDataNames.Length}개 상점 아이템 동기화 완료</color>");
    }

    private string BuildPayloadKey(GameManagers gm)
    {
        int round = -1;
        if (gm != null)
        {
            try
            {
                round = gm.currentRound;
            }
            catch (System.InvalidOperationException)
            {
                round = -1;
            }
        }
        string names = UnitDataNames != null ? string.Join(",", UnitDataNames) : "none";
        string stars = StarLevels != null ? string.Join(",", StarLevels.Select(star => star.ToString())) : "none";
        return $"round={round}|player={PlayerId}|items={names}|stars={stars}";
    }

    private static bool IsDuplicatePayload(string key)
    {
        float now = Time.unscaledTime;
        lock (RecentSyncPayloads)
        {
            var staleKeys = RecentSyncPayloads
                .Where(kv => now - kv.Value > SyncPayloadDedupWindowSeconds * 4f)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var stale in staleKeys)
            {
                RecentSyncPayloads.Remove(stale);
            }

            if (RecentSyncPayloads.TryGetValue(key, out float lastAt))
            {
                if (now - lastAt <= SyncPayloadDedupWindowSeconds)
                {
                    return true;
                }
            }

            RecentSyncPayloads[key] = now;
            return false;
        }
    }
}
