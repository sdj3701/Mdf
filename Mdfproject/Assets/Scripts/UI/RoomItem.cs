using UnityEngine;
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
    [SerializeField] private GameObject _fullIndicator;  // "FULL" 표시

    [Header("Status Colors")]
    [SerializeField] private Color _availableColor = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color _fullColor = new Color(0.8f, 0.2f, 0.2f);
    [SerializeField] private Color _waitingColor = new Color(0.8f, 0.8f, 0.2f);

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
            // 방이 가득 찬 경우 이름도 회색으로
            _roomNameText.color = (_currentPlayers >= _maxPlayers) ? Color.gray : Color.white;
        }

        // 플레이어 수 표시
        if (_playerCountText != null)
        {
            _playerCountText.text = $"{_currentPlayers}/{_maxPlayers}명";
            _playerCountText.color = (_currentPlayers >= _maxPlayers) ? _fullColor : Color.white;
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
                _roomStatusImage.color = _waitingColor;
            }
            else
            {
                _roomStatusImage.color = _availableColor;
            }
        }

        // FULL 표시
        if (_fullIndicator != null)
        {
            _fullIndicator.SetActive(isFull);
        }

        // 참여 버튼 텍스트 변경
        if (_joinButton != null)
        {
            TMP_Text buttonText = _joinButton.GetComponentInChildren<TMP_Text>();
            if (buttonText != null)
            {
                if (isFull)
                {
                    buttonText.text = "만석";
                }
                else if (_currentPlayers > 0)
                {
                    buttonText.text = "참여";
                }
                else
                {
                    buttonText.text = "입장";
                }
            }
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

