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

    [Header("정리용 부모 오브젝트")]
    [Tooltip("생성된 유닛들이 이 오브젝트의 자식으로 들어갑니다. 비워두면 '[Units]' 이름으로 자동 생성됩니다.")]
    public Transform unitParent;

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
        if (unitParent == null)
        {
            Transform foundParent = transform.parent.Find("[Units]");
            if (foundParent != null)
            {
                unitParent = foundParent;
            }
            else
            {
                GameObject parentObject = new GameObject("[Units]");
                parentObject.transform.SetParent(transform.parent); 
                unitParent = parentObject.transform;
            }
        }
        
        placementController = GetComponent<PlacementController>();
 
        if (obstacleTilemap == null)
            Debug.LogError("FieldManager의 자식 오브젝트에 Tilemap이 없습니다!", gameObject);
        
        placementController.Initialize(playerManager, placedUnits, obstacleTilemap);
    }

    void Update()
    {
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

    public void EnterUnitPlacementMode(GameObject unitPrefab)
    {
        placementController.StartPlacementMode(PlacementMode.Unit, unitPrefab);
    }

    public void CancelAllModes()
    {
        placementController.StopPlacementMode();
    }

    public void CreateAndPlaceUnitOnField(UnitData unitData)
    {
        Vector3Int? emptySlot = FindFirstEmptySlot();

        if (emptySlot.HasValue)
        {
            Vector3 worldPos = obstacleTilemap.CellToWorld(emptySlot.Value) + (obstacleTilemap.cellSize * 0.5f);
            
            // 1. UnitData로부터 프리팹을 가져와 생성합니다.
            GameObject newUnitGO = Instantiate(unitData.unitPrefab, worldPos, Quaternion.identity, unitParent);
            
            // 2. 생성된 게임오브젝트에서 Unit 컴포넌트를 가져옵니다.
            Unit newUnitComponent = newUnitGO.GetComponent<Unit>();

            // 3. Unit 컴포넌트에 UnitData를 주입하여 초기화합니다.
            if (newUnitComponent != null)
            {
                newUnitComponent.Initialize(unitData);
            }
            else
            {
                Debug.LogError($"{unitData.unitName} 프리팹에 Unit 컴포넌트가 없습니다!", newUnitGO);
            }
            
            placedUnits.Add(emptySlot.Value, newUnitGO);
            CheckForCombination();
        }
        else
        {
            Debug.LogWarning("필드에 빈 공간이 없어 유닛을 배치할 수 없습니다!");
            playerManager.AddGold(unitData.cost);
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
}