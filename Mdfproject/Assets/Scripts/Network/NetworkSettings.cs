/*using UnityEngine;

[CreateAssetMenu(fileName = "NetworkSettings", menuName = "Network/Settings")]
public class NetworkSettings : ScriptableObject
{
    [Header("Connection Settings")]
    public string AppIdFusion = "YOUR_FUSION_APP_ID_HERE";
    public string Region = "kr"; // Korea region
    public string GameVersion = "1.0.0";
    
    [Header("Room Settings")]
    public int MaxPlayersPerRoom = 2;
    public int MaxRoomNameLength = 20;
    public float RoomListRefreshInterval = 3f; // seconds
    
    [Header("Player Settings")]
    public string DefaultPlayerNamePrefix = "Player";
    public int MaxPlayerNameLength = 16;
    
    [Header("Network Object Prefabs")]
    public GameObject PlayerPrefab;
    public GameObject NetworkManagerPrefab;
    
    [Header("Debug Settings")]
    public bool EnableDebugLogs = true;
    public bool ShowNetworkStats = false;
    
    private static NetworkSettings _instance;
    
    public static NetworkSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Resources.Load<NetworkSettings>("NetworkSettings");
                
                if (_instance == null)
                {
                    Debug.LogError("NetworkSettings not found in Resources folder!");
                }
            }
            
            return _instance;
        }
    }
    
    /// <summary>
    /// 유효한 방 이름인지 확인
    /// </summary>
    public bool IsValidRoomName(string roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName))
            return false;
        
        if (roomName.Length > MaxRoomNameLength)
            return false;
        
        // 특수 문자 제한 (영문, 숫자, 언더스코어, 하이픈만 허용)
        foreach (char c in roomName)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// 유효한 플레이어 이름인지 확인
    /// </summary>
    public bool IsValidPlayerName(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return false;
        
        if (playerName.Length > MaxPlayerNameLength)
            return false;
        
        return true;
    }
    
    /// <summary>
    /// 랜덤 방 이름 생성
    /// </summary>
    public string GenerateRandomRoomName()
    {
        return $"Room_{Random.Range(1000, 9999)}";
    }
    
    /// <summary>
    /// 랜덤 플레이어 이름 생성
    /// </summary>
    public string GenerateRandomPlayerName()
    {
        return $"{DefaultPlayerNamePrefix}_{Random.Range(1000, 9999)}";
    }
}
*/
