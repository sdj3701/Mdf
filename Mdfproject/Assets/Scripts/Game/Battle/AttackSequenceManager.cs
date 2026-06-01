// Assets/Scripts/Game/Battle/AttackSequenceManager.cs
using UnityEngine;
using UnityEngine.EventSystems;
using Cysharp.Threading.Tasks;
using Fusion;

/// <summary>
/// 공격 시퀀스를 관리하는 매니저.
/// 플레이어가 공격자일 때 수동으로 몬스터를 소환할 수 있도록 합니다.
/// </summary>
public class AttackSequenceManager : MonoBehaviour
{
    #region 필드
    private PlayerManager _playerManager;
    private MonsterSpawner _monsterSpawner;
    private FieldManager _opponentFieldManager;
    private float _lastMissingRefLogTime;
    
    [Header("소환 설정")]
    [Tooltip("현재 선택된 몬스터")]
    private MonsterPoolEntry _selectedMonster;
    private int _selectedMonsterSlotIndex = -1;
    private int _pendingBattleSpawnSlotIndex = -1;
    private int _pendingBattleSpawnRevision = -1;
    private float _pendingBattleSpawnStartedAt;
    private const float PendingBattleSpawnTimeoutSeconds = 1.25f;

    [Tooltip("현재 선택된 마법 스크롤")]
    private MagicScrollData _selectedScroll;
    
    [Tooltip("홀드 소환 간격 (초)")]
    [SerializeField] private float holdSpawnInterval = 0.3f;
    
    private float _lastSpawnTime;
    private bool _isHolding;
    private bool _suppressMapInputUntilPointerRelease;
    private int _suppressMapInputThroughFrame = -1;
    private Camera _playerCamera;
    
    [Header("스폰 영역 설정")]
    [Tooltip("스폰 가능 영역 레이어")]
    [SerializeField] private LayerMask spawnAreaLayerMask;

    /// <summary>
    /// AI에서 스폰 영역 검증에 사용할 수 있도록 레이어마스크를 노출합니다.
    /// </summary>
    public LayerMask SpawnAreaLayer => spawnAreaLayerMask;

    public PlayerManager Owner => _playerManager;
    public bool IsScrollMode { get; private set; }
    #endregion

    #region 초기화
    public void Initialize(PlayerManager owner)
    {
        _playerManager = owner;
        _monsterSpawner = owner?.monsterSpawner;

        EnsureRuntimeReferences("Initialize", true);
        // Debug.Log($"<color=cyan>[AttackSequenceManager] Player {owner?.playerId} 초기화 완료 ({DescribeRuntimeState()})</color>");
    }

    /// <summary>
    /// 공격 시퀀스 시작 시 호출. 상대 필드 매니저를 설정합니다.
    /// </summary>
    public void StartAttackSequence(PlayerManager opponent)
    {
        if (opponent == null)
        {
            // Debug.LogWarning("[AttackSequenceManager] 상대가 없습니다 (관전 모드)");
            return;
        }

        if (!EnsureRuntimeReferences("StartAttackSequence", true))
        {
            // Debug.LogError($"[AttackSequenceManager] StartAttackSequence 중단: 필수 참조 누락 ({DescribeRuntimeState()})");
            return;
        }

        _opponentFieldManager = opponent.fieldManager;
        _selectedMonster = null;
        _selectedMonsterSlotIndex = -1;
        ClearPendingBattleSpawn();
        _selectedScroll = null;
        IsScrollMode = false;
        

        // Debug.Log($"<color=green>[AttackSequenceManager] 공격 시퀀스 시작! 상대: Player {opponent.playerId} ({DescribeRuntimeState()})</color>");
    }

    public void EndAttackSequence()
    {
        _selectedMonster = null;
        _selectedMonsterSlotIndex = -1;
        ClearPendingBattleSpawn();
        _selectedScroll = null;
        _opponentFieldManager = null;
        _isHolding = false;
        _suppressMapInputUntilPointerRelease = false;
        _suppressMapInputThroughFrame = -1;
        IsScrollMode = false;
    }
    #endregion

    #region 몬스터 선택
    /// <summary>
    /// UI에서 몬스터 슬롯 클릭 시 호출
    /// </summary>
    public void SelectMonster(MonsterPoolEntry entry)
    {
        if (entry == null || entry.IsEmpty)
        {
            // Debug.LogWarning("[AttackSequenceManager] 선택할 수 없는 몬스터입니다");
            return;
        }

        int slotIndex = _playerManager != null && _playerManager.AttackMonsterPool != null
            ? _playerManager.AttackMonsterPool.IndexOf(entry)
            : -1;
        if (slotIndex >= 0)
        {
            SelectMonsterSlot(slotIndex);
            return;
        }

        _selectedMonster = entry;
        _selectedMonsterSlotIndex = -1;
        _selectedScroll = null;
        IsScrollMode = false;
        SuppressBattleMapInputForCurrentPointer();
        AttackSequenceUIController.Instance?.SyncMonsterSelectionFromManager(slotIndex);
        // Debug.Log($"<color=yellow>[AttackSequenceManager] 몬스터 선택: {entry.MonsterData.monsterName} (남은 수량: {entry.RemainingCount})</color>");
    }

    public void SelectMonsterSlot(int slotIndex)
    {
        if (!TryResolveMonsterSlot(slotIndex, out var entry))
        {
            return;
        }

        _selectedMonster = entry;
        _selectedMonsterSlotIndex = slotIndex;
        _selectedScroll = null;
        IsScrollMode = false;
        SuppressBattleMapInputForCurrentPointer();
        AttackSequenceUIController.Instance?.SyncMonsterSelectionFromManager(slotIndex);
    }

    public void SuppressBattleMapInputForCurrentPointer()
    {
        if (MdfInput.PrimaryPointerIsPressed() ||
            MdfInput.PrimaryPointerWasPressedThisFrame() ||
            MdfInput.PrimaryPointerWasReleasedThisFrame())
        {
            _suppressMapInputUntilPointerRelease = true;
            _suppressMapInputThroughFrame = Time.frameCount + 1;
            _isHolding = false;
        }
    }

    public MonsterPoolEntry GetSelectedMonster() => _selectedMonster;
    #endregion

    #region 마법 스크롤 선택
    public void SelectMagicScroll(MagicScrollData scrollData)
    {
        if (scrollData == null)
        {
            return;
        }

        _selectedScroll = scrollData;
        _selectedMonster = null;
        _selectedMonsterSlotIndex = -1;
        IsScrollMode = true;
        SuppressBattleMapInputForCurrentPointer();

        int slotIndex = -1;
        var ownedScrolls = _playerManager?.OwnedScrolls;
        if (ownedScrolls != null)
        {
            for (int i = 0; i < ownedScrolls.Count; i++)
            {
                var ownedScroll = ownedScrolls[i];
                if (ownedScroll == scrollData || (ownedScroll != null && scrollData != null && ownedScroll.name == scrollData.name))
                {
                    slotIndex = i;
                    break;
                }
            }
        }

        AttackSequenceUIController.Instance?.SyncScrollSelectionFromManager(slotIndex);
    }

    public MagicScrollData GetSelectedScroll() => _selectedScroll;
    #endregion

    #region 업데이트 (입력 처리)
    void Update()
    {
        if (!EnsureRuntimeReferences("Update", false)) return;
        if (!_playerManager.IsAttackerInCurrentBattle) return;
        if (_opponentFieldManager == null) return;

        HandleInput();
    }

    private void HandleInput()
    {
        // UI 위에서 클릭하면 스폰 처리 스킵 (UI 관통 방지)
        bool isPointerOverUI = IsPointerOverBattleActionBlocker();
        if (ShouldSuppressBattleMapInput())
        {
            return;
        }

        // 마우스 왼쪽 버튼 클릭/홀드
        if (MdfInput.PrimaryPointerWasPressedThisFrame())
        {
            if (!isPointerOverUI)
            {
                if (IsScrollMode && _selectedScroll != null)
                {
                    TryUseMagicScrollAtMousePosition();
                }
                else
                {
                    TrySpawnMonsterAtMousePosition();
                }
            }
            _isHolding = !isPointerOverUI && !IsScrollMode;
            _lastSpawnTime = Time.time;
        }
        else if (MdfInput.PrimaryPointerIsPressed() && _isHolding)
        {
            // UI 위로 마우스가 이동했으면 홀드 중단
            if (isPointerOverUI)
            {
                _isHolding = false;
            }
            // 홀드 중 연속 소환
            else if (Time.time - _lastSpawnTime >= holdSpawnInterval)
            {
                TrySpawnMonsterAtMousePosition();
                _lastSpawnTime = Time.time;
            }
        }
        else if (MdfInput.PrimaryPointerWasReleasedThisFrame())
        {
            _isHolding = false;
        }

        // 숫자키로 몬스터 선택 (1~9)
        for (int i = 0; i < 9; i++)
        {
            if (MdfInput.GetKeyDown(KeyCode.Alpha1 + i))
            {
                if (i < _playerManager.AttackMonsterPool.Count)
                {
                    SelectMonsterSlot(i);
                }
            }
        }
    }

    private bool ShouldSuppressBattleMapInput()
    {
        bool pointerActive = MdfInput.PrimaryPointerIsPressed() ||
                             MdfInput.PrimaryPointerWasPressedThisFrame() ||
                             MdfInput.PrimaryPointerWasReleasedThisFrame();

        if (!_suppressMapInputUntilPointerRelease && Time.frameCount > _suppressMapInputThroughFrame)
        {
            return false;
        }

        if (_suppressMapInputUntilPointerRelease && !pointerActive && Time.frameCount > _suppressMapInputThroughFrame)
        {
            _suppressMapInputUntilPointerRelease = false;
            return false;
        }

        if (pointerActive || Time.frameCount <= _suppressMapInputThroughFrame)
        {
            _isHolding = false;
            return true;
        }

        return false;
    }
    #endregion

    #region 마법 스크롤 사용
    private void TryUseMagicScrollAtMousePosition()
    {
        if (!EnsureRuntimeReferences("TryUseMagicScrollAtMousePosition", true))
        {
            return;
        }

        if (_selectedScroll == null || _playerCamera == null)
        {
            return;
        }

        Ray ray = _playerCamera.ScreenPointToRay(MdfInput.PointerPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, spawnAreaLayerMask))
        {
            UseMagicScrollAsync(hit.point).Forget();
        }
    }

    private async UniTask UseMagicScrollAsync(Vector3 position)
    {
        if (!EnsureRuntimeReferences("UseMagicScrollAsync", true))
        {
            return;
        }

        if (_selectedScroll == null)
        {
            return;
        }

        var gameManagers = GameManagers.Instance;
        if (gameManagers == null)
        {
            return;
        }

        int scrollSlotIndex = _playerManager.FindOwnedMagicScrollSlot(_selectedScroll);
        if (scrollSlotIndex < 0)
        {
            return;
        }

        bool isHost = _playerManager.Object != null && _playerManager.Object.HasStateAuthority;
        bool submittedOrExecuted = false;

        if (isHost)
        {
            PlayerRef requestSource = _playerManager.Object != null
                ? _playerManager.Object.InputAuthority
                : PlayerRef.None;
            if (requestSource == PlayerRef.None && _playerManager.Runner != null)
            {
                requestSource = _playerManager.Runner.LocalPlayer;
            }

            var command = new UseMagicScrollCommand(
                _playerManager.playerId,
                scrollSlotIndex,
                position,
                "human_host_magic_scroll",
                _playerManager.AppliedOwnedMagicScrollRevision);

            BattleCommandResult result = await gameManagers.ExecuteUseMagicScrollCommandAsync(
                command,
                CommandExecutionScope.ClientRequest,
                requestSource);

            if (!result.Success)
            {
                return;
            }

            submittedOrExecuted = true;
        }
        else
        {
            if (!_playerManager.HasAppliedCurrentOwnedMagicScrollSnapshot)
            {
                _playerManager.RPC_RequestSyncData();
                return;
            }

            gameManagers.RPC_RequestUseMagicScrollCommand(
                _playerManager.playerId,
                scrollSlotIndex,
                position,
                _playerManager.AppliedOwnedMagicScrollRevision,
                "human_client_magic_scroll");
            submittedOrExecuted = true;
        }

        if (submittedOrExecuted)
        {
            _selectedScroll = null;
            IsScrollMode = false;
            AttackSequenceUIController.Instance?.RefreshUI();
        }

        await UniTask.CompletedTask;
    }
    #endregion

    #region 몬스터 소환
    private void TrySpawnMonsterAtMousePosition()
    {
        if (!EnsureRuntimeReferences("TrySpawnMonsterAtMousePosition", true))
        {
            return;
        }

        if (!TryResolveSelectedMonster(out _, out _))
        {
            // Debug.Log("[AttackSequenceManager] 선택된 몬스터가 없거나 수량이 0입니다");
            return;
        }

        if (TryGetSpawnPositionUnderPointer(out Vector3 resolvedSpawnPosition))
        {
            SpawnMonsterAsync(resolvedSpawnPosition).Forget();
            return;
        }

        if (_playerCamera == null)
        {
            // Debug.LogWarning("[AttackSequenceManager] 카메라가 없습니다");
            return;
        }

        // 마우스 위치에서 레이캐스트
        Ray ray = _playerCamera.ScreenPointToRay(MdfInput.PointerPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, spawnAreaLayerMask))
        {
            Vector3 spawnPosition = hit.point;
            
            // 스폰 가능 영역인지 확인
            if (IsValidSpawnZone(spawnPosition))
            {
                SpawnMonsterAsync(spawnPosition).Forget();
            }
            else
            {
                // Debug.Log("[AttackSequenceManager] 유효하지 않은 스폰 영역입니다");
            }
        }
    }

    private bool IsPointerOverBattleActionBlocker()
    {
        if (GamePrepareUIToolkitController.IsPointerOverBlockingElement(MdfInput.PointerPosition))
        {
            return true;
        }

        if (!MdfInput.IsPointerOverUI())
        {
            return false;
        }

        // UIToolkit panels and some legacy canvases can raycast across the screen.
        // If the pointer still resolves to a valid battle spawn ground point, keep the map click alive.
        return !TryGetSpawnPositionUnderPointer(out _);
    }

    private bool TryGetSpawnPositionUnderPointer(out Vector3 spawnPosition)
    {
        spawnPosition = default;
        if (_playerCamera == null)
        {
            return false;
        }

        Ray ray = _playerCamera.ScreenPointToRay(MdfInput.PointerPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, spawnAreaLayerMask))
        {
            return false;
        }

        spawnPosition = hit.point;
        return IsValidSpawnZone(spawnPosition);
    }

    private async UniTask SpawnMonsterAsync(Vector3 position)
    {
        if (!EnsureRuntimeReferences("SpawnMonsterAsync", true))
        {
            return;
        }

        if (!TryResolveSelectedMonster(out var selectedMonster, out int poolSlotIndex)) return;
        if (_monsterSpawner == null) return;
        if (_opponentFieldManager == null) return;
        if (selectedMonster.MonsterData == null)
        {
            // Debug.LogWarning("[AttackSequenceManager] SpawnMonsterAsync 중단: 선택 몬스터 데이터 null");
            return;
        }

        // 몬스터 데이터 이름 저장 (RPC 전송용)
        int defenderPlayerId = _opponentFieldManager.playerManager?.playerId ?? -1;
        
        // 보스인 경우 소환 시점에 고유 ID 발급 + 보유 리스트에서 제거
        if (defenderPlayerId < 0 || poolSlotIndex < 0)
        {
            return;
        }

        var gameManagers = GameManagers.Instance;
        if (gameManagers == null)
        {
            return;
        }

        // 호스트(StateAuthority)인 경우 직접 스폰, 클라이언트인 경우 RPC 요청
        bool isHost = _playerManager.Object != null && _playerManager.Object.HasStateAuthority;
        
        if (isHost)
        {
            // 호스트: 직접 스폰
            PlayerRef requestSource = _playerManager.Object != null
                ? _playerManager.Object.InputAuthority
                : PlayerRef.None;
            if (requestSource == PlayerRef.None && _playerManager.Runner != null)
            {
                requestSource = _playerManager.Runner.LocalPlayer;
            }

            var command = new BattleSpawnMonsterCommand(
                _playerManager.playerId,
                defenderPlayerId,
                poolSlotIndex,
                position,
                1,
                "human_host_attack_sequence",
                _playerManager.AppliedAttackMonsterPoolRevision);

            BattleCommandResult result = await gameManagers.ExecuteBattleSpawnMonsterCommandAsync(
                command,
                CommandExecutionScope.ClientRequest,
                requestSource);

            if (!result.Success)
            {
                // Debug.LogWarning($"[AttackSequenceManager] 스폰 실패 - 풀 소모 생략: {_selectedMonster.MonsterData.monsterName}");
                return;
            }

        }
        else
        {
            if (HasPendingBattleSpawnForCurrentSnapshot(poolSlotIndex))
            {
                return;
            }

            // 클라이언트 경로는 기존 동작 유지 (로컬 UI 즉시 반영)
            // 클라이언트: 서버에 RPC 요청
            if (!_playerManager.HasAppliedCurrentAttackMonsterPoolSnapshot)
            {
                _playerManager.RPC_RequestSyncData();
                return;
            }

            if (GameManagers.Instance != null)
            {
                gameManagers.RPC_RequestBattleSpawnMonster(
                    _playerManager.playerId,
                    defenderPlayerId,
                    poolSlotIndex,
                    position,
                    1,
                    _playerManager.AppliedAttackMonsterPoolRevision,
                    "human_client_attack_sequence"
                );
                MarkPendingBattleSpawn(poolSlotIndex);
                // Debug.Log($"<color=yellow>[AttackSequenceManager] RPC 소환 요청: {monsterDataName} at {position}</color>");
            }
        }

        // 선택된 몬스터가 소진되면 선택 해제 (다음 몬스터 자동 선택 안 함)
        // 사용자가 직접 UI에서 다른 몬스터를 선택해야 소환 가능
        if (!RefreshSelectedMonsterAfterSpawn(poolSlotIndex))
        {
            // Debug.Log($"<color=orange>[AttackSequenceManager] '{_selectedMonster.MonsterData.monsterName}' 소진! 다른 몬스터를 선택해주세요.</color>");
            _selectedMonster = null;
            _selectedMonsterSlotIndex = -1;
            
            // UI 갱신 이벤트 발생
            AttackSequenceUIController.Instance?.RefreshUI();
        }
    }

    private bool TryResolveMonsterSlot(int slotIndex, out MonsterPoolEntry entry)
    {
        entry = null;
        var pool = _playerManager?.AttackMonsterPool;
        if (pool == null || slotIndex < 0 || slotIndex >= pool.Count)
        {
            return false;
        }

        entry = pool[slotIndex];
        return entry != null && !entry.IsEmpty;
    }

    private bool TryResolveSelectedMonster(out MonsterPoolEntry entry, out int slotIndex)
    {
        if (TryResolveMonsterSlot(_selectedMonsterSlotIndex, out entry))
        {
            slotIndex = _selectedMonsterSlotIndex;
            _selectedMonster = entry;
            return true;
        }

        var pool = _playerManager?.AttackMonsterPool;
        if (pool != null && _selectedMonster != null)
        {
            int existingIndex = pool.IndexOf(_selectedMonster);
            if (TryResolveMonsterSlot(existingIndex, out entry))
            {
                slotIndex = existingIndex;
                _selectedMonsterSlotIndex = existingIndex;
                _selectedMonster = entry;
                return true;
            }

            string selectedName = _selectedMonster.MonsterData != null
                ? _selectedMonster.MonsterData.name
                : null;
            if (!string.IsNullOrEmpty(selectedName))
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    var candidate = pool[i];
                    if (candidate != null
                        && !candidate.IsEmpty
                        && candidate.MonsterData != null
                        && candidate.MonsterData.name == selectedName)
                    {
                        slotIndex = i;
                        entry = candidate;
                        _selectedMonsterSlotIndex = i;
                        _selectedMonster = candidate;
                        return true;
                    }
                }
            }
        }

        entry = null;
        slotIndex = -1;
        return false;
    }

    private bool RefreshSelectedMonsterAfterSpawn(int preferredSlotIndex)
    {
        if (TryResolveMonsterSlot(preferredSlotIndex, out var entry))
        {
            _selectedMonster = entry;
            _selectedMonsterSlotIndex = preferredSlotIndex;
            return true;
        }

        var pool = _playerManager?.AttackMonsterPool;
        if (pool == null)
        {
            return false;
        }

        for (int i = 0; i < pool.Count; i++)
        {
            if (TryResolveMonsterSlot(i, out entry))
            {
                _selectedMonster = entry;
                _selectedMonsterSlotIndex = i;
                AttackSequenceUIController.Instance?.SyncMonsterSelectionFromManager(i);
                return true;
            }
        }

        return false;
    }

    private bool HasPendingBattleSpawnForCurrentSnapshot(int poolSlotIndex)
    {
        if (_pendingBattleSpawnSlotIndex != poolSlotIndex)
        {
            return false;
        }

        if (_playerManager == null
            || _playerManager.AppliedAttackMonsterPoolRevision != _pendingBattleSpawnRevision
            || Time.unscaledTime - _pendingBattleSpawnStartedAt > PendingBattleSpawnTimeoutSeconds)
        {
            ClearPendingBattleSpawn();
            return false;
        }

        return true;
    }

    private void MarkPendingBattleSpawn(int poolSlotIndex)
    {
        _pendingBattleSpawnSlotIndex = poolSlotIndex;
        _pendingBattleSpawnRevision = _playerManager != null
            ? _playerManager.AppliedAttackMonsterPoolRevision
            : -1;
        _pendingBattleSpawnStartedAt = Time.unscaledTime;
    }

    private void ClearPendingBattleSpawn()
    {
        _pendingBattleSpawnSlotIndex = -1;
        _pendingBattleSpawnRevision = -1;
        _pendingBattleSpawnStartedAt = 0f;
    }
    #endregion

    #region 참조 복구
    private bool EnsureRuntimeReferences(string context, bool verboseFailure)
    {
        if (_playerManager == null)
        {
            _playerManager = GetComponent<PlayerManager>();
        }

        if (_monsterSpawner == null && _playerManager != null)
        {
            _monsterSpawner = _playerManager.monsterSpawner;
            if (_monsterSpawner == null)
            {
                _monsterSpawner = _playerManager.GetComponentInChildren<MonsterSpawner>(true);
            }
        }

        if (_playerCamera == null)
        {
            _playerCamera = ComponentRegistry.Get<Camera>("Main Camera", false);
            if (_playerCamera == null)
            {
                _playerCamera = Camera.main;
            }
        }

        bool ready = _playerManager != null && _monsterSpawner != null;
        if (!ready && verboseFailure && Time.unscaledTime - _lastMissingRefLogTime > 0.5f)
        {
            _lastMissingRefLogTime = Time.unscaledTime;
            // Debug.LogWarning($"[AttackSequenceManager] 참조 복구 실패 ({context}) {DescribeRuntimeState()}");
        }

        return ready;
    }

    private string DescribeRuntimeState()
    {
        string ownerState = _playerManager == null
            ? "owner=null"
            : $"owner=Player({_playerManager.playerId}, name={_playerManager.name}, hasObject={_playerManager.Object != null})";
        string spawnerState = _monsterSpawner == null
            ? "spawner=null"
            : $"spawner={_monsterSpawner.name}";
        string opponentState = _opponentFieldManager == null
            ? "opponentField=null"
            : $"opponentField={_opponentFieldManager.name}";
        string cameraState = _playerCamera == null
            ? "camera=null"
            : $"camera={_playerCamera.name}";
        return $"{ownerState}, {spawnerState}, {opponentState}, {cameraState}";
    }
    #endregion

    #region 스폰 영역 검증
    /// <summary>
    /// 주어진 위치가 스폰 가능 영역인지 확인합니다.
    /// 공격자는 상대 필드의 그리드 **바깥** 영역에서만 소환 가능합니다.
    /// </summary>
    private bool IsValidSpawnZone(Vector3 worldPosition)
    {
        // Keep local click filtering aligned with server validation.
        return BattleCommandValidator.IsInsideBattleSpawnZone(_opponentFieldManager, worldPosition);
    }
    #endregion
}
