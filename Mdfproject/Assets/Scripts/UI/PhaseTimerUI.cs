// Assets/Scripts/UI/PhaseTimerUI.cs
using UnityEngine;
using TMPro; // TextMeshPro를 사용하기 위해 필수!

public class PhaseTimerUI : MonoBehaviour
{
    // 인스펙터에서 1단계에서 만든 텍스트 UI를 연결해줄 변수
    public TextMeshProUGUI timerText;

    private GameManagers gameManager;

    void Start()
    {
        // GameManager 인스턴스를 한번만 찾아와서 저장해두면 효율적입니다.
        gameManager = GameManagers.Instance;

        if (timerText == null)
        {
            Debug.LogError("Timer Text가 PhaseTimerUI 스크립트에 할당되지 않았습니다!", gameObject);
            this.enabled = false; // 텍스트가 없으면 스크립트 비활성화
        }
    }

    void Update()
    {
        // GameManager가 없으면 아무것도 하지 않음
        if (gameManager == null)
        {
            return;
        }

        // GameManager로부터 현재 게임 상태와 남은 시간을 가져옵니다.
        GameManagers.GameState currentState = gameManager.GetGameState();
        int remainingTime = Mathf.CeilToInt(gameManager.currentPhaseTimer);

        // 텍스트 UI의 내용을 업데이트합니다.
        timerText.text = $" {remainingTime}";
    }
}