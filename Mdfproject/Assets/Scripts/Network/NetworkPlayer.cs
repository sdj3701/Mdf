/*using UnityEngine;
using Fusion;

public class NetworkPlayer : NetworkBehaviour
{
    [Networked] public string PlayerName { get; set; }
    [Networked] public int PlayerNumber { get; set; }
    [Networked] public bool IsReady { get; set; }
    
    private static NetworkPlayer _localPlayer;
    public static NetworkPlayer LocalPlayer => _localPlayer;
    
    public override void Spawned()
    {
        base.Spawned();
        
        if (HasInputAuthority)
        {
            _localPlayer = this;
            
            // 플레이어 이름 설정 (필요시 PlayerPrefs에서 가져오기)
            string savedName = PlayerPrefs.GetString("PlayerName", $"Player_{Random.Range(1000, 9999)}");
            RPC_SetPlayerName(savedName);
            
            Debug.Log($"Local player spawned: {savedName}");
        }
        
        // 플레이어 번호 설정 (호스트가 0, 클라이언트가 1)
        if (Runner.IsServer)
        {
            PlayerNumber = Runner.SessionInfo.PlayerCount - 1;
        }
        
        // DontDestroyOnLoad 설정
        DontDestroyOnLoad(gameObject);
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
        // 네트워크 업데이트 로직 (필요시 구현)
    }
    
    private void OnDestroy()
    {
        if (_localPlayer == this)
        {
            _localPlayer = null;
        }
    }
}
*/
