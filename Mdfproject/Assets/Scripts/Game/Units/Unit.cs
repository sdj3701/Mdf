// Assets/Scripts/Game/Units/Unit.cs
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(ManaController))]
public class Unit : MonoBehaviour
{
    [Header("참조 데이터")]
    [SerializeField] private UnitData unitData;
    public UnitData Data => unitData;
    
    [Header("수동 스킬 UI")]
    public GameObject skillButtonPrefab;
    public Canvas worldSpaceCanvas;

    // --- 현재 상태 및 시스템 컴포넌트 ---
    public int starLevel = 1;
    private float currentHP;
    private float currentAttackDamage;
    private float currentAttackSpeed;
    private float currentAttackRange;
    private float currentDefense;
    private float currentMagicResistance;
    
    private ManaController manaController;
    private ISkill skillInstance;
    private GameObject skillButtonInstance;
    
    private Coroutine attackCoroutine;
    private List<Monster> blockedMonsters = new List<Monster>();
    public LayerMask enemyLayerMask;
    private IEnemy targetEnemy;
    private Transform targetTransform;

    public void Initialize(UnitData data)
    {
        this.unitData = data;
        InitializeStats();

        manaController = GetComponent<ManaController>();
        manaController.Initialize(unitData.maxMana);

        if (unitData.skillData != null && unitData.skillData.skillLogicPrefab != null)
        {
            GameObject skillObject = Instantiate(unitData.skillData.skillLogicPrefab, transform);
            skillInstance = skillObject.GetComponent<ISkill>();
            manaController.OnManaFull += HandleManaFull;
        }

        StartAttackLoop();
    }
    
    public void InitializeStats()
    {
        if (unitData == null) return;
        float statMultiplier = Mathf.Pow(1.8f, starLevel - 1);
        currentHP = unitData.baseHealth * statMultiplier;
        currentAttackDamage = unitData.baseAttackDamage * statMultiplier;
        currentAttackSpeed = unitData.attackSpeed;
        currentAttackRange = unitData.attackRange;
        currentDefense = unitData.defense;
        currentMagicResistance = unitData.magicResistance;
    }
    
    // ✅ [수정] 누락되었던 메서드를 다시 추가했습니다.
    public void Upgrade()
    {
        if (starLevel < 3)
        {
            starLevel++;
            InitializeStats();
            Debug.Log($"{unitData.unitName}이(가) {starLevel}성으로 업그레이드되었습니다!");
        }
    }
    
    private void HandleManaFull()
    {
        if (unitData.skillData.activationType == SkillActivationType.Automatic)
        {
            ActivateSkill();
        }
        else
        {
            ShowSkillButton();
        }
    }

    public void ActivateSkill()
    {
        if (skillInstance == null || !manaController.IsManaFull) return;

        if (manaController.UseMana(unitData.skillData.manaCost))
        {
            skillInstance.Activate(this.gameObject);
            HideSkillButton();
        }
    }
    
    private void ShowSkillButton()
    {
        if (skillButtonPrefab == null || worldSpaceCanvas == null) return;
        if (skillButtonInstance == null)
        {
            skillButtonInstance = Instantiate(skillButtonPrefab, worldSpaceCanvas.transform);
            skillButtonInstance.GetComponent<Button>().onClick.AddListener(ActivateSkill);
        }
        skillButtonInstance.transform.position = transform.position + Vector3.up * 1.5f;
        skillButtonInstance.SetActive(true);
    }

    private void HideSkillButton()
    {
        if (skillButtonInstance != null)
        {
            skillButtonInstance.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (manaController != null) manaController.OnManaFull -= HandleManaFull;
        if (skillButtonInstance != null) Destroy(skillButtonInstance);
    }
    
    #region 공격 및 저지 로직 (완전 복구)
    public void StartAttackLoop()
    {
        if (attackCoroutine != null) StopCoroutine(attackCoroutine);
        attackCoroutine = StartCoroutine(AttackLoop());
    }
    private IEnumerator AttackLoop()
    {
        while (true)
        {
            FindNearestEnemy();
            if (targetEnemy != null)
            {
                Attack();
            }
            yield return new WaitForSeconds(1f / currentAttackSpeed);
        }
    }
    private void FindNearestEnemy()
    {
        Collider2D[] enemiesInRange = Physics2D.OverlapCircleAll(transform.position, currentAttackRange, enemyLayerMask);
        float closestDistanceSqr = float.MaxValue;
        IEnemy nearestEnemy = null;
        Transform nearestTransform = null;
        foreach (var enemyCollider in enemiesInRange)
        {
            if (enemyCollider.TryGetComponent<IEnemy>(out var enemy) && enemyCollider.TryGetComponent<Monster>(out var monster))
            {
                if (unitData.unitType == UnitType.Melee && monster.monsterData.monsterType == MonsterType.Flying)
                {
                    continue;
                }
                float distanceSqr = (transform.position - enemyCollider.transform.position).sqrMagnitude;
                if (distanceSqr < closestDistanceSqr)
                {
                    closestDistanceSqr = distanceSqr;
                    nearestEnemy = enemy;
                    nearestTransform = enemyCollider.transform;
                }
            }
        }
        targetEnemy = nearestEnemy;
        targetTransform = nearestTransform;
    }
    private void Attack()
    {
        if (targetEnemy == null || targetTransform == null || Vector2.Distance(transform.position, targetTransform.position) > currentAttackRange)
        {
            targetEnemy = null;
            return;
        }
        targetEnemy.TakeDamage(currentAttackDamage, unitData.damageType);
        if (skillInstance != null)
        {
            manaController.GainMana(15);
        }
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.TryGetComponent<Monster>(out var monster))
        {
            if (blockedMonsters.Contains(monster) || monster.IsBlocked() || monster.monsterData.monsterType == MonsterType.Flying)
            {
                return;
            }
            if (Data.blockCount > 0 && blockedMonsters.Count < Data.blockCount)
            {
                blockedMonsters.Add(monster);
                monster.Block(this);
            }
        }
    }
    public void ReleaseBlockedMonster(Monster monster)
    {
        if (blockedMonsters.Contains(monster))
        {
            blockedMonsters.Remove(monster);
        }
    }
    #endregion
}