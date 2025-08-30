// Assets/Scripts/Game/Game Rules/DestructibleWall.cs
using UnityEngine;
using UnityEngine.Tilemaps;

public class DestructibleWall : MonoBehaviour, IEnemy
{
    [Header("벽 스탯")]
    [SerializeField] private int maxHealth = 200;
    private int currentHealth;

    [SerializeField] private float defense = 10f;
    [SerializeField] private float magicResistance = 0f;
    
    private Vector3Int wallGridPosition;
    private FieldManager fieldManager;

    // [추가됨] FieldManager가 이 벽을 초기화할 때 호출해 줄 메소드
    public void Initialize(FieldManager manager, Vector3Int gridPosition)
    {
        this.fieldManager = manager;
        this.wallGridPosition = gridPosition;
        this.currentHealth = maxHealth;
    }

    public void TakeDamage(float baseDamage, DamageType damageType)
    {
        int finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, defense, magicResistance);
        currentHealth -= finalDamage;

        if (currentHealth <= 0)
        {
            if (fieldManager != null)
            {
                // FieldManager에게 자신의 파괴를 알려 오브젝트와 타일맵 그래픽을 모두 제거합니다.
                fieldManager.RemoveWallAt(wallGridPosition);
            }
            else
            {
                // FieldManager가 할당되지 않은 비정상적인 경우, 게임 오브젝트만이라도 제거합니다.
                // 이 경우 사용자가 겪는 문제처럼 타일맵의 스프라이트는 그대로 남게 됩니다.
                Debug.LogError("DestructibleWall에 FieldManager가 할당되지 않아 타일맵을 수정할 수 없습니다! 이 벽이 FieldManager를 통해 생성되었는지 확인하세요.", this);
                // Tilemap과 같은 중요 오브젝트가 실수로 파괴되는 것을 방지하기 위해 아래 코드를 주석 처리합니다.
                // Destroy(gameObject);
            }
        }
    }
}
