// Assets/Scripts/Network/NetworkPlayer.cs
using UnityEngine;
using Fusion;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

public enum LobbyMatchLoadingState
{
    Idle = 0,
    Warming = 1,
    Ready = 2,
    Failed = 3
}

/// <summary>
/// 네트워크 플레이어 오브젝트 (Fusion 2.0 ChangeDetector 방식 적용, PlayerPrefs 사용)
/// </summary>
public class NetworkPlayer : NetworkBehaviour
{
    [Networked] public NetworkString<_16> Nickname { get; set; }
    [Networked] public NetworkBool IsReady { get; set; }
    [Networked] public int SelectedKingUnitKeyHash { get; set; }
    [Networked] public int MatchContentLoadRevision { get; set; }
    [Networked] public int MatchContentLoadStateValue { get; set; }

    private ChangeDetector _changeDetector;
    private int _lastCachedKingSelectionHash;
    private bool _cachedKingSelectionWithDurableIdentity;
    private int _localMatchContentLoadRevision = -1;
    private CancellationTokenSource _localMatchContentLoadCancellation;

    public LobbyMatchLoadingState MatchContentLoadState =>
        (LobbyMatchLoadingState)MatchContentLoadStateValue;

    public override void Spawned()
    {
        base.Spawned();
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        // 이 오브젝트의 소유권을 가진 플레이어(로컬 플레이어)만 닉네임을 설정합니다.
        if (HasInputAuthority)
        {
            // FusionLobbyManager UI 대신 PlayerPrefs에서 닉네임을 가져옵니다.
            // "PlayerNickname" 키로 저장된 값이 없으면 "DefaultName"을 사용합니다.
            string nickname = PlayerPrefs.GetString(PlayerPrefsDefine.NicknameKey, NetworkDefine.DefaultNickname);
            int selectedKingHash = KingSelectionCatalog.NormalizeOrDefaultHash(
                PlayerPrefs.GetInt(KingSelectionCatalog.PlayerPrefsKey, KingSelectionCatalog.DefaultKeyHash));

            // 서버에 닉네임 설정을 요청하는 RPC를 호출합니다.
            RPC_SetInitialData(nickname, selectedKingHash);
        }

        TryRememberKingSelectionForSession();
        TryStartLocalMatchContentLoad();
    }

    public override void Render()
    {
        base.Render();
        foreach (var change in _changeDetector.DetectChanges(this))
        {
            switch (change)
            {
                case nameof(Nickname):
                case nameof(IsReady):
                case nameof(SelectedKingUnitKeyHash):
                case nameof(MatchContentLoadRevision):
                case nameof(MatchContentLoadStateValue):
                    if (JoinLobbyUI.Instance != null)
                    {
                        JoinLobbyUI.Instance.UpdatePlayerList();
                    }
                    break;
            }
        }

        // A remote peer can receive the selection before the replicated durable token
        // fingerprint. Retry only until both values have been cached for host migration.
        TryRememberKingSelectionForSession();
        TryStartLocalMatchContentLoad();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        CancelLocalMatchContentLoad();
        base.Despawned(runner, hasState);
    }

    private void OnDestroy()
    {
        CancelLocalMatchContentLoad();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetInitialData(string nickname, int requestedKingHash)
    {
        this.Nickname = nickname;
        this.IsReady = false;
        this.SelectedKingUnitKeyHash = NetworkManager.Instance != null
            ? NetworkManager.Instance.ResolveInitialLobbyKingSelection(this, requestedKingHash)
            : KingSelectionCatalog.NormalizeOrDefaultHash(requestedKingHash);
        TryRememberKingSelectionForSession();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_ToggleReady()
    {
        if (!KingSelectionCatalog.IsAllowedHash(SelectedKingUnitKeyHash))
        {
            IsReady = false;
            Debug.LogWarning($"[NetworkPlayer] Ready rejected because the king selection is missing or invalid: {SelectedKingUnitKeyHash}");
            return;
        }

        IsReady = !IsReady;
    }

    public bool RequestKingSelection(int requestedKingHash)
    {
        if (!HasInputAuthority || !KingSelectionCatalog.IsAllowedHash(requestedKingHash))
        {
            return false;
        }

        PlayerPrefs.SetInt(KingSelectionCatalog.PlayerPrefsKey, requestedKingHash);
        PlayerPrefs.Save();
        RPC_SetKingSelection(requestedKingHash);
        return true;
    }

    public bool BeginMatchContentLoadingAuthority(int revision)
    {
        if (!HasStateAuthority || revision <= 0)
        {
            return false;
        }

        MatchContentLoadRevision = revision;
        MatchContentLoadStateValue = (int)LobbyMatchLoadingState.Warming;
        RPC_BeginMatchContentLoading(revision);
        TryStartLocalMatchContentLoad();
        return true;
    }

    public void ResetMatchContentLoadingAuthority(int revision)
    {
        if (!HasStateAuthority || MatchContentLoadRevision != revision)
        {
            return;
        }

        MatchContentLoadStateValue = (int)LobbyMatchLoadingState.Idle;
    }

    public bool IsMatchContentReadyFor(int revision)
    {
        return revision > 0
               && MatchContentLoadRevision == revision
               && MatchContentLoadState == LobbyMatchLoadingState.Ready;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BeginMatchContentLoading(int revision)
    {
        if (revision != MatchContentLoadRevision
            || MatchContentLoadState != LobbyMatchLoadingState.Warming)
        {
            return;
        }

        TryStartLocalMatchContentLoad();
        JoinLobbyUI.Instance?.UpdatePlayerList();
    }

    private void TryStartLocalMatchContentLoad()
    {
        int revision = MatchContentLoadRevision;
        if (HasInputAuthority && MatchContentLoadState != LobbyMatchLoadingState.Warming)
        {
            CancelLocalMatchContentLoad();
            return;
        }

        if (!HasInputAuthority
            || revision <= 0
            || MatchContentLoadState != LobbyMatchLoadingState.Warming
            || _localMatchContentLoadRevision == revision)
        {
            return;
        }

        _localMatchContentLoadRevision = revision;
        CancelLocalMatchContentLoad();
        _localMatchContentLoadCancellation = new CancellationTokenSource();
        PrewarmLocalMatchContentAsync(revision, _localMatchContentLoadCancellation).Forget();
    }

    private async UniTaskVoid PrewarmLocalMatchContentAsync(
        int revision,
        CancellationTokenSource attemptCancellation)
    {
        bool succeeded = false;
        string failure = string.Empty;
        try
        {
            if (LoadManager.Instance == null)
            {
                throw new System.InvalidOperationException("LoadManager is unavailable.");
            }

            await LoadManager.Instance.PrewarmMatchContentAsync(
                Runner,
                attemptCancellation.Token);
            succeeded = true;
        }
        catch (System.OperationCanceledException)
        {
            return;
        }
        catch (System.Exception exception)
        {
            failure = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_localMatchContentLoadCancellation, attemptCancellation))
            {
                _localMatchContentLoadCancellation = null;
            }

            attemptCancellation.Dispose();
        }

        if (this == null
            || Object == null
            || !Object.IsValid
            || !HasInputAuthority
            || Runner == null
            || !Runner.IsRunning
            || MatchContentLoadRevision != revision
            || MatchContentLoadState != LobbyMatchLoadingState.Warming)
        {
            return;
        }

        if (!succeeded)
        {
            Debug.LogError(
                $"[MatchPrewarm] Local peer failed. revision={revision}, reason={failure}");
        }

        MPTestLogger.Log(
            "match_prewarm_peer",
            succeeded ? "pass" : "fail",
            succeeded ? null : "local_match_content_failed",
            succeeded ? "local match content ready" : failure,
            new Dictionary<string, object>
            {
                { "revision", revision },
                { "playerRef", Object.InputAuthority }
            });

        if (HasStateAuthority)
        {
            // Host-mode local RPCs report PlayerRef.None as RpcInfo.Source. Commit through the
            // same authority validator with the object's real InputAuthority instead of waiting
            // forever for an RPC source that Fusion intentionally omits locally.
            TryRecordMatchContentLoadingAuthority(
                revision,
                succeeded,
                Object.InputAuthority);
        }
        else
        {
            RPC_ReportMatchContentLoading(revision, succeeded);
        }
    }

    private void CancelLocalMatchContentLoad()
    {
        if (_localMatchContentLoadCancellation == null)
        {
            return;
        }

        _localMatchContentLoadCancellation.Cancel();
        _localMatchContentLoadCancellation = null;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ReportMatchContentLoading(
        int revision,
        NetworkBool succeeded,
        RpcInfo info = default)
    {
        TryRecordMatchContentLoadingAuthority(revision, succeeded, info.Source);
    }

    private bool TryRecordMatchContentLoadingAuthority(
        int revision,
        bool succeeded,
        PlayerRef source)
    {
        if (!HasStateAuthority
            || Object == null
            || !Object.IsValid
            || source != Object.InputAuthority
            || revision != MatchContentLoadRevision
            || MatchContentLoadState != LobbyMatchLoadingState.Warming)
        {
            Debug.LogWarning(
                $"[MatchPrewarm] Rejected stale or unauthorized ACK. source={source}, revision={revision}");
            return false;
        }

        MatchContentLoadStateValue = succeeded
            ? (int)LobbyMatchLoadingState.Ready
            : (int)LobbyMatchLoadingState.Failed;
        Debug.Log(
            $"[MatchPrewarm] Authority ACK recorded. player={Object.InputAuthority}, " +
            $"revision={revision}, success={(bool)succeeded}");
        MPTestLogger.Log(
            "match_prewarm_ack",
            succeeded ? "pass" : "fail",
            succeeded ? null : "peer_match_content_failed",
            "authority recorded peer match content acknowledgement",
            new Dictionary<string, object>
            {
                { "revision", revision },
                { "playerRef", Object.InputAuthority },
                { "success", succeeded }
            });
        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetKingSelection(int requestedKingHash)
    {
        if (!KingSelectionCatalog.IsAllowedHash(requestedKingHash))
        {
            IsReady = false;
            Debug.LogWarning($"[NetworkPlayer] King selection rejected by the server allow-list: {requestedKingHash}");
            return;
        }

        if (SelectedKingUnitKeyHash == requestedKingHash)
        {
            return;
        }

        SelectedKingUnitKeyHash = requestedKingHash;
        IsReady = false;
        TryRememberKingSelectionForSession();
    }

    private void TryRememberKingSelectionForSession()
    {
        int selectionHash = SelectedKingUnitKeyHash;
        if (_lastCachedKingSelectionHash != selectionHash)
        {
            _lastCachedKingSelectionHash = selectionHash;
            _cachedKingSelectionWithDurableIdentity = false;
        }

        if (_cachedKingSelectionWithDurableIdentity
            || !KingSelectionCatalog.IsAllowedHash(selectionHash)
            || NetworkManager.Instance == null)
        {
            return;
        }

        _cachedKingSelectionWithDurableIdentity =
            NetworkManager.Instance.RememberLobbyKingSelection(this);
    }
}
