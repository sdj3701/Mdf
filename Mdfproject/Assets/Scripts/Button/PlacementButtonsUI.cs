// Assets/Scripts/UI/PlacementButtonsUI.cs
using UnityEngine;

public class PlacementButtonsUI : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("플레이어의 FieldManager를 연결하세요.")]
    public FieldManager playerFieldManager;

    [Tooltip("임시로 배치 테스트할 유닛 프리팹")]
    public GameObject testUnitPrefab;

    public void OnPlaceWallButtonClicked()
    {
        if (playerFieldManager != null)
        {
            playerFieldManager.EnterWallPlacementMode();
        }
    }

    public void OnPlaceUnitTestButtonClicked()
    {
        if (playerFieldManager != null && testUnitPrefab != null)
        {
            // FIX: FieldManager에 추가된 EnterUnitPlacementMode 메서드를 호출합니다.
            playerFieldManager.EnterUnitPlacementMode(testUnitPrefab);
        }
    }

    public void OnCancelPlacementButtonClicked()
    {
        if(playerFieldManager != null)
        {
            // FIX: 변경된 메서드 이름인 CancelAllModes를 호출합니다.
            playerFieldManager.CancelAllModes();
        }
    }
}