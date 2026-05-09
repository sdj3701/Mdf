// Assets/Scripts/UI/PhaseTimerUI.cs
using UnityEngine;
using TMPro; // TextMeshPro를 사용하기 위해 필수!

public class PhaseTimerUI : MonoBehaviour
{
    // 인스펙터에서 1단계에서 만든 텍스트 UI를 연결해줄 변수
    public TextMeshProUGUI timerText;

    private GameManagers gameManager;

    private void RefreshGameManagerReference(bool verboseLog)
    {
        var latest = GameManagers.Instance;
        if (latest == gameManager)
        {
            return;
        }

        gameManager = latest;
        if (verboseLog && gameManager != null)
        {
            Debug.Log("[PhaseTimerUI] GameManagers 참조 재바인딩 완료");
        }
    }

    void OnEnable()
    {
        // GameManagers가 준비될 때 이벤트를 구독합니다.
        GameEvents.OnGameManagersReady += OnGameManagersReady;
        RefreshGameManagerReference(false);
    }

    void OnDisable()
    {
        // 이벤트 구독 해제
        GameEvents.OnGameManagersReady -= OnGameManagersReady;
    }

    void Start()
    {
        if (timerText == null)
        {
            Debug.LogError("Timer Text가 PhaseTimerUI 스크립트에 할당되지 않았습니다!", gameObject);
            this.enabled = false; // 텍스트가 없으면 스크립트 비활성화
            return;
        }

        // Start()에서도 시도해봅니다 (이미 준비되어 있을 수 있음)
        RefreshGameManagerReference(false);
    }

    private void OnGameManagersReady()
    {
        // GameManagers가 준비되면 참조를 저장합니다.
        RefreshGameManagerReference(true);
    }

    void Update()
    {
        // Host Migration 후 Instance 교체를 반영하기 위해 매 프레임 최신 참조를 확인합니다.
        RefreshGameManagerReference(false);
        if (gameManager == null)
        {
            return;
        }
        
        // GameOver 상태이면 네트워크 프로퍼티 접근 안함 (씬 전환 대기 중)
        try
        {
            GameManagers.GameState currentState = gameManager.GetGameState();
            if (currentState == GameManagers.GameState.GameOver)
            {
                return;
            }

            if (gameManager.IsSequenceTransitioning)
            {
                timerText.text = $"{Mathf.CeilToInt(gameManager.currentSequenceTransitionTimer)}";
                return;
            }
            
            float remainingTime = gameManager.currentPhaseTimer;

            // 텍스트 UI의 내용을 업데이트합니다.
            // 정수로 올림하여 표시합니다.
            timerText.text = $"{Mathf.CeilToInt(remainingTime)}";
        }
        catch (System.InvalidOperationException)
        {
            // 네트워크 객체가 파괴된 경우 무시 (씬 전환 중)
            return;
        }
    }
}
