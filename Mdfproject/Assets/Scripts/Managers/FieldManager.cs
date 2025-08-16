// Assets/Scripts/Managers/FieldManager.cs
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(PlacementController))]
public class FieldManager : MonoBehaviour
{
    [Header("관리 대상 플레이어")]
    public PlayerManager playerManager;

    [Header("내부 참조")]
    private PlacementController placementController;
    public Tilemap obstacleTilemap;

    private Dictionary<Vector3Int, GameObject> placedUnits = new Dictionary<Vector3Int, GameObject>();

    // --- 유닛 드래그 앤 드롭을 위한 변수들 ---
    private GameObject selectedUnit;
    private Vector3Int originalUnitPosition;
    private Vector3 offset;

    void Awake()
    {
        placementController = GetComponent<PlacementController>();
 
        if (obstacleTilemap == null)
            Debug.LogError("FieldManager의 자식 오브젝트에 Tilemap이 없습니다!", gameObject);
        
        // FIX: obstacleTilemap을 세 번째 인자로 전달합니다.
        placementController.Initialize(playerManager, placedUnits, obstacleTilemap);
    }

    void Update()
    {
        // FIX: GetCurrentMode() 메서드를 호출하여 현재 상태를 가져옵니다.
        if (placementController.GetCurrentMode() == PlacementMode.None)
        {
            HandleUnitDragAndDrop();
        }
    }

    #region Public API
    
    public void EnterWallPlacementMode()
    {
        placementController.StartPlacementMode(PlacementMode.Wall);
    }

    // FIX: UI 테스트를 위한 EnterUnitPlacementMode 메서드 추가
    public void EnterUnitPlacementMode(GameObject unitPrefab)
    {
        placementController.StartPlacementMode(PlacementMode.Unit, unitPrefab);
    }

    // FIX: 메서드 이름 수정
    public void CancelAllModes()
    {
        placementController.StopPlacementMode();
    }

    public void CreateAndPlaceUnitOnField(GameObject unitPrefab)
    {
        Vector3Int? emptySlot = FindFirstEmptySlot();

        if (emptySlot.HasValue)
        {
            Vector3 worldPos = obstacleTilemap.CellToWorld(emptySlot.Value) + (obstacleTilemap.cellSize * 0.5f);
            GameObject newUnitGO = Instantiate(unitPrefab, worldPos, Quaternion.identity);
            
            placedUnits.Add(emptySlot.Value, newUnitGO);
            CheckForCombination();
        }
        else
        {
            Debug.LogWarning("필드에 빈 공간이 없어 유닛을 배치할 수 없습니다!");
            playerManager.AddGold(unitPrefab.GetComponent<Unit>().unitData.cost);
        }
    }
    
    public void UnitDied(GameObject deadUnit)
    {
        if (placedUnits.ContainsValue(deadUnit))
        {
            var item = placedUnits.First(kvp => kvp.Value == deadUnit);
            placedUnits.Remove(item.Key);
        }
    }

    #endregion

    #region 유닛 드래그 앤 드롭 로직

    private void HandleUnitDragAndDrop()
    {
        if (GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare) return;
        
        Vector3 mouseWorldPos = GetMouseWorldPosition();
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
            selectedUnit.transform.position = mouseWorldPos + offset;
        }

        if (Input.GetMouseButtonUp(0) && selectedUnit != null)
        {
            // FIX: public으로 변경된 IsPositionValidForPlacement를 호출합니다.
            if (placementController.IsPositionValidForPlacement(gridPos))
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
    
    private Vector3 GetMouseWorldPosition()
    {
        return Camera.main.ScreenToWorldPoint(Input.mousePosition);
    }

    #endregion

    #region 유닛 조합 및 필드 관리

    private Vector3Int? FindFirstEmptySlot()
    {
        BoundsInt bounds = obstacleTilemap.cellBounds;
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);
                // FIX: public으로 변경된 IsPositionValidForPlacement를 호출합니다.
                if (placementController.IsPositionValidForPlacement(pos))
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
            .GroupBy(u => $"{u.unitData.unitName}_{u.starLevel}")
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
}