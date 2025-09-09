// Assets/Scripts/Network/NetworkPlayer.cs
using UnityEngine;
using Fusion;
using TMPro;
using System.Linq; // IEnumerable의 Count() 확장 메서드를 사용하기 위해 추가

/// <summary>
/// 네트워크 플레이어 오브젝트 (Fusion 2 버전)
/// NetworkObject와 함께 프리팹으로 만들어 사용
/// </summary>
public class NetworkPlayer : NetworkBehaviour
{
    // [수정] OnChangedRender 콜백을 사용하여 네트워크 변수가 렌더링 단계에서 변경될 때마다
    // 인스턴스 메소드인 OnChangeDetected가 자동으로 호출됩니다.
    // 이는 UI 업데이트에 더 안전하고 효율적입니다.
    [Networked, OnChangedRender(nameof(OnChangeDetected))]
    public NetworkString<_16> PlayerName { get; set; }

    [Networked, OnChangedRender(nameof(OnChangeDetected))]
    public int PlayerNumber { get; set; }

    [Networked, OnChangedRender(nameof(OnChangeDetected))]
    public NetworkBool IsReady { get; set; }

    [Header("UI References")]
    [SerializeField] private TMP_Text _nameLabel;
    [SerializeField] private GameObject _readyIndicator;
    [SerializeField] private GameObject _hostCrown;

    private static NetworkPlayer _localPlayer;
    public static NetworkPlayer LocalPlayer => _localPlayer;

    public override void Spawned()
    {
        base.Spawned();

        if (HasInputAuthority)
        {
            _localPlayer = this;

            string nickname = PlayerPrefs.GetString("PlayerNickname", $"Player_{Random.Range(1000, 9999)}");
            RPC_SetPlayerName(nickname);

            Debug.Log($"Local player spawned: {nickname}");
        }

        // 플레이어 번호 설정 (호스트가 0, 클라이언트가 1)
        // 이 로직은 간단한 1v1 게임을 가정합니다. 더 많은 플레이어를 지원하려면 수정이 필요합니다.
        if (Runner.IsServer)
        {
            // [수정] .Count 속성 대신 .Count() 메서드를 사용합니다.
            // 플레이어 ID를 기반으로 번호를 할당합니다.
            PlayerNumber = Runner.ActivePlayers.Count() - 1;
        }

        // 씬이 바뀌어도 파괴되지 않도록 설정
        DontDestroyOnLoad(gameObject);

        // 스폰 시점에 UI 즉시 업데이트
        UpdateUI();
    }

    /// <summary>
    /// [수정됨] [Networked] 변수가 변경될 때 렌더링 단계에서 호출되는 콜백 메소드입니다.
    /// 더 이상 static이 아니며, 인스턴스에 직접 접근하여 UI를 업데이트합니다.
    /// </summary>
    private void OnChangeDetected()
    {
        UpdateUI();
    }

    /// <summary>
    /// UI 요소들을 현재 네트워크 상태에 맞게 업데이트합니다.
    /// </summary>
    private void UpdateUI()
    {
        if (_nameLabel != null)
        {
            _nameLabel.text = PlayerName.Value; // NetworkString은 .Value로 값에 접근
            if (HasInputAuthority)
            {
                _nameLabel.color = Color.green; // 내 플레이어는 초록색
            }
        }

        if (_readyIndicator != null)
        {
            _readyIndicator.SetActive(IsReady);
        }

        if (_hostCrown != null)
        {
            // PlayerNumber가 0인 플레이어를 호스트로 간주합니다.
            _hostCrown.SetActive(PlayerNumber == 0);
        }
        
        // JoinLobbyUI에 상태 변경 알림
        if (JoinLobbyUI.Instance != null)
        {
            JoinLobbyUI.Instance.OnPlayerReadyChanged(PlayerNumber, IsReady);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetPlayerName(string name)
    {
        if (name.Length > 16) name = name.Substring(0, 16);
        PlayerName = name;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetReady(NetworkBool ready)
    {
        IsReady = ready;
        Debug.Log($"Player {PlayerName} ready status: {ready}");
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_SendChatMessage(string message)
    {
        Debug.Log($"[{PlayerName}]: {message}");

        if (LobbyChat.Instance != null)
        {
            LobbyChat.Instance.AddMessage(PlayerName.Value, message);
        }
    }
    
    private void OnDestroy()
    {
        if (_localPlayer == this)
        {
            _localPlayer = null;
        }
    }
}