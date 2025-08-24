// Assets/Scripts/UI/PlayerRankSlot.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 개별 플레이어 랭킹 UI 슬롯의 표시를 관리하는 클래스입니다.
/// </summary>
public class PlayerRankSlot : MonoBehaviour
{
    [Header("UI 요소 참조")]
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private Image playerPortraitImage; // 플레이어 초상화 (선택 사항)
    [SerializeField] private Image battleStatusImage;   // 전투 상태 아이콘 (UI_Img_Battle)

    [Header("전투 상태 스프라이트")]
    [SerializeField] private Sprite combatSprite; // 칼 모양 이미지
    [SerializeField] private Sprite waitingSprite; // 방패 모양 이미지

    private PlayerManager trackedPlayer;

    /// <summary>
    /// 이 슬롯이 추적하고 업데이트할 PlayerManager를 설정합니다.
    /// </summary>
    public void Initialize(PlayerManager player)
    {
        this.trackedPlayer = player;
        if (trackedPlayer == null)
        {
            gameObject.SetActive(false);
            return;
        }
        
        // TODO: 실제 플레이어 이름(닉네임)을 가져오는 로직 추가 필요
        playerNameText.text = $"Player {trackedPlayer.playerId}"; 
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 매 프레임 호출되어 UI를 최신 정보로 업데이트합니다.
    /// </summary>
    public void UpdateUI()
    {
        if (trackedPlayer == null || !gameObject.activeInHierarchy)
        {
            return;
        }

        // 체력 업데이트
        healthText.text = trackedPlayer.GetHealth().ToString();

        // ✅ [수정] 전투 상태 업데이트 로직 변경
        // GameManagers의 전체 상태 대신, 추적하는 플레이어의 개별 전투 상태(IsActivelyFighting)를 확인합니다.
        if (trackedPlayer.IsActivelyFighting)
        {
            battleStatusImage.sprite = combatSprite; // 싸우는 중이면 칼 모양
        }
        else
        {
            battleStatusImage.sprite = waitingSprite; // 싸움이 끝났으면 방패 모양
        }
    }

    /// <summary>
    /// 정렬을 위해 이 슬롯이 추적하는 PlayerManager를 반환합니다.
    /// </summary>
    public PlayerManager GetTrackedPlayer()
    {
        return trackedPlayer;
    }
}