// Assets/Scripts/Network/NetworkSettings.cs
using UnityEngine;

[CreateAssetMenu(fileName = "NetworkSettings", menuName = "Network/Settings")]
public class NetworkSettings : ScriptableObject
{
    [Header("Connection Settings")]
    [Tooltip("Photon Fusion App ID - 대시보드에서 확인")]
    public string AppIdFusion = "YOUR_FUSION_APP_ID_HERE";
    
    [Tooltip("서버 지역 - 아시아 지역 사용 권장")]
    public string Region = "asia"; // ✅ asia로 변경 (kr보다 안정적)
    
    [Tooltip("앱 버전 - 빌드와 에디터에서 동일해야 함")]
    public string GameVersion = "MDF_1.0"; // ✅ 고유한 버전명 사용
    
    [Tooltip("로비명 - 빌드와 에디터에서 동일해야 함")]
    public string LobbyName = "MainLobby"; // ✅ 고정 로비명
    
    [Header("Room Settings")]
    [Tooltip("방당 최대 플레이어 수")]
    public int MaxPlayersPerRoom = 2;
    
    [Tooltip("방 이름 최대 길이")]
    public int MaxRoomNameLength = 20;
    
    [Tooltip("방 목록 새로고침 간격 (초)")]
    public float RoomListRefreshInterval = 2f; // ✅ 2초로 안정화
    
    [Header("Player Settings")]
    [Tooltip("기본 플레이어 이름 접두사")]
    public string DefaultPlayerNamePrefix = "Player";
    
    [Tooltip("플레이어 이름 최대 길이")]
    public int MaxPlayerNameLength = 16;
    
    [Header("Network Object Prefabs")]
    [Tooltip("플레이어 프리팹")]
    public GameObject PlayerPrefab;
    
    [Tooltip("네트워크 매니저 프리팹")]
    public GameObject NetworkManagerPrefab;
    
    [Header("Debug Settings")]
    [Tooltip("디버그 로그 활성화")]
    public bool EnableDebugLogs = true;
    
    [Tooltip("네트워크 통계 표시")]
    public bool ShowNetworkStats = true; // ✅ 기본적으로 활성화
    
    [Header("Retry Settings")] // ✅ 새로 추가
    [Tooltip("연결 재시도 횟수")]
    public int MaxConnectionRetries = 3;
    
    [Tooltip("방 목록 새로고침 재시도 횟수")]
    public int MaxRefreshRetries = 5;
    
    [Tooltip("연결 타임아웃 시간 (초)")]
    public int ConnectionTimeoutSeconds = 10;
    
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
                    Debug.LogError("NetworkSettings not found in Resources folder! " +
                                 "Create one using 'Assets > Create > Network > Settings'");
                    
                    // ✅ 런타임에 기본 설정 생성
                    _instance = CreateInstance<NetworkSettings>();
                    _instance.name = "RuntimeNetworkSettings";
                    Debug.LogWarning("Created runtime NetworkSettings with default values.");
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
        
        if (roomName.Length < 3 || roomName.Length > MaxRoomNameLength)
            return false;
        
        // ✅ 더 관대한 문자 허용 (한글, 영문, 숫자, 특수문자 일부)
        foreach (char c in roomName)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != ' ')
            {
                // 한글 범위 확인
                if (c < 0xAC00 || c > 0xD7A3)
                    return false;
            }
        }
        
        // 시스템 예약어 확인
        string[] reservedNames = { "Lobby_", "Browse_", "LobbyBrowser_", "TempRoom_" };
        foreach (string reserved in reservedNames)
        {
            if (roomName.StartsWith(reserved))
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
        
        if (playerName.Length < 2 || playerName.Length > MaxPlayerNameLength)
            return false;
        
        // ✅ 플레이어 이름도 한글 허용
        foreach (char c in playerName)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != ' ')
            {
                // 한글 범위 확인
                if (c < 0xAC00 || c > 0xD7A3)
                    return false;
            }
        }
        
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
    
    /// <summary>
    /// ✅ 새로 추가: 현재 설정 검증
    /// </summary>
    public bool ValidateSettings()
    {
        bool isValid = true;
        
        if (string.IsNullOrEmpty(AppIdFusion) || AppIdFusion == "YOUR_FUSION_APP_ID_HERE")
        {
            Debug.LogError("[NetworkSettings] AppIdFusion이 설정되지 않았습니다!");
            isValid = false;
        }
        
        if (string.IsNullOrEmpty(GameVersion))
        {
            Debug.LogError("[NetworkSettings] GameVersion이 설정되지 않았습니다!");
            isValid = false;
        }
        
        if (string.IsNullOrEmpty(LobbyName))
        {
            Debug.LogError("[NetworkSettings] LobbyName이 설정되지 않았습니다!");
            isValid = false;
        }
        
        if (MaxPlayersPerRoom < 1 || MaxPlayersPerRoom > 8)
        {
            Debug.LogWarning("[NetworkSettings] MaxPlayersPerRoom은 1-8 사이를 권장합니다.");
        }
        
        if (PlayerPrefab == null)
        {
            Debug.LogWarning("[NetworkSettings] PlayerPrefab이 설정되지 않았습니다.");
        }
        
        if (isValid)
        {
            Debug.Log($"<color=green>[NetworkSettings] ✅ 설정 검증 완료!</color>");
            Debug.Log($"<color=green>[NetworkSettings] - GameVersion: {GameVersion}</color>");
            Debug.Log($"<color=green>[NetworkSettings] - Region: {Region}</color>");
            Debug.Log($"<color=green>[NetworkSettings] - LobbyName: {LobbyName}</color>");
        }
        
        return isValid;
    }
    
    /// <summary>
    /// ✅ 새로 추가: 설정 정보 출력
    /// </summary>
    public void LogCurrentSettings()
    {
        Debug.Log($"<color=cyan>[NetworkSettings] === 현재 네트워크 설정 ===</color>");
        Debug.Log($"<color=cyan>[NetworkSettings] GameVersion: {GameVersion}</color>");
        Debug.Log($"<color=cyan>[NetworkSettings] Region: {Region}</color>");
        Debug.Log($"<color=cyan>[NetworkSettings] LobbyName: {LobbyName}</color>");
        Debug.Log($"<color=cyan>[NetworkSettings] MaxPlayersPerRoom: {MaxPlayersPerRoom}</color>");
        Debug.Log($"<color=cyan>[NetworkSettings] RefreshInterval: {RoomListRefreshInterval}s</color>");
        Debug.Log($"<color=cyan>[NetworkSettings] MaxRetries: {MaxRefreshRetries}</color>");
    }
    
    /// <summary>
    /// ✅ 새로 추가: 에디터에서 설정 리셋
    /// </summary>
    [ContextMenu("Reset to Default Settings")]
    public void ResetToDefaults()
    {
        GameVersion = "MDF_1.0";
        Region = "asia";
        LobbyName = "MainLobby";
        MaxPlayersPerRoom = 2;
        RoomListRefreshInterval = 2f;
        MaxRefreshRetries = 5;
        EnableDebugLogs = true;
        ShowNetworkStats = true;
        
        Debug.Log("<color=yellow>[NetworkSettings] 기본 설정으로 리셋되었습니다.</color>");
        
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
