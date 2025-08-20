// Assets/Scripts/Managers/FieldManager.cs
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(PlacementManager))]
public class FieldManager : MonoBehaviour
{
    [Header("관리 대상 플레이어")]
    public PlayerManager playerManager;

    [Header("정리용 부모 오브젝트")]
    [Tooltip("생성된 유닛들이 이 오브젝트의 자식으로 들어갑니다.")]
    public Transform unitParent;
    
    private PlacementManager placementManager;
    private Dictionary<Vector3Int, GameObject> placedUnits = new Dictionary<Vector3Int, GameObject>();

    private GameObject selectedUnit;
    private Vector3Int originalUnitPosition;
    private Vector3 offset;
    
    private Tilemap obstacleTilemap => GameAssets.TileMaps.BreakWallTilemap;
    private Camera playerCamera => GameAssets.Cameras.MainCamera;

    void Awake()
    {
        if (unitParent == null)
        {
            GameObject parentObject = new GameObject("[Units]");
            parentObject.transform.SetParent(transform.parent);
            unitParent = parentObject.transform;
        }

        placementManager = GetComponent<PlacementManager>();
        placementManager.Initialize(playerManager, placedUnits);
    }

    void Update()
    {
        if (placementManager.GetCurrentMode() == PlacementMode.None)
        {
            HandleUnitDragAndDrop();
        }
    }

    #region Public API (외부 호출용)

    public void EnterWallPlacementMode()
    {
        placementManager.StartPlacementMode(PlacementMode.Wall);
    }

    public void EnterUnitPlacementMode(GameObject unitPrefab)
    {
        placementManager.StartPlacementMode(PlacementMode.Unit, unitPrefab);
    }

    public void CancelAllModes()
    {
        placementManager.StopPlacementMode();
    }
    
    public void CreateAndPlaceUnitOnField(UnitData unitData)
    {
        // [디버그 로그 추가]
        Debug.Log($"[FieldManager] {unitData.unitName} 배치 시도. 빈 슬롯을 찾습니다...");
        Vector3Int? emptySlot = FindFirstEmptySlot();

        if (emptySlot.HasValue)
        {
            // [디버그 로그 추가]
            Debug.Log($"[FieldManager] 빈 슬롯 ({emptySlot.Value}) 발견! 유닛을 생성합니다.");
            CreateUnitAt(unitData.unitPrefab, emptySlot.Value, unitData);
            CheckForCombination();
        }
        else
        {
            // [디버그 로그 수정]
            Debug.LogWarning("[FieldManager] 필드에 빈 공간이 없어 유닛을 배치할 수 없습니다! 골드를 환불합니다.");
            playerManager.AddGold(unitData.cost);
        }
    }
    
    public void CreateAndPlaceUnitFromPlacement(GameObject unitPrefab, Vector3Int gridPosition)
    {
        UnitData data = unitPrefab.GetComponent<Unit>()?.Data;
        CreateUnitAt(unitPrefab, gridPosition, data);
    }

    #endregion
    
    #region 유닛 생성 및 관리
    
    private void CreateUnitAt(GameObject prefab, Vector3Int gridPosition, UnitData data)
    {
        Vector3 worldPos = obstacleTilemap.CellToWorld(gridPosition) + (obstacleTilemap.cellSize * 0.5f);
        GameObject newUnitGO = Instantiate(prefab, worldPos, Quaternion.identity, unitParent);
        Unit newUnitComponent = newUnitGO.GetComponent<Unit>();

        if (newUnitComponent != null && data != null)
        {
            newUnitComponent.Initialize(data);
        }
        else
        {
            Debug.LogError($"{prefab.name} 프리팹에 Unit 컴포넌트가 없거나 UnitData가 null입니다!", newUnitGO);
        }
        
        placedUnits.Add(gridPosition, newUnitGO);
    }
    
    private void UnitDied(GameObject deadUnit)
    {
        if (placedUnits.ContainsValue(deadUnit))
        {
            var item = placedUnits.First(kvp => kvp.Value == deadUnit);
            placedUnits.Remove(item.Key);
        }
    }

    private Vector3Int? FindFirstEmptySlot()
    {
        if (obstacleTilemap == null) return null;
        
        BoundsInt bounds = obstacleTilemap.cellBounds;
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);
                if (placementManager.IsPositionValidForPlacement(pos))
                {
                    return pos;
                }
            }
        }
        return null;
    }
    
    public void CheckForCombination()
    {
        var combinableGroup = placedUnits.Values
            .Select(go => go.GetComponent<Unit>())
            .Where(u => u != null && u.starLevel < 3)
            .GroupBy(u => $"{u.Data.unitName}_{u.starLevel}")
            .Where(g => g.Count() >= 3)
            .FirstOrDefault();

        if (combinableGroup != null)
        {
            List<Unit> unitsToCombine = combinableGroup.Take(3).ToList();
            
            for (int i = 0; i < 2; i++)
            {
                UnitDied(unitsToCombine[i].gameObject);
                Destroy(unitsToCombine[i].gameObject);
            }
            
            unitsToCombine[2].Upgrade();
            CheckForCombination();
        }
    }

    #endregion

    #region 유닛 드래그 앤 드롭 로직

    private void HandleUnitDragAndDrop()
    {
        if (GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare) return;
        if (playerCamera == null || obstacleTilemap == null) return;
        
        Vector3 mouseWorldPos = playerCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int gridPos = obstacleTilemap.WorldToCell(mouseWorldPos);

        if (Input.GetMouseButtonDown(0))
        {
            if (placedUnits.ContainsKey(gridPos))
            {
                selectedUnit = placedUnits[gridPos];
                originalUnitPosition = gridPos;
                offset = selectedUnit.transform.position - mouseWorldPos;
                placedUnits.Remove(gridPos);
            }
        }

        if (Input.GetMouseButton(0) && selectedUnit != null)
        {
            selectedUnit.transform.position = new Vector3(mouseWorldPos.x + offset.x, mouseWorldPos.y + offset.y, selectedUnit.transform.position.z);
        }

        if (Input.GetMouseButtonUp(0) && selectedUnit != null)
        {
            if (placementManager.IsPositionValidForPlacement(gridPos))
            {
                Vector3 finalWorldPos = obstacleTilemap.CellToWorld(gridPos) + (obstacleTilemap.cellSize * 0.5f);
                selectedUnit.transform.position = finalWorldPos;
                placedUnits.Add(gridPos, selectedUnit);
                CheckForCombination();
            }
            else
            {
                Vector3 originalWorldPos = obstacleTilemap.CellToWorld(originalUnitPosition) + (obstacleTilemap.cellSize * 0.5f);
                selectedUnit.transform.position = originalWorldPos;
                placedUnits.Add(originalUnitPosition, selectedUnit);
            }
            selectedUnit = null;
        }
    }

    #endregion
}