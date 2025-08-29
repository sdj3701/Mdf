// Assets/Scripts/Managers/PlayerManager.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Linq; // ✅ [추가] FirstOrDefault를 사용하기 위해 반드시 필요합니다.

public class PlayerManager : MonoBehaviour
{
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
    
    [HideInInspector]
    public PlayerManager opponentManager;
    
    public bool IsActivelyFighting { get; private set; }

    void Awake()
    {
        fieldManager = GetComponentInChildren<FieldManager>();
        shopManager = GetComponentInChildren<ShopManager>();
        monsterSpawner = GetComponentInChildren<MonsterSpawner>();
        augmentManager = GetComponentInChildren<AugmentManager>();
    }
    
    public void InitializePlayer(int id, GameObject gridInstance)
    {
        this.playerId = id;

        var allTilemaps = gridInstance.GetComponentsInChildren<Tilemap>();
        Tilemap groundTilemap = allTilemaps.FirstOrDefault(t => t.name == "Ground Tilemap");
        Tilemap obstacleTilemap = allTilemaps.FirstOrDefault(t => t.name == "BreakWall Tilemap");
        AstarGrid astarGrid = gridInstance.GetComponentInChildren<AstarGrid>();

        if (fieldManager) fieldManager.Initialize(this, groundTilemap, obstacleTilemap);
        if (shopManager) shopManager.playerManager = this;
        if (monsterSpawner) monsterSpawner.Initialize(this, astarGrid);
        if (augmentManager) augmentManager.playerManager = this;
        
        IsActivelyFighting = false;
    }

    void OnEnable()
    {
        GameEvents.OnUnitPurchased += HandleUnitPurchaseRequest;
    }

    void OnDisable()
    {
        GameEvents.OnUnitPurchased -= HandleUnitPurchaseRequest;
    }

    private void HandleUnitPurchaseRequest(PlayerManager purchasingPlayer, UnitData unitData, int starLevel)
    {
        if (purchasingPlayer.playerId != this.playerId) return;

        ShopItem item = new ShopItem(unitData, starLevel);
    
        if (SpendGold(item.CalculatedCost))
        {
            AddUnit(unitData, starLevel);
        }
        else
        {
            Debug.Log($"Player {playerId}: 골드가 부족하여 {unitData.unitName} 구매에 실패했습니다.");
        }
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