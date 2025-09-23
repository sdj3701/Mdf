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
    
    // [수정] 기존 InitializePlayer 메서드를 RPC(Remote Procedure Call)로 변경합니다.
    // 이 RPC는 호스트가 호출하며, 모든 클라이언트에서 실행됩니다.
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void Rpc_InitializePlayer(int id, NetworkObject gridNetworkObject)
    {
        // 네트워크를 통해 전달받은 ID를 [Networked] 프로퍼티에 저장합니다.
        this.playerId = id;
        Debug.Log($"--- Player {playerId} RPC 초기화 실행 ---");

        // 전달받은 NetworkObject 참조로부터 그리드 게임오브젝트를 가져옵니다.
        GameObject gridInstance = gridNetworkObject.gameObject;

        // --- 기존 InitializePlayer의 로직을 이곳으로 옮깁니다. ---
        var allTilemaps = gridInstance.GetComponentsInChildren<Tilemap>();
        Tilemap groundTilemap = allTilemaps.FirstOrDefault(t => t.name == "Ground Tilemap");
        Tilemap obstacleTilemap = allTilemaps.FirstOrDefault(t => t.name == "BreakWall Tilemap");
        this.astarGrid = gridInstance.GetComponentInChildren<AstarGrid>();
        if (this.astarGrid != null)
        {
            this.astarGrid.Initialize();
        }
        this.spawnPoint = gridInstance.transform.Find("SpawnPoint");
        this.goalTransform = gridInstance.transform.Find("Goal");

        if (fieldManager) fieldManager.Initialize(this, groundTilemap, obstacleTilemap);
        if (shopManager) shopManager.playerManager = this;
        
        if (monsterSpawner)
        {
            // defaultMonsterPrefab은 GameManagers가 가지고 있으므로, 거기서 참조를 가져옵니다.
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