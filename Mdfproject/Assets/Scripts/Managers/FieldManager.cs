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
    public TileBase wallTileToPlace;
    public GameObject statusBarPrefab;

    [Header("범위 표시")]
    public GameObject attackRangeIndicatorPrefab;
    public GameObject skillRangeIndicatorPrefab;
    private GameObject attackRangeIndicatorInstance;
    private GameObject skillRangeIndicatorInstance;

    public Tilemap ObstacleTilemap { get; private set; }
    public Tilemap GroundTilemap { get; private set; }

    private PlacementManager placementManager;
    private Dictionary<Vector3Int, Unit> placedUnits = new Dictionary<Vector3Int, Unit>();
    private Dictionary<Vector3Int, DestructibleWall> placedWalls = new Dictionary<Vector3Int, DestructibleWall>();

    private Unit selectedUnit;
    private Vector3Int originalUnitPosition;
    private Vector3 offset;

    // 유닛 클릭/드래그 및 상세 정보 패널 관련 변수
    private float mouseDownTimer;
    private const float dragDelay = 0.2f; // 0.2초 이상 누르면 드래그 시작
    private bool isDragStarted = false;
    private GameObject unitDetailPanelInstance;
    private Unit unitDisplayedInPanel;
    
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
        // [수정] 게임 상태가 전투로 변경될 때의 처리
        else if (newState == GameManagers.GameState.Combat)
        {
            // 활성화된 배치 모드(유닛, 벽 등)가 있다면 강제로 종료합니다.
            if (placementManager.GetCurrentMode() != PlacementMode.None)
            {
                placementManager.StopPlacementMode();
            }

            // 유닛을 드래그하는 중이었다면 취소하고 원위치시킵니다.
            if (selectedUnit != null)
            {
                Vector3 originalWorldPos = ObstacleTilemap.CellToWorld(originalUnitPosition) + (ObstacleTilemap.cellSize * 0.5f);
                selectedUnit.transform.position = originalWorldPos;
                // 드래그 중에는 placedUnits에서 제거되지 않으므로, 다시 Add할 필요가 없습니다.

                Debug.Log($"<color=orange>전투 시작으로 인해 {selectedUnit.Data.unitName}의 배치가 취소되고 원위치로 돌아갑니다.</color>");

                // 드래그 상태를 초기화합니다.
                selectedUnit = null;
            }
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
        
        if (wallTileToPlace != null)
        {
            ObstacleTilemap.SetTile(gridPosition, wallTileToPlace);
        }

        Vector3 worldPos = ObstacleTilemap.CellToWorld(gridPosition) + (ObstacleTilemap.cellSize * 0.5f);
        GameObject wallGO = Instantiate(destructibleWallPrefab, worldPos, Quaternion.identity, wallParent);
        DestructibleWall wallComponent = wallGO.GetComponent<DestructibleWall>();

        if (wallComponent != null)
        {
            if (statusBarPrefab != null)
            {
                GameObject statusBarGO = Instantiate(statusBarPrefab, wallGO.transform);
                wallComponent.SetStatusBar(statusBarGO.GetComponent<StatusBarUI>());
            }
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
        // 유닛을 드래그하는 중이고, 그 유닛의 원래 위치를 확인하는 경우
        // 배치 목적상 해당 위치는 비어있는 것으로 간주합니다.
        // 이렇게 하면 유닛을 원래 위치에 다시 놓을 수 있습니다.
        if (selectedUnit != null && originalUnitPosition == gridPosition)
        {
            return false;
        }
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
    
    public void CreateUnitAt(UnitData data, Vector3Int gridPosition, int starLevel)
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
            if (statusBarPrefab != null)
            {
                GameObject statusBarGO = Instantiate(statusBarPrefab, newUnitGO.transform);
                newUnitComponent.SetStatusBar(statusBarGO.GetComponent<StatusBarUI>());
            }
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

    public void MoveUnit(Vector3Int from, Vector3Int to)
    {
        if (placedUnits.TryGetValue(from, out Unit unit))
        {
            placedUnits.Remove(from);
            Vector3 finalWorldPos = ObstacleTilemap.CellToWorld(to) + (ObstacleTilemap.cellSize * 0.5f);
            unit.transform.position = finalWorldPos;
            placedUnits.Add(to, unit);
            CheckForCombination();
        }
        else
        {
            Debug.LogWarning($"[FieldManager] MoveUnit: '{from}' 위치에서 유닛을 찾을 수 없습니다.");
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

    #region 유닛 상세 정보 패널 및 드래그 앤 드롭
    
    private void HandleUnitDragAndDrop()
    {
        var gameState = GameManagers.Instance.GetGameState();
        if (gameState != GameManagers.GameState.Prepare && gameState != GameManagers.GameState.Combat) return;

        if (playerCamera == null || ObstacleTilemap == null) return;
        
        Vector3 mouseWorldPos = playerCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int gridPos = ObstacleTilemap.WorldToCell(mouseWorldPos);

        // 마우스 버튼을 눌렀을 때
        if (Input.GetMouseButtonDown(0))
        {
            Unit clickedUnit = placedUnits.ContainsKey(gridPos) ? placedUnits[gridPos] : null;

            // 패널이 열려있는 상태에서
            if (unitDetailPanelInstance != null && unitDetailPanelInstance.activeSelf)
            {
                // 표시된 유닛을 다시 클릭한 경우 -> 패널 닫고 아무것도 안 함
                if (clickedUnit != null && clickedUnit == unitDisplayedInPanel)
                {
                    UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
                    unitDetailPanelInstance = null;
                    unitDisplayedInPanel = null;
                    selectedUnit = null; // 모든 상태 초기화
                    return;
                }
                
                // UI가 아닌 다른 곳을 클릭한 경우 -> 패널 닫고 클릭한 대상에 대한 처리 계속
                if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                {
                    UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
                    unitDetailPanelInstance = null;
                    unitDisplayedInPanel = null;
                }
            }

            // 이제 클릭한 대상에 대한 처리 (드래그 시작 또는 새 패널 열기 준비)
            if (clickedUnit != null)
            {
                selectedUnit = clickedUnit;
                mouseDownTimer = 0f;
                isDragStarted = false;
                originalUnitPosition = ObstacleTilemap.WorldToCell(selectedUnit.transform.position);
                offset = selectedUnit.transform.position - mouseWorldPos;
            }
        }

        // 마우스 버튼을 누르고 있을 때
        if (Input.GetMouseButton(0) && selectedUnit != null)
        {
            // 드래그 상태와 관계없이 유닛 미리보기 위치를 부드럽게 업데이트합니다.
            selectedUnit.transform.position = new Vector3(mouseWorldPos.x + offset.x, mouseWorldPos.y + offset.y, selectedUnit.transform.position.z);

            // 아직 드래그가 시작되지 않았다면, 타이머를 확인하여 드래그 상태로 전환할지 결정합니다.
            if (!isDragStarted)
            {
                mouseDownTimer += Time.deltaTime;
                // 준비 단계일 때만 드래그를 시작할 수 있습니다.
                if (mouseDownTimer >= dragDelay && gameState == GameManagers.GameState.Prepare)
                {
                    // 드래그 시작
                    isDragStarted = true;
                    // offset과 originalUnitPosition은 이미 GetMouseButtonDown에서 설정되었습니다.
                    
                    // 드래그가 시작되면 열려있던 상세 정보 패널을 닫음
                    if (unitDetailPanelInstance != null && unitDetailPanelInstance.activeSelf)
                    {
                        UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
                        unitDetailPanelInstance = null;
                        unitDisplayedInPanel = null;
                    }
                }
            }
        }

        // 마우스 버튼을 뗐을 때
        if (Input.GetMouseButtonUp(0) && selectedUnit != null)
        {
            if (isDragStarted)
            {
                // 드래그 종료 로직 (기존과 동일)
                if (placementManager.IsPositionValidForPlacement(gridPos, selectedUnit.Data))
                {
                    GameEvents.TriggerUnitMoveRequested(playerManager, originalUnitPosition, gridPos);
                }
                else
                {
                    Vector3 originalWorldPos = ObstacleTilemap.CellToWorld(originalUnitPosition) + (ObstacleTilemap.cellSize * 0.5f);
                    selectedUnit.transform.position = originalWorldPos;
                }
            }
            else
            {
                // 짧은 클릭이었으므로, 유닛을 원래 위치로 되돌리고 상세 정보 패널을 엽니다.
                Vector3 originalWorldPos = ObstacleTilemap.CellToWorld(originalUnitPosition) + (ObstacleTilemap.cellSize * 0.5f);
                selectedUnit.transform.position = originalWorldPos;
                ShowUnitDetailPanel(selectedUnit);
            }
            
            // 상태 초기화
            selectedUnit = null;
            isDragStarted = false;
        }
    }

    private async void ShowUnitDetailPanel(Unit unit)
    {
        // 패널 인스턴스가 없으면 UIManagers를 통해 가져옵니다.
        // 이는 씬에 미리 배치된 패널을 찾거나, 없을 경우 새로 생성하는 역할을 합니다.
        if (unitDetailPanelInstance == null)
        {
            unitDetailPanelInstance = await UIManagers.Instance.GetUIElement("UI_Pnl_UnitDetail");
        }

        if (unitDetailPanelInstance != null)
        {
            var controller = unitDetailPanelInstance.GetComponent<UnitDetailPanelController>();
            if (controller != null)
            {
                controller.DisplayUnitInfo(unit);
                // 패널의 위치는 프리팹/씬에 설정된 고정 위치를 사용하므로, 여기서 위치를 변경하지 않습니다.
                unitDetailPanelInstance.SetActive(true);
                unitDisplayedInPanel = unit;
            }
        }
    }
    #endregion

    #region 범위 표시
    
    /// <summary>
    /// 지정된 유닛의 공격 및 스킬 범위를 원형으로 표시하고, 겹치는 경우 렌더링 순서를 조정합니다.
    /// </summary>
    public void ShowRanges(Unit unit)
    {
        if (unit == null) return;

        ClearRanges();

        // 1. 각 범위 값 가져오기
        float attackRange = unit.currentAttackRange;
        float skillRange = 0f;

        if (skillRangeIndicatorPrefab != null && unit.Data.skillsByStarLevel.Length >= unit.starLevel &&
            unit.Data.skillsByStarLevel[unit.starLevel - 1] != null)
        {
            SkillData currentSkill = unit.Data.skillsByStarLevel[unit.starLevel - 1];
            // [중요] 아래 코드는 SkillData 스크립트에 'public float range;' 변수가 있다고 가정합니다.
            // 만약 변수 이름이 다르다면 이 부분을 실제 변수 이름으로 수정해야 합니다.
            skillRange = currentSkill.range; 
        }

        bool showAttack = attackRangeIndicatorPrefab != null && attackRange > 0;
        bool showSkill = skillRangeIndicatorPrefab != null && skillRange > 0;

        if (!showAttack && !showSkill) return;

        // 2. 범위가 동일할 경우 시각적 조정을 위해 공격 범위 약간 축소
        float attackDiameter = attackRange * 2f;
        float skillDiameter = skillRange * 2f;

        if (showAttack && showSkill && Mathf.Approximately(attackRange, skillRange))
        {
            attackDiameter *= 0.95f; // 공격 범위를 약간 줄여서 둘 다 보이게 함
        }
        
        // 3. 범위 인디케이터 생성 및 크기 설정
        if (showAttack)
        {
            attackRangeIndicatorInstance = Instantiate(attackRangeIndicatorPrefab, unit.transform.position, Quaternion.identity, transform);
            attackRangeIndicatorInstance.transform.localScale = new Vector3(attackDiameter, attackDiameter, 1f);
        }
        if (showSkill)
        {
            skillRangeIndicatorInstance = Instantiate(skillRangeIndicatorPrefab, unit.transform.position, Quaternion.identity, transform);
            skillRangeIndicatorInstance.transform.localScale = new Vector3(skillDiameter, skillDiameter, 1f);
        }

        // 4. 두 범위가 모두 표시될 때 렌더링 순서(Sorting Order) 조정
        if (showAttack && showSkill)
        {
            SpriteRenderer attackRenderer = attackRangeIndicatorInstance.GetComponent<SpriteRenderer>();
            SpriteRenderer skillRenderer = skillRangeIndicatorInstance.GetComponent<SpriteRenderer>();

            if (attackRenderer != null && skillRenderer != null)
            {
                // 더 큰 범위를 뒤에(sortingOrder = 0), 작은 범위를 앞에(sortingOrder = 1) 렌더링
                if (attackDiameter > skillDiameter)
                {
                    attackRenderer.sortingOrder = 0; // 뒤
                    skillRenderer.sortingOrder = 1;  // 앞
                }
                else
                {
                    skillRenderer.sortingOrder = 0;  // 뒤
                    attackRenderer.sortingOrder = 1; // 앞
                }
            }
        }
    }

    /// <summary>
    /// 표시된 모든 범위를 제거합니다.
    /// </summary>
    public void ClearRanges()
    {
        if (attackRangeIndicatorInstance != null)
        {
            Destroy(attackRangeIndicatorInstance);
            attackRangeIndicatorInstance = null;
        }
        if (skillRangeIndicatorInstance != null)
        {
            Destroy(skillRangeIndicatorInstance);
            skillRangeIndicatorInstance = null;
        }
    }

    #endregion
}
