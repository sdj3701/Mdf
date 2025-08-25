// Assets/Scripts/Managers/PlayerManager.cs
using System.Collections.Generic;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    [Header("플레이어 식별 정보")]
    public int playerId;

    [Header("핵심 능력치 (읽기 전용)")]
    // 이 값들은 이제 GameManagers가 게임 시작 시 설정해줍니다.
    [SerializeField] private int health;
    [SerializeField] private int gold;
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
    
    /// <summary>
    /// 이 플레이어의 필드에 몬스터가 남아있어 실제 교전 중인지 여부를 나타냅니다.
    /// </summary>
    public bool IsActivelyFighting { get; private set; }

    void Awake()
    {
        // 자신의 하위에 있는 매니저들을 자동으로 찾아 할당합니다.
        fieldManager = GetComponentInChildren<FieldManager>();
        shopManager = GetComponentInChildren<ShopManager>();
        monsterSpawner = GetComponentInChildren<MonsterSpawner>();
        augmentManager = GetComponentInChildren<AugmentManager>();

        // 각 하위 매니저에게 자신(PlayerManager)의 참조를 넘겨줍니다.
        if (fieldManager) fieldManager.playerManager = this;
        else Debug.LogError($"Player {playerId}에서 FieldManager를 찾을 수 없습니다!", gameObject);

        if (shopManager) shopManager.playerManager = this;
        else Debug.LogError($"Player {playerId}에서 ShopManager를 찾을 수 없습니다!", gameObject);

        if (monsterSpawner) monsterSpawner.playerManager = this;
        else Debug.LogError($"Player {playerId}에서 MonsterSpawner를 찾을 수 없습니다!", gameObject);

        if (augmentManager) augmentManager.playerManager = this;
        else Debug.LogError($"Player {playerId}에서 AugmentManager를 찾을 수 없습니다!", gameObject);
        
        // 게임 시작 시 전투 상태를 false로 초기화합니다.
        IsActivelyFighting = false;
    }

    /// <summary>
    /// GameManagers가 호출하여 이 플레이어의 초기 스탯을 설정하는 메서드입니다.
    /// </summary>
    public void InitializeStats(int startHealth, int startGold)
    {
        this.health = startHealth;
        this.gold = startGold;
    }

    /// <summary>
    /// MonsterSpawner가 호출하여 이 플레이어의 실제 전투 상태를 갱신합니다.
    /// </summary>
    public void SetFightingState(bool isFighting)
    {
        this.IsActivelyFighting = isFighting;
    }
    
    #region Public Getters & Setters

    public int GetHealth() => health;
    public int GetGold() => gold;
    public int GetWallCount() => wallCount;

    public bool SpendGold(int amount)
    {
        if (gold >= amount)
        {
            gold -= amount;
            // TODO: 골드 변경 시 UI 업데이트 이벤트 호출
            return true;
        }
        return false;
    }

    public void AddGold(int amount)
    {
        if (amount <= 0) return;
        gold += amount;
        // TODO: 골드 변경 시 UI 업데이트 이벤트 호출
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
        // TODO: 체력 변경 시 UI 업데이트 이벤트 호출
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
            Debug.Log($"벽 사용. 남은 개수: {wallCount}");
            // TODO: 벽 개수 변경 시 UI 업데이트 이벤트 호출
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
            Debug.Log($"벽 반환. 현재 개수: {wallCount}");
            // TODO: 벽 개수 변경 시 UI 업데이트 이벤트 호출
        }
    }

    #endregion
}