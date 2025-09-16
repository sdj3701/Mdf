// Assets/Scripts/Managers/PlayerManager.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Linq;

public class PlayerManager : MonoBehaviour
{
    // ... (변수 선언은 동일) ...
    [Header("플레이어 식별 정보")]
    public int playerId;

    [Header("핵심 능력치 (읽기 전용)")]
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
        fieldManager = GetComponentInChildren<FieldManager>();
        shopManager = GetComponentInChildren<ShopManager>();
        monsterSpawner = GetComponentInChildren<MonsterSpawner>();
        augmentManager = GetComponentInChildren<AugmentManager>();

        // ✅ [진단 코드] Awake에서 MonsterSpawner를 찾았는지 확인
        if (monsterSpawner == null)
        {
            Debug.LogError($"PlayerManager '{gameObject.name}'의 자식에서 MonsterSpawner를 찾지 못했습니다!", gameObject);
        }
    }
    
    public void InitializePlayer(int id, GameObject gridInstance, GameObject monsterPrefab)
    {
        this.playerId = id;
        Debug.Log($"--- Player {id} 초기화 시작 ---");

        // ✅ [핵심 수정] Grid 내의 TilemapController들이 고유 ID를 갖도록 재등록합니다.
        // 이렇게 하면 "중복 등록" 경고가 해결됩니다.
        var allTilemapControllers = gridInstance.GetComponentsInChildren<TilemapController>();
        foreach (var controller in allTilemapControllers)
        {
            controller.UnregisterSelf(); // Awake에서 등록된 기본 ID를 해제합니다.
            // 플레이어 ID와 TilemapController의 Type을 조합하여 고유 ID를 새로 만듭니다. (예: "Player0_Ground")
            controller.componentId = $"Player{this.playerId}_{controller.Type}";
            controller.RegisterSelf(); // 새로운 고유 ID로 다시 등록합니다.
        }

        // ✅ [진단 코드] 전달받은 참조들이 null이 아닌지 하나씩 확인
        if (gridInstance == null) Debug.LogError($"Player {id}: 전달받은 gridInstance가 null입니다!");
        if (monsterPrefab == null) Debug.LogError($"Player {id}: 전달받은 monsterPrefab이 null입니다!");

        var allTilemaps = gridInstance.GetComponentsInChildren<Tilemap>();
        Tilemap groundTilemap = allTilemaps.FirstOrDefault(t => t.name == "Ground Tilemap");
        Tilemap obstacleTilemap = allTilemaps.FirstOrDefault(t => t.name == "BreakWall Tilemap");
        this.astarGrid = gridInstance.GetComponentInChildren<AstarGrid>();
        if (this.astarGrid != null)
        {
            this.astarGrid.Initialize(); // 그리드의 월드 좌표를 현재 위치 기준으로 설정합니다.
        }
        this.spawnPoint = gridInstance.transform.Find("SpawnPoint");
        this.goalTransform = gridInstance.transform.Find("Goal");

        // ✅ [진단 코드] Grid 프리팹 내부에서 컴포넌트를 제대로 찾았는지 확인
        if (this.astarGrid == null) Debug.LogError($"Player {id}: Grid 프리팹에서 AstarGrid 컴по넌트를 찾지 못했습니다!");
        if (this.spawnPoint == null) Debug.LogError($"Player {id}: Grid 프리팹에서 'SpawnPoint' 자식 오브젝트를 찾지 못했습니다!");
        if (this.goalTransform == null) Debug.LogError($"Player {id}: Grid 프리팹에서 'Goal' 자식 오브젝트를 찾지 못했습니다!");

        if (fieldManager) fieldManager.Initialize(this, groundTilemap, obstacleTilemap);
        if (shopManager) shopManager.playerManager = this;
        
        if (monsterSpawner)
        {
            Debug.Log($"Player {id}: MonsterSpawner에게 참조 전달 시도...");
            monsterSpawner.Initialize(this, this.astarGrid, monsterPrefab, this.spawnPoint, this.goalTransform);
        }
        else
        {
            Debug.LogError($"Player {id}: monsterSpawner 참조가 null이라서 Initialize를 호출할 수 없습니다!");
        }

        if (augmentManager) augmentManager.playerManager = this;
        
        IsActivelyFighting = false;
        Debug.Log($"--- Player {id} 초기화 완료 ---");
    }

    // ... (이하 나머지 코드는 이전과 동일) ...
    void OnEnable()
    {
    }

    void OnDisable()
    {
    }

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
        Debug.LogWarning("벽이 부족하여 사용할 수 없습니다.");
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
