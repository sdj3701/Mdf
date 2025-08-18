// Assets/Scripts/UI/PlayerHUDController.cs
using UnityEngine;
using TMPro;

public class PlayerHUDController : MonoBehaviour
{
    [Header("HUD UI 요소")]
    public TextMeshProUGUI goldText;
    public TextMeshProUGUI healthText;
    public TextMeshProUGUI roundText;
    // 필요하다면 다른 HUD 요소들도 추가...

    private PlayerManager localPlayer;

    void Start()
    {
        // 로컬 플레이어의 참조를 한 번만 가져옵니다.
        if (GameManagers.Instance != null)
        {
            localPlayer = GameManagers.Instance.localPlayer;
        }
    }

    void Update()
    {
        // 참조가 유효할 때만 UI를 업데이트합니다.
        if (localPlayer != null)
        {
            goldText.text = $"<color=yellow>{localPlayer.GetGold()}</color>";
            healthText.text = $"<color=red>{localPlayer.GetHealth()}</color>";
        }

        if (GameManagers.Instance != null)
        {
            roundText.text = $"Round {GameManagers.Instance.currentRound}";
        }
    }
}