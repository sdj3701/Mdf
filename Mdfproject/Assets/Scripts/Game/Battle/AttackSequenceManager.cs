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
    
    [Header("소환 설정")]
    [Tooltip("현재 선택된 몬스터")]
    private MonsterPoolEntry _selectedMonster;
    
    [Tooltip("홀드 소환 간격 (초)")]
    [SerializeField] private float holdSpawnInterval = 0.3f;
    
    private float _lastSpawnTime;
    private bool _isHolding;
    private Camera _playerCamera;
    
    [Header("스폰 영역 설정")]
    [Tooltip("스폰 가능 영역 레이어")]
    [SerializeField] private LayerMask spawnAreaLayerMask;
    #endregion

    #region 초기화
    public void Initialize(PlayerManager owner)
    {
        _playerManager = owner;
        _monsterSpawner = owner?.monsterSpawner;
        
        // 카메라 찾기
        _playerCamera = ComponentRegistry.Get<Camera>("Main Camera", false);
        if (_playerCamera == null) _playerCamera = Camera.main;

        Debug.Log($"<color=cyan>[AttackSequenceManager] Player {owner?.playerId} 초기화 완료</color>");
    }

    /// <summary>
    /// 공격 시퀀스 시작 시 호출. 상대 필드 매니저를 설정합니다.
    /// </summary>
    public void StartAttackSequence(PlayerManager opponent)
    {
        if (opponent == null)
        {
            Debug.LogWarning("[AttackSequenceManager] 상대가 없습니다 (관전 모드)");
            return;
        }

        _opponentFieldManager = opponent.fieldManager;
        _selectedMonster = null;
        
        // 첫 번째 몬스터 자동 선택
        if (_playerManager.AttackMonsterPool.Count > 0)
        {
            SelectMonster(_playerManager.AttackMonsterPool[0]);
        }

        Debug.Log($"<color=green>[AttackSequenceManager] 공격 시퀀스 시작! 상대: Player {opponent.playerId}</color>");
    }

    public void EndAttackSequence()
    {
        _selectedMonster = null;
        _opponentFieldManager = null;
        _isHolding = false;
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
            Debug.LogWarning("[AttackSequenceManager] 선택할 수 없는 몬스터입니다");
            return;
        }

        _selectedMonster = entry;
        Debug.Log($"<color=yellow>[AttackSequenceManager] 몬스터 선택: {entry.MonsterData.monsterName} (남은 수량: {entry.RemainingCount})</color>");
    }

    public MonsterPoolEntry GetSelectedMonster() => _selectedMonster;
    #endregion

    #region 업데이트 (입력 처리)
    void Update()
    {
        if (_playerManager == null) return;
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
                TrySpawnMonsterAtMousePosition();
            }
            _isHolding = !isPointerOverUI;
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

    #region 몬스터 소환
    private void TrySpawnMonsterAtMousePosition()
    {
        if (_selectedMonster == null || _selectedMonster.IsEmpty)
        {
            Debug.Log("[AttackSequenceManager] 선택된 몬스터가 없거나 수량이 0입니다");
            return;
        }

        if (_playerCamera == null)
        {
            Debug.LogWarning("[AttackSequenceManager] 카메라가 없습니다");
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
                Debug.Log("[AttackSequenceManager] 유효하지 않은 스폰 영역입니다");
            }
        }
    }

    private async UniTask SpawnMonsterAsync(Vector3 position)
    {
        if (_selectedMonster == null || _selectedMonster.IsEmpty) return;
        if (_monsterSpawner == null) return;

        // 풀에서 소비
        if (!_playerManager.TryConsumeMonsterFromPool(_selectedMonster.MonsterData))
        {
            Debug.LogWarning("[AttackSequenceManager] 몬스터 풀에서 소비 실패");
            return;
        }

        // 상대 필드에 몬스터 소환
        await _monsterSpawner.SpawnMonsterAtPositionAsync(
            _selectedMonster.MonsterData,
            position,
            _opponentFieldManager
        );

        // 선택된 몬스터가 소진되면 다음 몬스터로 자동 전환
        if (_selectedMonster.IsEmpty)
        {
            var nextAvailable = _playerManager.AttackMonsterPool.Find(p => !p.IsEmpty);
            if (nextAvailable != null)
            {
                SelectMonster(nextAvailable);
            }
            else
            {
                _selectedMonster = null;
                Debug.Log("<color=orange>[AttackSequenceManager] 모든 몬스터 소진!</color>");
            }
        }
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
            Debug.Log($"[AttackSequenceManager] 그리드 안쪽 위치 (소환 불가): ({rawGridX}, {rawGridY})");
            return false;
        }

        // 그리드 바깥이면 소환 가능
        Debug.Log($"[AttackSequenceManager] 그리드 바깥 위치 (소환 가능): ({rawGridX}, {rawGridY})");
        return true;
    }
    #endregion
}
