// Assets/Scripts/Game/Game Rules/DestructibleWall.cs
using UnityEngine;
using UnityEngine.Tilemaps;

// [RequireComponent(typeof(TilemapCollider2D))] // ??? ?? ??? ???? ??
public class DestructibleWall : MonoBehaviour, IEnemy
{
    [Header("? ??")]
    [SerializeField] private int maxHealth = 200;
    private int currentHealth;

    // TODO: ?? ???/?????? ??? ? ????.
    [SerializeField] private float defense = 10f;
    [SerializeField] private float magicResistance = 0f;

    private Tilemap wallTilemap;
    private Vector3Int wallGridPosition;

    void Start()
    {
        currentHealth = maxHealth;
        // ? ????? ???? ???? ??? ??? ????.
        wallTilemap = GetComponentInParent<Tilemap>();
        if (wallTilemap != null)
        {
            wallGridPosition = wallTilemap.WorldToCell(transform.position);
        }
    }

    /// <summary>
    /// IEnemy ?????? ??? ??? ?? TakeDamage ??? ??
    /// </summary>
    public void TakeDamage(float baseDamage, DamageType damageType)
    {
        // ?? ??? ???? ?? ?? ??? ??? ??? ??????.
        int finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, defense, magicResistance);
        
        currentHealth -= finalDamage;
        Debug.Log($"?? {finalDamage}? ??? ?????! (?? ??: {currentHealth}/{maxHealth})");

        if (currentHealth <= 0)
        {
            DestroyWall();
        }
    }

    private void DestroyWall()
    {
        Debug.Log("?? ???????!");
        
        // ????? ?? ??? ?????.
        if (wallTilemap != null)
        {
            wallTilemap.SetTile(wallGridPosition, null);
        }
        
        // ?? ???? ???? ??? ? ????.
        // Instantiate(destructionEffect, transform.position, Quaternion.identity);

        // ? ??????? ? ?? ?? ???? ?????.
        Destroy(gameObject);
    }
}