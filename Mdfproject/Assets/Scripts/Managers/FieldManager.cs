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
    // [수정됨] 이제 Key는 그리드 좌표, Value는 유닛의 GameObject가 아닌 Unit 컴포넌트 자체를 저장합니다.
    // 이렇게 하면 유닛 정보에 더 쉽게 접근할 수 있습니다.
    private Dictionary<Vector3Int, Unit> placedUnits = new Dictionary<Vector3Int, Unit>();

    private Unit selectedUnit; // 드래그 앤 드롭 대상 유닛
    private Vector3Int originalUnitPosition;
    private Vector3 offset;
    
    private Tilemap obstacleTilemap => GameAssets.TileMaps.BreakWallTilemap;
    private Camera playerCamera => GameAssets.Cameras.MainCamera;


    public bool IsUnitAt(Vector3Int gridPosition)
    {
        return placedUnits.ContainsKey(gridPosition);
    }


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
    
    /// <summary>
    /// 상점에서 유닛 구매 시 호출됩니다.
    /// </summary>
    public void CreateAndPlaceUnitOnField(UnitData unitData, int starLevel)
    {
        Vector3Int? emptySlot = FindFirstEmptySlot();

        if (emptySlot.HasValue)
        {
            CreateUnitAt(unitData, emptySlot.Value, starLevel);
            // 유닛 생성 후 즉시 조합을 확인합니다.
            CheckForCombination();
        }
        else
        {
            Debug.LogWarning("[FieldManager] 필드에 빈 공간이 없어 유닛을 배치할 수 없습니다! 골드를 환불합니다.");
            // [수정됨] 가격 계산 로직을 반영하여 환불합니다.
            int refundCost = (starLevel == 2) ? unitData.cost * 4 : unitData.cost;
            playerManager.AddGold(refundCost);
        }
    }
    
    /// <summary>
    /// PlacementManager가 유닛 배치 모드에서 유닛을 생성할 때 호출됩니다.
    /// </summary>
    public void CreateAndPlaceUnitFromPlacement(GameObject unitPrefab, Vector3Int gridPosition)
    {
        Unit unitComponent = unitPrefab.GetComponent<Unit>();
        if (unitComponent != null)
        {
            // 배치 모드에서는 항상 1성 유닛을 생성합니다.
            CreateUnitAt(unitComponent.Data, gridPosition, 1);
        }
    }

    #endregion
    
    #region 유닛 생성 및 관리

    /// <summary>
    /// 지정된 위치에 특정 성급의 유닛을 생성하고 초기화합니다.
    /// </summary>
    private void CreateUnitAt(UnitData data, Vector3Int gridPosition, int starLevel)
    {
        // 1. 성급에 맞는 프리팹을 UnitData 배열에서 가져옵니다.
        GameObject prefabToCreate = data.prefabsByStarLevel[starLevel - 1];
        if (prefabToCreate == null)
        {
            Debug.LogError($"{data.unitName}의 {starLevel}성에 해당하는 프리팹이 UnitData에 설정되지 않았습니다!");
            return;
        }

        // 2. 유닛 생성 및 위치 설정
        Vector3 worldPos = obstacleTilemap.CellToWorld(gridPosition) + (obstacleTilemap.cellSize * 0.5f);
        GameObject newUnitGO = Instantiate(prefabToCreate, worldPos, Quaternion.identity, unitParent);
        Unit newUnitComponent = newUnitGO.GetComponent<Unit>();

        // 3. 유닛 초기화
        if (newUnitComponent != null)
        {
            newUnitComponent.Initialize(data, starLevel);
            placedUnits.Add(gridPosition, newUnitComponent);
        }
        else
        {
            Debug.LogError($"{prefabToCreate.name} 프리팹에 Unit 컴포넌트가 없습니다!", newUnitGO);
            Destroy(newUnitGO); // 잘못된 프리팹이면 파괴
        }
    }
    
    /// <summary>
    /// 유닛이 죽었을 때 호출되어 관리 목록에서 제거합니다.
    /// </summary>
    public void UnitDied(Unit deadUnit)
    {
        if (placedUnits.ContainsValue(deadUnit))
        {
            // KeyValuePair를 찾아 해당 키로 제거
            var item = placedUnits.First(kvp => kvp.Value == deadUnit);
            placedUnits.Remove(item.Key);
        }
    }

    private Vector3Int? FindFirstEmptySlot()
    {
        if (obstacleTilemap == null) return null;
        
        BoundsInt bounds = obstacleTilemap.cellBounds;
        // 타일맵 전체를 순회하며 비어있는 유효한 위치를 찾습니다.
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
    
    /// <summary>
    /// 필드 위의 유닛들을 확인하여 조합(업그레이드)을 실행합니다.
    /// </summary>
    public void CheckForCombination()
    {
        // 조합 가능한 그룹을 찾습니다: 이름이 같고, 3성 미만이며, 3개 이상 모인 유닛 그룹
        var combinableGroup = placedUnits.Values
            .Where(u => u != null && u.starLevel < 3)
            .GroupBy(u => new { u.Data.unitName, u.starLevel })
            .Where(g => g.Count() >= 3)
            .FirstOrDefault();

        if (combinableGroup != null)
        {
            // 조합 대상 유닛 3개를 리스트로 만듭니다.
            List<Unit> unitsToCombine = combinableGroup.Take(3).ToList();
            
            // 1. 조합 재료가 될 유닛 2개를 필드에서 제거합니다.
            for (int i = 0; i < 2; i++)
            {
                UnitDied(unitsToCombine[i]);
                Destroy(unitsToCombine[i].gameObject);
            }
            
            // 2. 베이스가 될 마지막 유닛의 Upgrade() 메서드를 호출합니다.
            Unit baseUnit = unitsToCombine[2];
            baseUnit.Upgrade();

            // 3. [핵심] 업그레이드된 유닛의 외형을 교체합니다.
            ReplaceUnitPrefab(baseUnit);

            // 4. 또 다른 조합이 가능한지 재귀적으로 확인합니다.
            CheckForCombination();
        }
    }

    /// <summary>
    /// 유닛의 성급이 변경되었을 때, 그에 맞는 새로운 프리팹으로 교체합니다.
    /// </summary>
    private void ReplaceUnitPrefab(Unit unitToReplace)
    {
        // 현재 유닛의 위치와 UnitData를 기억합니다.
        Vector3Int currentPos = placedUnits.First(kvp => kvp.Value == unitToReplace).Key;
        UnitData unitData = unitToReplace.Data;
        int newStarLevel = unitToReplace.starLevel;

        // 기존 게임 오브젝트를 파괴합니다.
        UnitDied(unitToReplace);
        Destroy(unitToReplace.gameObject);
        
        // 새로운 성급의 프리팹으로 같은 위치에 다시 생성합니다.
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
            // 내려놓는 위치가 비어있는지 확인 (자기 자신은 제외)
            if (placementManager.IsPositionValidForPlacement(gridPos))
            {
                Vector3 finalWorldPos = obstacleTilemap.CellToWorld(gridPos) + (obstacleTilemap.cellSize * 0.5f);
                selectedUnit.transform.position = finalWorldPos;
                placedUnits.Add(gridPos, selectedUnit);
                // 이동 후에도 조합을 확인합니다.
                CheckForCombination();
            }
            else
            {
                // 유효하지 않은 위치라면 원래 자리로 돌려놓습니다.
                Vector3 originalWorldPos = obstacleTilemap.CellToWorld(originalUnitPosition) + (obstacleTilemap.cellSize * 0.5f);
                selectedUnit.transform.position = originalWorldPos;
                placedUnits.Add(originalUnitPosition, selectedUnit);
            }
            selectedUnit = null;
        }
    }

    #endregion
}