// Assets/Scripts/UI/PlayerHUDController.cs
using UnityEngine;
using TMPro; // TextMeshPro를 사용하기 위해 필수입니다.

/// <summary>
/// 플레이어의 주요 정보(골드, 라운드 등)를 표시하는 HUD UI를 제어합니다.
/// </summary>
public class PlayerHUDController : MonoBehaviour
{
    [Header("HUD UI 요소")]
    [Tooltip("골드를 표시할 TextMeshPro UI 요소를 연결하세요.")]
    public TextMeshProUGUI goldText;

    [Tooltip("현재 라운드를 표시할 TextMeshPro UI 요소를 연결하세요.")]
    public TextMeshProUGUI roundText;

    // 게임 데이터에 접근하기 위한 참조 변수들
    private PlayerManager localPlayer;
    private GameManagers gameManager;

    void Start()
    {
        // GameManagers 싱글톤 인스턴스를 한 번만 찾아와서 저장합니다.
        gameManager = GameManagers.Instance;

        if (gameManager != null)
        {
            // 게임 매니저를 통해 로컬 플레이어의 참조를 가져옵니다.
            localPlayer = gameManager.localPlayer;
        }
        else
        {
            Debug.LogError("GameManagers 인스턴스를 찾을 수 없습니다! HUD가 작동하지 않을 수 있습니다.");
        }

        if (goldText == null || roundText == null)
        {
            Debug.LogError("HUD UI 요소 중 일부가 PlayerHUDController에 할당되지 않았습니다!", gameObject);
            this.enabled = false; // 필수 요소가 없으면 스크립트를 비활성화합니다.
        }
    }

    void Update()
    {
        // 로컬 플레이어 참조가 유효할 때만 플레이어 관련 UI를 업데이트합니다.
        if (localPlayer != null)
        {
            // PlayerManager의 GetGold() 메서드를 호출하여 최신 골드 정보를 가져와 텍스트로 표시합니다.
            goldText.text = localPlayer.GetGold().ToString();
        }

        // 게임 매니저 참조가 유효할 때만 게임 전체 관련 UI를 업데이트합니다.
        if (gameManager != null)
        {
            // [수정됨] 텍스트를 두 줄로 표시하기 위해 줄바꿈 문자(\n)를 추가합니다.
            roundText.text = $"ROUND\n{gameManager.currentRound}";
        }
    }
}