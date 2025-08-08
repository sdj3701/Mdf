using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

public class GameDataCenter : MonoBehaviour
{
    [Header("GameObject 관리 설정")]
    public GameObject CreateWall;          // 플레이어 오브젝트
    public TMP_Text wallCountText;      // 벽 개수 표시 UI 텍스트

    [Header("게임룰 설정")]
    public bool IsUnitPlacement = false;    // 유닛 배치 여부
    public static bool IsWallPlacement = false;    // 벽 배치 여부
    public float PlacementTime = 10.0f;     // 배치 시간 제한
    public static int CreateWallCount = 5;         // 생성된 벽의 개수

    [Header("벽 타일맵 설정")]
    public Tilemap Groundtilemap;            // 타일을 배치할 타일맵
    public Tilemap BreakWalltilemap;            // 타일을 배치할 타일맵
    public TileBase BreakWalltileToPlace;       // 배치할 타일
    public Camera PlayerCamera;                 // 메인 카메라

    [Header("프리뷰 설정")]
    public bool ShowPreview = true;   // 마우스 위치 프리뷰 표시 여부
    public Color PreviewColor = Color.green;  // 프리뷰 색상

    protected GameObject PreviewObject; // 프리뷰용 오브젝트
    protected Vector3Int CurrentMouseGridPosition; // 현재 마우스의 그리드 좌표

    // 이벤트 시스템으로 자식 정보를 관리
    public static event System.Action<bool> OnWallModeChanged;
    private bool currentWallMode = false;

    private void Update()
    {
        bool newWallMode = CheckWallCondition();

        // 상태가 변경된 경우만 이벤트 발생
        if (newWallMode != currentWallMode)
        {
            Debug.Log("GameDataCenter : " + newWallMode);
            currentWallMode = newWallMode;
            OnWallModeChanged?.Invoke(currentWallMode);
        }

        // TODO : 배치 시간 감소
        /*
         * placementTime -= Time.deltaTime;
         */

        // 벽 개수 UI 업데이트
        wallCountText.text = CreateWallCount.ToString();
    }

    // 자식의 기능을 활성화 할지 않할지 판단하는 함수
    bool CheckWallCondition()
    {
        // 시간과 벽 배치 버튼을 눌렀는지 확인
        bool isCreateWall;
        if (PlacementTime > 0.0f && IsWallPlacement)
            // 벽 배치가 활성화되어 있고, 배치 시간이 남아있는 경우
            isCreateWall = true;
        else
            // 벽 배치가 비활성화되거나 배치 시간이 초과된 경우
            isCreateWall = false;
        
        return isCreateWall;
    }

    // 버튼 눌러서 벽 배치 활성화/비활성화
    public void IsCreateButtonActive()
    {
        IsWallPlacement = !IsWallPlacement;
    }
}
