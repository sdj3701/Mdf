using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New UnitData", menuName = "Game/Unit Data")]
public class UnitData : ScriptableObject
{
    public string unitName;
    public GameObject unitPrefab;
    public Sprite unitIcon;
    public int cost;

    [Header("Stats")]
    public float baseHealth;
    public float baseAttackDamage;
    public float attackSpeed; // 초당 공격 횟수
    public float attackRange;

    public enum UnitType { Melee, Ranged }
    public UnitType unitType;
}