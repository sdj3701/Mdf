// Assets/Scripts/Managers/PlayerManager.cs

using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Fusion; // Fusion 네임스페이스 추가
using Cysharp.Threading.Tasks;

public class PlayerManager : NetworkBehaviour // [수정] MonoBehaviour -> NetworkBehaviour
{
    // [수정] playerId를 모든 클라이언트가 동기화할 수 있도록 [Networked] 프로퍼티로 변경합니다.
    [Networked] public int playerId { get; set; }

    [Header("핵심 능력치 (읽기 전용)")]
    // 참고: 이 능력치들도 [Networked]로 변경하면 더 안정적이지만,
    // 현재는 Command 패턴을 사용하므로 playerId만 동기화해도 동작합니다.
    [SerializeField] private int health = 100;
    [SerializeField] private int gold = 10;
    [SerializeField] private int wallCount = 5;
    private const int MAX_WALL_COUNT = 5;
    [SerializeField] private int wallReserveK = 2;
    [SerializeField] private Vector2 wallBuildDelayRange = new Vector2(0.3f, 0.8f);
    [SerializeField] private Vector2 unitPurchaseDelayRange = new Vector2(0.5f, 1.0f);
    [SerializeField] private Vector2 unitMoveDelayRange = new Vector2(0.4f, 0.9f);

    [HideInInspector] public List<UnityEngine.Vector3Int> mazePlannedOrder = new List<UnityEngine.Vector3Int>();
    [HideInInspector] public bool mazePlanned = false;
    [HideInInspector] public int mazeBuildCursor = 0;

    // AI 준비 단계 진행 상태 추적
    [HideInInspector] public bool mazeConstructionComplete = false;
    [HideInInspector] public bool unitPurchaseComplete = false;

    [Header("소유 객체 목록")]
    public List<Unit> ownedUnits = new List<Unit>();
    public List<AugmentData> chosenAugments = new List<AugmentData>();

    [Header("하위 매니저 참조 (자동 할당)")]
    public FieldManager fieldManager;
    public ShopManager shopManager;
    public MonsterSpawner monsterSpawner;
    public AugmentManager augmentManager;
    public AstarGrid astarGrid;
    public Transform spawnPoint { get; private set; }
    public Transform goalTransform { get; private set; }

    [HideInInspector]
    public PlayerManager opponentManager;

    public bool IsActivelyFighting { get; private set; }

    // Pending unit registrations received before FieldManager is ready
    private struct PendingUnitReg
    {
        public NetworkObject unitNO;
        public int x;
        public int y;
        public string unitDataKey;
        public int starLevel;
    }
    private List<PendingUnitReg> _pendingUnitRegs = new List<PendingUnitReg>();

    /// <summary>
    /// 이 플레이어 오브젝트에 있는 하위 매니저 참조를 캐시합니다.
    /// </summary>
     void Awake()
    {
        // Awake는 그대로 유지하여 하위 컴포넌트 참조를 미리 찾아둡니다.
        fieldManager = GetComponentInChildren<FieldManager>();
        shopManager = GetComponentInChildren<ShopManager>();
        monsterSpawner = GetComponentInChildren<MonsterSpawner>();
        augmentManager = GetComponentInChildren<AugmentManager>();
    }

    /// <summary>
    /// 상태 권한(서버)에서 호출되어 모든 클라이언트에 이 플레이어의 ID와 그리드 참조를 초기화합니다.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void Rpc_InitializePlayer(int id, NetworkObject gridNetworkObject)
    {
        // [수정] 네트워크를 통해 전달받은 ID를 [Networked] 프로퍼티에 저장합니다.
        this.playerId = id;
        //Debug.Log($"--- Player {playerId} RPC 초기화 실행 (IsServer: {Object.ToString()}) ---");

        // --- 1. 가장 중요한 gridNetworkObject가 제대로 전달되었는지 확인 ---
        if (gridNetworkObject == null)
        {
            Debug.LogError($"[Player {playerId}]: RPC로 전달받은 gridNetworkObject가 null입니다! 초기화 실패.");
            return;
        }

        // 전달받은 NetworkObject 참조로부터 그리드 게임오브젝트를 가져옵니다.
        GameObject gridInstance = gridNetworkObject.gameObject;

        // 3D Ground 오브젝트 찾기
        GameObject ground3D = null;
        // 우선 활성화된 오브젝트 중에서 이름이 "Ground" 또는 "Field"인 것을 찾습니다.
        var groundCandidates = gridInstance.GetComponentsInChildren<Transform>(true)
            .Where(t => t != null && (t.name == "Ground" || t.name == "Field"))
            .Select(t => t.gameObject)
            .ToList();

        ground3D = groundCandidates.FirstOrDefault(go => go != null && go.activeInHierarchy);
        // 폴백: 없다면 첫 후보를 사용
        if (ground3D == null)
        {
            ground3D = groundCandidates.FirstOrDefault();
        }

        this.astarGrid = gridInstance.GetComponentInChildren<AstarGrid>();
        this.spawnPoint = gridInstance.transform.Find("SpawnPoint");
        this.goalTransform = gridInstance.transform.Find("Goal");

        // 각 컴포넌트/오브젝트를 찾았는지 확인하는 로그

        // AstarGrid 초기화는 FieldManager 초기화 이후에 수행하여 3D 그리드 정보를 공유합니다.

        // 하위 매니저 초기화
        // 3D Ground 기반 초기화
        if (fieldManager)
        {
            if (ground3D != null)
            {
                fieldManager.Initialize(this, ground3D);
            }
            else
            {
                Debug.LogError($"[Player {playerId}]: FieldManager 초기화 실패 - Ground 오브젝트를 찾을 수 없습니다!");
            }
        }

        // 이제 FieldManager가 준비되었으므로 AstarGrid를 FieldManager와 동기화하여 초기화합니다.
        if (this.astarGrid != null)
        {
            this.astarGrid.fieldManager = fieldManager;
            this.astarGrid.useFieldManagerGrid = true;
            this.astarGrid.Initialize();
        }
        else
        {
            Debug.LogError($"[Player {playerId}]: AstarGrid 컴포넌트를 찾지 못해 경로 탐색을 초기화할 수 없습니다.");
        }
        if (shopManager) shopManager.playerManager = this;

        if (monsterSpawner)
        {
            var defaultMonsterPrefab = GameManagers.Instance.defaultMonsterPrefab;
            monsterSpawner.Initialize(this, this.astarGrid, defaultMonsterPrefab, this.spawnPoint, this.goalTransform);
        }

        if (augmentManager) augmentManager.playerManager = this;

        IsActivelyFighting = false;

        // Process any unit registrations that arrived early
        if (_pendingUnitRegs.Count > 0)
        {
            // Make a copy to avoid modification during iteration
            var pending = new List<PendingUnitReg>(_pendingUnitRegs);
            _pendingUnitRegs.Clear();
            foreach (var p in pending)
            {
                await RPC_RegisterUnitAt_Internal(p.unitNO, p.x, p.y, p.unitDataKey, p.starLevel);
            }
        }
    }

    /// <summary>
    /// 서버에서 전달된 영구 벽 정보를 모든 클라이언트의 이 플레이어 필드에 적용합니다.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ApplyPermanentWalls(int[] flatPositions)
    {
        if (fieldManager != null)
        {
            fieldManager.ApplyPermanentWallsFromServer(flatPositions);
        }
    }

    /// <summary>
    /// NetworkId로 스폰된 유닛을 찾아 지정된 그리드 위치에 이 플레이어 소유로 등록합니다.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void RPC_RegisterUnitAt(NetworkId unitId, int x, int y, string unitDataKey, int starLevel)
    {
        try
        {
            if (Object != null && Object.HasStateAuthority) return;
            NetworkObject unitNO = null;
            bool resolved = false;
            int attempts = 0;
            do
            {
                if (Runner != null)
                {
                    resolved = Runner.TryFindObject(unitId, out unitNO);
                }
                if (!resolved)
                {
                    await Cysharp.Threading.Tasks.UniTask.Yield();
                    attempts++;
                }
            } while (!resolved && attempts < 300);
            if (!resolved || unitNO == null)
            {
                Debug.LogWarning($"<color=yellow>[RPC_RegisterUnitAt] failed to resolve NetworkObject by NetworkId='{unitId}' key='{unitDataKey}'</color>");
                return;
            }

            if (fieldManager == null || fieldManager.ground3D == null)
            {
                _pendingUnitRegs.Add(new PendingUnitReg
                {
                    unitNO = unitNO,
                    x = x,
                    y = y,
                    unitDataKey = unitDataKey,
                    starLevel = starLevel
                });
                return;
            }

            await RPC_RegisterUnitAt_Internal(unitNO, x, y, unitDataKey, starLevel);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RPC_RegisterUnitAt] exception: {ex.Message}");
        }
    }

    /// <summary>
    /// 필드에 등록하기 전에 유닛 데이터를 초기화하고 UI를 연결하는 내부 도우미입니다.
    /// </summary>
    private async Cysharp.Threading.Tasks.UniTask RPC_RegisterUnitAt_Internal(NetworkObject unitNO, int x, int y, string unitDataKey, int starLevel)
    {
        try
        {
            if (fieldManager == null)
            {
                Debug.LogWarning($"<color=yellow>[RPC_Internal] fieldManager null</color>");
                return;
            }
            var unit = unitNO.GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogWarning($"<color=yellow>[RPC_Internal] Unit component missing on '{unitNO?.name}'</color>");
                return;
            }
            var pos = new Vector3Int(x, y, 0);
            if (fieldManager.IsUnitAt(pos))
            {
                Debug.LogWarning($"<color=yellow>[RPC_Internal] position already occupied. Skipping register. pos={pos}</color>");
                return;
            }

            if (fieldManager.statusBarPrefab != null)
            {
                var preExistingStatusBar = unit.GetComponentInChildren<StatusBarUI>(includeInactive: true);
                if (preExistingStatusBar == null)
                {
                    var statusBarGO = UnityEngine.Object.Instantiate(fieldManager.statusBarPrefab, unit.transform);
                    var statusBarUI = statusBarGO.GetComponent<StatusBarUI>();
                    if (statusBarUI != null)
                    {
                        unit.SetStatusBar(statusBarUI);
                    }
                }
            }

            bool needInit = unit.Data == null || (!string.IsNullOrEmpty(unitDataKey) && unit.Data.name != unitDataKey);
            if (needInit)
            {
                UnitData data = null;
                if (!string.IsNullOrEmpty(unitDataKey))
                {
                    if (LoadManager.Instance == null)
                    {
                        await Cysharp.Threading.Tasks.UniTask.WaitUntil(() => LoadManager.Instance != null);
                    }
                    var lmReady = LoadManager.Instance.IsReady;
                    if (!lmReady)
                    {
                        await LoadManager.Instance.WaitUntilReady();
                    }
                    data = LoadManager.Instance.GetUnitData(unitDataKey);
                    if (data == null)
                    {
                        data = await AssetLoader.LoadAssetAsync<UnitData>(unitDataKey);
                    }
                }
                if (data != null)
                {
                    await unit.Initialize(data, starLevel, this);
                }
                else
                {
                    Debug.LogError($"[Player {playerId}] RPC_RegisterUnitAt could not resolve UnitData for key '{unitDataKey}'.");
                }
            }

            if (fieldManager.statusBarPrefab != null)
            {
                var existingStatusBar = unit.GetComponentInChildren<StatusBarUI>(includeInactive: true);
                if (existingStatusBar == null)
                {
                    var statusBarGO = UnityEngine.Object.Instantiate(fieldManager.statusBarPrefab, unit.transform);
                    var statusBarUI = statusBarGO.GetComponent<StatusBarUI>();
                    if (statusBarUI != null)
                    {
                        unit.SetStatusBar(statusBarUI);
                    }
                }
            }
            if (unit.Data == null)
            {
                Debug.LogWarning($"<color=yellow>[RPC_Internal] unit.Data still null after resolve. Skip Register. key='{unitDataKey}', pos={pos}</color>");
                return;
            }
            fieldManager.RegisterUnitAt(unit, pos);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RPC_Internal] exception: {ex.Message}");
        }
    }

    // ... (이하 나머지 코드는 기존과 동일) ...

    /// <summary>
    /// 플레이어가 전투 중(전투 단계)인지 상태를 설정합니다.
    /// </summary>
    public void SetFightingState(bool isFighting)
    {
        this.IsActivelyFighting = isFighting;
    }

    #region Public Getters & Stat Modifiers

    /// <summary>현재 체력을 반환합니다.</summary>
    public int GetHealth() => health;
    /// <summary>현재 골드를 반환합니다.</summary>
    public int GetGold() => gold;
    /// <summary>남은 설치 가능한 벽 개수를 반환합니다.</summary>
    public int GetWallCount() => wallCount;
    /// <summary>벽 건설에 사용하는 예비 계수 값을 반환합니다.</summary>
    public int GetWallReserveK() => wallReserveK;
    /// <summary>벽 건설 행동의 지연 범위를 정규화하여 반환합니다.</summary>
    public Vector2 GetWallBuildDelayRange() => NormalizeDelayRange(wallBuildDelayRange);
    /// <summary>유닛 구매 지연 범위를 정규화하여 반환합니다.</summary>
    public Vector2 GetUnitPurchaseDelayRange() => NormalizeDelayRange(unitPurchaseDelayRange);
    /// <summary>유닛 이동 지연 범위를 정규화하여 반환합니다.</summary>
    public Vector2 GetUnitMoveDelayRange() => NormalizeDelayRange(unitMoveDelayRange);

    /// <summary>
    /// 지정한 골드만큼 사용을 시도합니다. 성공 시 true를 반환합니다.
    /// </summary>
    public bool SpendGold(int amount)
    {
        if (gold >= amount)
        {
            gold -= amount;
            GameEvents.TriggerPlayerStatsChanged(playerId, this.health, this.gold);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 지정한 골드를 추가하고 능력치 갱신 이벤트를 발생시킵니다.
    /// </summary>
    public void AddGold(int amount)
    {
        if (amount <= 0) return;
        gold += amount;
        GameEvents.TriggerPlayerStatsChanged(playerId, this.health, this.gold);
    }

    /// <summary>
    /// 데미지를 적용하고 체력이 0이 되면 게임 오버를 처리합니다.
    /// </summary>
    public void TakeDamage(int damage)
    {
        if (damage <= 0) return;
        health -= damage;

        if (health <= 0)
        {
            health = 0;
            if (GameManagers.Instance != null)
            {
                GameManagers.Instance.GameOver(this);
            }
        }
        GameEvents.TriggerPlayerStatsChanged(playerId, this.health, this.gold);
    }

    /// <summary>
    /// 지정한 데이터와 성급으로 유닛을 생성하여 필드에 배치합니다.
    /// </summary>
    public void AddUnit(UnitData unitData, int starLevel)
    {
        if(fieldManager != null)
        {
            fieldManager.CreateAndPlaceUnitOnField(unitData, starLevel);
        }
    }

    /// <summary>
    /// 벽이 남아 있으면 하나를 사용합니다. 성공 시 true를 반환하고 이벤트를 발생시킵니다.
    /// </summary>
    public bool TryUseWall()
    {
        if (wallCount > 0)
        {
            wallCount--;
            GameEvents.TriggerPlayerWallCountChanged(playerId, wallCount);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 최대치까지 벽을 1개 돌려주고 이벤트를 발생시킵니다.
    /// </summary>
    public void ReturnWall()
    {
        if (wallCount < MAX_WALL_COUNT)
        {
            wallCount++;
            GameEvents.TriggerPlayerWallCountChanged(playerId, wallCount);
        }
    }

    /// <summary>
    /// 클라이언트→서버 RPC로 명령 처리 요청을 전달합니다. 서버에서만 실행됩니다.
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestCommandToServer(CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams, RpcInfo info = default)
    {
        if (Runner == null || !Runner.IsServer) return; // 서버에서만 처리
        var gm = GameManagers.Instance;
        if (gm == null)
        {
            gm = FindObjectOfType<GameManagers>();
            if (gm == null)
            {
                Debug.LogWarning("<color=green>[NetFlow] GameManagers not found on server yet. Dropping command.</color>");
                return;
            }
        }
        gm.RPC_BroadcastCommandToClients(type, intParams, stringParams, vectorParams);
    }

    /// <summary>
    /// 벡터를 [최소, 최대] 순서의 음수가 아닌 값으로 보정합니다.
    /// </summary>
    private static Vector2 NormalizeDelayRange(Vector2 range)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        if (min < 0f) min = 0f;
        if (max < min) max = min;
        return new Vector2(min, max);
    }

    #endregion

    #region 디버그 시각화

    /// <summary>
    /// 에디터에서 미로 계획과 건설 순서를 시각화하는 기즈모를 그립니다.
    /// </summary>
    void OnDrawGizmos()
    {
        // 미로 계획 시각화 (연한 노란색)
        if (mazePlanned && mazePlannedOrder != null && mazePlannedOrder.Count > 0 && fieldManager != null)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f); // 연한 노란색

            foreach (var wallPos in mazePlannedOrder)
            {
                // 이미 건설된 벽은 건너뛰기
                if (fieldManager.HasWallAt(wallPos)) continue;

                // 그리드 좌표를 월드 좌표로 변환
                Vector3 worldPos = fieldManager.GridToWorld(wallPos);

                // 큐브로 표시 (연하게)
                Gizmos.DrawCube(worldPos, new Vector3(0.9f, 0.5f, 0.9f));
                Gizmos.DrawWireCube(worldPos, new Vector3(0.9f, 0.5f, 0.9f));
            }

            // 건설 순서 표시 (선으로 연결)
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.5f);
            for (int i = 0; i < mazePlannedOrder.Count - 1; i++)
            {
                if (fieldManager.HasWallAt(mazePlannedOrder[i])) continue;
                if (fieldManager.HasWallAt(mazePlannedOrder[i + 1])) continue;

                Vector3 from = fieldManager.GridToWorld(mazePlannedOrder[i]) + Vector3.up * 0.5f;
                Vector3 to = fieldManager.GridToWorld(mazePlannedOrder[i + 1]) + Vector3.up * 0.5f;
                Gizmos.DrawLine(from, to);
            }
        }
    }

    #endregion
}
