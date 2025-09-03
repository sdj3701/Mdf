// Assets/Scripts/Game/Units/Unit.cs
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
// using Fusion; // 네트워크 모드에서 주석 해제

// public class Unit : NetworkBehaviour, IEnemy, IHealth // 네트워크 모드에서 NetworkBehaviour로 변경
public class Unit : MonoBehaviour, IEnemy, IHealth
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
    [Header("현재 상태 (읽기 전용)")]
    [Tooltip("유닛의 현재 성급입니다. (1~3성)")]
    [SerializeField]
    private int m_starLevel = 1;
    public int starLevel { get { return m_starLevel; } private set { m_starLevel = value; } }
    
    public bool IsDead { get; private set; } = false;

    public float CurrentHealth => currentHP;
    public float MaxHealth => maxHP;
    public event System.Action<float, float> OnHealthChanged;


    [Header("현재 스탯 (읽기 전용)")]
    [SerializeField] private float currentHP;
    private float maxHP;
    [SerializeField] private float currentAttackDamage;
    [SerializeField] private float currentAttackSpeed;
    [SerializeField] private float currentAttackRange;
    [SerializeField] private float currentDefense;
    [SerializeField] private float currentMagicResistance;

    private ManaController manaController;
    // [제거됨] private ISkill skillInstance;
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
        manaController.Initialize(unitData.maxMana);

        InitializeStats();
        // [변경됨] 스킬 인스턴스 생성 로직이 필요 없어졌으므로 InitializeSkill() 호출 제거
        // 대신 마나 이벤트 핸들러를 직접 연결합니다.
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
    }

    public void Upgrade()
    {
        if (starLevel < 3)
        {
            starLevel++;
            InitializeStats();
            // [참고] 성급이 오르면 스킬 데이터가 바뀌므로 별도의 초기화는 필요 없습니다.
            // ActivateSkill()이 호출될 때마다 올바른 성급의 SkillData를 참조하게 됩니다.
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
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0) return; // 죽은 유닛은 회복 불가

        currentHP = Mathf.Min(currentHP + amount, maxHP); // 최대 체력을 넘지 않도록
        OnHealthChanged?.Invoke(currentHP, maxHP);
    }
    /// <summary>
    /// [핵심 수정] 새로운 스킬 시스템을 사용하여 스킬을 발동합니다.
    /// </summary>
    public void ActivateSkill()
    {
        // [네트워크] 스킬 로직은 서버(StateAuthority)에서만 실행되어야 합니다.
        // if (!Object.HasStateAuthority) return;

        if (unitData.skillsByStarLevel.Length < starLevel) return;
        SkillData currentSkillData = unitData.skillsByStarLevel[starLevel - 1];

        // 스킬 데이터, 타겟팅 전략, 효과가 모두 설정되어 있는지 확인합니다.
        if (currentSkillData == null || currentSkillData.targetingStrategy == null || currentSkillData.effects.Count == 0)
        {
            Debug.LogError($"{unitData.unitName} ({starLevel}성)의 SkillData 또는 그 내용이 올바르게 설정되지 않았습니다.");
            return;
        }
        
        if (!manaController.IsManaFull) return;

        if (manaController.UseMana(currentSkillData.manaCost))
        {
            Debug.Log($"<color=yellow>{unitData.unitName} 스킬 발동: {currentSkillData.skillName}</color>");

            // 1. 타겟팅 전략을 사용해 대상들을 찾습니다.
            List<GameObject> targets = currentSkillData.targetingStrategy.FindTargets(this.gameObject, transform.position);

            // 2. SkillData에 등록된 모든 효과를 순차적으로 적용합니다.
            foreach (var effect in currentSkillData.effects)
            {
                if (effect != null)
                {
                    // effect.ApplyEffect(Runner, this.gameObject, targets); // 네트워크 모드
                    effect.ApplyEffect(null, this.gameObject, targets); // 비-네트워크 모드
                }
            }
            
            // 3. (선택) RPC를 통해 모든 클라이언트에게 시각 효과를 재생하라고 명령합니다.
            if (currentSkillData.vfxPrefab != null)
            {
                // RPC_PlaySkillVFX(currentSkillData.vfxPrefab.name, transform.position); // 네트워크 모드
                
                // 비-네트워크 모드에서는 즉시 생성합니다.
                Instantiate(currentSkillData.vfxPrefab, transform.position, Quaternion.identity);
            }

            HideSkillButton();
        }
    }
    
    /*
    // [네트워크] 스킬 시각 효과(VFX)를 모든 클라이언트에서 재생하기 위한 RPC
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlaySkillVFX(string vfxPrefabName, Vector3 position)
    {
        // AssetRegistry나 Addressables를 통해 vfxPrefabName에 해당하는 프리팹을 로드하고 생성합니다.
        // 이 이펙트는 네트워크 동기화되지 않는 순수 시각 효과여야 합니다.
        GameObject vfxPrefab = AssetRegistry.GetPrefab(vfxPrefabName); // AssetRegistry 사용 예시
        if (vfxPrefab != null)
        {
            Instantiate(vfxPrefab, position, Quaternion.identity);
        }
    }
    */

    #region 공격 로직 (기존과 동일)
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

        if (unitData.unitType == UnitType.Melee)
        {
            targetEnemy.TakeDamage(currentAttackDamage, unitData.damageType);
        }
        else if (unitData.unitType == UnitType.Ranged)
        {
            if (unitData.projectilePrefabsByStarLevel != null && unitData.projectilePrefabsByStarLevel.Length >= starLevel)
            {
                GameObject projectilePrefab = unitData.projectilePrefabsByStarLevel[starLevel - 1];
                if (projectilePrefab != null && firePoint != null)
                {
                    GameObject projectileGO = Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
                    Projectile projectileScript = projectileGO.GetComponent<Projectile>();
                    if (projectileScript != null)
                    {
                        projectileScript.Initialize(targetTransform, currentAttackDamage, unitData.damageType);
                    }
                }
            }
        }

        // 공격 시 마나 획득
        if (unitData.skillsByStarLevel.Length >= starLevel && unitData.skillsByStarLevel[starLevel - 1] != null)
        {
            manaController.GainMana(15);
        }
    }
    #endregion

    #region 저지, 스킬 UI, IEnemy 구현 등 (기존과 동일)

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

    #endregion
}