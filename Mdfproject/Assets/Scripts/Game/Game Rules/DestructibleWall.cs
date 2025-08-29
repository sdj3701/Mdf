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
            // [수정됨] FieldManager에게 자신의 파괴를 알립니다.
            fieldManager.RemoveWallAt(wallGridPosition);
        }
    }
}