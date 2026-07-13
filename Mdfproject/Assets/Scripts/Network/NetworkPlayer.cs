// Assets/Scripts/Network/NetworkPlayer.cs
using UnityEngine;
using Fusion;

/// <summary>
/// 네트워크 플레이어 오브젝트 (Fusion 2.0 ChangeDetector 방식 적용, PlayerPrefs 사용)
/// </summary>
public class NetworkPlayer : NetworkBehaviour
{
    [Networked] public NetworkString<_16> Nickname { get; set; }
    [Networked] public NetworkBool IsReady { get; set; }
    [Networked] public int SelectedKingUnitKeyHash { get; set; }

    private ChangeDetector _changeDetector;
    private int _lastCachedKingSelectionHash;
    private bool _cachedKingSelectionWithDurableIdentity;

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
