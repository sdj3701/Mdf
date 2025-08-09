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
    public static bool IsUnitPlacement = false;    // 유닛 배치 여부
    public static bool IsWallPlacement = false;    // 벽 배치 여부
    public float PlacementTime = 10.0f;     // 배치 시간 제한
    public static int CreateWallCount = 5;         // 생성된 벽의 개수

    [Header("벽 타일맵 설정")]
    public Tilemap Groundtilemap;            // 타일을 배치할 타일맵
    public Tilemap BreakWalltilemap;            // 타일을 배치할 타일맵
    public TileBase BreakWalltileToPlace;       // 배치할 타일
    public Camera PlayerCamera;                 // 메인 카메라

    [Header("캐릭터 생성 설정")]
    public Sprite CharacterSprite;              // 생성할 캐릭터 스프라이트
    public GameObject CharacterPrefab;          // 캐릭터 프리팹 (선택사항)
    public float CharacterSortingOrder = 5f;    // 캐릭터 렌더링 순서

    [Header("프리뷰 설정")]
    public bool ShowPreview = true;   // 마우스 위치 프리뷰 표시 여부
    public Color PreviewColor = Color.green;  // 프리뷰 색상

    protected GameObject PreviewObject; // 프리뷰용 오브젝트
    protected Vector3Int CurrentMouseGridPosition; // 현재 마우스의 그리드 좌표

    // 생성된 캐릭터들을 관리하는 딕셔너리 (위치별로 저장)
    public static Dictionary<Vector3Int, GameObject> SpawnedCharacters = new Dictionary<Vector3Int, GameObject>();

    private void Update()
    {
        // TODO : 배치 시간 감소
        /*
         * placementTime -= Time.deltaTime;
         */

        // 벽 개수 UI 업데이트
        wallCountText.text = CreateWallCount.ToString();
    }

    // 버튼 눌러서 벽 배치 활성화/비활성화
    public void IsCreateButtonActive()
    {
        IsWallPlacement = !IsWallPlacement;
        IsUnitPlacement = false; // 벽 배치 모드일 때 유닛 배치 비활성화
    }

    // 버튼 눌러서 유닛 배치 활성화/비활성화
    public void IsCreateUnitButtonActive()
    {
        IsUnitPlacement = !IsUnitPlacement;
        IsWallPlacement = false; // 유닛 배치 모드일 때 벽 배치 비활성화
    }

    // 모든 생성된 캐릭터 제거
    public static void ClearAllCharacters()
    {
        foreach (var character in SpawnedCharacters.Values)
        {
            if (character != null)
                DestroyImmediate(character);
        }
        SpawnedCharacters.Clear();
        Debug.Log("모든 캐릭터가 제거되었습니다!");
    }
}
