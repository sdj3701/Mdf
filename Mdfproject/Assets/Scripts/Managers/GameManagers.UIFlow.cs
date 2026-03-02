using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

public partial class GameManagers
{
    private void OnEnable()
    {
        GameEvents.OnAugmentApplied += HandleAugmentChosen;
    }

    private void OnDisable()
    {
        GameEvents.OnAugmentApplied -= HandleAugmentChosen;
    }

    // [새로 추가] 네트워크 상태가 변경될 때 모든 클라이언트에서 반응하는 함수 (Render에서 호출됨)
    private void HandleNetworkStateChange(GameState newState)
    {
        // UI 초기화가 완료되기 전에는 처리하지 않음
        if (!_isSpawned) return;

        // 로컬 플레이어의 UI만 업데이트해야 하므로, 로컬 플레이어 확인 후 비동기 UI 로직 호출
        if (localPlayer == null)
        {
            RelinkLocalPlayer();
            if (localPlayer == null) return;
        }

        // UI 업데이트 및 이벤트 발송은 UniTask의 'Fire-and-Forget' 패턴으로 처리
        // Render()는 async/await을 할 수 없습니다.
        GameEvents.TriggerGameStateChanged(newState);
        HandleUIForNewState(newState).Forget();
    }

    /// <summary>
    /// 공격 시퀀스 UI를 비동기로 초기화하고 표시합니다.
    /// </summary>
    private async UniTask ShowAttackSequenceUIAsync(AttackSequenceManager attackSeqMgr)
    {
        if (localPlayer == null || attackSeqMgr == null) return;

        // UI 로드/초기화
        var ui = await AttackSequenceUIController.GetOrCreateAsync(localPlayer, attackSeqMgr);
        if (ui != null)
        {
            ui.Show(true);
            // Debug.Log($"<color=cyan>[ShowAttackSequenceUIAsync] 공격 시퀀스 UI 표시 완료</color>");
        }
        else
        {
            // Debug.LogWarning("[ShowAttackSequenceUIAsync] UI 로드 실패");
        }
    }

    /// <summary>
    /// UI 요소를 로드하고 참조를 저장합니다. 상태 관리는 각 UIController가 담당합니다.
    /// </summary>
    internal async UniTask<bool> EnsureGameUIReadyForSyncCommands()
    {
        const float timeoutSeconds = 8f;
        float waited = 0f;
        BuildDebugGUI.LogClient("EnsureGameUIReadyForSyncCommands: enter");

        while (waited < timeoutSeconds)
        {
            if (UIManagers.Instance == null)
            {
                var resolvedUIManager = FindObjectOfType<UIManagers>(true);
                if (resolvedUIManager != null)
                {
                    UIManagers.Instance = resolvedUIManager;
                    if (!resolvedUIManager.gameObject.activeSelf)
                    {
                        resolvedUIManager.gameObject.SetActive(true);
                    }
                }
            }

            await SetupGameUI();

            if (_hasCompletedGameUISetup && localPlayerShopUI != null && augmentSelectionUI != null)
            {
                BuildDebugGUI.LogClient($"EnsureGameUIReadyForSyncCommands: ready in {waited:F1}s");
                return true;
            }

            BuildDebugGUI.LogClientThrottled(
                "ui_sync_wait",
                $"EnsureGameUIReadyForSyncCommands: waiting {waited:F1}s (setup={_hasCompletedGameUISetup},shopUI={(localPlayerShopUI != null)},augmentUI={(augmentSelectionUI != null)})",
                0.8f);

            await UniTask.Delay(100);
            waited += 0.1f;
        }

        Debug.LogWarning($"[GameManagers] EnsureGameUIReadyForSyncCommands timeout. hasSetup={_hasCompletedGameUISetup}, shopUI={(localPlayerShopUI != null)}, augmentUI={(augmentSelectionUI != null)}");
        BuildDebugGUI.LogClient($"EnsureGameUIReadyForSyncCommands: timeout {timeoutSeconds:F1}s");
        return false;
    }

    private async UniTask SetupGameUI(bool forceRefresh = false)
    {
        LogMigrationTrace("SetupGameUI:ENTER", $"forceRefresh={forceRefresh}");

        if (UIManagers.Instance == null)
        {
            var resolvedUIManager = FindObjectOfType<UIManagers>(true);
            if (resolvedUIManager != null)
            {
                UIManagers.Instance = resolvedUIManager;
                if (!resolvedUIManager.gameObject.activeSelf)
                {
                    resolvedUIManager.gameObject.SetActive(true);
                }
                // Debug.LogWarning("[SetupGameUI] UIManagers.Instance가 null이어서 FindObjectOfType로 복구했습니다.");
            }
        }

        if (UIManagers.Instance == null)
        {
            // Debug.LogWarning("[SetupGameUI] UIManagers.Instance가 null입니다.");
            return;
        }

        if (_isSettingUpGameUI)
        {
            LogMigrationTrace("SetupGameUI:WAIT_OTHER_TASK");
            await UniTask.WaitUntil(() => !_isSettingUpGameUI);
            return;
        }

        if (_hasCompletedGameUISetup && !forceRefresh && localPlayerShopUI != null && augmentSelectionUI != null)
        {
            LogMigrationTrace("SetupGameUI:SKIP_ALREADY_DONE");
            return;
        }

        _isSettingUpGameUI = true;

        try
        {
            var shopPanelTask = UIManagers.Instance.GetUIElement("UI_Pnl_Shop");
            var augmentPanelTask = UIManagers.Instance.GetUIElement("UI_Pnl_Augment");
            var (shopPanelInstance, augmentPanelInstance) = await UniTask.WhenAll(shopPanelTask, augmentPanelTask);

            // 참조 저장 후 Controller의 초기화 메서드 호출
            if (shopPanelInstance != null)
            {
                localPlayerShopUI = shopPanelInstance.GetComponent<ShopUIController>();
                localPlayerShopUIGameObject = shopPanelInstance;
                if (localPlayerShopUI != null)
                {
                    localPlayerShopUI.InitializeAndHide();
                }
                else
                {
                    // Debug.LogError("[SetupGameUI] UI_Pnl_Shop에 ShopUIController가 없습니다.");
                }
            }
            else
            {
                // Debug.LogError("[SetupGameUI] UI_Pnl_Shop 로드 실패 (null)");
            }

            if (augmentPanelInstance != null)
            {
                augmentSelectionUI = augmentPanelInstance.GetComponent<AugmentUIController>();
                if (augmentSelectionUI != null)
                {
                    augmentSelectionUI.InitializeAndHide();
                }
                else
                {
                    // Debug.LogError("[SetupGameUI] UI_Pnl_Augment에 AugmentUIController가 없습니다.");
                }
            }
            else
            {
                // Debug.LogError("[SetupGameUI] UI_Pnl_Augment 로드 실패 (null)");
            }

            // 모든 플레이어의 상점/증강 데이터 로딩
            var playersSnapshot = AllPlayers?.Where(p => p != null).ToList() ?? new List<PlayerManager>();
            if (playersSnapshot.Count == 0)
            {
                playersSnapshot = FindObjectsOfType<PlayerManager>(true)
                    .Where(p => p != null && p.Object != null && p.Runner == Runner)
                    .ToList();
                // Debug.LogWarning($"[SetupGameUI] AllPlayers 스냅샷이 비어 FindObjectsOfType 폴백 사용: count={playersSnapshot.Count}");
            }

            foreach (var player in playersSnapshot)
            {
                try
                {
                    if (player.shopManager == null)
                    {
                        player.shopManager = player.GetComponentInChildren<ShopManager>(true);
                    }

                    if (player.augmentManager == null)
                    {
                        player.augmentManager = player.GetComponentInChildren<AugmentManager>(true);
                    }

                    if (player.shopManager != null && player.shopManager.playerManager == null)
                    {
                        player.shopManager.playerManager = player;
                    }

                    if (player.augmentManager != null && player.augmentManager.playerManager == null)
                    {
                        player.augmentManager.playerManager = player;
                    }

                    // 증강 데이터 로딩 (명시적 호출 + 대기)
                    if (player.augmentManager != null && !player.augmentManager.IsDataLoaded)
                    {
                        await player.augmentManager.LoadAllAugmentsAsync();
                    }

                    // 상점 데이터 로딩 (ShopManager.Start에서 이미 시작됨, 대기만)
                    if (player.shopManager != null)
                    {
                        await player.shopManager.WaitUntilDatabaseLoaded();
                    }

                    // Host Migration 복원 중 Prepare 단계에서 상점이 비어 있으면
                    // 새 Host가 즉시 무료 리롤 + 동기화하여 클라이언트 대기 타임아웃을 방지한다.
                    if (_migrationRestoreInProgress &&
                        currentState == GameState.Prepare &&
                        Object != null &&
                        Object.HasStateAuthority &&
                        player.shopManager != null)
                    {
                        var migratedShopItems = player.shopManager.GetCurrentShopItems();
                        if (migratedShopItems == null || migratedShopItems.Count == 0)
                        {
                            player.shopManager.Reroll(true);
                            migratedShopItems = player.shopManager.GetCurrentShopItems();

                            Debug.Log($"[복원/UI] Host 보정 리롤 실행: Player {player.playerId}, itemCount={migratedShopItems?.Count ?? 0}");

                            if (CommandProcessor != null && migratedShopItems != null && migratedShopItems.Count > 0)
                            {
                                string[] shopNames = migratedShopItems.Select(i => i.UnitData?.name ?? string.Empty).ToArray();
                                int[] shopStars = migratedShopItems.Select(i => i.StarLevel).ToArray();
                                var syncShopCmd = new SyncShopItemsCommand(player.playerId, shopNames, shopStars);
                                CommandProcessor.RequestCommandExecution(syncShopCmd);
                                Debug.Log($"[복원/UI] Host 상점 동기화 커맨드 전송: Player {player.playerId}, itemCount={migratedShopItems.Count}");
                            }
                        }
                    }

                    if (_activeMigrationTraceId >= 0)
                    {
                        int shopCount = player.shopManager != null ? player.shopManager.GetCurrentShopItems().Count : -1;
                        int augmentCount = player.augmentManager?.GetPresentedAugments()?.Count ?? -1;
                        Debug.Log($"[HM-TRACE #{_activeMigrationTraceId}] SetupGameUI:PLAYER_READY P{player.playerId} shopCount={shopCount} shopDbLoaded={(player.shopManager != null && player.shopManager.IsDatabaseLoaded)} augmentChoices={augmentCount} augmentLoaded={(player.augmentManager != null && player.augmentManager.IsDataLoaded)}");
                    }
                }
                catch (System.Exception playerEx)
                {
                    // Debug.LogError($"[SetupGameUI] Player {(player != null ? player.playerId.ToString() : "null")} 처리 중 예외: {playerEx.Message}");
                    // Debug.LogException(playerEx);
                }
            }

            _hasCompletedGameUISetup = localPlayerShopUI != null && augmentSelectionUI != null;
            _migrationSetupUiCompleted = _hasCompletedGameUISetup;
            // Debug.Log("<color=green>[SetupGameUI] 모든 플레이어의 상점/증강 데이터 로딩 완료</color>");
            LogMigrationTrace("SetupGameUI:SUCCESS", $"hasCompleted={_hasCompletedGameUISetup}");
        }
        catch (System.Exception ex)
        {
            // Debug.LogError($"UI 설정 중 심각한 에러 발생: {ex.Message}");
            // Debug.LogException(ex);
            // Debug.LogError($"[SetupGameUI] 상태 덤프: state={currentState}, round={currentRound}, localPlayer={(localPlayer != null ? localPlayer.playerId.ToString() : "null")}, runner={(Runner != null ? Runner.name : "null")}");
            if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log("UI 설정 중 심각한 에러 발생");
            LogMigrationTrace("SetupGameUI:EXCEPTION", $"error={ex.Message}");
        }
        finally
        {
            _isSettingUpGameUI = false;
            LogMigrationTrace("SetupGameUI:EXIT");
        }
    }

    private async void HandleAugmentChosen(PlayerManager selectingPlayer, AugmentData chosenAugment)
    {
        if (selectingPlayer != localPlayer) return;

        // Debug.Log($"<color=cyan>[HandleAugmentChosen] 증강 '{chosenAugment?.augmentName}' 선택됨 → 증강 UI 비활성화</color>");

        // 1. 증강 UI 비활성화 (부모 GameObject 비활성화) - 활성 상태일 때만
        if (UIManagers.Instance != null && UIManagers.Instance.IsUIElementActive("UI_Pnl_Augment"))
        {
            UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
        }

        // 2. 상점 UI 활성화 - 준비 단계에서만 열도록 체크
        // [버그 수정] 플레이어가 잠수해서 증강이 자동 선택된 경우, 이미 전투 상태일 수 있음
        // 전투 중에는 상점 UI를 열지 않음
        if (currentState != GameState.Prepare)
        {
            // Debug.Log($"<color=yellow>[HandleAugmentChosen] 현재 {currentState} 상태이므로 상점 UI를 열지 않음 (잠수 플레이어 자동 선택)</color>");
            return;
        }

        if ((localPlayerShopUIGameObject == null || localPlayerShopUI == null) && UIManagers.Instance != null)
        {
            var resolvedShopPanel = await UIManagers.Instance.GetUIElement("UI_Pnl_Shop");
            if (resolvedShopPanel != null)
            {
                localPlayerShopUIGameObject = resolvedShopPanel;
                localPlayerShopUI = resolvedShopPanel.GetComponent<ShopUIController>();
                if (localPlayerShopUI != null)
                {
                    localPlayerShopUI.InitializeAndHide();
                }
            }
        }

        if (localPlayerShopUIGameObject != null && localPlayerShopUI != null)
        {
            // Debug.Log($"<color=cyan>[HandleAugmentChosen] 상점 UI 활성화</color>");
            localPlayerShopUIGameObject.SetActive(true);  // 부모 GameObject 활성화
            localPlayerShopUI.SetContentVisibility(true);  // 콘텐츠 표시

            // 상점 UI를 표시하기 전에, 데이터베이스 로드를 기다리고 상점을 채우는 것을 보장합니다.
            await localPlayer.shopManager.EnsureShopRerolledAsync();
            var shopItems = localPlayer.shopManager.GetCurrentShopItems();
            localPlayerShopUI.DisplayShopItems(shopItems);

            // Debug.Log($"<color=cyan>[HandleAugmentChosen] 상점 UI 표시 완료 (아이템 수: {shopItems?.Count ?? 0})</color>");
        }
        else
        {
            // Debug.LogWarning("[HandleAugmentChosen] 상점 UI 참조가 null입니다!");
        }
    }

    private async UniTask HandleUIForNewState(GameState newState)
    {
        if (localPlayer == null)
        {
            RelinkLocalPlayer();
        }

        // [수정] 싱글플레이 모드 지원: 서버(호스트)이거나 로컬 플레이어가 있을 때만 UI 처리
        if (localPlayer == null)
        {
            if (!Runner.IsServer)
            {
                return; // 클라이언트인데 로컬 플레이어가 없으면 UI 처리 안함
            }
            else
            {
                // Debug.LogWarning("[HandleUIForNewState] 로컬 플레이어가 아직 설정되지 않았습니다. UI 처리를 건너뜁니다.");
                return;
            }
        }

        switch (newState)
        {
            case GameState.Prepare:
                // 상점 UI 숨김 (증강 UI는 SyncAugmentsCommand에서 활성화)
                if (localPlayerShopUIGameObject != null)
                {
                    localPlayerShopUIGameObject.SetActive(false);
                }
                break;
            case GameState.Battle1:
            case GameState.Battle2:
                // 전투 단계 진입 시 모든 UI 비활성화
                // Debug.Log($"<color=yellow>[HandleUIForNewState] {newState} 단계 - UI 비활성화</color>");
                if (UIManagers.Instance != null && UIManagers.Instance.IsUIElementActive("UI_Pnl_Augment"))
                {
                    UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
                }
                if (localPlayerShopUIGameObject != null)
                {
                    localPlayerShopUIGameObject.SetActive(false);
                }
                // TODO: 공격 시퀀스 UI 활성화 (공격자인 경우)
                break;
            case GameState.GameOver:
                if (UIManagers.Instance == null)
                {
                    // Debug.LogWarning("[HandleUIForNewState] GameOver UI를 표시할 UIManagers.Instance가 없습니다.");
                    break;
                }

                // 승자 판정: 체력이 가장 높은 플레이어 (0 이하여도 덜 마이너스인 쪽이 승리)
                PlayerManager winner = AllPlayers
                    .Where(p => p != null)
                    .OrderByDescending(p => p.GetHealth())
                    .FirstOrDefault();

                // 로컬 플레이어의 승패 UI 표시
                if (localPlayer != null)
                {
                    if (localPlayer == winner)
                    {
                        await UIManagers.Instance.GetUIElement("UI_Pnl_Victory");
                    }
                    else
                    {
                        await UIManagers.Instance.GetUIElement("UI_Pnl_Defeat");
                    }
                }
                break;
        }
    }
}
