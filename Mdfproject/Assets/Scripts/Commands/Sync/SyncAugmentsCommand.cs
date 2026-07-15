// Assets/Scripts/Commands/Sync/SyncAugmentsCommand.cs

using UnityEngine;
using Cysharp.Threading.Tasks;
using Fusion;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// 서버에서 생성한 증강체 목록을 클라이언트에 동기화하는 커맨드입니다.
/// 동기화 완료 후 로컬 플레이어인 경우 증강 UI 이벤트를 트리거합니다.
/// </summary>
public class SyncAugmentsCommand : ICommand, IAsyncCommand
{
    public int PlayerId { get; set; }
    public string[] AugmentContentIds { get; private set; }
    private static readonly Dictionary<string, float> RecentUiTriggerKeys = new Dictionary<string, float>();
    private const float UiTriggerDedupWindowSeconds = 1.5f;

    private static void TraceClient(string message)
    {
        BuildDebugGUI.LogClient($"[SyncAugments] {message}");
    }

    public SyncAugmentsCommand(int playerId, string[] augmentContentIds)
    {
        PlayerId = playerId;
        AugmentContentIds = augmentContentIds ?? System.Array.Empty<string>();
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

    private static bool IsConfirmedLocalCandidate(GameManagers gm, PlayerManager player)
    {
        if (player == null || player.Object == null || !player.Object.IsValid)
        {
            return false;
        }

        if (player.Object.HasInputAuthority)
        {
            return true;
        }

        return gm != null &&
               gm.Runner != null &&
               gm.Runner.IsRunning &&
               gm.Runner.LocalPlayer != PlayerRef.None &&
               player.Object.InputAuthority == gm.Runner.LocalPlayer;
    }

    private static PlayerManager ResolveLocalPlayer(GameManagers gm)
    {
        if (gm == null)
        {
            return null;
        }

        if (IsConfirmedLocalCandidate(gm, gm.localPlayer))
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

        var byRunnerRef = gm.AllPlayers.FirstOrDefault(p => p != null && p.Object != null && p.Object.IsValid && p.Object.InputAuthority == localRef);
        if (byRunnerRef != null)
        {
            return byRunnerRef;
        }

        if (gm.localPlayer != null && gm.localPlayer.Object != null && gm.localPlayer.Object.IsValid)
        {
            return gm.localPlayer;
        }

        return null;
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

    private async UniTask<bool> WaitForLocalMatchAsync(
        GameManagers gm,
        PlayerManager player,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 60;
        int lastKnownLocalId = -1;

        for (int i = 0; i < maxAttempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            bool hasConfirmedLocal = IsConfirmedLocalCandidate(gm, gm.localPlayer);
            if (hasConfirmedLocal && TryGetPlayerIdSafe(gm.localPlayer, out int localPlayerId))
            {
                lastKnownLocalId = localPlayerId;
                byId = localPlayerId == PlayerId;

                // 로컬 플레이어가 이미 다른 플레이어로 확정되었다면
                // 이 커맨드는 원격 플레이어 동기화이므로 UI 트리거를 기다리지 않는다.
                if (!byAuthority && !byRunnerRef && localPlayerId != PlayerId)
                {
                    string snapshot = BuildLocalDebugSnapshot(gm, player, PlayerId);
                    Debug.Log($"[SyncAugmentsCommand] Skip remote augment UI sync. {snapshot}");
                    TraceClient($"Skip remote augment UI sync. {snapshot}");
                    return false;
                }
            }

            if (byAuthority || byRunnerRef || byId)
            {
                if (i > 0)
                {
                    TraceClient($"Local match resolved attempt={i + 1}/{maxAttempts}. {BuildLocalDebugSnapshot(gm, player, PlayerId)}");
                }
                return true;
            }

            if (i % 15 == 0)
            {
                BuildDebugGUI.LogClientThrottled(
                    $"sync_aug_wait_local_{PlayerId}",
                    $"Waiting local match attempt={i + 1}/{maxAttempts}. {BuildLocalDebugSnapshot(gm, player, PlayerId)}",
                    0.7f);
            }

            await UniTask.Delay(100, cancellationToken: cancellationToken);
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
            string snapshot = BuildLocalDebugSnapshot(gm, player, PlayerId);
            Debug.LogWarning($"[SyncAugmentsCommand] Local player match timeout. {snapshot}");
            TraceClient($"Local player match timeout. {snapshot}");
        }
        else
        {
            string snapshot = BuildLocalDebugSnapshot(gm, player, PlayerId);
            Debug.Log($"[SyncAugmentsCommand] Skip non-local augment UI trigger after wait. {snapshot}");
            TraceClient($"Skip non-local augment UI trigger after wait. {snapshot}");
        }

        return false;
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
            Debug.LogError($"[SyncAugmentsCommand] Execution failed. target={PlayerId}, error={ex}");
            return CommandExecutionResult.Failed(ex.Message);
        }
    }

    private async UniTask ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var gm = GameManagers.Instance;
        if (gm == null)
        {
            // Debug.LogError("[SyncAugmentsCommand] GameManagers.Instance is null.");
            return;
        }

        TraceClient($"Execute enter target={PlayerId}, incomingChoices={AugmentContentIds.Length}");

        var player = await WaitForPlayerAsync(gm, cancellationToken);
        if (player == null)
        {
            // Debug.LogWarning($"[SyncAugmentsCommand] Player {PlayerId} not ready. Sync skipped.");
            TraceClient($"Player not ready timeout. target={PlayerId}");
            return;
        }

        if (player.augmentManager == null)
        {
            player.augmentManager = player.GetComponentInChildren<AugmentManager>(true);
        }

        if (player.augmentManager == null)
        {
            // Debug.LogWarning($"[SyncAugmentsCommand] Player {PlayerId} augmentManager is null. Sync skipped.");
            TraceClient($"augmentManager null. target={PlayerId}");
            return;
        }

        if (player.augmentManager.playerManager == null)
        {
            player.augmentManager.playerManager = player;
        }

        bool isServer = gm.Object != null && gm.Object.HasStateAuthority;
        if (!isServer)
        {
            try
            {
                bool applied = await player.augmentManager.SetPresentedAugmentsByContentIdsAsync(AugmentContentIds);
                cancellationToken.ThrowIfCancellationRequested();
                if (!applied)
                {
                    Debug.LogError($"[SyncAugmentsCommand] Exact presented augment sync rejected. target={PlayerId}");
                    TraceClient($"Exact presented augment sync rejected. target={PlayerId}");
                    return;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SyncAugmentsCommand] SetPresentedAugments failed. target={PlayerId}, error={ex.Message}");
                TraceClient($"SetPresentedAugments failed. target={PlayerId}, error={ex.Message}");
                return;
            }

            // State Authority is already running the bounded Prepare prewarm in GameManagers.
            // This command owns the client-side presentation warmup only.
            if (player.monsterSpawner != null &&
                (player.Object == null || !player.Object.IsValid || !player.Object.HasStateAuthority))
            {
                MonsterData[] presentedBosses = player.augmentManager.GetPresentedAugments()
                    .Select(augment => augment != null && augment.TryGetBossMonster(out MonsterData boss) ? boss : null)
                    .Where(boss => boss != null)
                    .Distinct()
                    .ToArray();
                if (presentedBosses.Length > 0)
                {
                    try
                    {
                        // Client providers must be warm too: replicated monster spawns instantiate
                        // locally even though only State Authority may choose or spawn the boss.
                        await player.monsterSpawner.PrewarmMonsterDataSetAsync(
                            presentedBosses,
                            isBoss: true,
                            requestedCount: 1,
                            context: $"SyncPresentedBosses.P{PlayerId}",
                            cancellationToken: cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    catch (System.OperationCanceledException)
                    {
                        throw;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning(
                            $"[SyncAugmentsCommand] Boss presentation prewarm skipped. target={PlayerId}, error={ex.Message}");
                    }
                }
            }

            TraceClient($"SetPresentedAugments applied. target={PlayerId}, count={AugmentContentIds.Length}");
            bool uiReady = await gm.EnsureGameUIReadyForSyncCommands();
            cancellationToken.ThrowIfCancellationRequested();
            if (!uiReady)
            {
                Debug.LogWarning($"[SyncAugmentsCommand] UI readiness timeout before augment trigger. {BuildLocalDebugSnapshot(gm, player, PlayerId)}");
                TraceClient($"UI readiness timeout before augment trigger. {BuildLocalDebugSnapshot(gm, player, PlayerId)}");
            }
            else
            {
                TraceClient("UI readiness confirmed.");
            }
            // Debug.Log($"<color=magenta>[SyncAugmentsCommand] Player {PlayerId}: {AugmentContentIds.Length} augments synchronized</color>");
        }

        bool isLocalPlayer = await WaitForLocalMatchAsync(gm, player, cancellationToken);
        if (!isLocalPlayer)
        {
            TraceClient("Abort augment UI trigger: non-local target.");
            return;
        }

        // 빌드 환경에서 첫 Prepare 진입 직후에는 UI/로컬 참조가 늦게 준비될 수 있어
        // Host/Client 공통으로 첫 라운드 증강 UI 트리거를 잠시 지연합니다.
        if (gm.currentRound <= 1 &&
            gm.currentState == GameManagers.GameState.Prepare &&
            (UIManagers.Instance == null || !UIManagers.Instance.IsUIElementActive("UI_Pnl_Augment")))
        {
            Debug.Log($"[SyncAugmentsCommand] Delay initial local augment UI trigger by 2s. {BuildLocalDebugSnapshot(gm, player, PlayerId)}");
            TraceClient("Delay initial local augment UI trigger by 2s.");
            await UniTask.Delay(2000, DelayType.Realtime, cancellationToken: cancellationToken);
        }

        var presentedAugments = player.augmentManager.GetPresentedAugments();
        if (presentedAugments == null || presentedAugments.Count == 0)
        {
            TraceClient("Skip augment UI trigger: choices were already consumed.");
            return;
        }

        string triggerKey = BuildUiTriggerKey(gm);
        if (IsDuplicateUiTrigger(triggerKey))
        {
            TraceClient($"Skip duplicate augment UI trigger key={triggerKey}");
            return;
        }

        Debug.Log($"[SyncAugmentsCommand] TriggerAugmentPhaseStart {BuildLocalDebugSnapshot(gm, player, PlayerId)}, choices={presentedAugments?.Count ?? 0}");
        TraceClient($"TriggerAugmentPhaseStart choices={presentedAugments?.Count ?? 0}");
        GameEvents.TriggerAugmentPhaseStart(player, presentedAugments);

        // Build client에서는 Awake/구독 타이밍이 늦을 수 있어 짧게 재시도합니다.
        if (UIManagers.Instance != null
            && !GamePrepareUIToolkitController.IsToolkitActive
            && presentedAugments.Count > 0)
        {
            for (int retry = 0; retry < 3; retry++)
            {
                await UniTask.Delay(120, cancellationToken: cancellationToken);
                if (presentedAugments.Count == 0)
                {
                    TraceClient($"Stop augment UI retry={retry}: choices were consumed.");
                    break;
                }

                if (UIManagers.Instance.IsUIElementActive("UI_Pnl_Augment"))
                {
                    TraceClient($"Augment panel active after retry={retry}");
                    break;
                }

                GameEvents.TriggerAugmentPhaseStart(player, presentedAugments);
                TraceClient($"Re-trigger augment event retry={retry + 1}");
            }
        }
        // Debug.Log($"<color=cyan>[SyncAugmentsCommand] Player {PlayerId} augment UI event triggered. choices={presentedAugments?.Count ?? 0}</color>");
    }

    private string BuildUiTriggerKey(GameManagers gm)
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
        string state = gm != null ? gm.currentState.ToString() : "Unknown";
        string ids = AugmentContentIds != null ? string.Join(",", AugmentContentIds) : "none";
        return $"round={round}|state={state}|player={PlayerId}|augments={ids}";
    }

    private static bool IsDuplicateUiTrigger(string key)
    {
        float now = Time.unscaledTime;
        lock (RecentUiTriggerKeys)
        {
            var staleKeys = RecentUiTriggerKeys
                .Where(kv => now - kv.Value > UiTriggerDedupWindowSeconds * 4f)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var stale in staleKeys)
            {
                RecentUiTriggerKeys.Remove(stale);
            }

            if (RecentUiTriggerKeys.TryGetValue(key, out float lastAt))
            {
                if (now - lastAt <= UiTriggerDedupWindowSeconds)
                {
                    return true;
                }
            }

            RecentUiTriggerKeys[key] = now;
            return false;
        }
    }
}
