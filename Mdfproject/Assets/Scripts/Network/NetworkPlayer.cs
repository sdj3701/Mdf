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

    private ChangeDetector _changeDetector;

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

            // 서버에 닉네임 설정을 요청하는 RPC를 호출합니다.
            RPC_SetInitialData(nickname);
        }
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
                    if (JoinLobbyUI.Instance != null)
                    {
                        JoinLobbyUI.Instance.UpdatePlayerList();
                    }
                    break;
            }
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetInitialData(string nickname)
    {
        this.Nickname = nickname;
        this.IsReady = false;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_ToggleReady()
    {
        IsReady = !IsReady;
    }
}
