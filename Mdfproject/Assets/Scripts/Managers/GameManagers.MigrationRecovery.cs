using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

public partial class GameManagers
{
    /// <summary>
    /// Host Migration 복원 전에 캐시 상태가 현재 상태보다 앞선 상태면 역행을 막기 위해 적용합니다.
    /// </summary>
    public bool TryApplyCachedStateForMigration(GameMigrationData cachedData, float elapsedSinceCacheSeconds, string context)
    {
        if (!IsReadyForNetworkAccess || Runner == null || Object == null || !Object.HasStateAuthority)
        {
            return false;
        }

        if (cachedData.CurrentRound <= 0)
        {
            return false;
        }

        if (cachedData.GameStateValue < (int)GameState.Setup || cachedData.GameStateValue > (int)GameState.GameOver)
        {
            // Debug.LogWarning($"[GameManagers] 캐시 상태 값이 유효하지 않아 적용하지 않습니다. stateValue={cachedData.GameStateValue}, context={context}");
            return false;
        }

        GameState cachedState = (GameState)cachedData.GameStateValue;
        int cachedRank = GetMigrationStateRank(cachedState);
        int currentRank = GetMigrationStateRank(currentState);

        bool roundBehind = currentRound < cachedData.CurrentRound;
        bool stateBehind = currentRound == cachedData.CurrentRound && currentRank < cachedRank;
        bool shouldPromoteState = roundBehind || stateBehind;

        float elapsed = Mathf.Max(0f, elapsedSinceCacheSeconds);
        float adjustedCachedRemaining = Mathf.Max(0f, cachedData.RemainingPhaseTime - elapsed);
        float currentRemaining = phaseTimer.IsRunning ? (phaseTimer.RemainingTime(Runner) ?? 0f) : 0f;
        bool sameRoundSameState = currentRound == cachedData.CurrentRound && currentRank == cachedRank;
        bool shouldPromoteTimerOnly = !shouldPromoteState && sameRoundSameState && adjustedCachedRemaining > currentRemaining + 1f;
        bool timerRequired =
            cachedState == GameState.Prepare ||
            cachedState == GameState.Battle1 ||
            cachedState == GameState.Battle2;
        bool shouldRestoreExpiredBoundaryTimer =
            !shouldPromoteState &&
            sameRoundSameState &&
            timerRequired &&
            !phaseTimer.IsRunning &&
            cachedData.RemainingPhaseTime > 0f;

        if (!shouldPromoteState && !shouldPromoteTimerOnly && !shouldRestoreExpiredBoundaryTimer)
        {
            return false;
        }

        int beforeRound = currentRound;
        GameState beforeState = currentState;
        float beforeRemaining = currentRemaining;

        if (shouldPromoteState)
        {
            currentRound = cachedData.CurrentRound;
            TransitionToState(cachedState, "TryApplyCachedStateForMigration");
        }

        if (timerRequired && (shouldPromoteState || shouldPromoteTimerOnly || shouldRestoreExpiredBoundaryTimer))
        {
            // Migration 직전 timer가 만료 경계(<= 0.25s)에 있었더라도 None으로 만들면
            // 복구 gate의 phaseTimerNotRunning 조건과 모순되어 8초 timeout으로 빠진다.
            // 짧은 grace tick을 복원한 뒤 기존 pause/resume 경로가 결정적으로 다음
            // 상태 전이를 처리하도록 한다.
            float restoredRemaining = Mathf.Max(0.25f, adjustedCachedRemaining);
            phaseTimer = TickTimer.CreateFromSeconds(Runner, restoredRemaining);
        }

        Debug.Log($"<color=magenta>[GameManagers] HostMigration 캐시 상태 적용 ({context})\n  before=R{beforeRound}/{beforeState} {beforeRemaining:F1}s\n  cached=R{cachedData.CurrentRound}/{cachedState} {cachedData.RemainingPhaseTime:F1}s (elapsed={elapsed:F1})\n  after=R{currentRound}/{currentState} {currentPhaseTimer:F1}s</color>");
        return true;
    }

    private static int GetMigrationStateRank(GameState state)
    {
        return state switch
        {
            GameState.Setup => 0,
            GameState.DataLoading => 1,
            GameState.Prepare => 2,
            GameState.Battle1 => 3,
            GameState.Battle2 => 4,
            GameState.GameOver => 5,
            _ => -1
        };
    }

    /// <summary>
    /// Host Migration 이후 게임 상태를 복원합니다.
    /// Networked 속성(currentState, currentRound, phaseTimer)은 Fusion이 자동 복원합니다.
    /// 이 메서드는 로컬 상태만 복원합니다.
    /// </summary>
    public void RestoreAfterHostMigration()
    {
        _activeMigrationTraceId = ++_hostMigrationTraceSeq;
        ResetMigrationCancellationToken();
        SetMigrationRestoreStage(MigrationRestoreStage.Preparing, "RestoreAfterHostMigration.Begin");
        _migrationWarnedPrepareExpiryRace = false;
        _migrationRestoreStartedRealtime = Time.realtimeSinceStartup;
        _migrationRestoreStartFrame = Time.frameCount;
        _migrationTimerPaused = false;
        _migrationPausedTimerRemainingSeconds = 0f;
        ResetMigrationOneShotGuards();

        Debug.Log("<color=yellow>[GameManagers] RestoreAfterHostMigration 시작!</color>");
        LogMigrationTrace("RestoreAfterHostMigration:BEGIN", $"startFrame={_migrationRestoreStartFrame}");
        
        // 상태 체크 로깅
        Debug.Log($"[복원] Object 유효: {Object != null}");
        Debug.Log($"[복원] Object.IsValid: {Object?.IsValid}");
        Debug.Log($"[복원] _isSpawned: {_isSpawned}");
        Debug.Log($"[복원] IsReadyForNetworkAccess: {IsReadyForNetworkAccess}");
        Debug.Log($"[복원] HasStateAuthority: {Object?.HasStateAuthority}");
        bool runnerMatched = IsBoundToActiveRunner();
        Debug.Log($"[복원] RunnerMatched: {runnerMatched}");
        
        // Spawned 상태가 아니면 대기/재시도
        if (!IsReadyForNetworkAccess)
        {
            Debug.LogWarning("<color=red>[GameManagers] 아직 Spawned 상태가 아닙니다. 복원을 건너뜁니다.</color>");
            LogMigrationTrace("RestoreAfterHostMigration:ABORT_NOT_READY");
            SetMigrationRestoreStage(MigrationRestoreStage.Failed, "RestoreAfterHostMigration.NotReady");
            return;
        }

        if (!runnerMatched)
        {
            Debug.LogError("<color=red>[GameManagers] 활성 Runner가 불일치하여 복원을 중단합니다.</color>");
            LogMigrationTrace("RestoreAfterHostMigration:ABORT_RUNNER_MISMATCH");
            SetMigrationRestoreStage(MigrationRestoreStage.Failed, "RestoreAfterHostMigration.RunnerMismatch");
            return;
        }

        if (Runner != null && Runner.IsServer && (Object == null || !Object.HasStateAuthority))
        {
            Debug.LogError("<color=red>[GameManagers] 서버 Host인데 StateAuthority가 없어 복원을 중단합니다.</color>");
            LogMigrationTrace("RestoreAfterHostMigration:ABORT_NO_STATE_AUTH");
            SetMigrationRestoreStage(MigrationRestoreStage.Failed, "RestoreAfterHostMigration.NoStateAuthority");
            return;
        }
        
        Debug.Log($"<color=cyan>[복원] 현재 게임 상태 - Round: {currentRound}, State: {currentState}, Timer: {currentPhaseTimer:F1}s</color>");
        
        // 1. ChangeDetector 재초기화
        Debug.Log("[복원] 1. ChangeDetector 재초기화...");
        if (Object != null)
        {
            _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
            Debug.Log("[복원] ChangeDetector 재초기화 완료");
        }
        
        // 2. 로컬 플레이어 참조 재연결
        Debug.Log("[복원] 2. 로컬 플레이어 재연결...");
        RebuildNetworkPlayersAfterMigration("RestoreAfterHostMigration");
        RebindLocalPresentationAfterPlayerRegistryChanged("RestoreAfterHostMigration");
        Debug.Log($"[복원] localPlayer: {(localPlayer != null ? $"Player {localPlayer.playerId}" : "null")}");
        
        // 3. CommandProcessor 초기화 (필요 시)
        Debug.Log("[복원] 3. CommandProcessor 체크...");
        if (CommandProcessor == null)
        {
            CommandProcessor = new CommandProcessor();
            Debug.Log("[복원] CommandProcessor 새로 생성");
        }
        else
        {
            Debug.Log("[복원] CommandProcessor 이미 존재");
        }
        
        // 4. UI 상태 복원
        Debug.Log("[복원] 4. UI 상태 복원...");
        LogMigrationTrace("RestoreAfterHostMigration:KickRestoreLocalUI");
        SetMigrationRestoreStage(MigrationRestoreStage.UiRestoreRunning, "RestoreAfterHostMigration.KickRestoreUI");
        RunMigrationTask(RestoreLocalUIAfterMigrationAsync(), "RestoreAfterHostMigration/RestoreLocalUIAfterMigrationAsync");
        
        // 5. 싱글턴 인스턴스 재설정
        Debug.Log("[복원] 5. 싱글턴 인스턴스 체크...");
        if (Instance == null || Instance != this)
        {
            Instance = this;
            Debug.Log("[복원] 싱글턴 인스턴스 재설정 완료");
        }
        
        // 6. 상점/증강 데이터 동기화
        Debug.Log("[복원] 6. 상점/증강 데이터 동기화 체크...");
        if (Object != null && Object.HasStateAuthority)
        {
            // HostMigrationHandler의 post-pass가 먼저 모든 authority runtime shop을 정확히
            // 복원·검증한 뒤 revisioned snapshot sync를 발행한다. 여기서 names/stars-only
            // command를 미리 발행하면 sold 상태가 지워지고 오래된 cache가 전파된다.
            Debug.Log("[MigrationRestore] Host shop sync deferred until exact post-pass restore completes.");
        }
        else if (Object != null && !Object.HasStateAuthority && localPlayer != null)
        {
            Debug.Log("<color=cyan>[복원] 클라이언트 - 서버에 데이터 동기화 요청</color>");
            localPlayer.RPC_RequestSyncData();
        }
        
        // 7. 게임 흐름 재개는 "권한 + 플레이어 준비 + UI 복원 완료" 이후에만 수행
        PausePhaseTimerForMigrationIfNeeded();
        Debug.Log("<color=magenta>[STEP 6] 게임 흐름 재개 조건 대기 시작</color>");
        StartCoroutine(WaitForRestoreDependenciesAndResumeFlow());
        
        // 8. [Observer Pattern] 상태 복원 완료 이벤트 발행
        GameEvents.TriggerGameStateRestored(currentState);
        
        Debug.Log("<color=magenta>[STEP 7] Host Migration 복원 처리 완료</color>");
        Debug.Log("<color=green>[STEP 7] 최종 상태 확인:</color>");
        // Debug.Log($"  GameState: {currentState}");
        // Debug.Log($"  라운드: {currentRound}");
        // Debug.Log($"  타이머 실행 중: {phaseTimer.IsRunning}");
        // Debug.Log($"  남은 시간: {currentPhaseTimer:F1}초");
        // Debug.Log($"  StateAuthority: {Object?.HasStateAuthority}");
        
        // Battle 상태 확인
        bool isInBattle = currentState == GameState.Battle1 || currentState == GameState.Battle2;
        if (isInBattle)
        {
            Debug.Log($"<color=cyan>[STEP 7] Battle 상태 복원 성공! ({currentState})</color>");
        }

        LogMigrationTrace("RestoreAfterHostMigration:END");
    }

    private void PausePhaseTimerForMigrationIfNeeded()
    {
        if (Runner == null || Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        if (!phaseTimer.IsRunning)
        {
            return;
        }

        float remaining = phaseTimer.RemainingTime(Runner) ?? 0f;
        if (remaining <= 0f)
        {
            return;
        }

        _migrationTimerPaused = true;
        _migrationPausedTimerRemainingSeconds = remaining;
        phaseTimer = TickTimer.None;

        Debug.Log($"[STEP 6] Host Migration restore paused phase timer: {remaining:F1}s");
        LogMigrationTrace("PausePhaseTimerForMigrationIfNeeded", $"remaining={remaining:F1}");
    }

    private IEnumerator WaitForRestoreDependenciesAndResumeFlow()
    {
        float waitTime = 0f;
        const float maxWaitTime = 8f;

        LogMigrationTrace("WaitForRestoreDependenciesAndResumeFlow:BEGIN");

        while (waitTime < maxWaitTime)
        {
            if (_migrationCts != null && _migrationCts.IsCancellationRequested)
            {
                LogMigrationTrace("WaitForRestoreDependenciesAndResumeFlow:CANCELED");
                yield break;
            }

            bool hasAuthority = Object != null && Object.HasStateAuthority;
            bool runnerMatched = IsBoundToActiveRunner();
            bool uiReady = IsMigrationUiRestoreCompleted;
            bool playersReady = AreAllPlayersRuntimeReadyForMigration(out string notReadyReason);
            bool mappingReady = IsMigrationBattleMappingReady(out string mappingReason);
            bool timerReady = IsMigrationTimerReady(out string timerReason);
            bool wallMapReady = AreWallMapsReadyForMigration(out string wallReason);
            bool aiTakeoverReady = IsMigrationAiTakeoverReady(out string aiReason);
            bool restoreReportsReady = IsMigrationRestoreReportGateReady(
                out bool restoreReportsFailed,
                out string restoreReportReason);

            if (restoreReportsFailed)
            {
                phaseTimer = TickTimer.None;
                Debug.LogError($"[STEP 6] Host migration restore report gate failed; game flow remains stopped. reason={restoreReportReason}");
                LogMigrationTrace("WaitForRestoreDependenciesAndResumeFlow:RESTORE_REPORT_FAILED", restoreReportReason);
                SetMigrationRestoreStage(MigrationRestoreStage.Failed, "WaitForRestoreDependenciesAndResumeFlow.RestoreReportFailed");
                yield break;
            }

            if (hasAuthority && runnerMatched && uiReady && playersReady && mappingReady && timerReady && wallMapReady && aiTakeoverReady && restoreReportsReady)
            {
                Debug.Log($"<color=green>[STEP 6] 재개 조건 충족 ({waitTime:F1}s): authority={hasAuthority}, runnerMatched={runnerMatched}, uiReady={uiReady}, playersReady={playersReady}, mappingReady={mappingReady}, timerReady={timerReady}, wallMapReady={wallMapReady}, aiTakeoverReady={aiTakeoverReady}</color>");
                LogMigrationTrace("WaitForRestoreDependenciesAndResumeFlow:READY", $"waited={waitTime:F1}s");
                SetMigrationRestoreStage(MigrationRestoreStage.WaitingForFlowResume, "WaitForRestoreDependenciesAndResumeFlow.Ready");
                ResumeGameFlowFromCurrentState();
                yield break;
            }

            if (waitTime == 0f || Mathf.Abs((waitTime * 10f) % 10f) < 0.001f)
            {
                Debug.Log($"[STEP 6] 조건 대기 중... ({waitTime:F1}s) authority={hasAuthority}, runnerMatched={runnerMatched}, uiReady={uiReady}, playersReady={playersReady}, mappingReady={mappingReady}, timerReady={timerReady}, wallMapReady={wallMapReady}, aiTakeoverReady={aiTakeoverReady}");
                if (!playersReady && !string.IsNullOrEmpty(notReadyReason))
                {
                    Debug.LogWarning($"[STEP 6] 플레이어 런타임 준비 미완료: {notReadyReason}");
                }
                if (!runnerMatched)
                {
                    Debug.LogWarning("[STEP 6] GameManagers.Runner가 현재 활성 Runner와 불일치합니다.");
                }
                if (!mappingReady && !string.IsNullOrEmpty(mappingReason))
                {
                    Debug.LogWarning($"[STEP 6] 매칭 복원 미완료: {mappingReason}");
                }
                if (!timerReady && !string.IsNullOrEmpty(timerReason))
                {
                    Debug.LogWarning($"[STEP 6] 타이머 복원 미완료: {timerReason}");
                }
                if (!wallMapReady && !string.IsNullOrEmpty(wallReason))
                {
                    Debug.LogWarning($"[STEP 6] 벽 맵 복원 미완료: {wallReason}");
                }
                if (!aiTakeoverReady && !string.IsNullOrEmpty(aiReason))
                {
                    Debug.LogWarning($"[STEP 6] AI takeover 준비 미완료: {aiReason}");
                }
            }

            yield return new WaitForSeconds(0.1f);
            waitTime += 0.1f;
        }

        Debug.LogWarning($"<color=orange>[STEP 6] 재개 조건 대기 시간 초과 ({maxWaitTime:F1}s)</color>");
        LogMigrationTrace("WaitForRestoreDependenciesAndResumeFlow:TIMEOUT", $"waited={maxWaitTime:F1}s");

        bool timeoutHasAuthority = Object != null && Object.HasStateAuthority;
        bool timeoutRunnerMatched = IsBoundToActiveRunner();
        bool timeoutUiReady = IsMigrationUiRestoreCompleted;
        bool timeoutPlayersReady = AreAllPlayersRuntimeReadyForMigration(out string timeoutPlayersReason);
        bool timeoutMappingReady = IsMigrationBattleMappingReady(out string timeoutMappingReason);
        bool timeoutTimerReady = IsMigrationTimerReady(out string timeoutTimerReason);
        bool timeoutWallMapReady = AreWallMapsReadyForMigration(out string timeoutWallReason);
        bool timeoutAiTakeoverReady = IsMigrationAiTakeoverReady(out string timeoutAiReason);
        bool timeoutRestoreReportsReady = IsMigrationRestoreReportGateReady(
            out bool timeoutRestoreReportsFailed,
            out string timeoutRestoreReportReason);

        if (!timeoutRestoreReportsFailed && timeoutHasAuthority && timeoutRunnerMatched && timeoutUiReady && timeoutPlayersReady && timeoutMappingReady && timeoutTimerReady && timeoutWallMapReady && timeoutAiTakeoverReady && timeoutRestoreReportsReady)
        {
            SetMigrationRestoreStage(MigrationRestoreStage.WaitingForFlowResume, "WaitForRestoreDependenciesAndResumeFlow.TimeoutFallback");
            ResumeGameFlowFromCurrentState();
        }
        else
        {
            Debug.LogError($"[STEP 6] timeout fallback 차단: hasAuthority={timeoutHasAuthority}, runnerMatched={timeoutRunnerMatched}, uiReady={timeoutUiReady}, playersReady={timeoutPlayersReady}, mappingReady={timeoutMappingReady}, timerReady={timeoutTimerReady}, wallMapReady={timeoutWallMapReady}, aiTakeoverReady={timeoutAiTakeoverReady}, playersReason={timeoutPlayersReason}, mappingReason={timeoutMappingReason}, timerReason={timeoutTimerReason}, wallReason={timeoutWallReason}, aiReason={timeoutAiReason}");
            phaseTimer = TickTimer.None;
            if (timeoutRestoreReportsFailed)
            {
                LogMigrationTrace("WaitForRestoreDependenciesAndResumeFlow:RESTORE_REPORT_FAILED", timeoutRestoreReportReason);
            }
            SetMigrationRestoreStage(MigrationRestoreStage.Failed, "WaitForRestoreDependenciesAndResumeFlow.TimeoutNoGate");
        }
    }

    public void FailHostMigrationRecoveryFromHandler(string reason)
    {
        phaseTimer = TickTimer.None;
        _migrationTimerPaused = true;
        _migrationPausedTimerRemainingSeconds = 0f;
        LogMigrationTrace("HostMigrationHandler:RESTORE_SCHEDULING_FAILED", reason ?? "unknown");
        SetMigrationRestoreStage(MigrationRestoreStage.Failed, reason ?? "HostMigrationHandler.RestoreSchedulingFailed");
    }

    private bool IsBoundToActiveRunner()
    {
        if (Runner == null)
        {
            return false;
        }

        var activeRunner = NetworkManager.Instance?._runner;
        if (activeRunner != null)
        {
            return activeRunner == Runner;
        }

        return Runner.IsRunning && Runner.GameMode == GameMode.Single;
    }

    private bool AreAllPlayersRuntimeReadyForMigration(out string reason)
    {
        reason = string.Empty;

        var players = AllPlayers
            .Where(p => p != null && p.playerId >= 0 && p.Object != null && p.Object.IsValid)
            .ToList();
        if (players.Count == 0)
        {
            reason = "players=0(valid)";
            return false;
        }

        var notReady = new List<string>();
        foreach (var player in players)
        {
            player.RebindRuntimeReferencesAfterMigration("GameManagers.WaitForRestoreDependencies", false);
            if (!player.IsRuntimeReady(out string playerReason))
            {
                notReady.Add($"P{player.playerId}:{playerReason}");
            }
        }

        if (notReady.Count > 0)
        {
            reason = string.Join(", ", notReady);
            return false;
        }

        return true;
    }

    private bool IsMigrationBattleMappingReady(out string reason)
    {
        reason = string.Empty;
        if (currentState != GameState.Battle1 && currentState != GameState.Battle2)
        {
            return true;
        }

        EnsureBattleMappingAfterMigration();
        var alivePlayerIds = AllPlayers
            .Where(player => player != null && player.GetHealth() > 0)
            .Select(player => player.playerId)
            .ToList();

        foreach (int playerId in alivePlayerIds)
        {
            if (!_battleOpponents.ContainsKey(playerId))
            {
                reason = $"battleOpponentsMissing:P{playerId}";
                return false;
            }

            if (!_matchFirstAttacker.ContainsKey(playerId))
            {
                reason = $"matchFirstAttackerMissing:P{playerId}";
                return false;
            }
        }

        return true;
    }

    private bool IsMigrationTimerReady(out string reason)
    {
        reason = string.Empty;
        bool requiresPhaseTimer = currentState == GameState.Prepare
                                  || currentState == GameState.Battle1
                                  || currentState == GameState.Battle2;
        if (!requiresPhaseTimer)
        {
            return true;
        }

        if (_migrationTimerPaused)
        {
            return true;
        }

        if (Runner == null)
        {
            reason = "runner=null";
            return false;
        }

        if (!phaseTimer.IsRunning)
        {
            reason = "phaseTimerNotRunning";
            return false;
        }

        return true;
    }

    private bool AreWallMapsReadyForMigration(out string reason)
    {
        reason = string.Empty;
        var players = AllPlayers
            .Where(player => player != null && player.Object != null && player.Object.IsValid)
            .ToList();
        if (players.Count == 0)
        {
            reason = "players=0";
            return false;
        }

        foreach (var player in players)
        {
            if (player.fieldManager == null)
            {
                reason = $"P{player.playerId}:fieldManager=null";
                return false;
            }

            player.fieldManager.RebuildWallMapsAfterMigration("GameManagers.WaitForRestoreDependencies", false, out string wallSummary);
            if (!player.fieldManager.IsWallMapReady)
            {
                reason = $"P{player.playerId}:wallMapNotReady ({wallSummary})";
                return false;
            }
        }

        return true;
    }

    private bool IsMigrationRestoreReportGateReady(out bool terminalFailure, out string reason)
    {
        terminalFailure = false;
        reason = string.Empty;
        if (Runner == null || !Runner.IsRunning || !Runner.IsServer)
        {
            return true;
        }

        HostMigrationHandler handler = HostMigrationHandler.Instance;
        if (handler == null)
        {
            if (NetworkManager.Instance == null && Runner.GameMode == GameMode.Single)
            {
                return true;
            }

            terminalFailure = true;
            reason = "hostMigrationHandler=null";
            return false;
        }

        return handler.IsMigrationRestoreReadyForFlow(Runner, this, out terminalFailure, out reason);
    }

    private bool IsMigrationAiTakeoverReady(out string reason)
    {
        reason = string.Empty;
        if (Runner == null || !Runner.IsRunning || !Runner.IsServer)
        {
            return true;
        }

        if (HostMigrationHandler.Instance == null)
        {
            if (NetworkManager.Instance == null && Runner.GameMode == GameMode.Single)
            {
                return true;
            }

            reason = "hostMigrationHandler=null";
            return false;
        }

        if (!HostMigrationHandler.Instance.IsAiTakeoverReady)
        {
            reason = "aiTakeoverReconciliationPending";
            return false;
        }

        return true;
    }

    private static bool HasRemainingAttackPool(PlayerManager player)
    {
        return player != null &&
               player.AttackMonsterPool != null &&
               player.AttackMonsterPool.Exists(entry => entry != null && !entry.IsEmpty);
    }

    /// <summary>
    /// Battle 상태 복원 직후, 전투 시작 부트스트랩(side effect)이 누락된 페어를 1회 보정합니다.
    /// </summary>
    private void RebootstrapBattleAfterMigrationIfNeeded(string context)
    {
        if (currentState != GameState.Battle1 && currentState != GameState.Battle2)
        {
            return;
        }

        if (Runner == null || Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        EnsureBattleMappingAfterMigration();

        float remaining = phaseTimer.IsRunning ? (phaseTimer.RemainingTime(Runner) ?? 0f) : 0f;
        bool allowPoolRefresh = !phaseTimer.IsRunning || remaining >= Mathf.Max(3f, combatTime - 8f);

        var attackers = AllPlayers
            .Where(player => player != null && player.Object != null && player.Object.IsValid)
            .Where(player => player.IsAttackerInCurrentBattle)
            .ToList();

        if (attackers.Count == 0)
        {
            LogMigrationTrace("BATTLE-REBOOTSTRAP:SKIP_NO_ATTACKER", $"context={context}");
            return;
        }

        LogMigrationTrace(
            "BATTLE-REBOOTSTRAP:ENTER",
            $"context={context}, attackerCount={attackers.Count}, state={currentState}, remain={remaining:F1}, allowPoolRefresh={allowPoolRefresh}");

        foreach (var attacker in attackers)
        {
            if (!TryGetPlayerIdSafe(attacker, out int attackerId) || attackerId < 0)
            {
                continue;
            }

            int defenderId = GetBattleOpponent(attackerId);
            if (defenderId < 0)
            {
                continue;
            }

            var defender = GetPlayer(defenderId);
            if (defender == null || defender.Object == null || !defender.Object.IsValid)
            {
                LogMigrationTrace("BATTLE-REBOOTSTRAP:SKIP_DEFENDER_NULL", $"context={context}, attacker={attackerId}, defender={defenderId}");
                continue;
            }

            attacker.RebindRuntimeReferencesAfterMigration($"BattleRebootstrap.A{attackerId}", false);
            defender.RebindRuntimeReferencesAfterMigration($"BattleRebootstrap.D{defenderId}", false);

            bool attackerReady = attacker.IsRuntimeReady(out string attackerReason);
            bool defenderReady = defender.IsRuntimeReady(out string defenderReason);
            if (!attackerReady || !defenderReady)
            {
                LogMigrationTrace(
                    "BATTLE-REBOOTSTRAP:SKIP_RUNTIME_NOT_READY",
                    $"context={context}, attacker={attackerId}({attackerReason}), defender={defenderId}({defenderReason})");
                continue;
            }

            bool defenderHasLivingMonsters =
                defender.monsterSpawner != null &&
                defender.monsterSpawner.HasLivingMonsters();
            bool attackerHasPool = HasRemainingAttackPool(attacker);

            if (!attackerHasPool && allowPoolRefresh && attacker.AttackMonsterPoolRevision <= 0)
            {
                attacker.RefreshAttackMonsterPool(currentRound, defenderId);
                attackerHasPool = HasRemainingAttackPool(attacker);
                LogMigrationTrace(
                    "BATTLE-REBOOTSTRAP:POOL_REFRESH",
                    $"context={context}, attacker={attackerId}, defender={defenderId}, poolReady={attackerHasPool}");
            }

            if (!TryAcquireBattleRebootstrapKey(attacker, defender, context, out string battleKey))
            {
                LogMigrationTrace(
                    "BATTLE-REBOOTSTRAP:SKIP_DUPLICATE",
                    $"context={context}, attacker={attackerId}, defender={defenderId}");
                continue;
            }

            if (defenderHasLivingMonsters)
            {
                LogMigrationTrace(
                    "BATTLE-REBOOTSTRAP:SKIP_ALREADY_ACTIVE",
                    $"context={context}, attacker={attackerId}, defender={defenderId}, key={battleKey}");
                continue;
            }

            attacker.SetFightingState(true);
            defender.SetFightingState(true);

            // Battle 시작 RPC를 재발행해서 로컬 공격 UI/카메라/입력 경로를 재정렬한다.
            RPC_NotifyBattleStart(
                attackerId,
                true,
                defenderId,
                attacker.BlackMagicCurrent,
                attacker.BlackMagicMaximum,
                attacker.BlackMagicMaxBonus,
                attacker.BlackMagicRevision,
                attacker.BlackMagicSequenceId);
            RPC_NotifyBattleStart(
                defenderId,
                false,
                attackerId,
                defender.BlackMagicCurrent,
                defender.BlackMagicMaximum,
                defender.BlackMagicMaxBonus,
                defender.BlackMagicRevision,
                defender.BlackMagicSequenceId);

            bool isAiAttacker = ComponentRegistry.Has<AIPlayerController>(attackerId.ToString());
            LogMigrationTrace(
                "BATTLE-REBOOTSTRAP:APPLIED",
                $"context={context}, key={battleKey}, attacker={attackerId}, defender={defenderId}, mode={(isAiAttacker ? "AICommandPolicy" : "HumanNotify")}");
        }
    }
    
    /// <summary>
    /// [State Machine Pattern]
    /// 현재 상태에서 게임 흐름을 재개합니다(서버 Host 전용).
    /// 타이머가 없거나 만료되었으면 현재 상태에 맞게 재설정합니다.
    /// </summary>
    private void ResumeGameFlowFromCurrentState()
    {
        LogMigrationTrace("ResumeGameFlowFromCurrentState:ENTER");

        if ((IsCombatExitDebtGateActive || IsCombatExitDebtTerminalFailureSafeStopped) &&
            IsSequenceTransitioning)
        {
            SetMigrationRestoreStage(
                MigrationRestoreStage.FlowResumed,
                "ResumeGameFlowFromCurrentState.CombatExitDebtGate");
            return;
        }

        if (_migrationTimerPaused && Runner != null && Object != null && Object.HasStateAuthority)
        {
            float restoreSeconds = Mathf.Max(0.25f, _migrationPausedTimerRemainingSeconds);
            phaseTimer = TickTimer.CreateFromSeconds(Runner, restoreSeconds);
            Debug.Log($"[STEP 6] Restored paused phase timer: {restoreSeconds:F1}s");
            LogMigrationTrace("ResumeGameFlowFromCurrentState:RESTORE_PAUSED_TIMER", $"restoreSeconds={restoreSeconds:F1}");
            _migrationTimerPaused = false;
            _migrationPausedTimerRemainingSeconds = 0f;
        }

        RebootstrapBattleAfterMigrationIfNeeded("ResumeGameFlowFromCurrentState");

        bool timerRunning = phaseTimer.IsRunning;
        bool timerExpired = timerRunning && phaseTimer.Expired(Runner);
        float remainingTime = timerRunning ? (phaseTimer.RemainingTime(Runner) ?? 0f) : 0f;
        
        Debug.Log($"<color=yellow>[STEP 6] 현재 상태: {currentState}</color>");
        Debug.Log("[STEP 6] 타이머 상태:");
        // Debug.Log($"  - Running: {timerRunning}");
        // Debug.Log($"  - Expired: {timerExpired}");
        // Debug.Log($"  - Remaining: {remainingTime:F1}초");
        
        // 타이머가 정상 동작 중이면 유지
        if (timerRunning && !timerExpired && remainingTime > 0.5f)
        {
            Debug.Log($"<color=green>[STEP 6] 기존 타이머 유지 ({remainingTime:F1}초 남음) - 게임 재개!</color>");
            LogMigrationTrace("ResumeGameFlowFromCurrentState:KEEP_TIMER");
            SetMigrationRestoreStage(MigrationRestoreStage.FlowResumed, "ResumeGameFlowFromCurrentState.KeepTimer");
            return;
        }
        
        // 타이머가 없거나 만료되었으면 현재 상태에 맞게 재설정
        float newDuration = currentState switch
        {
            GameState.Prepare => firstPrepareDurationUsed ? preparePhaseTime : firstPreparePhaseTime,
            GameState.Battle1 => combatTime,
            GameState.Battle2 => combatTime,
            _ => 0f
        };
        
        if (newDuration > 0f)
        {
            phaseTimer = TickTimer.CreateFromSeconds(Runner, newDuration);
            Debug.Log($"<color=cyan>[STEP 6] 새 타이머 설정: {newDuration}초 - 게임 재개!</color>");
            Debug.Log($"<color=cyan>[STEP 6] 현재 상태 ({currentState})에서 계속 진행합니다.</color>");
            LogMigrationTrace("ResumeGameFlowFromCurrentState:RESET_TIMER", $"newDuration={newDuration:F1}");
            SetMigrationRestoreStage(MigrationRestoreStage.FlowResumed, "ResumeGameFlowFromCurrentState.ResetTimer");
        }
        else
        {
            Debug.Log($"[STEP 6] {currentState} 상태는 타이머가 필요 없음");
            LogMigrationTrace("ResumeGameFlowFromCurrentState:NO_TIMER");
            SetMigrationRestoreStage(MigrationRestoreStage.FlowResumed, "ResumeGameFlowFromCurrentState.NoTimer");
        }
    }
    
    private async UniTask RestoreLocalUIAfterMigrationAsync()
    {
        bool completed = false;
        LogMigrationTrace("RestoreLocalUI:ENTER");

        try
        {
            await SetupGameUI(forceRefresh: true);
            LogMigrationTrace("RestoreLocalUI:AFTER_SETUP_UI");

            if (localPlayer == null)
            {
                RelinkLocalPlayer();
            }

            if (localPlayer == null)
            {
                Debug.LogWarning("[복원/UI] localPlayer를 찾지 못해 UI 이벤트를 재발행할 수 없습니다.");
                LogMigrationTrace("RestoreLocalUI:ABORT_NO_LOCAL_PLAYER");
                return;
            }

            Debug.Log($"[복원/UI] UI 이벤트 재발행 시작 - State: {currentState}, LocalPlayer: {localPlayer.playerId}");

            TriggerMigrationReadyEventOnce("RestoreLocalUIAfterMigrationAsync");
            TriggerMigrationStateChangedOnce(currentState, "RestoreLocalUIAfterMigrationAsync");
            await HandleUIForNewState(currentState);
            LogMigrationTrace("RestoreLocalUI:AFTER_STATE_EVENTS");

            if (currentState != GameState.Prepare)
            {
                completed = true;
                LogMigrationTrace("RestoreLocalUI:END_NON_PREPARE");
                return;
            }

            var augmentChoices = localPlayer.augmentManager?.GetPresentedAugments();
            if (augmentChoices != null && augmentChoices.Count > 0)
            {
                GameEvents.TriggerAugmentPhaseStart(localPlayer, augmentChoices);
                Debug.Log($"[복원/UI] 증강 UI 이벤트 재발행 완료 (선택지: {augmentChoices.Count})");
                completed = true;
                LogMigrationTrace("RestoreLocalUI:END_AUGMENT_EVENT", $"choiceCount={augmentChoices.Count}");
                return;
            }

            Debug.LogWarning("[복원/UI] 증강 선택지가 없어 상점 UI 폴백을 시도합니다.");
            await ShowLocalShopFallbackAsync();
            completed = true;
            LogMigrationTrace("RestoreLocalUI:END_SHOP_FALLBACK");
        }
        catch (System.OperationCanceledException)
        {
            LogMigrationTrace("RestoreLocalUI:CANCELED");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[복원/UI] RestoreLocalUIAfterMigrationAsync 예외: {ex.Message}");
            // Debug.LogException(ex);
            LogMigrationTrace("RestoreLocalUI:EXCEPTION", $"error={ex.Message}");
        }
        finally
        {
            if (completed)
            {
                // flow 재개 전에 UI 복원이 먼저 성공하면 UiRestored로 전이하고, 이미 재개된 경우 FlowResumed를 유지한다.
                if (_migrationRestoreStage != MigrationRestoreStage.FlowResumed)
                {
                    SetMigrationRestoreStage(MigrationRestoreStage.UiRestored, "RestoreLocalUIAfterMigrationAsync.Completed");
                }
            }
            else if (_migrationRestoreStage != MigrationRestoreStage.FlowResumed)
            {
                SetMigrationRestoreStage(MigrationRestoreStage.Failed, "RestoreLocalUIAfterMigrationAsync.Incomplete");
            }

            LogMigrationTrace("RestoreLocalUI:FINALLY", $"completed={completed}");
        }
    }

    private async UniTask ShowLocalShopFallbackAsync()
    {
        LogMigrationTrace("ShowLocalShopFallback:ENTER");

        if (localPlayer == null || localPlayer.shopManager == null)
        {
            LogMigrationTrace("ShowLocalShopFallback:ABORT_NO_PLAYER_OR_SHOP");
            return;
        }

        if (localPlayerShopUIGameObject == null || localPlayerShopUI == null)
        {
            LogMigrationTrace("ShowLocalShopFallback:ABORT_UI_REF_NULL");
            return;
        }

        await EnsurePrepareShopRecoveredAndSyncedAsync(localPlayer, "ShowLocalShopFallback");

        localPlayerShopUIGameObject.SetActive(true);
        var shopItems = localPlayer.shopManager.GetCurrentShopItems();
        localPlayerShopUI.ShowWithItems(shopItems);
        Debug.Log("[복원/UI] 상점 UI 폴백 표시 완료");
        LogMigrationTrace("ShowLocalShopFallback:SUCCESS", $"shopCount={shopItems?.Count ?? 0}");
    }

    private async UniTask EnsurePrepareShopRecoveredAndSyncedAsync(PlayerManager targetPlayer, string context)
    {
        if (targetPlayer == null || targetPlayer.shopManager == null)
        {
            LogMigrationTrace("PrepareShopRecovery:ABORT_NO_PLAYER_OR_SHOP", $"context={context}");
            return;
        }

        await targetPlayer.shopManager.WaitUntilDatabaseLoaded();
        bool snapshotApplied = await targetPlayer.shopManager.ApplySnapshotFromNetworkAsync(
            $"{context}.NetworkSnapshot",
            triggerRefreshedEvent: false);
        if (snapshotApplied)
        {
            LogMigrationTrace("PrepareShopRecovery:APPLIED_SNAPSHOT", $"context={context}, player={targetPlayer.playerId}");
            return;
        }

        bool canMutatePrepareShop =
            Object != null &&
            Object.HasStateAuthority &&
            currentState == GameState.Prepare;

        if (canMutatePrepareShop)
        {
            string key;
            bool acquired = TryAcquirePrepareShopRecoveryKey(targetPlayer, context, out key);
            if (acquired)
            {
                var items = targetPlayer.shopManager.GetCurrentShopItems();
                if (items == null || items.Count == 0)
                {
                    targetPlayer.shopManager.Reroll(true);
                    items = targetPlayer.shopManager.GetCurrentShopItems();
                    Debug.Log($"[복원/UI] Prepare 상점 단일 복원 리롤: key={key}, player={targetPlayer.playerId}, itemCount={items?.Count ?? 0}");
                }
            }
            else
            {
                LogMigrationTrace("PrepareShopRecovery:SKIP_DUPLICATE", $"context={context}, player={targetPlayer.playerId}");
            }
        }

        await targetPlayer.shopManager.EnsureShopRerolledAsync();
        await targetPlayer.shopManager.ApplySnapshotFromNetworkAsync(
            $"{context}.NetworkSnapshotRetry",
            triggerRefreshedEvent: false);
    }
}
