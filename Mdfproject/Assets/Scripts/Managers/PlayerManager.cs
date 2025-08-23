// Assets/Scripts/Managers/PlayerManager.cs
using System.Collections.Generic;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    [Header("플레이어 식별 정보")]
    public int playerId;

    [Header("핵심 능력치")]
    [SerializeField] private int health = 100;
    [SerializeField] private int gold = 10;
    [SerializeField] private int wallCount = 5; // GameDataCenter에서 이동: 벽 보유 개수
    private const int MAX_WALL_COUNT = 5; // 벽 최대 보유량

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

    void Awake()
    {
        // --- 기존 자동 할당 로직 ---
        fieldManager = GetComponentInChildren<FieldManager>();
        shopManager = GetComponentInChildren<ShopManager>();
        monsterSpawner = GetComponentInChildren<MonsterSpawner>();
        augmentManager = GetComponentInChildren<AugmentManager>();

        if (fieldManager) fieldManager.playerManager = this;
        else Debug.LogError($"Player {playerId}에서 FieldManager를 찾을 수 없습니다!", gameObject);

        if (shopManager) shopManager.playerManager = this;
        else Debug.LogError($"Player {playerId}에서 ShopManager를 찾을 수 없습니다!", gameObject);

        if (monsterSpawner) monsterSpawner.playerManager = this;
        else Debug.LogError($"Player {playerId}에서 MonsterSpawner를 찾을 수 없습니다!", gameObject);

        if (augmentManager) augmentManager.playerManager = this;
        else Debug.LogError($"Player {playerId}에서 AugmentManager를 찾을 수 없습니다!", gameObject);
    }
    
    public int GetHealth() => health;
    public int GetGold() => gold;
    public int GetWallCount() => wallCount; // 현재 벽 개수 반환

    public bool SpendGold(int amount)
    {
        if (gold >= amount)
        {
            gold -= amount;
            // TODO: 골드 UI 업데이트
            return true;
        }
        return false;
    }

    public void AddGold(int amount)
    {
        if (amount <= 0) return;
        gold += amount;
        // TODO: 골드 UI 업데이트
    }

    public void TakeDamage(int damage)
    {
        health -= damage;
        if (health <= 0)
        {
            health = 0;
            if (GameManagers.Instance != null)
            {
                GameManagers.Instance.GameOver(this);
            }
        }
    }

    public void AddUnit(UnitData unitData, int starLevel)
    {
        Debug.Log($"Player {playerId}가 {starLevel}성 {unitData.unitName} 유닛을 획득했습니다.");
        if(fieldManager != null)
        {
            // FieldManager의 새 메서드 호출
            fieldManager.CreateAndPlaceUnitOnField(unitData, starLevel);
        }
    }

    /// <summary>
    /// 벽을 하나 사용합니다. 성공적으로 사용했는지 여부를 반환합니다.
    /// </summary>
    public bool TryUseWall()
    {
        if (wallCount > 0)
        {
            wallCount--;
            // TODO: 벽 개수 UI 업데이트
            Debug.Log($"벽 사용. 남은 개수: {wallCount}");
            return true;
        }
        Debug.LogWarning("벽이 부족하여 사용할 수 없습니다.");
        return false;
    }

    /// <summary>
    /// 제거된 벽을 다시 개수에 추가합니다.
    /// </summary>
    public void ReturnWall()
    {
        if (wallCount < MAX_WALL_COUNT)
        {
            wallCount++;
            // TODO: 벽 개수 UI 업데이트
            Debug.Log($"벽 반환. 현재 개수: {wallCount}");
        }
    }
}