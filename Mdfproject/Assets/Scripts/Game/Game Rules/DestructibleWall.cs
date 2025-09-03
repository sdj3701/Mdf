// Assets/Scripts/Game/Game Rules/DestructibleWall.cs (수정된 버전)
using UnityEngine;
using UnityEngine.Tilemaps;

public class DestructibleWall : MonoBehaviour, IEnemy, IHealth
{
    [Header("벽 스탯")]
    [SerializeField] private float maxHealth = 200f;
    private float currentHealth;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public event System.Action<float, float> OnHealthChanged;

    [SerializeField] private float defense = 10f;
    [SerializeField] private float magicResistance = 0f;
    
    private Vector3Int wallGridPosition;
    private FieldManager fieldManager;

    public void Initialize(FieldManager manager, Vector3Int gridPosition)
    {
        this.fieldManager = manager;
        this.wallGridPosition = gridPosition;
        this.currentHealth = maxHealth;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void TakeDamage(float baseDamage, DamageType damageType)
    {
        int finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, defense, magicResistance);
        currentHealth -= finalDamage;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0)
        {
            if (fieldManager != null)
            {
                fieldManager.RemoveWallAt(wallGridPosition);
            }
            else
            {
                Debug.LogError("DestructibleWall에 FieldManager가 할당되지 않아 타일맵을 수정할 수 없습니다!", this);
            }
        }
    }

    /// <summary>
    /// [새로 추가됨] IHealth 인터페이스를 만족시키기 위한 Heal 메서드입니다.
    /// 벽의 체력을 회복시킵니다.
    /// </summary>
    /// <param name="amount">회복할 체력의 양</param>
    public void Heal(float amount)
    {
        // 이미 파괴되었거나 회복량이 0 이하면 아무것도 하지 않습니다.
        if (currentHealth <= 0 || amount <= 0) return;

        // 최대 체력을 넘지 않도록 체력을 회복합니다.
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        
        // 체력 바 UI 등을 업데이트하기 위해 이벤트를 호출합니다.
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }
}