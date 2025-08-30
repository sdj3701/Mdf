// Assets/Scripts/Managers/FieldManager.cs
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(PlacementManager))]
public class FieldManager : MonoBehaviour
{
    // ✅ [수정] public 필드 제거, 이제 PlayerManager로부터 주입받음
    public PlayerManager playerManager;

    [Header("정리용 부모 오브젝트")]
    public Transform unitParent;
    public Transform wallParent;

    [Header("생성할 프리팹")]
    [Tooltip("몬스터가 공격하거나 플레이어가 설치할 때 사용되는 파괴 가능한 벽 프리팹입니다.")]
    public GameObject destructibleWallPrefab;

    public Tilemap ObstacleTilemap { get; private set; }
    public Tilemap GroundTilemap { get; private set; }

    private PlacementManager placementManager;
    private Dictionary<Vector3Int, Unit> placedUnits = new Dictionary<Vector3Int, Unit>();
    private Dictionary<Vector3Int, DestructibleWall> placedWalls = new Dictionary<Vector3Int, DestructibleWall>();

    private Unit selectedUnit;
    private Vector3Int originalUnitPosition;
    private Vector3 offset;
    
    private Camera playerCamera => GameAssets.Cameras.MainCamera;

    void Awake()
    {
        placementManager = GetComponent<PlacementManager>();
    }
    
    // ✅ [추가된 핵심 로직] PlayerManager가 호출하여 초기화
    public void Initialize(PlayerManager owner, Tilemap ground, Tilemap obstacle)
    {
        this.playerManager = owner;
        this.GroundTilemap = ground;
        this.ObstacleTilemap = obstacle;

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
        
        PrepopulateWallsFromTilemap();
    }
    
    // ... (이하 나머지 코드는 이전과 동일) ...
    // OnEnable, OnDisable, Update, Event Handlers, 벽/유닛 관리, 드래그앤드롭 로직 등
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
        // [추가] 게임 상태가 전투로 변경될 때, 진행 중이던 유닛 드래그를 취소합니다.
        else if (selectedUnit != null)
        {
            // 유닛을 원래 위치로 되돌립니다.
            Vector3 originalWorldPos = ObstacleTilemap.CellToWorld(originalUnitPosition) + (ObstacleTilemap.cellSize * 0.5f);
            selectedUnit.transform.position = originalWorldPos;
            placedUnits.Add(originalUnitPosition, selectedUnit);

            Debug.Log($"<color=orange>게임 상태 변경으로 인해 {selectedUnit.Data.unitName}의 배치가 취소되고 원위치로 돌아갑니다.</color>");
            
            // 드래그 상태를 초기화합니다.
            selectedUnit = null;
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

    public void CreateWallAt(Vector3Int gridPosition)
    {
        if (destructibleWallPrefab == null || placedWalls.ContainsKey(gridPosition)) return;
        if (ObstacleTilemap == null)
        {
            Debug.LogError("FieldManager에 ObstacleTilemap 참조가 없습니다!");
            return;
        }

        Vector3 worldPos = ObstacleTilemap.CellToWorld(gridPosition) + (ObstacleTilemap.cellSize * 0.5f);
        GameObject wallGO = Instantiate(destructibleWallPrefab, worldPos, Quaternion.identity, wallParent);
        DestructibleWall wallComponent = wallGO.GetComponent<DestructibleWall>();

        if (wallComponent != null)
        {
            wallComponent.Initialize(this, gridPosition);
            placedWalls.Add(gridPosition, wallComponent);
        }
        else
        {
            Debug.LogError($"{destructibleWallPrefab.name} 프리팹에 DestructibleWall 컴포넌트가 없습니다!", wallGO);
            Destroy(wallGO);
        }
    }

    public void RemoveWallAt(Vector3Int gridPosition)
    {
        if (placedWalls.TryGetValue(gridPosition, out DestructibleWall wall))
        {
            Unit unitOnTop = GetUnitAt(gridPosition);
            if (unitOnTop != null)
            {
                Debug.Log($"<color=orange>벽이 파괴되어 위에 있던 {unitOnTop.Data.unitName}이(가) 함께 파괴됩니다!</color>");
                unitOnTop.TakeDamage(99999, DamageType.Physical); 
            }

            Destroy(wall.gameObject);
            placedWalls.Remove(gridPosition);

            if (ObstacleTilemap != null)
            {
                ObstacleTilemap.SetTile(gridPosition, null);
            }
        }
    }

    public DestructibleWall GetWallAt(Vector3Int gridPosition)
    {
        placedWalls.TryGetValue(gridPosition, out DestructibleWall wall);
        return wall;
    }

    /// <summary>
    /// 게임 시작 시 타일맵을 스캔하여 'BreakWall' 타일이 있는 모든 위치에
    /// DestructibleWall 프리팹을 미리 생성합니다.
    /// </summary>
    private void PrepopulateWallsFromTilemap()
    {
        if (ObstacleTilemap == null) return;

        BoundsInt bounds = ObstacleTilemap.cellBounds;
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);
                if (ObstacleTilemap.GetTile(pos) != null)
                {
                    // CreateWallAt 내부에서 중복 생성을 방지하므로 여기서 별도 확인은 필요 없습니다.
                    CreateWallAt(pos);
                }
            }
        }
        Debug.Log($"[{playerManager.name}] 타일맵으로부터 {placedWalls.Count}개의 벽 오브젝트를 사전 생성했습니다.");
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
        if (ObstacleTilemap == null)
        {
            Debug.LogError("FieldManager에 ObstacleTilemap 참조가 없습니다!");
            return;
        }

        GameObject prefabToCreate = data.prefabsByStarLevel[starLevel - 1];
        if (prefabToCreate == null)
        {
            Debug.LogError($"{data.unitName}의 {starLevel}성에 해당하는 프리팹이 UnitData에 설정되지 않았습니다!");
            return;
        }
        Vector3 worldPos = ObstacleTilemap.CellToWorld(gridPosition) + (ObstacleTilemap.cellSize * 0.5f);
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
        if (GroundTilemap == null) return null;

        BoundsInt bounds = GroundTilemap.cellBounds;
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
        if (playerCamera == null || ObstacleTilemap == null) return;
        
        Vector3 mouseWorldPos = playerCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int gridPos = ObstacleTilemap.WorldToCell(mouseWorldPos);

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
                Vector3 finalWorldPos = ObstacleTilemap.CellToWorld(gridPos) + (ObstacleTilemap.cellSize * 0.5f);
                selectedUnit.transform.position = finalWorldPos;
                placedUnits.Add(gridPos, selectedUnit);
                CheckForCombination();
            }
            else
            {
                Vector3 originalWorldPos = ObstacleTilemap.CellToWorld(originalUnitPosition) + (ObstacleTilemap.cellSize * 0.5f);
                selectedUnit.transform.position = originalWorldPos;
                placedUnits.Add(originalUnitPosition, selectedUnit);
            }
            selectedUnit = null;
        }
    }
    #endregion
}
