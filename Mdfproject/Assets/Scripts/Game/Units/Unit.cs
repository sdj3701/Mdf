// Assets/Scripts/Game/Units/Unit.cs
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(ManaController))]
public class Unit : MonoBehaviour, IEnemy
{
    [Header("참조 데이터")]
    [SerializeField] private UnitData unitData;
    public UnitData Data => unitData;

    [Header("원거리 유닛 참조")]
    [Tooltip("투사체가 생성될 위치입니다. 유닛 프리팹의 자식 오브젝트를 이 곳에 연결하세요.")]
    public Transform firePoint;

    [Header("수동 스킬 UI")]
    public GameObject skillButtonPrefab;
    public Canvas worldSpaceCanvas; // 월드 스페이스 캔버스 참조

    // --- 현재 상태 및 시스템 컴포넌트 ---
    public int starLevel { get; private set; } = 1;

    [Header("현재 스탯 (읽기 전용)")]
    [SerializeField] private float currentHP;
    [SerializeField] private float currentAttackDamage;
    [SerializeField] private float currentAttackSpeed;
    [SerializeField] private float currentAttackRange;
    [SerializeField] private float currentDefense;
    [SerializeField] private float currentMagicResistance;

    private ManaController manaController;
    private ISkill skillInstance;
    private GameObject skillButtonInstance;

    private Coroutine attackCoroutine;
    [SerializeField] private List<Monster> blockedMonsters = new List<Monster>();
    public LayerMask enemyLayerMask;
    private IEnemy targetEnemy;
    private Transform targetTransform;

    /// <summary>
    /// 유닛이 처음 생성될 때 FieldManager에 의해 호출됩니다.
    /// </summary>
    public void Initialize(UnitData data)
    {
        this.unitData = data;
        this.starLevel = 1; // 처음 생성 시 항상 1성

        manaController = GetComponent<ManaController>();
        manaController.Initialize(unitData.maxMana);

        InitializeStats();
        InitializeSkill();

        StartAttackLoop();
    }

    /// <summary>
    /// 현재 starLevel에 따라 스탯을 계산하고 적용합니다.
    /// </summary>
    public void InitializeStats()
    {
        if (unitData == null) return;
        // 1.8의 (성급-1) 제곱만큼 스탯을 강화합니다. (1성: 1배, 2성: 1.8배, 3성: 3.24배)
        float statMultiplier = Mathf.Pow(1.8f, starLevel - 1);

        currentHP = unitData.baseHealth * statMultiplier;
        currentAttackDamage = unitData.baseAttackDamage * statMultiplier;
        currentAttackSpeed = unitData.attackSpeed;
        currentAttackRange = unitData.attackRange;
        currentDefense = unitData.defense;
        currentMagicResistance = unitData.magicResistance;
    }


    private void InitializeSkill()
    {
        if (skillInstance != null)
        {
            manaController.OnManaFull -= HandleManaFull;
            Destroy((skillInstance as MonoBehaviour).gameObject);
            skillInstance = null;
        }

        if (unitData.skillsByStarLevel != null && unitData.skillsByStarLevel.Length >= starLevel)
        {
            SkillData currentSkillData = unitData.skillsByStarLevel[starLevel - 1];
            if (currentSkillData != null && currentSkillData.skillLogicPrefab != null)
            {
                GameObject skillObject = Instantiate(currentSkillData.skillLogicPrefab, transform);
                skillInstance = skillObject.GetComponent<ISkill>();
                manaController.OnManaFull += HandleManaFull;
            }
        }
    }


    /// <summary>
    /// 현재 starLevel에 맞는 스킬을 UnitData에서 가져와 설정합니다.
    /// </summary>
    public void Initialize(UnitData data, int initialStarLevel)
    {
        this.unitData = data;
        this.starLevel = initialStarLevel; // [변경됨] 1로 고정하지 않고, 받은 값으로 설정

        manaController = GetComponent<ManaController>();
        manaController.Initialize(unitData.maxMana);

        InitializeStats();
        InitializeSkill();

        StartAttackLoop();
    }

    /// <summary>
    /// FieldManager에 의해 호출되어 유닛을 업그레이드합니다.
    /// </summary>
    public void Upgrade()
    {
        if (starLevel < 3)
        {
            starLevel++;
            InitializeStats(); // 새로운 성급에 맞게 스탯 재계산
            InitializeSkill(); // 새로운 성급에 맞는 스킬로 교체

            Debug.Log($"<color=cyan>{unitData.unitName}이(가) {starLevel}성으로 업그레이드되었습니다!</color>");
            // 참고: 실제 외형(프리팹) 교체는 이 유닛을 관리하는 FieldManager에서 담당해야 합니다.
        }
    }

    private void HandleManaFull()
    {
        // UnitData의 배열에서 현재 성급에 맞는 스킬 데이터를 가져옵니다.
        SkillData currentSkillData = unitData.skillsByStarLevel[starLevel - 1];
        if (currentSkillData == null) return;

        if (currentSkillData.activationType == SkillActivationType.Automatic)
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
        SkillData currentSkillData = unitData.skillsByStarLevel[starLevel - 1];
        if (skillInstance == null || !manaController.IsManaFull || currentSkillData == null) return;

        if (manaController.UseMana(currentSkillData.manaCost))
        {
            skillInstance.Activate(this.gameObject);
            HideSkillButton();
        }
    }

    #region 공격 로직 (수정됨)
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
        // (기존 코드와 동일)
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

    /// <summary>
    /// 유닛 타입(근접/원거리)에 따라 다른 방식으로 공격합니다.
    /// </summary>
    private void Attack()
    {
        if (targetEnemy == null || targetTransform == null || Vector2.Distance(transform.position, targetTransform.position) > currentAttackRange)
        {
            targetEnemy = null;
            return;
        }

        // 유닛 타입에 따라 공격 방식 분기
        if (unitData.unitType == UnitType.Melee)
        {
            // 근접 유닛: 즉시 데미지 적용
            targetEnemy.TakeDamage(currentAttackDamage, unitData.damageType);
        }
        else if (unitData.unitType == UnitType.Ranged)
        {
            // 원거리 유닛: 투사체 발사
            // UnitData의 projectilePrefabsByStarLevel 배열이 유효한지 확인
            if (unitData.projectilePrefabsByStarLevel != null && unitData.projectilePrefabsByStarLevel.Length >= starLevel)
            {
                // 현재 성급에 맞는 투사체 프리팹을 가져옴
                GameObject projectilePrefab = unitData.projectilePrefabsByStarLevel[starLevel - 1];

                if (projectilePrefab != null && firePoint != null)
                {
                    // 투사체 생성
                    GameObject projectileGO = Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
                    
                    // TODO: 생성된 투사체 스크립트에 목표(targetTransform)와 데미지(currentAttackDamage) 정보를 전달해야 합니다.
                    // 예: projectileGO.GetComponent<Projectile>().Initialize(targetTransform, currentAttackDamage, unitData.damageType);
                }
            }
        }

        // 공격 시 마나 획득
        if (skillInstance != null)
        {
            manaController.GainMana(15);
        }
    }
    #endregion

    #region 저지, 스킬 UI, IEnemy 구현 등 (기존 코드와 대부분 동일)

    // ... (ShowSkillButton, HideSkillButton 메서드는 기존 코드와 동일) ...
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


    // ... (OnTriggerEnter2D, ReleaseBlockedMonster 메서드는 기존 코드와 동일) ...
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.TryGetComponent<Monster>(out var monster))
        {
            if (blockedMonsters.Contains(monster) || monster.IsBlocked() || 
                monster.monsterData.monsterType == MonsterType.Flying || Data.blockCount <= 0 || 
                blockedMonsters.Count >= Data.blockCount)
            {
                return;
            }
            blockedMonsters.Add(monster);
            monster.Block(this);
        }
    }
    public void ReleaseBlockedMonster(Monster monster)
    {
        if (blockedMonsters.Contains(monster))
        {
            blockedMonsters.Remove(monster);
        }
    }

    // ... (OnDestroy 메서드는 기존 코드와 동일) ...
    private void OnDestroy()
    {
        if (manaController != null) manaController.OnManaFull -= HandleManaFull;
        if (skillButtonInstance != null) Destroy(skillButtonInstance);
    }

    // ... (IEnemy 인터페이스 구현부: TakeDamage, Die 메서드는 기존 코드와 동일) ...
    public void TakeDamage(float baseDamage, DamageType damageType)
    {
        if (unitData == null) return;
        int finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, currentDefense, currentMagicResistance);
        currentHP -= finalDamage;
        if (currentHP <= 0)
        {
            Die();
        }
    }
    private void Die()
    {
        foreach (var monster in blockedMonsters)
        {
            if (monster != null)
            {
                monster.Unblock();
            }
        }
        blockedMonsters.Clear();
        // TODO: FieldManager에게 유닛이 죽었음을 알려서 관리 목록에서 제거하도록 해야 합니다.
        // 예: FindObjectOfType<FieldManager>().UnitDied(this.gameObject);
        Destroy(gameObject);
    }

    #endregion
}