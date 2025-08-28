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
    private Dictionary<Vector3Int, Unit> placedUnits = new Dictionary<Vector3Int, Unit>();

    private Unit selectedUnit;
    private Vector3Int originalUnitPosition;
    private Vector3 offset;
    
    private Tilemap obstacleTilemap => GameAssets.TileMaps.BreakWallTilemap;
    private Camera playerCamera => GameAssets.Cameras.MainCamera;

    void Awake()
    {
        if (unitParent == null)
        {
            GameObject parentObject = new GameObject($"[{playerManager.name} Units]");
            parentObject.transform.SetParent(transform.parent);
            unitParent = parentObject.transform;
        }

        placementManager = GetComponent<PlacementManager>();
    }

    void OnEnable()
    {
        GameEvents.OnPlacementModeEnterRequested += HandlePlacementModeEnterRequest;
        GameEvents.OnPlacementModeExitRequested += HandlePlacementModeExitRequest;
    }

    void OnDisable()
    {
        GameEvents.OnPlacementModeEnterRequested -= HandlePlacementModeEnterRequest;
        GameEvents.OnPlacementModeExitRequested -= HandlePlacementModeExitRequest;
    }

    void Update()
    {
        if (placementManager.GetCurrentMode() == PlacementMode.None)
        {
            HandleUnitDragAndDrop();
        }
    }

    #region Event Handlers

    /// <summary>
    /// 배치 모드 진입 요청 이벤트를 처리합니다.
    /// </summary>
    private void HandlePlacementModeEnterRequest(PlacementMode mode, GameObject unitPrefab)
    {
        // [핵심 수정] 이 FieldManager가 로컬 플레이어의 것이 아닐 경우, 이벤트를 무시합니다.
        if (this.playerManager != GameManagers.Instance.localPlayer) return;
        
        placementManager.StartPlacementMode(mode, unitPrefab);
    }

    /// <summary>
    /// 배치 모드 종료 요청 이벤트를 처리합니다.
    /// </summary>
    private void HandlePlacementModeExitRequest()
    {
        // [핵심 수정] 이 FieldManager가 로컬 플레이어의 것이 아닐 경우, 이벤트를 무시합니다.
        if (this.playerManager != GameManagers.Instance.localPlayer) return;

        placementManager.StopPlacementMode();
    }

    #endregion

    #region 유닛 생성 및 관리
    
    public bool IsUnitAt(Vector3Int gridPosition)
    {
        return placedUnits.ContainsKey(gridPosition);
    }
    
    public void CreateAndPlaceUnitOnField(UnitData unitData, int starLevel)
    {
        Vector3Int? emptySlot = FindFirstEmptySlot();
        if (emptySlot.HasValue)
        {
            CreateUnitAt(unitData, emptySlot.Value, starLevel);
            CheckForCombination();
        }
        else
        {
            Debug.LogWarning("[FieldManager] 필드에 빈 공간이 없어 유닛을 배치할 수 없습니다! 골드를 환불합니다.");
            int refundCost = (starLevel == 2) ? unitData.cost * 4 : unitData.cost;
            playerManager.AddGold(refundCost);
        }
    }
    
    public void CreateAndPlaceUnitFromPlacement(GameObject unitPrefab, Vector3Int gridPosition)
    {
        Unit unitComponent = unitPrefab.GetComponent<Unit>();
        if (unitComponent != null)
        {
            CreateUnitAt(unitComponent.Data, gridPosition, 1);
        }
    }

    private void CreateUnitAt(UnitData data, Vector3Int gridPosition, int starLevel)
    {
        GameObject prefabToCreate = data.prefabsByStarLevel[starLevel - 1];
        if (prefabToCreate == null)
        {
            Debug.LogError($"{data.unitName}의 {starLevel}성에 해당하는 프리팹이 UnitData에 설정되지 않았습니다!");
            return;
        }
        Vector3 worldPos = obstacleTilemap.CellToWorld(gridPosition) + (obstacleTilemap.cellSize * 0.5f);
        GameObject newUnitGO = Instantiate(prefabToCreate, worldPos, Quaternion.identity, unitParent);
        Unit newUnitComponent = newUnitGO.GetComponent<Unit>();

        if (newUnitComponent != null)
        {
            newUnitComponent.Initialize(data, starLevel);
            placedUnits.Add(gridPosition, newUnitComponent);
        }
        else
        {
            Debug.LogError($"{prefabToCreate.name} 프리팹에 Unit 컴포넌트가 없습니다!", newUnitGO);
            Destroy(newUnitGO);
        }
    }
    
    public void UnitDied(Unit deadUnit)
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
            .Where(u => u != null && u.starLevel < 3)
            .GroupBy(u => new { u.Data.unitName, u.starLevel })
            .Where(g => g.Count() >= 3)
            .FirstOrDefault();

        if (combinableGroup != null)
        {
            List<Unit> unitsToCombine = combinableGroup.Take(3).ToList();
            for (int i = 0; i < 2; i++)
            {
                UnitDied(unitsToCombine[i]);
                Destroy(unitsToCombine[i].gameObject);
            }
            Unit baseUnit = unitsToCombine[2];
            baseUnit.Upgrade();
            ReplaceUnitPrefab(baseUnit);
            CheckForCombination();
        }
    }

    private void ReplaceUnitPrefab(Unit unitToReplace)
    {
        Vector3Int currentPos = placedUnits.First(kvp => kvp.Value == unitToReplace).Key;
        UnitData unitData = unitToReplace.Data;
        int newStarLevel = unitToReplace.starLevel;
        UnitDied(unitToReplace);
        Destroy(unitToReplace.gameObject);
        CreateUnitAt(unitData, currentPos, newStarLevel);
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