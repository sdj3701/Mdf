// Assets/Scripts/UI/PlacementButtonsUI.cs
using UnityEngine;

public class PlacementButtonsUI : MonoBehaviour
{
    // [제거됨] 더 이상 FieldManager를 직접 참조하지 않습니다.
    // public FieldManager playerFieldManager;

    [Header("참조")]
    [Tooltip("임시로 배치 테스트할 유닛 프리팹")]
    public GameObject testUnitPrefab;

    /// <summary>
    /// '벽 배치' 버튼을 클릭했을 때 호출됩니다.
    /// </summary>
    public void OnPlaceWallButtonClicked()
    {
        // [수정됨] FieldManager의 메서드를 직접 호출하는 대신,
        // "벽 배치 모드로 들어가고 싶다"는 요청 이벤트를 시스템 전체에 알립니다.
        if (GameManagers.Instance.localPlayer != null)
        {
            GameManagers.Instance.localPlayer.fieldManager.StartPlacementMode(PlacementMode.Wall);
        }
    }

    /// <summary>
    /// '유닛 테스트' 버튼을 클릭했을 때 호출됩니다.
    /// </summary>
    public void OnPlaceUnitTestButtonClicked()
    {
        if (testUnitPrefab != null && GameManagers.Instance.localPlayer != null)
        {
            // [수정됨] "유닛 배치 모드로 들어가고 싶다"는 요청 이벤트를 알립니다.
            GameManagers.Instance.localPlayer.fieldManager.StartPlacementMode(PlacementMode.Unit, testUnitPrefab);
        }
    }

    /// <summary>
    /// '배치 취소' 버튼을 클릭했을 때 호출됩니다.
    /// </summary>
    public void OnCancelPlacementButtonClicked()
    {
        // [수정됨] "배치 모드를 끝내고 싶다"는 요청 이벤트를 알립니다.
        if (GameManagers.Instance.localPlayer != null)
        {
            GameManagers.Instance.localPlayer.fieldManager.StopPlacementMode();
        }
    }
}
