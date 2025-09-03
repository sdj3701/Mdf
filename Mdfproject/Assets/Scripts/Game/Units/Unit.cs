// Assets/Scripts/Game/Units/Unit.cs
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class Unit : MonoBehaviour, IEnemy, IHealth
{
    [Header("참조 데이터")]
    [SerializeField] private UnitData unitData;
    public UnitData Data => unitData;

    [Header("원거리 유닛 참조")]
    public Transform firePoint;

    [Header("수동 스킬 UI")]
    public GameObject skillButtonPrefab;
    public Canvas worldSpaceCanvas;

    [Header("현재 상태 (읽기 전용)")]
    [SerializeField]
    private int m_starLevel = 1;
    public int starLevel { get { return m_starLevel; } private set { m_starLevel = value; } }
    
    public bool IsDead { get; private set; } = false;

    public float CurrentHealth => currentHP;
    public float MaxHealth => maxHP;
    public event System.Action<float, float> OnHealthChanged;

    [Header("현재 스탯 (읽기 전용)")]
    [SerializeField] private float currentHP;
    
    public float maxHP { get; private set; }
    public float currentAttackDamage { get; private set; }
    public float currentAttackSpeed { get; private set; }
    public float currentAttackRange { get; private set; }
    public float currentDefense { get; private set; }
    public float currentMagicResistance { get; private set; }

    private ManaController manaController;
    private GameObject skillButtonInstance;
    private Coroutine attackCoroutine;
    [SerializeField] private List<Monster> blockedMonsters = new List<Monster>();
    public LayerMask enemyLayerMask;
    private IEnemy targetEnemy;
    private Transform targetTransform;

    public void Initialize(UnitData data, int initialStarLevel)
    {
        this.unitData = data;
        this.starLevel = initialStarLevel;
        manaController = GetComponent<ManaController>();
        InitializeStats();
        manaController.OnManaFull += HandleManaFull;
        StartAttackLoop();
    }

    public void InitializeStats()
    {
        if (unitData == null) return;
        float statMultiplier = Mathf.Pow(1.8f, starLevel - 1);

        maxHP = unitData.baseHealth * statMultiplier;
        currentHP = maxHP;
        OnHealthChanged?.Invoke(currentHP, maxHP);
        currentAttackDamage = unitData.baseAttackDamage * statMultiplier;
        currentAttackSpeed = unitData.attackSpeed;
        currentAttackRange = unitData.attackRange;
        currentDefense = unitData.defense;
        currentMagicResistance = unitData.magicResistance;

        int newMaxMana = 0;
        if (unitData.skillsByStarLevel != null && unitData.skillsByStarLevel.Length >= starLevel)
        {
            SkillData currentSkill = unitData.skillsByStarLevel[starLevel - 1];
            if (currentSkill != null)
            {
                newMaxMana = currentSkill.manaCost;
            }
        }
        manaController.Initialize(newMaxMana);
    }

    public void Upgrade()
    {
        if (starLevel < 3)
        {
            starLevel++;
            InitializeStats();
            Debug.Log($"<color=cyan>{unitData.unitName}이(가) {starLevel}성으로 업그레이드되었습니다!</color>");
        }
    }
    
    public void Respawn()
    {
        if (!IsDead) return;
        IsDead = false;
        InitializeStats();
        gameObject.SetActive(true);
        StartAttackLoop();
        Debug.Log($"<color=green>{unitData.unitName}이(가) 부활했습니다!</color>");
    }

    private void HandleManaFull()
    {
        if (unitData.skillsByStarLevel.Length < starLevel) return;
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
        if (unitData.skillsByStarLevel.Length < starLevel) return;
        SkillData currentSkillData = unitData.skillsByStarLevel[starLevel - 1];

        if (currentSkillData == null || currentSkillData.targetingStrategy == null || currentSkillData.effects.Count == 0)
        {
            Debug.LogError($"{unitData.unitName} ({starLevel}성)의 SkillData 또는 그 내용이 올바르게 설정되지 않았습니다.");
            return;
        }
        
        if (!manaController.IsManaFull) return;

        if (manaController.UseMana(currentSkillData.manaCost))
        {
            Debug.Log($"<color=yellow>{unitData.unitName} 스킬 발동: {currentSkillData.skillName}</color>");

            List<GameObject> targets = currentSkillData.targetingStrategy.FindTargets(this.gameObject, transform.position);

            foreach (var effect in currentSkillData.effects)
            {
                if (effect != null)
                {
                    effect.ApplyEffect(null, this.gameObject, targets);
                }
            }
            
            if (currentSkillData.vfxPrefab != null)
            {
                GameObject vfxInstance = Instantiate(currentSkillData.vfxPrefab, transform.position, Quaternion.identity);
                
                float maxDuration = 0f;
                foreach (var effect in currentSkillData.effects)
                {
                    if (effect is IDurationEffect durationEffect)
                    {
                        if (durationEffect.Duration > maxDuration)
                        {
                            maxDuration = durationEffect.Duration;
                        }
                    }
                }

                float vfxLifetime = (maxDuration > 0) ? maxDuration : 2f;

                if (vfxInstance.TryGetComponent<VFXAutoDestroy>(out var autoDestroy))
                {
                    autoDestroy.Initialize(vfxLifetime);
                }
                else
                {
                    Debug.LogWarning($"VFX 프리팹 '{vfxInstance.name}'에 VFXAutoDestroy.cs 컴포넌트가 없습니다. 자동으로 파괴되지 않습니다.");
                    // ✅ [수정] 컴파일 오류를 유발하던 아래 코드를 완전히 삭제했습니다.
                    // GameManagers.Instance.RegisterActiveVFX(vfxInstance); 
                }
            }

            HideSkillButton();
        }
    }

    #region 공격 로직 (이하 동일)
    public void StartAttackLoop()
    {
        if (attackCoroutine != null) StopCoroutine(attackCoroutine);
        attackCoroutine = StartCoroutine(AttackLoop());
    }

    private IEnumerator AttackLoop()
    {
        while (true)
        {
            if (currentAttackSpeed <= 0)
            {
                Debug.LogWarning($"{gameObject.name}의 공격 속도가 0 이하여서 공격할 수 없습니다. AttackSpeed 값을 확인해주세요.");
                yield return new WaitForSeconds(1f);
                continue;
            }

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
            if (enemyCollider.TryGetComponent<IEnemy>(out var enemy))
            {
                if (unitData.unitType == UnitType.Melee && enemyCollider.TryGetComponent<Monster>(out var monster))
                {
                    if (monster.monsterData.monsterType == MonsterType.Flying)
                    {
                        continue;
                    }
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

        if (unitData.unitType == UnitType.Melee)
        {
            targetEnemy.TakeDamage(currentAttackDamage, unitData.damageType);
        }
        else if (unitData.unitType == UnitType.Ranged)
        {
            if (unitData.projectilePrefabsByStarLevel == null || unitData.projectilePrefabsByStarLevel.Length < starLevel)
            {
                Debug.LogError($"[공격 실패] {unitData.unitName} ({starLevel}성)의 UnitData에 'projectilePrefabsByStarLevel' 배열이 설정되지 않았습니다!", unitData);
                return;
            }

            GameObject projectilePrefab = unitData.projectilePrefabsByStarLevel[starLevel - 1];
            if (projectilePrefab == null)
            {
                Debug.LogError($"[공격 실패] {unitData.unitName} ({starLevel}성)의 UnitData에 {starLevel}성 투사체 프리팹이 할당되지 않았습니다!", unitData);
                return;
            }

            if (firePoint == null)
            {
                Debug.LogError($"[공격 실패] {gameObject.name} 프리팹에 'firePoint'가 할당되지 않았습니다!", gameObject);
                return;
            }

            GameObject projectileGO = Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
            Projectile projectileScript = projectileGO.GetComponent<Projectile>();
            if (projectileScript != null)
            {
                projectileScript.Initialize(targetTransform, currentAttackDamage, unitData.damageType);
            }
            else
            {
                Debug.LogError($"[공격 실패] 투사체 프리팹 '{projectilePrefab.name}'에 Projectile.cs 스크립트가 없습니다!", projectilePrefab);
                Destroy(projectileGO);
            }
        }

        if (unitData.skillsByStarLevel.Length >= starLevel && unitData.skillsByStarLevel[starLevel - 1] != null)
        {
            manaController.GainMana(15);
        }
    }
    #endregion

    #region 저지, 스킬 UI, IEnemy 구현 등
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

    private void OnDestroy()
    {
        if (manaController != null) manaController.OnManaFull -= HandleManaFull;
        if (skillButtonInstance != null) Destroy(skillButtonInstance);
    }

    public void TakeDamage(float baseDamage, DamageType damageType)
    {
        if (unitData == null || IsDead) return;
        int finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, currentDefense, currentMagicResistance);
        currentHP -= finalDamage;
        OnHealthChanged?.Invoke(currentHP, maxHP);
        if (currentHP <= 0)
        {
            Die();
        }
    }
    private void Die()
    {
        if (IsDead) return;
        IsDead = true; 
        
        foreach (var monster in blockedMonsters)
        {
            if (monster != null)
            {
                monster.Unblock();
            }
        }
        blockedMonsters.Clear();
        
        if(attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        gameObject.SetActive(false);
        Debug.Log($"<color=red>{unitData.unitName}이(가) 전투에서 쓰러졌습니다.</color>");
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0) return;
        currentHP = Mathf.Min(currentHP + amount, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);
    }
    #endregion

    #region 스탯 수정 메서드 (BuffManager용)

    public void ApplyStatModifiers(float attackDamage, float attackSpeed)
    {
        this.currentAttackDamage = attackDamage;
        this.currentAttackSpeed = attackSpeed;
    }

    #endregion
}