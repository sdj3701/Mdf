/*using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class RoomItem : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text _roomNameText;
    [SerializeField] private TMP_Text _playerCountText;
    [SerializeField] private Button _joinButton;
    [SerializeField] private Image _roomStatusImage;
    
    [Header("Status Colors")]
    [SerializeField] private Color _availableColor = Color.green;
    [SerializeField] private Color _fullColor = Color.red;
    [SerializeField] private Color _inProgressColor = Color.yellow;
    
    private string _roomName;
    private int _currentPlayers;
    private int _maxPlayers;
    private Action _onJoinCallback;
    
    /// <summary>
    /// 방 아이템 설정
    /// </summary>
    public void Setup(string roomName, int currentPlayers, int maxPlayers, Action onJoinCallback)
    {
        _roomName = roomName;
        _currentPlayers = currentPlayers;
        _maxPlayers = maxPlayers;
        _onJoinCallback = onJoinCallback;
        
        UpdateUI();
    }
    
    private void UpdateUI()
    {
        // 방 이름 설정
        if (_roomNameText != null)
        {
            _roomNameText.text = _roomName;
        }
        
        // 플레이어 수 표시
        if (_playerCountText != null)
        {
            _playerCountText.text = $"{_currentPlayers}/{_maxPlayers}";
        }
        
        // 방 상태에 따른 UI 업데이트
        bool isFull = _currentPlayers >= _maxPlayers;
        
        // 참여 버튼 활성화/비활성화
        if (_joinButton != null)
        {
            _joinButton.interactable = !isFull;
            _joinButton.onClick.RemoveAllListeners();
            
            if (!isFull)
            {
                _joinButton.onClick.AddListener(() => _onJoinCallback?.Invoke());
            }
        }
        
        // 상태 색상 변경
        if (_roomStatusImage != null)
        {
            if (isFull)
            {
                _roomStatusImage.color = _fullColor;
            }
            else if (_currentPlayers > 0)
            {
                _roomStatusImage.color = _inProgressColor;
            }
            else
            {
                _roomStatusImage.color = _availableColor;
            }
        }
        
        // 방이 가득 찬 경우 텍스트 색상 변경
        if (isFull && _playerCountText != null)
        {
            _playerCountText.color = _fullColor;
        }
    }
    
    /// <summary>
    /// 플레이어 수 업데이트
    /// </summary>
    public void UpdatePlayerCount(int currentPlayers)
    {
        _currentPlayers = currentPlayers;
        UpdateUI();
    }
    
    /// <summary>
    /// 방 이름 가져오기
    /// </summary>
    public string GetRoomName()
    {
        return _roomName;
    }
    
    /// <summary>
    /// 방이 가득 찼는지 확인
    /// </summary>
    public bool IsFull()
    {
        return _currentPlayers >= _maxPlayers;
    }
}
*/
