// Assets/Scripts/Managers/PlayerManager.cs
using System.Collections.Generic;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    [Header("플레이어 식별 정보")]
    public int playerId; // 0번 또는 1번

    [Header("핵심 능력치")]
    [SerializeField] private int health = 100;
    [SerializeField] private int gold = 10;

    [Header("소유 객체 목록")]
    public List<Unit> ownedUnits = new List<Unit>(); // 필드와 벤치를 포함한 모든 유닛
    public List<AugmentData> chosenAugments = new List<AugmentData>(); // 현재까지 선택한 모든 증강

    [Header("하위 매니저 참조 (자동 할당)")]
    public FieldManager fieldManager;
    public ShopManager shopManager;
    public MonsterSpawner monsterSpawner;
    public AugmentManager augmentManager;
    
    [HideInInspector]
    public PlayerManager opponentManager; // 상대 플레이어 매니저 (GameManager가 할당)

    void Awake()
    {
        // --- 자동 할당 로직 ---
        // 자신의 자식 오브젝트들 중에서 필요한 매니저 컴포넌트를 찾습니다.
        fieldManager = GetComponentInChildren<FieldManager>();
        shopManager = GetComponentInChildren<ShopManager>();
        monsterSpawner = GetComponentInChildren<MonsterSpawner>();
        augmentManager = GetComponentInChildren<AugmentManager>();

        // 각 하위 매니저들에게 자신(PlayerManager)의 참조를 넘겨줍니다.
        // 참조 할당이 성공했는지 확인하는 방어 코드도 추가합니다.
        if (fieldManager) 
        {
            fieldManager.playerManager = this;
        }
        else 
        {
            Debug.LogError($"Player {playerId}에서 FieldManager를 찾을 수 없습니다!", gameObject);
        }

        if (shopManager) 
        {
            shopManager.playerManager = this;
        }
        else 
        {
            Debug.LogError($"Player {playerId}에서 ShopManager를 찾을 수 없습니다!", gameObject);
        }

        if (monsterSpawner) 
        {
            monsterSpawner.playerManager = this;
        }
        else 
        {
            Debug.LogError($"Player {playerId}에서 MonsterSpawner를 찾을 수 없습니다!", gameObject);
        }

        if (augmentManager) 
        {
            augmentManager.playerManager = this;
        }
        else 
        {
            Debug.LogError($"Player {playerId}에서 AugmentManager를 찾을 수 없습니다!", gameObject);
        }
    }
    
    public int GetHealth() => health;
    public int GetGold() => gold;

    public bool SpendGold(int amount)
    {
        if (gold >= amount)
        {
            gold -= amount;
            Debug.Log($"Player {playerId}: 골드 {amount} 사용. 남은 골드: {gold}");
            // TODO: 골드 UI 업데이트
            return true;
        }
        else
        {
            Debug.LogWarning($"Player {playerId}: 골드가 부족합니다! (보유: {gold}, 필요: {amount})");
            return false;
        }
    }

    public void AddGold(int amount)
    {
        if (amount <= 0) return;
        gold += amount;
        Debug.Log($"Player {playerId}: 골드 {amount} 획득. 현재 골드: {gold}");
        // TODO: 골드 UI 업데이트
    }

    public void TakeDamage(int damage)
    {
        health -= damage;
        Debug.Log($"Player {playerId}: 피해 {damage} 받음. 남은 체력: {health}");
        // TODO: 체력 UI 업데이트

        if (health <= 0)
        {
            health = 0;
            // GameManager가 null이 아닐 때만 호출하도록 안전장치 추가
            if (GameManagers.Instance != null)
            {
                GameManagers.Instance.GameOver(this);
            }
        }
    }

    public void AddUnit(UnitData unitData)
    {
        // TODO: 유닛 생성 및 벤치에 추가하는 로직
        // GameObject unitGO = Instantiate(unitData.unitPrefab, benchPosition, Quaternion.identity);
        // Unit newUnit = unitGO.GetComponent<Unit>();
        // newUnit.unitData = unitData;
        // ownedUnits.Add(newUnit);
        Debug.Log($"Player {playerId}가 {unitData.unitName} 유닛을 획득했습니다.");
    }
}