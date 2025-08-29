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
    [Tooltip("생성된 벽들이 이 오브젝트의 자식으로 들어갑니다.")]
    public Transform wallParent;
    
    private PlacementManager placementManager;
    private Dictionary<Vector3Int, Unit> placedUnits = new Dictionary<Vector3Int, Unit>();
    private Dictionary<Vector3Int, DestructibleWall> placedWalls = new Dictionary<Vector3Int, DestructibleWall>();


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
        
        if (wallParent == null)
        {
            GameObject parentObject = new GameObject($"[{playerManager.name} Walls]");
            parentObject.transform.SetParent(transform.parent);
            wallParent = parentObject.transform;
        }

        placementManager = GetComponent<PlacementManager>();
    }

    void OnEnable()
    {
        GameEvents.OnPlacementModeEnterRequested += HandlePlacementModeEnterRequest;
        GameEvents.OnPlacementModeExitRequested += HandlePlacementModeExitRequest;
        GameEvents.OnGameStateChanged += HandleGameStateChange;
    }

    void OnDisable()
    {
        GameEvents.OnPlacementModeEnterRequested -= HandlePlacementModeEnterRequest;
        GameEvents.OnPlacementModeExitRequested -= HandlePlacementModeExitRequest;
        GameEvents.OnGameStateChanged -= HandleGameStateChange;
    }

    void Update()
    {
        if (placementManager.GetCurrentMode() == PlacementMode.None)
        {
            HandleUnitDragAndDrop();
        }
    }

    #region Event Handlers
    
    private void HandleGameStateChange(GameManagers.GameState newState)
    {
        if (newState == GameManagers.GameState.Prepare)
        {
            RespawnAllUnits();
        }
    }

    private void HandlePlacementModeEnterRequest(PlacementMode mode, GameObject unitPrefab)
    {
        if (this.playerManager != GameManagers.Instance.localPlayer) return;
        
        placementManager.StartPlacementMode(mode, unitPrefab);
    }

    private void HandlePlacementModeExitRequest()
    {
        if (this.playerManager != GameManagers.Instance.localPlayer) return;

        placementManager.StopPlacementMode();
    }

    #endregion

    #region 벽 생성 및 관리

    public void CreateWallAt(GameObject wallPrefab, Vector3Int gridPosition)
    {
        if (wallPrefab == null || placedWalls.ContainsKey(gridPosition)) return;

        Vector3 worldPos = obstacleTilemap.CellToWorld(gridPosition) + (obstacleTilemap.cellSize * 0.5f);
        GameObject wallGO = Instantiate(wallPrefab, worldPos, Quaternion.identity, wallParent);
        DestructibleWall wallComponent = wallGO.GetComponent<DestructibleWall>();

        if (wallComponent != null)
        {
            wallComponent.Initialize(this, gridPosition);
            placedWalls.Add(gridPosition, wallComponent);
        }
        else
        {
            Debug.LogError($"{wallPrefab.name} 프리팹에 DestructibleWall 컴포넌트가 없습니다!", wallGO);
            Destroy(wallGO);
        }
    }

    public void RemoveWallAt(Vector3Int gridPosition)
    {
        if (placedWalls.TryGetValue(gridPosition, out DestructibleWall wall))
        {
            // 벽 위에 유닛이 있는지 확인
            Unit unitOnTop = GetUnitAt(gridPosition);
            if (unitOnTop != null)
            {
                Debug.Log($"<color=orange>벽이 파괴되어 위에 있던 {unitOnTop.Data.unitName}이(가) 함께 파괴됩니다!</color>");
                // TakeDamage(99999, ...)를 호출하여 Unit의 Die() 메소드를 실행시킵니다.
                unitOnTop.TakeDamage(99999, DamageType.Physical); 
            }

            Destroy(wall.gameObject);
            placedWalls.Remove(gridPosition);
        }
    }

    public DestructibleWall GetWallAt(Vector3Int gridPosition)
    {
        placedWalls.TryGetValue(gridPosition, out DestructibleWall wall);
        return wall;
    }

    #endregion
    
    #region 유닛 생성 및 관리
    
    public Unit GetUnitAt(Vector3Int gridPosition)
    {
        placedUnits.TryGetValue(gridPosition, out Unit unit);
        return unit;
    }
    
    public bool IsUnitAt(Vector3Int gridPosition)
    {
        return placedUnits.ContainsKey(gridPosition);
    }
    
    public void CreateAndPlaceUnitOnField(UnitData unitData, int starLevel)
    {
        Vector3Int? emptySlot = FindFirstEmptySlot(unitData);
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

    private void RespawnAllUnits()
    {
        foreach (Unit unit in placedUnits.Values)
        {
            if (unit != null && unit.IsDead)
            {
                unit.Respawn();
            }
        }
    }

    private Vector3Int? FindFirstEmptySlot(UnitData unitData)
    {
        if (obstacleTilemap == null) return null;
        BoundsInt bounds = obstacleTilemap.cellBounds;
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);
                if (placementManager.IsPositionValidForPlacement(pos, unitData))
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
            if (placementManager.IsPositionValidForPlacement(gridPos, selectedUnit.Data))
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