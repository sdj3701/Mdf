// Assets/Scripts/Game/Battle/AttackSequenceManager.cs
using UnityEngine;
using UnityEngine.EventSystems;
using Cysharp.Threading.Tasks;
using Fusion;
using System.Linq;

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

    [Tooltip("현재 선택된 마법 스크롤")]
    private MagicScrollData _selectedScroll;
    
    [Tooltip("홀드 소환 간격 (초)")]
    [SerializeField] private float holdSpawnInterval = 0.3f;
    
    private float _lastSpawnTime;
    private bool _isHolding;
    private Camera _playerCamera;
    
    [Header("스폰 영역 설정")]
    [Tooltip("스폰 가능 영역 레이어")]
    [SerializeField] private LayerMask spawnAreaLayerMask;

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
        _selectedScroll = null;
        IsScrollMode = false;
        
        // 첫 번째 몬스터 자동 선택
        var pool = _playerManager.AttackMonsterPool;
        if (pool != null && pool.Count > 0)
        {
            var firstValid = pool.FirstOrDefault(entry => entry != null && !entry.IsEmpty);
            if (firstValid != null)
            {
                SelectMonster(firstValid);
            }
        }
        else
        {
            // Debug.LogWarning($"[AttackSequenceManager] 공격 시퀀스 시작 시 몬스터 풀 비어있음. player={_playerManager.playerId}");
        }

        // Debug.Log($"<color=green>[AttackSequenceManager] 공격 시퀀스 시작! 상대: Player {opponent.playerId} ({DescribeRuntimeState()})</color>");
    }

    public void EndAttackSequence()
    {
        _selectedMonster = null;
        _selectedScroll = null;
        _opponentFieldManager = null;
        _isHolding = false;
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

        _selectedMonster = entry;
        _selectedScroll = null;
        IsScrollMode = false;
        int slotIndex = _playerManager != null && _playerManager.AttackMonsterPool != null
            ? _playerManager.AttackMonsterPool.IndexOf(entry)
            : -1;
        AttackSequenceUIController.Instance?.SyncMonsterSelectionFromManager(slotIndex);
        // Debug.Log($"<color=yellow>[AttackSequenceManager] 몬스터 선택: {entry.MonsterData.monsterName} (남은 수량: {entry.RemainingCount})</color>");
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
        IsScrollMode = true;

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
        bool isPointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // 마우스 왼쪽 버튼 클릭/홀드
        if (Input.GetMouseButtonDown(0))
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
        else if (Input.GetMouseButton(0) && _isHolding)
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
        else if (Input.GetMouseButtonUp(0))
        {
            _isHolding = false;
        }

        // 숫자키로 몬스터 선택 (1~9)
        for (int i = 0; i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                if (i < _playerManager.AttackMonsterPool.Count)
                {
                    SelectMonster(_playerManager.AttackMonsterPool[i]);
                }
            }
        }
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

        Ray ray = _playerCamera.ScreenPointToRay(Input.mousePosition);
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

        string scrollDataName = _selectedScroll.name;
        bool isHost = _playerManager.Object != null && _playerManager.Object.HasStateAuthority;

        if (isHost)
        {
            if (!_playerManager.TryConsumeMagicScroll(_selectedScroll))
            {
                return;
            }

            gameManagers.RPC_BroadcastMagicScrollUsed(_playerManager.playerId, scrollDataName, position);
        }
        else
        {
            gameManagers.RPC_RequestUseMagicScroll(_playerManager.playerId, scrollDataName, position);
        }

        _selectedScroll = null;
        IsScrollMode = false;
        AttackSequenceUIController.Instance?.RefreshUI();

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

        if (_selectedMonster == null || _selectedMonster.IsEmpty)
        {
            // Debug.Log("[AttackSequenceManager] 선택된 몬스터가 없거나 수량이 0입니다");
            return;
        }

        if (_playerCamera == null)
        {
            // Debug.LogWarning("[AttackSequenceManager] 카메라가 없습니다");
            return;
        }

        // 마우스 위치에서 레이캐스트
        Ray ray = _playerCamera.ScreenPointToRay(Input.mousePosition);
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

    private async UniTask SpawnMonsterAsync(Vector3 position)
    {
        if (!EnsureRuntimeReferences("SpawnMonsterAsync", true))
        {
            return;
        }

        if (_selectedMonster == null || _selectedMonster.IsEmpty) return;
        if (_monsterSpawner == null) return;
        if (_opponentFieldManager == null) return;
        if (_selectedMonster.MonsterData == null)
        {
            // Debug.LogWarning("[AttackSequenceManager] SpawnMonsterAsync 중단: 선택 몬스터 데이터 null");
            return;
        }

        // 몬스터 데이터 이름 저장 (RPC 전송용)
        string monsterDataName = _selectedMonster.MonsterData?.name;
        bool isBoss = _selectedMonster.IsBoss;
        int originPlayerId = _selectedMonster.OriginPlayerId;
        int defenderPlayerId = _opponentFieldManager.playerManager?.playerId ?? -1;
        
        // 보스인 경우 소환 시점에 고유 ID 발급 + 보유 리스트에서 제거
        int bossUniqueId = -1;
        if (isBoss)
        {
            bossUniqueId = SurvivorBossManager.Instance?.GetNextBossUniqueId() ?? -1;
        }

        // 호스트(StateAuthority)인 경우 직접 스폰, 클라이언트인 경우 RPC 요청
        bool isHost = _playerManager.Object != null && _playerManager.Object.HasStateAuthority;
        
        if (isHost)
        {
            // 호스트: 직접 스폰
            var spawnedMonster = await _monsterSpawner.SpawnMonsterAtPositionAsync(
                _selectedMonster.MonsterData,
                position,
                _opponentFieldManager,
                isBoss,
                bossUniqueId,
                originPlayerId
            );

            if (spawnedMonster == null)
            {
                // Debug.LogWarning($"[AttackSequenceManager] 스폰 실패 - 풀 소모 생략: {_selectedMonster.MonsterData.monsterName}");
                return;
            }

            if (isBoss)
            {
                _playerManager.ConsumeOwnedBoss(_selectedMonster.MonsterData);
                // Debug.Log($"<color=red>[AttackSequenceManager] 보스 소환! ID:{bossUniqueId}, 타겟: Player {defenderPlayerId}</color>");
            }

            if (!_playerManager.TryConsumeMonsterFromPool(_selectedMonster.MonsterData))
            {
                // Debug.LogWarning("[AttackSequenceManager] 호스트 소환 성공 후 몬스터 풀 소비 실패");
            }
        }
        else
        {
            if (isBoss)
            {
                _playerManager.ConsumeOwnedBoss(_selectedMonster.MonsterData);
                // Debug.Log($"<color=red>[AttackSequenceManager] 보스 소환! ID:{bossUniqueId}, 타겟: Player {defenderPlayerId}</color>");
            }

            // 클라이언트 경로는 기존 동작 유지 (로컬 UI 즉시 반영)
            if (!_playerManager.TryConsumeMonsterFromPool(_selectedMonster.MonsterData))
            {
                // Debug.LogWarning("[AttackSequenceManager] 몬스터 풀에서 소비 실패");
                return;
            }

            // 클라이언트: 서버에 RPC 요청
            if (GameManagers.Instance != null)
            {
                GameManagers.Instance.RPC_RequestSpawnMonster(
                    _playerManager.playerId,
                    defenderPlayerId,
                    monsterDataName,
                    position,
                    isBoss,
                    bossUniqueId,
                    originPlayerId
                );
                // Debug.Log($"<color=yellow>[AttackSequenceManager] RPC 소환 요청: {monsterDataName} at {position}</color>");
            }
        }

        // 선택된 몬스터가 소진되면 선택 해제 (다음 몬스터 자동 선택 안 함)
        // 사용자가 직접 UI에서 다른 몬스터를 선택해야 소환 가능
        if (_selectedMonster.IsEmpty)
        {
            // Debug.Log($"<color=orange>[AttackSequenceManager] '{_selectedMonster.MonsterData.monsterName}' 소진! 다른 몬스터를 선택해주세요.</color>");
            _selectedMonster = null;
            
            // UI 갱신 이벤트 발생
            AttackSequenceUIController.Instance?.RefreshUI();
        }
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
        if (_opponentFieldManager == null) return false;

        // 클램핑 없이 직접 그리드 좌표 계산 (FieldManager.WorldToGrid는 클램핑되어 있음)
        Vector3 gridOrigin = _opponentFieldManager.gridOrigin;
        float cellSize = _opponentFieldManager.cellSize;
        Vector2Int gridSize = _opponentFieldManager.gridSize;

        int rawGridX = Mathf.FloorToInt((worldPosition.x - gridOrigin.x) / cellSize);
        int rawGridY = Mathf.FloorToInt((worldPosition.z - gridOrigin.z) / cellSize);

        // 그리드 **안쪽**이면 소환 불가 (그리드 바깥만 허용)
        bool isInsideGrid = rawGridX >= 0 && rawGridX < gridSize.x &&
                            rawGridY >= 0 && rawGridY < gridSize.y;

        if (isInsideGrid)
        {
            // Debug.Log($"[AttackSequenceManager] 그리드 안쪽 위치 (소환 불가): ({rawGridX}, {rawGridY})");
            return false;
        }

        // 그리드 바깥이면 소환 가능
        // Debug.Log($"[AttackSequenceManager] 그리드 바깥 위치 (소환 가능): ({rawGridX}, {rawGridY})");
        return true;
    }
    #endregion
}
