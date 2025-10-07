// Assets/Scripts/Managers/PlayerManager.cs

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Linq;
using Fusion; // Fusion 네임스페이스 추가

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

     void Awake()
    {
        // Awake는 그대로 유지하여 하위 컴포넌트 참조를 미리 찾아둡니다.
        fieldManager = GetComponentInChildren<FieldManager>();
        shopManager = GetComponentInChildren<ShopManager>();
        monsterSpawner = GetComponentInChildren<MonsterSpawner>();
        augmentManager = GetComponentInChildren<AugmentManager>();
    }
    
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void Rpc_InitializePlayer(int id, NetworkObject gridNetworkObject)
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
        Debug.Log($"[Player {playerId}]: gridNetworkObject를 성공적으로 받았습니다. (ID: {gridNetworkObject.Id})");

        // 전달받은 NetworkObject 참조로부터 그리드 게임오브젝트를 가져옵니다.
        GameObject gridInstance = gridNetworkObject.gameObject;

        // --- 중복 등록 경고 해결 ---
        // 1. Grid에 포함된 모든 TilemapController를 찾습니다.
        var tilemapControllers = gridInstance.GetComponentsInChildren<TilemapController>();
        foreach (var controller in tilemapControllers)
        {
            // 2. 각 컨트롤러에 플레이어 ID를 알려주어 고유 ID를 설정하게 합니다.
            controller.SetPlayerOwner(this.playerId);
        }
        // -------------------------

        // --- 2. 그리드 내부의 구성 요소들을 찾고, 각각 성공 여부를 로그로 남깁니다. ---
        // [3D Migration] Tilemap과 3D Ground 모두 지원
        var allTilemaps = gridInstance.GetComponentsInChildren<Tilemap>();
        Tilemap groundTilemap = allTilemaps.FirstOrDefault(t => t.name == "Ground Tilemap");
        Tilemap obstacleTilemap = allTilemaps.FirstOrDefault(t => t.name == "BreakWall Tilemap");
        
        // 3D Ground 오브젝트 찾기 (Tilemap이 없을 경우)
        GameObject ground3D = gridInstance.transform.Find("Ground")?.gameObject;
        if (ground3D == null)
        {
            // 다른 이름도 시도
            ground3D = gridInstance.transform.Find("Field")?.gameObject;
        }
        
        this.astarGrid = gridInstance.GetComponentInChildren<AstarGrid>();
        this.spawnPoint = gridInstance.transform.Find("SpawnPoint");
        this.goalTransform = gridInstance.transform.Find("Goal");

        // 각 컴포넌트/오브젝트를 찾았는지 확인하는 로그
        Debug.Log($"[Player {playerId}]: Ground Tilemap 찾음? -> {(groundTilemap != null)}");
        Debug.Log($"[Player {playerId}]: BreakWall Tilemap 찾음? -> {(obstacleTilemap != null)}");
        Debug.Log($"[Player {playerId}]: 3D Ground 찾음? -> {(ground3D != null)}");
        Debug.Log($"[Player {playerId}]: AstarGrid 찾음? -> {(this.astarGrid != null)}");
        Debug.Log($"[Player {playerId}]: SpawnPoint 찾음? -> {(this.spawnPoint != null)}");
        Debug.Log($"[Player {playerId}]: Goal 찾음? -> {(this.goalTransform != null)}");

        // AstarGrid 초기화
        if (this.astarGrid != null)
        {
            this.astarGrid.Initialize();
        }
        else
        {
            Debug.LogError($"[Player {playerId}]: AstarGrid 컴포넌트를 찾지 못해 경로 탐색을 초기화할 수 없습니다.");
        }

        // 하위 매니저 초기화
        // [3D Migration] 3D Ground가 있으면 3D 모드로, 없으면 2D Tilemap 모드로 초기화
        if (fieldManager)
        {
            if (ground3D != null)
            {
                Debug.Log($"[Player {playerId}]: FieldManager를 3D 모드로 초기화합니다.");
                fieldManager.Initialize(this, ground3D);
            }
            else if (groundTilemap != null && obstacleTilemap != null)
            {
                Debug.Log($"[Player {playerId}]: FieldManager를 2D Tilemap 모드로 초기화합니다.");
                fieldManager.Initialize(this, groundTilemap, obstacleTilemap);
            }
            else
            {
                Debug.LogError($"[Player {playerId}]: FieldManager 초기화 실패 - Ground 오브젝트나 Tilemap을 찾을 수 없습니다!");
            }
        }
        if (shopManager) shopManager.playerManager = this;
        
        if (monsterSpawner)
        {
            var defaultMonsterPrefab = GameManagers.Instance.defaultMonsterPrefab;
            monsterSpawner.Initialize(this, this.astarGrid, defaultMonsterPrefab, this.spawnPoint, this.goalTransform);
        }
        
        if (augmentManager) augmentManager.playerManager = this;
        
        IsActivelyFighting = false;
        Debug.Log($"--- Player {playerId} RPC 초기화 완료 ---");
    }

    // ... (이하 나머지 코드는 기존과 동일) ...

    public void SetFightingState(bool isFighting)
    {
        this.IsActivelyFighting = isFighting;
    }
    
    #region Public Getters & Stat Modifiers

    public int GetHealth() => health;
    public int GetGold() => gold;
    public int GetWallCount() => wallCount;

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

    public void AddGold(int amount)
    {
        if (amount <= 0) return;
        gold += amount;
        GameEvents.TriggerPlayerStatsChanged(playerId, this.health, this.gold);
    }

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

    public void AddUnit(UnitData unitData, int starLevel)
    {
        Debug.Log($"Player {playerId}가 {starLevel}성 {unitData.unitName} 유닛을 획득했습니다.");
        if(fieldManager != null)
        {
            fieldManager.CreateAndPlaceUnitOnField(unitData, starLevel);
        }
    }

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

    public void ReturnWall()
    {
        if (wallCount < MAX_WALL_COUNT)
        {
            wallCount++;
            GameEvents.TriggerPlayerWallCountChanged(playerId, wallCount);
        }
    }

    #endregion
}
