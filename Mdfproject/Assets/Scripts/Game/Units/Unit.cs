// Assets/Scripts/Game/Units/Unit.cs

using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using System.Threading.Tasks;
public class Unit : MonoBehaviour, IEnemy, IHealth
{
    [Header("참조 데이터")]
    [SerializeField] private UnitData unitData;
    public UnitData Data => unitData;

    [Header("원거리 유닛 참조")]
    public Transform firePoint;

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
    
    public SkillActivationType currentSkillActivationType { get; set; }
    private SkillData _loadedSkillData;
    private ManaController manaController;
    private StatusBarUI statusBarUI;
    private Coroutine attackCoroutine;
    [SerializeField] private List<Monster> blockedMonsters = new List<Monster>();
    private List<Unit> subscribedAllies = new List<Unit>();
    public LayerMask enemyLayerMask;
    private IEnemy targetEnemy;
    private Transform targetTransform;
    
    private bool isCombatPhase = false;

    void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        UnsubscribeFromAllies();
    }

    public void SetStatusBar(StatusBarUI ui)
    {
        this.statusBarUI = ui;
    }

    // Force AI purchase to enable skill auto-use during the next initialization.
    // This flag is consumed during InitializeStats() and then cleared.
    private bool _forceSkillAutoUseOnNextInitialize = false;
    public void SetForceSkillAutoUse(bool enabled)
    {
        _forceSkillAutoUseOnNextInitialize = enabled;
    }

   public async void Initialize(UnitData data, int initialStarLevel, PlayerManager owner)
    {
        this.unitData = data;
        this.starLevel = initialStarLevel;
        manaController = GetComponent<ManaController>();

        // AI가 소유한 유닛인 경우, 스킬 자동 사용을 강제합니다.
        if (owner != null && ComponentRegistry.Has<AIPlayerController>(owner.playerId.ToString()))
        {
            SetForceSkillAutoUse(true);
        }

        // 이벤트 구독/해지는 그대로 둡니다.
        if (manaController != null)
        {
            manaController.OnManaFull -= HandleManaFull;
            manaController.OnManaFull += HandleManaFull;
        }
        
        // InitializeStats가 비동기 함수가 되었으므로 await로 호출을 기다립니다.
        await InitializeStats();
    }

    void Update()
    {
        if (!isCombatPhase || !DoesHaveSkill() || unitData.manaRegenType != ManaRegenType.Passive)
        {
            return;
        }

        if (manaController != null)
        {
            manaController.GainManaOverTime(unitData.manaPerSecond);
        }
    }
    
    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        isCombatPhase = (newState == GameManagers.GameState.Combat);

        if (isCombatPhase)
        {
            StartAttackLoop();

            // 힐러인 경우, 전투 시작 시 주변 아군 유닛의 체력 변화를 구독합니다.
            if (DoesHaveSkill() && _loadedSkillData != null && _loadedSkillData.name == "Skill_Heal" && currentSkillActivationType == SkillActivationType.Automatic)
            {
                SubscribeToNearbyAllies();
            }
        }
        else
        {
            if (attackCoroutine != null)
            {
                StopCoroutine(attackCoroutine);
                attackCoroutine = null;
            }
            // 전투 종료 시 구독을 해제합니다.
            UnsubscribeFromAllies();
        }
    }

    public async UniTask InitializeStats()
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
        
        // --- [핵심 수정 부분] ---
        if (DoesHaveSkill())
        {
            // 주소(string)를 사용해 AssetLoader로 실제 SkillData를 로드합니다.
            string skillKey = unitData.skillsByStarLevel[starLevel - 1];
            _loadedSkillData = await AssetLoader.LoadAssetAsync<SkillData>(skillKey);

            if (_loadedSkillData != null)
            {
                newMaxMana = _loadedSkillData.manaCost;
                currentSkillActivationType = _loadedSkillData.activationType;
                // If this unit was created by an AI purchase and requested auto-skill, override activation type.
                if (_forceSkillAutoUseOnNextInitialize)
                {
                    currentSkillActivationType = SkillActivationType.Automatic;
                    _forceSkillAutoUseOnNextInitialize = false;
                }
            }
        }
        else
        {
            _loadedSkillData = null;
        }
        // --- [수정 끝] ---

        manaController.Initialize(newMaxMana);

        if (statusBarUI != null)
        {
            // InitializeSkillButton도 비동기가 되었으므로 await로 호출합니다.
            await statusBarUI.InitializeSkillButton(this);
        }
        else
        {
            Debug.LogWarning($"[Unit] {gameObject.name}에 StatusBarUI가 주입되지 않았습니다.", this.gameObject);
        }
    }
    
    public async Task Upgrade()
    {
        if (starLevel < 3)
        {
            starLevel++;
            await InitializeStats();
            Debug.Log($"<color=cyan>{unitData.unitName}이(가) {starLevel}성으로 업그레이드되었습니다!</color>");
        }
    }
    
    public async void Respawn()
    {
        if (!IsDead) return;
        IsDead = false;
        await InitializeStats();
        gameObject.SetActive(true);
        Debug.Log($"<color=green>{unitData.unitName}이(가) 부활했습니다!</color>");
    }

    private void HandleManaFull()
    {
        if (!isCombatPhase || !DoesHaveSkill()) return;
        
        // --- [핵심 수정 부분] ---
        // 더 이상 SkillData를 직접 접근하거나 로드할 필요가 없습니다.
        // InitializeStats에서 미리 저장해 둔 currentSkillActivationType 값을 사용합니다.
        if (currentSkillActivationType == SkillActivationType.Automatic)
        {
            // 'Skill_Heal' 스킬은 특별한 발동 조건 확인
            if (_loadedSkillData != null && _loadedSkillData.name == "Skill_Heal")
            {
                // 마나가 꽉 찼을 때 즉시 힐이 필요한 아군이 있는지 확인합니다.
                bool needsHeal = subscribedAllies.Any(ally => ally != null && !ally.IsDead && ally.CurrentHealth / ally.MaxHealth < 0.7f);
                if (needsHeal)
                {
                    ActivateSkill();
                }
                // 필요한 아군이 없다면, OnAllyHealthChanged 이벤트에 의해 스킬이 발동되기를 기다립니다.
            }
            else
            {
                // 그 외 스킬은 마나가 차면 즉시 발동
                ActivateSkill();
            }
        }
        // --- [수정 끝] ---
    }

    public async void ActivateSkill()
    {
        if (!isCombatPhase || !DoesHaveSkill()) return;
        
        // 스킬 데이터가 로드되었는지 다시 한번 확인합니다.
        if (_loadedSkillData == null)
        {
            // 만약 로드가 안됐다면, 이 시점에서 다시 로드를 시도할 수도 있습니다.
            string skillKey = unitData.skillsByStarLevel[starLevel - 1];
            _loadedSkillData = await AssetLoader.LoadAssetAsync<SkillData>(skillKey);
            if (_loadedSkillData == null) return; // 그래도 없으면 종료
        }
        
        SkillData currentSkillData = _loadedSkillData; // 로드된 데이터를 사용합니다.

        if (currentSkillData.targetingStrategy == null || currentSkillData.effects.Count == 0)
        {
            Debug.LogError($"{unitData.unitName} ({starLevel}성)의 SkillData 또는 그 내용이 올바르게 설정되지 않았습니다.");
            return;
        }
        
        if (!manaController.IsManaFull) return;

        if (manaController.UseMana(currentSkillData.manaCost))
        {
            Debug.Log($"<color=yellow>{unitData.unitName} 스킬 발동: {currentSkillData.skillName}</color>");

            List<GameObject> targets = currentSkillData.targetingStrategy.FindTargets(this.gameObject, transform.position, currentSkillData.range);

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
                }
            }
        }
    }
    
    private bool DoesHaveSkill()
    {
        return unitData.skillsByStarLevel.Length >= starLevel && unitData.skillsByStarLevel[starLevel - 1] != null;
    }

    #region 힐러 스킬 로직
    private void SubscribeToNearbyAllies()
    {
        if (_loadedSkillData?.targetingStrategy == null) return;

        // 자신을 제외한 아군을 찾습니다.
        List<GameObject> alliesGO = _loadedSkillData.targetingStrategy.FindTargets(this.gameObject, transform.position, _loadedSkillData.range)
            .Where(go => go != this.gameObject).ToList();

        foreach (var allyGO in alliesGO)
        {
            if (allyGO.TryGetComponent<Unit>(out var allyUnit))
            {
                if (!subscribedAllies.Contains(allyUnit))
                {
                    subscribedAllies.Add(allyUnit);
                    allyUnit.OnHealthChanged += OnAllyHealthChanged;
                    
                    // 구독 시점에도 체력이 낮은 아군이 있다면 즉시 힐을 시도할 수 있도록 체크합니다.
                    OnAllyHealthChanged(allyUnit.CurrentHealth, allyUnit.MaxHealth);
                }
            }
        }
    }

    private void UnsubscribeFromAllies()
    {
        foreach (var ally in subscribedAllies)
        {
            if (ally != null)
            {
                ally.OnHealthChanged -= OnAllyHealthChanged;
            }
        }
        subscribedAllies.Clear();
    }

    private void OnAllyHealthChanged(float currentHP, float maxHP)
    {
        // 전투 중이 아니거나, 스킬이 없거나, 힐 스킬이 아니거나, 최대 체력이 0 이하면 무시
        if (!isCombatPhase || !DoesHaveSkill() || _loadedSkillData?.name != "Skill_Heal" || maxHP <= 0) return;

        // 체력이 70% 미만으로 떨어졌을 때
        if (currentHP / maxHP < 0.7f)
        {
            // 마나가 가득 찼고, 스킬이 자동사용 모드일 때
            if (manaController.IsManaFull && currentSkillActivationType == SkillActivationType.Automatic)
            {
                ActivateSkill();
            }
        }
    }
    #endregion

    #region 공격 로직 (이하 동일)
    public void StartAttackLoop()
    {
        if (attackCoroutine != null) StopCoroutine(attackCoroutine);
        attackCoroutine = StartCoroutine(AttackLoop());
    }

    private IEnumerator AttackLoop()
    {
        while (isCombatPhase)
        {
            if (currentAttackSpeed <= 0)
            {
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

    private async void Attack()
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

            // --- [핵심 수정 부분] ---
            string projectileKey = unitData.projectilePrefabsByStarLevel[starLevel - 1];
            GameObject projectilePrefab = await AssetLoader.LoadAssetAsync<GameObject>(projectileKey);
            // --- [수정 끝] ---

            if (projectilePrefab == null)
            {
                Debug.LogError($"[공격 실패] {unitData.unitName} ({starLevel}성)의 UnitData에 {starLevel}성 투사체 프리팹({projectileKey})이 할당되지 않았거나 로드에 실패했습니다!", unitData);
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
        
        if (DoesHaveSkill() && unitData.manaRegenType == ManaRegenType.OnAttack)
        {
            manaController.GainMana(unitData.manaOnAttack);
        }
    }
    #endregion

    #region 저지, 스킬 UI, IEnemy 구현 등 (이하 동일)
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
        // [수정됨] 오브젝트 파괴 시 이벤트 구독을 확실히 해제합니다.
        if (manaController != null)
        {
            manaController.OnManaFull -= HandleManaFull;
        }
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
