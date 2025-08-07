// Assets/Scripts/Game/Units/UnitData.cs
using UnityEngine;

public enum UnitType { Melee, Ranged }

[CreateAssetMenu(fileName = "New UnitData", menuName = "Game/Unit Data")]
public class UnitData : ScriptableObject
{
    [Header("기본 정보")]
    public string unitName;
    public GameObject unitPrefab;
    public Sprite unitIcon;
    public int cost;
    public UnitType unitType;

    [Header("기본 스탯 (1성 기준)")]
    public float baseHealth;
    public float baseAttackDamage;
    public float attackSpeed; // 초당 공격 횟수
    public float attackRange;
}