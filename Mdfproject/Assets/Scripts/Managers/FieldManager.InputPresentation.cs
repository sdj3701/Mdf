using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Fusion;

public partial class FieldManager
{
    #region 유닛 상세 정보 패널 및 드래그 앤 드롭

    /// <summary>
    /// [3D] 마우스 위치를 3D 월드 좌표로 변환합니다. Ground 평면과의 교차점을 사용합니다.
    /// </summary>
    private Vector3 GetMouseWorldPosition()
    {
        if (ground3D == null) return Vector3.zero;
        Ray ray = playerCamera.ScreenPointToRay(MdfInput.PointerPosition);
        Plane groundPlane = new Plane(Vector3.up, gridOrigin);
        if (groundPlane.Raycast(ray, out float enter))
        {
            return ray.GetPoint(enter);
        }
        return Vector3.zero;
    }

    /// <summary>
    /// 마우스 아래의 유닛을 찾습니다. 3D Raycast를 사용하고, 실패 시 스크린 거리 근사치를 사용합니다.
    /// </summary>
    private Unit GetUnitUnderMouse()
    {
        if (playerCamera == null) return null;

        Vector2 mouseScreen = MdfInput.PointerPosition;
        Ray ray = playerCamera.ScreenPointToRay(mouseScreen);
        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var unit3D = hit.collider.GetComponentInParent<Unit>();
                if (unit3D == null || !placedUnits.ContainsValue(unit3D)) continue;

                if (TryGetUnitScreenRect(unit3D, out Rect rect, out _))
                {
                    if (rect.Contains(mouseScreen))
                    {
                        return unit3D;
                    }
                }
                else
                {
                    return unit3D;
                }
            }
        }

        Unit screenHit = GetUnitFromScreenBounds(mouseScreen);
        if (screenHit != null)
        {
            return screenHit;
        }

        if (useWorldDistanceFallback && clickPickMaxWorldDistance > 0f && placedUnits.Count > 0 && ground3D != null)
        {
            Vector3 mouseWorld = GetMouseWorldPosition();
            float thresholdSq = clickPickMaxWorldDistance * clickPickMaxWorldDistance;
            Unit bestUnit = null;
            float bestDistSq = thresholdSq;

            foreach (var unit in placedUnits.Values)
            {
                if (unit == null) continue;
                Vector3 unitPos = unit.transform.position;
                Vector2 delta = new Vector2(unitPos.x - mouseWorld.x, unitPos.z - mouseWorld.z);
                float distSq = delta.sqrMagnitude;
                if (distSq <= bestDistSq)
                {
                    bestDistSq = distSq;
                    bestUnit = unit;
                }
            }

            if (bestUnit != null)
            {
                return bestUnit;
            }
        }

        return null;
    }

    private Unit GetUnitFromScreenBounds(Vector2 mouseScreen)
    {
        if (placedUnits.Count == 0) return null;

        Unit bestUnit = null;
        float bestDepth = float.MaxValue;

        foreach (var unit in placedUnits.Values)
        {
            if (unit == null) continue;
            if (!TryGetUnitScreenRect(unit, out Rect rect, out float depth)) continue;
            if (!rect.Contains(mouseScreen)) continue;

            if (depth < bestDepth)
            {
                bestDepth = depth;
                bestUnit = unit;
            }
        }

        return bestUnit;
    }

    private bool TryGetUnitScreenRect(Unit unit, out Rect rect, out float depth)
    {
        rect = default;
        depth = float.MaxValue;
        if (playerCamera == null) return false;
        if (!TryGetUnitBounds(unit, out Bounds bounds)) return false;

        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        bool anyInFront = false;

        anyInFront |= AccumulateScreenRect(center + new Vector3(-extents.x, -extents.y, -extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(-extents.x, -extents.y, extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(-extents.x, extents.y, -extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(-extents.x, extents.y, extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(extents.x, -extents.y, -extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(extents.x, -extents.y, extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(extents.x, extents.y, -extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(extents.x, extents.y, extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);

        if (!anyInFront) return false;

        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private bool AccumulateScreenRect(
        Vector3 worldPoint,
        ref float minX,
        ref float maxX,
        ref float minY,
        ref float maxY,
        ref float minDepth)
    {
        Vector3 screen = playerCamera.WorldToScreenPoint(worldPoint);
        if (screen.z <= 0f) return false;

        if (screen.x < minX) minX = screen.x;
        if (screen.x > maxX) maxX = screen.x;
        if (screen.y < minY) minY = screen.y;
        if (screen.y > maxY) maxY = screen.y;
        if (screen.z < minDepth) minDepth = screen.z;
        return true;
    }

    private bool TryGetUnitBounds(Unit unit, out Bounds bounds)
    {
        bounds = default;
        if (unit == null) return false;

        bool hasBounds = false;
        Renderer[] renderers = unit.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled) continue;
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds) return true;

        Collider[] colliders = unit.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled) continue;
            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    private void HandleUnitDragAndDrop()
    {
        if (GameManagers.Instance == null)
        {
            // 아직 GameManagers가 준비되지 않았으면 아무것도 하지 않고 함수를 종료합니다.
            return;
        }
        var gameState = GameManagers.Instance.GetGameState();
        bool isActiveGameState = gameState == GameManagers.GameState.Prepare 
            || gameState == GameManagers.GameState.Battle1 
            || gameState == GameManagers.GameState.Battle2;
        if (!isActiveGameState) return;

        if (playerCamera == null)
        {
            // Debug.LogWarning("[FieldManager] playerCamera is null!");
            return;
        }

        // [3D] 초기화 확인 - Host Migration 후 재할당 필요할 수 있음
        if (ground3D == null)
        {
            // Debug.Log($"[FieldManager] ground3D null 감지, fallback 시도... playerManager={playerManager?.name}");
            
            // 방법 1: 부모 계층에서 Ground 찾기
            var parentTransform = transform.parent;
            // Debug.Log($"[FieldManager] parentTransform={parentTransform?.name}");
            
            if (parentTransform != null)
            {
                var groundTransform = parentTransform.Find("Ground") ?? parentTransform.Find("Field");
                if (groundTransform != null)
                {
                    ground3D = groundTransform.gameObject;
                    // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법1: parent.Find)");
                }
                else
                {
                    var meshRenderer = parentTransform.GetComponentInChildren<MeshRenderer>(true);
                    if (meshRenderer != null)
                    {
                        ground3D = meshRenderer.gameObject;
                        // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법2: MeshRenderer)");
                    }
                }
            }
            
            // 방법 2: playerManager.astarGrid에서 찾기
            if (ground3D == null && playerManager != null && playerManager.astarGrid != null)
            {
                var gridParent = playerManager.astarGrid.transform.parent;
                // Debug.Log($"[FieldManager] astarGrid.parent={gridParent?.name}");
                
                if (gridParent != null)
                {
                    var groundTransform = gridParent.Find("Ground") ?? gridParent.Find("Field");
                    if (groundTransform != null)
                    {
                        ground3D = groundTransform.gameObject;
                        // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법3: astarGrid.parent)");
                    }
                    else
                    {
                        // 자식 전체 순회
                        foreach (Transform child in gridParent)
                        {
                            if (child.name.Contains("Ground") || child.name.Contains("Field"))
                            {
                                ground3D = child.gameObject;
                                // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법4: 자식 순회)");
                                break;
                            }
                        }
                    }
                }
            }
            
            // 방법 3: GameManagers.localPlayer에서 찾기
            if (ground3D == null && GameManagers.Instance != null)
            {
                var localPlayer = GameManagers.Instance.localPlayer;
                // Debug.Log($"[FieldManager] GameManagers.localPlayer={localPlayer?.name}");
                
                if (localPlayer != null && localPlayer.astarGrid != null)
                {
                    var gridParent = localPlayer.astarGrid.transform.parent;
                    if (gridParent != null)
                    {
                        var groundTransform = gridParent.Find("Ground") ?? gridParent.Find("Field");
                        if (groundTransform != null)
                        {
                            ground3D = groundTransform.gameObject;
                            // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법5: GameManagers)");
                        }
                    }
                }
            }
            
            // 방법 4: FindObjectOfType으로 AstarGrid 찾아서 parent에서 Ground 찾기
            if (ground3D == null)
            {
                // Debug.Log("[FieldManager] 방법6 시도: FindObjectOfType<AstarGrid>");
                var allGrids = UnityEngine.Object.FindObjectsOfType<AstarGrid>(true);
                // Debug.Log($"[FieldManager] 발견된 AstarGrid 수: {allGrids.Length}");
                
                foreach (var grid in allGrids)
                {
                    var gridParent = grid.transform.parent;
                    if (gridParent != null)
                    {
                        var groundTransform = gridParent.Find("Ground") ?? gridParent.Find("Field");
                        if (groundTransform != null)
                        {
                            ground3D = groundTransform.gameObject;
                            
                            // astarGrid도 복구
                            if (playerManager != null && playerManager.astarGrid == null)
                            {
                                playerManager.astarGrid = grid;
                                // Debug.Log($"[FieldManager] astarGrid도 재할당: {grid.name}");
                            }
                            
                            // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법6: FindObjectOfType)");
                            break;
                        }
                    }
                }
            }
            
            // 방법 5: 마지막으로 Ground 이름이 포함된 모든 오브젝트 찾기
            if (ground3D == null)
            {
                // Debug.Log("[FieldManager] 방법7 시도: GameObject.Find");
                var foundGround = GameObject.Find("Ground");
                if (foundGround != null)
                {
                    ground3D = foundGround;
                    // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법7: GameObject.Find)");
                }
            }
            
            // 그래도 못 찾으면 에러
            if (ground3D == null)
            {
                // Debug.LogWarning("[FieldManager] ground3D is null - 모든 fallback 실패!");
                return;
            }
        }

        // [3D Migration] 마우스 월드 좌표 및 그리드 좌표 계산
        Vector3 mouseWorldPos = GetMouseWorldPosition();
        Vector3Int gridPos = WorldToGridInt(mouseWorldPos);

        if (MdfInput.SecondaryPointerWasPressedThisFrame() && !MdfInput.IsPointerOverFieldBlockingUI())
        {
            if (TryRequestRemoveWallAt(gridPos))
            {
                return;
            }
        }

        // 마우스 버튼을 눌렀을 때
        if (MdfInput.PrimaryPointerWasPressedThisFrame())
        {
            // 셀 기반이 아니라 실제 유닛 콜라이더를 클릭해야 드래그 시작
            Unit clickedUnit = GetUnitUnderMouse();
            bool pointerOverUI = MdfInput.IsPointerOverFieldBlockingUI();
            if (pointerOverUI && clickedUnit != null && ShouldAllowUnitDragThroughPrepareToolkit())
            {
                pointerOverUI = false;
            }

            // 패널이 열려있는 상태에서
            if (unitDetailPanelInstance != null && unitDetailPanelInstance.activeSelf)
            {
                // 표시된 유닛을 다시 클릭한 경우 -> 패널 닫고 아무것도 안 함
                if (!pointerOverUI && clickedUnit != null && clickedUnit == unitDisplayedInPanel)
                {
                    UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
                    unitDetailPanelInstance = null;
                    unitDisplayedInPanel = null;
                    HideUnitSellPanel();
                    HideWallRemovePanel();
                    selectedUnit = null; // 모든 상태 초기화
                    return;
                }

                // UI가 아닌 다른 곳을 클릭한 경우 -> 패널 닫고 클릭한 대상에 대한 처리 계속
                if (!pointerOverUI)
                {
                    UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
                    unitDetailPanelInstance = null;
                    unitDisplayedInPanel = null;
                    HideUnitSellPanel();
                    HideWallRemovePanel();
                }
            }

            // 이제 클릭한 대상에 대한 처리 (드래그 시작 또는 새 패널 열기 준비)
            // UI 위를 클릭한 경우에는 새 선택/드래그를 시작하지 않습니다.
            if (pointerOverUI)
            {
                return;
            }
            if (clickedUnit != null)
            {
                selectedUnit = clickedUnit;
                mouseDownTimer = 0f;
                isDragStarted = false;
                // [3D Migration] 유닛의 현재 위치를 그리드 좌표로 변환
                originalUnitPosition = GetUnitPosition(selectedUnit) ?? WorldToGridInt(selectedUnit.transform.position);
                // 3D 드래그를 위한 XZ 오프셋 및 기준 Y 저장
                dragBaseY = selectedUnit.transform.position.y;
                offsetXZ = new Vector2(
                    selectedUnit.transform.position.x - mouseWorldPos.x,
                    selectedUnit.transform.position.z - mouseWorldPos.z
                );

                // NetworkTransform 참조 저장 (드래그 시작 시 비활성화할 예정)
                selectedUnitNetworkTransform = selectedUnit.GetComponent<Fusion.NetworkTransform>();
            }
            else
            {
                // 유닛이 없는 곳을 클릭함 -> 벽만 있는지 확인
                Vector3Int clickedGridPos = WorldToGridInt(mouseWorldPos);
                var clickedWall = GetWallAt(clickedGridPos);
                if (clickedWall != null)
                {
                    // 벽만 있는 경우: 벽 제거 패널만 표시
                    ShowWallRemovePanel(clickedWall, clickedGridPos);
                }
                else
                {
                    // 유닛도 벽도 없는 빈 공간: 벽 제거 패널 숨김
                    HideWallRemovePanel();
                }
            }
        }

        // 마우스 버튼을 누르고 있을 때
        if (MdfInput.PrimaryPointerIsPressed() && selectedUnit != null)
        {
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

                    // ✅ NetworkTransform 비활성화 (로컬 드래그를 위해)
                    if (selectedUnitNetworkTransform != null)
                    {
                        selectedUnitNetworkTransform.enabled = false;
                        string selName = (selectedUnit != null && selectedUnit.Data != null) ? selectedUnit.Data.unitName : (selectedUnit != null ? selectedUnit.name : "Unit");
                    }

                    // 드래그가 시작되면 열려있던 상세 정보 패널을 닫음
                    if (unitDetailPanelInstance != null && unitDetailPanelInstance.activeSelf)
                    {
                        UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
                        unitDetailPanelInstance = null;
                        unitDisplayedInPanel = null;
                        HideUnitSellPanel();
                    }
                }
            }
            else
            {
                // 드래그 중: 유닛이 마우스를 부드럽게 따라감 (XZ는 마우스, Y는 들어올림)
                float targetX = mouseWorldPos.x + offsetXZ.x;
                float targetZ = mouseWorldPos.z + offsetXZ.y;
                float targetY = dragBaseY + dragLiftHeight;
                Vector3 targetPos = new Vector3(targetX, targetY, targetZ);

                // NetworkTransform이 비활성화되어 있으므로 직접 transform.position 변경 가능
                selectedUnit.transform.position = Vector3.MoveTowards(
                    selectedUnit.transform.position,
                    targetPos,
                    dragFollowSpeed * Time.deltaTime
                );
            }
        }

        // 마우스 버튼을 뗐을 때
        if (MdfInput.PrimaryPointerWasReleasedThisFrame() && selectedUnit != null)
        {
            if (isDragStarted)
            {
                RestoreSelectedUnitNetworkTransform();

                // 드래그 종료: 화면상 마우스와 가장 겹쳐 보이는 셀을 최종 선택
                Vector3Int bestGrid = GetBestGridUnderMouse();
                bestGrid.x = Mathf.Clamp(bestGrid.x, 0, gridSize.x - 1);
                bestGrid.y = Mathf.Clamp(bestGrid.y, 0, gridSize.y - 1);

                if (placementManager.IsPositionValidForPlacement(bestGrid, selectedUnit.Data))
                {
                    // 네트워크 준비 상태 확인 (Runner가 실행 중이면 localPlayer와 InputAuthority 확인)
                    bool canSend = IsNetworkReadyAndHasInputAuthority();

                    // 드래그 중 비활성화한 NetworkTransform을 성공 드랍 시 항상 복구
                    if (selectedUnitNetworkTransform != null && !selectedUnitNetworkTransform.enabled)
                    {
                        selectedUnitNetworkTransform.enabled = true;
                    }

                    if (canSend)
                    {
                        var command = new MoveUnitCommand(playerManager.playerId, originalUnitPosition, bestGrid);
                        GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
                    }
                    else
                    {
                        // 네트워크 준비가 안 되었으면 원위치 복귀
                        Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);
                        SnapbackSelectedUnit(originalWorldPos);
                    }
                }
                else
                {
                    Unit target = GetUnitAt(bestGrid);
                    if (target != null)
                    {
                        bool destWallForSelected = HasWallAt(bestGrid);
                        bool destWallForTarget = HasWallAt(originalUnitPosition);
                        bool invalidForSelected = selectedUnit.Data != null && selectedUnit.Data.unitType == UnitType.Melee && destWallForSelected;
                        bool invalidForTarget = target.Data != null && target.Data.unitType == UnitType.Melee && destWallForTarget;
                        if (!invalidForSelected && !invalidForTarget)
                        {
                            // 네트워크 준비 상태 확인
                            bool canSend = IsNetworkReadyAndHasInputAuthority();

                            // 성공 드랍 경로에서도 NetworkTransform 복구
                            if (selectedUnitNetworkTransform != null && !selectedUnitNetworkTransform.enabled)
                            {
                                selectedUnitNetworkTransform.enabled = true;
                            }

                            if (canSend)
                            {
                                var swapCmd = new SwapUnitCommand(playerManager.playerId, originalUnitPosition, bestGrid);
                                GameManagers.Instance.CommandProcessor.RequestCommandExecution(swapCmd);
                            }
                            else
                            {
                                
                                // 네트워크 준비가 안 되었으면 원위치 복귀
                                Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);
                                SnapbackSelectedUnit(originalWorldPos);
                            }
                        }
                        else
                        {
                            
                            Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);

                            // ✅ NetworkTransform 처리
                            SnapbackSelectedUnit(originalWorldPos);
                        }
                    }
                    else
                    {
                        
                        Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);

                        // ✅ NetworkTransform 처리
                        SnapbackSelectedUnit(originalWorldPos);
                    }
                }
            }
            else
            {
                // 짧은 클릭: 이 FieldManager 소유 유닛만 스냅백. (교차 플레이어 유닛 보호)
                if (placedUnits.ContainsValue(selectedUnit))
                {
                    Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);
                    SnapbackSelectedUnit(originalWorldPos);
                }
                // 죽은 유닛인 경우 UI 표시 건너뛰기
                if (selectedUnit.IsDead)
                {
                    selectedUnit = null;
                    selectedUnitNetworkTransform = null;
                    isDragStarted = false;
                    return;
                }
                ShowUnitDetailPanel(selectedUnit);
                ShowUnitSellPanel(selectedUnit);
                // 유닛이 서 있는 그리드에 벽이 있으면 벽 제거 패널도 표시
                var wallAtUnitPos = GetWallAt(originalUnitPosition);
                if (wallAtUnitPos != null)
                {
                    ShowWallRemovePanel(wallAtUnitPos, originalUnitPosition);
                }
            }

            // 상태 초기화
            selectedUnit = null;
            selectedUnitNetworkTransform = null;
            isDragStarted = false;
        }
    }

    private bool TryRequestRemoveWallAt(Vector3Int gridPosition)
    {
        var gm = GameManagers.Instance;
        if (gm == null || gm.GetGameState() != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning)
        {
            return false;
        }

        if (playerManager == null || playerManager.playerId < 0 || GetWallAt(gridPosition) == null)
        {
            return false;
        }

        if (gm.CommandProcessor == null)
        {
            return false;
        }

        gm.CommandProcessor.RequestCommandExecution(new RemoveWallCommand(playerManager.playerId, gridPosition));
        return true;
    }

    private bool ShouldAllowUnitDragThroughPrepareToolkit()
    {
        var gm = GameManagers.Instance;
        if (gm == null || gm.GetGameState() != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning)
        {
            return false;
        }

        if (!GamePrepareUIToolkitController.IsToolkitActive)
        {
            return false;
        }

        return !GamePrepareUIToolkitController.IsPointerOverBlockingElement(MdfInput.PointerPosition);
    }

    private void RestoreSelectedUnitNetworkTransform()
    {
        if (selectedUnitNetworkTransform != null && !selectedUnitNetworkTransform.enabled)
        {
            selectedUnitNetworkTransform.enabled = true;
        }
    }

    private async void ShowUnitDetailPanel(Unit unit)
    {
        // 죽은 유닛인 경우 패널을 표시하지 않음
        if (unit == null || unit.IsDead) return;

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

    private async void ShowUnitSellPanel(Unit unit)
    {
        if (unit == null || UIManagers.Instance == null) return;

        // 전투 시퀀스에서는 판매 패널을 표시하지 않음
        var gm = GameManagers.Instance;
        if (gm != null && (gm.GetGameState() != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning))
        {
            return;
        }

        if (unitSellPanelInstance == null)
        {
            unitSellPanelInstance = await UIManagers.Instance.GetUIElement("UI_Can_UnitSell");
        }

        if (unitSellPanelInstance != null)
        {
            unitSellPanelInstance.transform.SetParent(unit.transform, false);

            var rootCanvas = unitSellPanelInstance.GetComponent<Canvas>();
            if (rootCanvas != null)
            {
                rootCanvas.renderMode = RenderMode.WorldSpace;
                rootCanvas.worldCamera = playerCamera;
            }

            var controller = unitSellPanelInstance.GetComponentInChildren<UnitSellPanelController>(true);
            if (controller != null)
            {
                var controllerCanvas = controller.GetComponent<Canvas>();
                if (controllerCanvas != null && controllerCanvas != rootCanvas)
                {
                    controllerCanvas.renderMode = RenderMode.WorldSpace;
                    controllerCanvas.worldCamera = playerCamera;
                }
                controller.Bind(unit, this);
                unitSellPanelInstance.SetActive(true);
                unitDisplayedInSellPanel = unit;
            }
        }
    }

    private void HideUnitSellPanel()
    {
        if (unitSellPanelInstance != null && UIManagers.Instance != null)
        {
            UIManagers.Instance.ReturnUIElement("UI_Can_UnitSell");
        }
        unitSellPanelInstance = null;
        unitDisplayedInSellPanel = null;
    }

    /// <summary>
    /// 벽 제거 패널을 표시합니다.
    /// </summary>
    private async void ShowWallRemovePanel(DestructibleWall wall, Vector3Int gridPosition)
    {
        if (wall == null || UIManagers.Instance == null) return;

        // 전투 시퀀스에서는 벽 제거 패널을 표시하지 않음
        var gm = GameManagers.Instance;
        if (gm != null && (gm.GetGameState() != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning))
        {
            return;
        }

        if (wallRemovePanelInstance == null)
        {
            wallRemovePanelInstance = await UIManagers.Instance.GetUIElement("UI_Can_WallRemove");
        }

        if (wallRemovePanelInstance != null)
        {
            // 벽의 자식이 아닌 FieldManager의 자식으로 설정하여 렌더링 순서 문제 해결
            wallRemovePanelInstance.transform.SetParent(transform, false);

            var rootCanvas = wallRemovePanelInstance.GetComponent<Canvas>();
            if (rootCanvas != null)
            {
                rootCanvas.renderMode = RenderMode.WorldSpace;
                rootCanvas.worldCamera = playerCamera;
                rootCanvas.overrideSorting = true;
                rootCanvas.sortingOrder = 300;
            }

            var controller = wallRemovePanelInstance.GetComponentInChildren<WallRemovePanelController>(true);
            if (controller != null)
            {
                var controllerCanvas = controller.GetComponent<Canvas>();
                if (controllerCanvas != null && controllerCanvas != rootCanvas)
                {
                    controllerCanvas.renderMode = RenderMode.WorldSpace;
                    controllerCanvas.worldCamera = playerCamera;
                    controllerCanvas.overrideSorting = true;
                    controllerCanvas.sortingOrder = 300;
                }
                controller.Bind(wall, gridPosition, this);
                wallRemovePanelInstance.SetActive(true);
                wallDisplayedInRemovePanel = wall;
            }
        }
    }

    /// <summary>
    /// 모든 선택 관련 UI 패널을 숨깁니다. (유닛 디테일, 유닛 판매, 벽 제거)
    /// </summary>
    public void HideAllSelectionPanels()
    {
        // 유닛 디테일 패널 숨기기
        if (unitDetailPanelInstance != null && UIManagers.Instance != null)
        {
            UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
            unitDetailPanelInstance = null;
            unitDisplayedInPanel = null;
        }

        // 유닛 판매 패널 숨기기
        HideUnitSellPanel();

        // 벽 제거 패널 숨기기
        HideWallRemovePanel();
    }

    /// <summary>
    /// 벽 제거 패널을 숨깁니다.
    /// </summary>
    private void HideWallRemovePanel()
    {
        if (wallRemovePanelInstance != null && UIManagers.Instance != null)
        {
            UIManagers.Instance.ReturnUIElement("UI_Can_WallRemove");
        }
        wallRemovePanelInstance = null;
        wallDisplayedInRemovePanel = null;
    }
    #endregion

}
