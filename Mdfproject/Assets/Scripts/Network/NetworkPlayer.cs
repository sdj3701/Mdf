using UnityEngine;
using Fusion;
using TMPro;

/// <summary>
/// 네트워크 플레이어 오브젝트
/// NetworkObject와 함께 프리팹으로 만들어 사용
/// Unity 2021.3.45f1 버전용 (Fusion 1.x 호환)
/// </summary>
public class NetworkPlayer : NetworkBehaviour
{
    // Fusion 1.x에서는 OnChanged 대신 일반 Networked 속성 사용
    [Networked] 
    public string PlayerName { get; set; }
    
    [Networked] 
    public int PlayerNumber { get; set; }
    
    [Networked] 
    public bool IsReady { get; set; }
    
    [Header("UI References")]
    [SerializeField] private TMP_Text _nameLabel;
    [SerializeField] private GameObject _readyIndicator;
    [SerializeField] private GameObject _hostCrown;

    private static NetworkPlayer _localPlayer;
    public static NetworkPlayer LocalPlayer => _localPlayer;
    
    // 이전 값을 저장하여 변경 감지
    private string _previousPlayerName;
    private int _previousPlayerNumber = -1;
    private bool _previousIsReady;
    
    // Fusion 1.x 스타일의 ChangeDetector (선택적)
    private ChangeDetector _changeDetector;

    public override void Spawned()
    {
        base.Spawned();
        
        // ChangeDetector 초기화 (Fusion 1.x)
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        
        if (HasInputAuthority)
        {
            _localPlayer = this;
            
            // NetworkManager에서 닉네임 가져오기
            if (NetworkManager.Instance != null)
            {
                string nickname = NetworkManager.Instance.PlayerNickname;
                if (!string.IsNullOrEmpty(nickname))
                {
                    RPC_SetPlayerName(nickname);
                }
            }
            else
            {
                // PlayerPrefs에서 가져오기
                string savedName = PlayerPrefs.GetString("PlayerNickname", $"Player_{Random.Range(1000, 9999)}");
                RPC_SetPlayerName(savedName);
            }
            
            Debug.Log($"Local player spawned: {PlayerName}");
        }
        
        // 플레이어 번호 설정 (호스트가 0, 클라이언트가 1)
        if (Runner.IsServer)
        {
            if (Object.HasInputAuthority)
            {
                PlayerNumber = 0; // 호스트
            }
            else
            {
                PlayerNumber = 1; // 클라이언트
            }
        }
        
        // DontDestroyOnLoad 설정
        DontDestroyOnLoad(gameObject);
        
        // 초기 값 저장
        _previousPlayerName = PlayerName;
        _previousPlayerNumber = PlayerNumber;
        _previousIsReady = IsReady;
        
        // UI 업데이트
        UpdateUI();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetPlayerName(string name)
    {
        PlayerName = name;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetReady(bool ready)
    {
        IsReady = ready;
        Debug.Log($"Player {PlayerName} ready status: {ready}");
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_SendChatMessage(string message)
    {
        Debug.Log($"[{PlayerName}]: {message}");

        // UI에 채팅 메시지 표시 (LobbyUI나 게임 UI에서 처리)
        if (LobbyChat.Instance != null)
        {
            LobbyChat.Instance.AddMessage(PlayerName, message);
        }
    }

    /// <summary>
    /// 플레이어가 호스트인지 확인
    /// </summary>
    public bool IsHost()
    {
        return Runner != null && Runner.IsServer;
    }

    /// <summary>
    /// 로컬 플레이어인지 확인
    /// </summary>
    public bool IsLocalPlayer()
    {
        return HasInputAuthority;
    }

    public override void FixedUpdateNetwork()
    {
        // 수동으로 변경 감지 (Unity 2021.3.45f1용)
        DetectChanges();
    }
    
    /// <summary>
    /// Unity 2021.3.45f1에서 OnChanged 대신 수동으로 변경 감지
    /// </summary>
    private void DetectChanges()
    {
        // PlayerName 변경 감지
        if (_previousPlayerName != PlayerName)
        {
            _previousPlayerName = PlayerName;
            OnPlayerNameChanged();
        }
        
        // PlayerNumber 변경 감지
        if (_previousPlayerNumber != PlayerNumber)
        {
            _previousPlayerNumber = PlayerNumber;
            OnPlayerNumberChanged();
        }
        
        // IsReady 변경 감지
        if (_previousIsReady != IsReady)
        {
            _previousIsReady = IsReady;
            OnReadyChanged();
        }
    }
    
    /// <summary>
    /// Render 메서드에서도 변경 감지 (더 부드러운 UI 업데이트)
    /// </summary>
    public override void Render()
    {
        // Fusion 1.x에서는 Render에서 ChangeDetector 사용 가능
        if (_changeDetector != null)
        {
            foreach (var change in _changeDetector.DetectChanges(this))
            {
                switch (change)
                {
                    case nameof(PlayerName):
                        OnPlayerNameChanged();
                        break;
                    case nameof(PlayerNumber):
                        OnPlayerNumberChanged();
                        break;
                    case nameof(IsReady):
                        OnReadyChanged();
                        break;
                }
            }
        }
    }
    
    /// <summary>
    /// UI 업데이트
    /// </summary>
    private void UpdateUI()
    {
        // 이름 라벨 업데이트
        if (_nameLabel != null)
        {
            _nameLabel.text = PlayerName;
            if (HasInputAuthority)
            {
                _nameLabel.color = Color.green; // 내 플레이어는 초록색
            }
        }
        
        // 준비 상태 표시
        if (_readyIndicator != null)
        {
            _readyIndicator.SetActive(IsReady);
        }
        
        // 호스트 표시
        if (_hostCrown != null)
        {
            _hostCrown.SetActive(PlayerNumber == 0);
        }
    }
    
    // 네트워크 변수 변경 콜백 (Unity 2021.3.45f1용)
    private void OnPlayerNameChanged()
    {
        UpdateUI();
        Debug.Log($"Player name changed to: {PlayerName}");
    }
    
    private void OnPlayerNumberChanged()
    {
        UpdateUI();
        Debug.Log($"Player number changed to: {PlayerNumber}");
    }
    
    private void OnReadyChanged()
    {
        UpdateUI();
        Debug.Log($"Player ready status changed to: {IsReady}");
        
        // JoinLobbyUI에 알림
        if (JoinLobbyUI.Instance != null)
        {
            JoinLobbyUI.Instance.OnPlayerReadyChanged(PlayerNumber, IsReady);
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
