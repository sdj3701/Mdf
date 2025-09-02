/*using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LobbyChat : MonoBehaviour
{
    public static LobbyChat Instance { get; private set; }
    
    [Header("Chat UI")]
    [SerializeField] private GameObject _chatPanel;
    [SerializeField] private TMP_InputField _messageInput;
    [SerializeField] private Button _sendButton;
    [SerializeField] private Transform _messageContent;
    [SerializeField] private GameObject _messagePrefab;
    [SerializeField] private ScrollRect _scrollRect;
    [SerializeField] private int _maxMessages = 50;
    
    private Queue<GameObject> _messageQueue = new Queue<GameObject>();
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        // 버튼 이벤트 연결
        if (_sendButton != null)
        {
            _sendButton.onClick.AddListener(SendMessage);
        }
        
        // Enter 키로 메시지 전송
        if (_messageInput != null)
        {
            _messageInput.onSubmit.AddListener((text) => SendMessage());
        }
    }
    
    /// <summary>
    /// 메시지 전송
    /// </summary>
    public void SendMessage()
    {
        if (_messageInput == null || string.IsNullOrWhiteSpace(_messageInput.text))
            return;
        
        string message = _messageInput.text.Trim();
        
        // 로컬 플레이어를 통해 RPC 전송
        if (NetworkPlayer.LocalPlayer != null)
        {
            NetworkPlayer.LocalPlayer.RPC_SendChatMessage(message);
        }
        else
        {
            // 네트워크 연결이 없는 경우 로컬 메시지로 표시
            AddMessage("You", message);
        }
        
        // 입력 필드 초기화
        _messageInput.text = "";
        _messageInput.ActivateInputField();
    }
    
    /// <summary>
    /// 채팅 메시지 추가
    /// </summary>
    public void AddMessage(string playerName, string message)
    {
        if (_messagePrefab == null || _messageContent == null)
            return;
        
        // 메시지 수 제한
        if (_messageQueue.Count >= _maxMessages)
        {
            GameObject oldMessage = _messageQueue.Dequeue();
            Destroy(oldMessage);
        }
        
        // 새 메시지 생성
        GameObject messageObj = Instantiate(_messagePrefab, _messageContent);
        _messageQueue.Enqueue(messageObj);
        
        // 메시지 텍스트 설정
        TMP_Text messageText = messageObj.GetComponent<TMP_Text>();
        if (messageText != null)
        {
            messageText.text = $"<color=#00ff00>{playerName}</color>: {message}";
        }
        
        // 스크롤을 맨 아래로
        if (_scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = 0f;
        }
    }
    
    /// <summary>
    /// 시스템 메시지 추가
    /// </summary>
    public void AddSystemMessage(string message)
    {
        if (_messagePrefab == null || _messageContent == null)
            return;
        
        // 메시지 수 제한
        if (_messageQueue.Count >= _maxMessages)
        {
            GameObject oldMessage = _messageQueue.Dequeue();
            Destroy(oldMessage);
        }
        
        // 새 메시지 생성
        GameObject messageObj = Instantiate(_messagePrefab, _messageContent);
        _messageQueue.Enqueue(messageObj);
        
        // 메시지 텍스트 설정
        TMP_Text messageText = messageObj.GetComponent<TMP_Text>();
        if (messageText != null)
        {
            messageText.text = $"<color=#ffff00>[System]</color> {message}";
        }
        
        // 스크롤을 맨 아래로
        if (_scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = 0f;
        }
    }
    
    /// <summary>
    /// 채팅 창 토글
    /// </summary>
    public void ToggleChat()
    {
        if (_chatPanel != null)
        {
            _chatPanel.SetActive(!_chatPanel.activeSelf);
            
            if (_chatPanel.activeSelf && _messageInput != null)
            {
                _messageInput.ActivateInputField();
            }
        }
    }
    
    /// <summary>
    /// 모든 메시지 삭제
    /// </summary>
    public void ClearMessages()
    {
        while (_messageQueue.Count > 0)
        {
            GameObject message = _messageQueue.Dequeue();
            Destroy(message);
        }
    }
    
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
*/
