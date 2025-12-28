// Assets/Scripts/Game/Units/Unit.cs

using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using System.Threading.Tasks;
using Fusion;
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
    [SerializeField] private Animator animator;
    [SerializeField] private string attackTriggerParam = "AttackTrigger";
    [SerializeField] private string skillTriggerParam = "SkillTrigger";
    [SerializeField] private string skillStateTag = "Skill";
    [SerializeField] private bool blockAttacksDuringSkill = true;
    [SerializeField] private float maxAttackAnimationsPerSecond = 4f;
    [SerializeField] private float baseAttackAnimationDuration = 1f;
    private float lastAttackAnimTime = -999f;
    private Coroutine animSpeedResetRoutine;
    private bool attackClipDurationInitialized = false;
    private Coroutine attackClipDetectRoutine;
    private PlayerManager owner;
    private NetworkObject _networkObject;
    private float _nextProjectileVfxTime;
    private float _cachedProjectileSpeed = -1f;
    private bool _hasPendingProjectileAttack;
    private PendingProjectileAttack _pendingProjectileAttack;
    private bool _isSkillCasting;
    private Coroutine _skillCastingRoutine;

    private struct PendingProjectileAttack
    {
        public NetworkObject Target;
        public IEnemy TargetEnemy;
        public float Damage;
        public DamageType DamageType;
        public float ProjectileSpeed;
    }

    private bool isCombatPhase = false;

    private bool HasStateAuthorityOrNoNetwork()
    {
        if (_networkObject == null)
        {
            _networkObject = GetComponent<NetworkObject>();
        }

        if (_networkObject == null || _networkObject.Runner == null || !_networkObject.Runner.IsRunning)
        {
            return true;
        }

        return _networkObject.HasStateAuthority;
    }

    void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private bool IsSkillCasting()
    {
        return blockAttacksDuringSkill && _isSkillCasting;
    }

    private void BeginSkillCasting()
    {
        TriggerSkillAnimation();
        CancelPendingProjectileAttack();

        if (!blockAttacksDuringSkill || animator == null || string.IsNullOrEmpty(skillStateTag))
        {
            return;
        }

        _isSkillCasting = true;

        if (_skillCastingRoutine != null)
        {
            StopCoroutine(_skillCastingRoutine);
        }

        _skillCastingRoutine = StartCoroutine(MonitorSkillAnimation());
    }

    private void TriggerSkillAnimation()
    {
        if (animator == null || string.IsNullOrEmpty(skillTriggerParam))
        {
            return;
        }

        if (animSpeedResetRoutine != null)
        {
            StopCoroutine(animSpeedResetRoutine);
            animSpeedResetRoutine = null;
        }

        animator.speed = 1f;

        if (!string.IsNullOrEmpty(attackTriggerParam))
        {
            animator.ResetTrigger(attackTriggerParam);
        }

        animator.ResetTrigger(skillTriggerParam);
        animator.SetTrigger(skillTriggerParam);
    }

    private IEnumerator MonitorSkillAnimation()
    {
        float elapsed = 0f;
        const float enterTimeout = 0.5f;

        while (elapsed < enterTimeout)
        {
            if (animator == null)
            {
                _isSkillCasting = false;
                _skillCastingRoutine = null;
                yield break;
            }

            var st = animator.GetCurrentAnimatorStateInfo(0);
            if (st.IsTag(skillStateTag))
            {
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        while (animator != null)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            if (!st.IsTag(skillStateTag))
            {
                break;
            }

            yield return null;
        }

        _isSkillCasting = false;
        _skillCastingRoutine = null;
    }

    private void CancelPendingProjectileAttack()
    {
        _hasPendingProjectileAttack = false;
        _pendingProjectileAttack = new PendingProjectileAttack();
    }

    private bool TryPlayAttackAnimation()
    {
        if (IsSkillCasting()) return false;
        if (animator == null) return false;
        float animRate = Mathf.Min(currentAttackSpeed, maxAttackAnimationsPerSecond);
        if (animRate <= 0f) return false;
        float now = Time.time;
        float minInterval = 1f / animRate;
        if (now - lastAttackAnimTime < minInterval) return false;
        lastAttackAnimTime = now;
        float speed = baseAttackAnimationDuration > 0f ? baseAttackAnimationDuration * animRate : animRate;
        animator.speed = Mathf.Max(0.01f, speed);
        animator.ResetTrigger(attackTriggerParam);
        animator.SetTrigger(attackTriggerParam);
        if (animSpeedResetRoutine != null)
        {
            StopCoroutine(animSpeedResetRoutine);
        }
        animSpeedResetRoutine = StartCoroutine(ResetAnimatorSpeedAfter(minInterval));
        if (!attackClipDurationInitialized && attackClipDetectRoutine == null)
        {
            attackClipDetectRoutine = StartCoroutine(CaptureAttackClipDuration());
        }
        return true;
    }

    private IEnumerator ResetAnimatorSpeedAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (animator != null)
        {
            animator.speed = 1f;
        }
        animSpeedResetRoutine = null;
    }

    private IEnumerator CaptureAttackClipDuration()
    {
        float elapsed = 0f;
        float timeout = 1f;
        while (elapsed < timeout)
        {
            if (animator == null) break;
            var st = animator.GetCurrentAnimatorStateInfo(0);
            if (st.IsTag("Attack"))
            {
                var infos = animator.GetCurrentAnimatorClipInfo(0);
                if (infos != null && infos.Length > 0 && infos[0].clip != null)
                {
                    baseAttackAnimationDuration = Mathf.Max(0.01f, infos[0].clip.length);
                    attackClipDurationInitialized = true;
                    break;
                }
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        attackClipDetectRoutine = null;
    }

    private void CacheAttackClipDurationFromController()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        var clips = animator.runtimeAnimatorController.animationClips;
        if (clips == null || clips.Length == 0) return;

        AnimationClip attackClip = null;
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            if (clip.name.IndexOf("attack", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                attackClip = clip;
                break;
            }
        }

        if (attackClip == null)
        {
            attackClip = clips[0];
        }

        baseAttackAnimationDuration = Mathf.Max(0.01f, attackClip.length);
        attackClipDurationInitialized = true;
    }

    public float GetPermanentAdjustedBaseAttackDamage()
    {
        if (unitData == null) return 0f;
        float statMultiplier = Mathf.Pow(1.8f, starLevel - 1);
        float baseDamage = unitData.baseAttackDamage * statMultiplier;
        float bonusPercent = owner != null ? owner.permanentAttackDamagePercent : 0f;
        return baseDamage * (1f + bonusPercent);
    }

    public float GetPermanentAdjustedBaseAttackSpeed()
    {
        if (unitData == null) return 0f;
        float baseSpeed = unitData.attackSpeed;
        float bonusPercent = owner != null ? owner.permanentAttackSpeedPercent : 0f;
        return baseSpeed * (1f + bonusPercent);
    }

    public void RefreshPermanentBonuses()
    {
        var buffManager = GetComponent<BuffManager>();
        if (buffManager != null)
        {
            buffManager.RecalculateStats();
            return;
        }
        float baseDmg = GetPermanentAdjustedBaseAttackDamage();
        float baseSpd = GetPermanentAdjustedBaseAttackSpeed();
        ApplyStatModifiers(baseDmg, baseSpd);
    }

    void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        UnsubscribeFromAllies();
        if (animSpeedResetRoutine != null)
        {
            StopCoroutine(animSpeedResetRoutine);
            animSpeedResetRoutine = null;
        }
        if (animator != null)
        {
            animator.speed = 1f;
        }
        if (attackClipDetectRoutine != null)
        {
            StopCoroutine(attackClipDetectRoutine);
            attackClipDetectRoutine = null;
        }
        if (_skillCastingRoutine != null)
        {
            StopCoroutine(_skillCastingRoutine);
            _skillCastingRoutine = null;
        }
        _isSkillCasting = false;
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

   public async UniTask Initialize(UnitData data, int initialStarLevel, PlayerManager owner)
    {
        this.unitData = data;
        this.owner = owner;
        if (_networkObject == null)
        {
            _networkObject = GetComponent<NetworkObject>();
        }
        if(this.unitData == null)
        {
            Debug.LogError($"UnitData is null for unit {name}");
            return;
        }
        this.starLevel = initialStarLevel;
        manaController = GetComponent<ManaController>();
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }
        CacheAttackClipDurationFromController();

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
        await CacheProjectileSpeedAsync();
    }

    void Update()
    {
        if (!isCombatPhase || !DoesHaveSkill() || unitData.manaRegenType != ManaRegenType.Passive)
        {
            return;
        }

        if (!HasStateAuthorityOrNoNetwork())
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
            _nextProjectileVfxTime = Time.time;

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

            // 전투 종료 시 체력을 최대로, 마나를 0으로 초기화합니다.
            Heal(maxHP);

            if (manaController != null)
            {
                int maxMana = 0;
                if (DoesHaveSkill() && _loadedSkillData != null)
                {
                    maxMana = _loadedSkillData.manaCost;
                }
                manaController.Initialize(maxMana);
            }

            if (_skillCastingRoutine != null)
            {
                StopCoroutine(_skillCastingRoutine);
                _skillCastingRoutine = null;
            }
            _isSkillCasting = false;
        }
    }

    public async UniTask InitializeStats()
    {
        if (unitData == null) return;
        float statMultiplier = Mathf.Pow(1.8f, starLevel - 1);

        maxHP = unitData.baseHealth * statMultiplier;
        currentHP = maxHP;
        OnHealthChanged?.Invoke(currentHP, maxHP);
        currentAttackDamage = GetPermanentAdjustedBaseAttackDamage();
        currentAttackSpeed = GetPermanentAdjustedBaseAttackSpeed();
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
    
    private async UniTask CacheProjectileSpeedAsync()
    {
        if (unitData == null || unitData.unitType != UnitType.Ranged)
        {
            return;
        }

        if (unitData.projectilePrefabsByStarLevel == null || unitData.projectilePrefabsByStarLevel.Length < starLevel)
        {
            return;
        }

        string projectileKey = unitData.projectilePrefabsByStarLevel[starLevel - 1];
        if (string.IsNullOrEmpty(projectileKey))
        {
            return;
        }

        GameObject projectilePrefab = await AssetLoader.LoadAssetAsync<GameObject>(projectileKey);
        if (projectilePrefab == null)
        {
            return;
        }

        var projectile = projectilePrefab.GetComponent<Projectile>();
        if (projectile != null)
        {
            _cachedProjectileSpeed = projectile.Speed;
        }
    }

    public async Task Upgrade()
    {
        if (starLevel < 3)
        {
            starLevel++;
            await InitializeStats();
        await CacheProjectileSpeedAsync();
            Debug.Log($"<color=cyan>{unitData.unitName}이(가) {starLevel}성으로 업그레이드되었습니다!</color>");
        }
    }
    
    public async void Respawn()
    {
        if (!IsDead) return;
        IsDead = false;
        await InitializeStats();
        await CacheProjectileSpeedAsync();
        gameObject.SetActive(true);
        Debug.Log($"<color=green>{unitData.unitName}이(가) 부활했습니다!</color>");
    }

    private void HandleManaFull()
    {
        if (!isCombatPhase || !DoesHaveSkill()) return;
        if (!HasStateAuthorityOrNoNetwork()) return;
        if (IsSkillCasting()) return;
        
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
                // 그 외 스킬은 스킬 범위 내에 적이 있을 때만 발동합니다.
                if (IsEnemyInSkillRange())
                {
                    ActivateSkill();
                }
            }
        }
        // --- [수정 끝] ---
    }

    public async void ActivateSkill()
    {
        if (!isCombatPhase || !DoesHaveSkill()) return;
        if (!HasStateAuthorityOrNoNetwork()) return;
        if (IsSkillCasting()) return;
        
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
            BeginSkillCasting();
            Debug.Log($"<color=yellow>{unitData.unitName} 스킬 발동: {currentSkillData.skillName}</color>");

            List<GameObject> targets = currentSkillData.targetingStrategy.FindTargets(this.gameObject, transform.position, currentSkillData.range);

            foreach (var effect in currentSkillData.effects)
            {
                if (effect != null)
                {
                    effect.ApplyEffect(null, this.gameObject, targets, currentSkillData.range, currentSkillData.targetingStrategy);
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

    private bool DoesHaveSkill() {
        if (unitData == null) return false;
        if (unitData.skillsByStarLevel == null) return false;
        if (starLevel <= 0) return false;
        var arr = unitData.skillsByStarLevel;
        if (arr.Length < starLevel) return false;
        // 예전 의미와 동일: null만 배제하고 빈 문자열은 허용
        return arr[starLevel - 1] != null;
    }

    private bool IsEnemyInSkillRange()
    {
        if (_loadedSkillData == null) return false;
        // 3D 환경: XZ 평면 기준 구면 탐색
        Collider[] enemiesInRange = Physics.OverlapSphere(transform.position, _loadedSkillData.range, enemyLayerMask);
        // 적이 한 명이라도 있으면 true를 반환합니다.
        return enemiesInRange.Length > 0;
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
        if (!HasStateAuthorityOrNoNetwork()) return;

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
        float nextAttackTime = 0f;
        while (isCombatPhase)
        {
            if (currentAttackSpeed <= 0)
            {
                yield return null;
                continue;
            }

            if (IsSkillCasting())
            {
                yield return null;
                continue;
            }

            FindNearestEnemy();
            if (targetEnemy != null)
            {
                if (Time.time >= nextAttackTime)
                {
                    Attack();
                    nextAttackTime = Time.time + 1f / currentAttackSpeed;
                }
            }

            // 매 프레임마다 적 탐색/쿨다운 확인
            yield return null;
        }
    }

    private void FindNearestEnemy()
    {
        // 3D 환경: XZ 평면 기준 구면 탐색
        Collider[] enemiesInRange = Physics.OverlapSphere(transform.position, currentAttackRange, enemyLayerMask);
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
        if (IsSkillCasting())
        {
            return;
        }
        if (targetEnemy == null || targetTransform == null || Vector3.Distance(transform.position, targetTransform.position) > currentAttackRange)
        {
            targetEnemy = null;
            return;
        }
        bool playedAnim = TryPlayAttackAnimation();

        bool isRanged = unitData.unitType == UnitType.Ranged;

        if (_networkObject == null)
        {
            _networkObject = GetComponent<NetworkObject>();
        }

        bool hasAuthority = _networkObject == null || _networkObject.HasStateAuthority;
        if (hasAuthority)
        {
            var scheduler = CombatScheduler.Instance;
            if (scheduler != null && scheduler.Runner != null && scheduler.Runner.IsRunning)
            {
                var targetNo = targetTransform.GetComponentInParent<NetworkObject>();
                if (targetNo != null)
                {
                    if (isRanged && playedAnim && !_hasPendingProjectileAttack && ShouldEmitProjectileVfx())
                    {
                        _pendingProjectileAttack = new PendingProjectileAttack
                        {
                            Target = targetNo,
                            TargetEnemy = targetEnemy,
                            Damage = currentAttackDamage,
                            DamageType = unitData.damageType,
                            ProjectileSpeed = _cachedProjectileSpeed
                        };
                        _hasPendingProjectileAttack = true;
                    }
                    else
                    {
                        Vector3 firePos = firePoint != null ? firePoint.position : transform.position;
                        scheduler.ScheduleHit(_networkObject, targetNo, firePos, currentAttackDamage, unitData.damageType,
                            isRanged, false, _cachedProjectileSpeed);
                    }
                }
                else if (targetEnemy != null)
                {
                    targetEnemy.TakeDamage(currentAttackDamage, unitData.damageType);
                }
            }
            else if (targetEnemy != null)
            {
                targetEnemy.TakeDamage(currentAttackDamage, unitData.damageType);
            }
        }

        if (DoesHaveSkill() && unitData.manaRegenType == ManaRegenType.OnAttack && HasStateAuthorityOrNoNetwork())
        {
            manaController.GainMana(unitData.manaOnAttack);
        }
    }

    private bool ShouldEmitProjectileVfx()
    {
        if (maxAttackAnimationsPerSecond <= 0f)
        {
            return false;
        }

        if (currentAttackSpeed <= maxAttackAnimationsPerSecond + 1e-4f)
        {
            return true;
        }

        if (Time.time >= _nextProjectileVfxTime)
        {
            _nextProjectileVfxTime = Time.time + 1f / maxAttackAnimationsPerSecond;
            return true;
        }

        return false;
    }

    public void AnimEvent_AttackImpact()
    {
        if (!_hasPendingProjectileAttack)
        {
            return;
        }

        if (_networkObject == null)
        {
            _networkObject = GetComponent<NetworkObject>();
        }

        bool hasAuthority = _networkObject == null || _networkObject.HasStateAuthority;
        if (!hasAuthority)
        {
            _hasPendingProjectileAttack = false;
            return;
        }

        var scheduler = CombatScheduler.Instance;
        if (scheduler != null && scheduler.Runner != null && scheduler.Runner.IsRunning && _pendingProjectileAttack.Target != null)
        {
            Vector3 firePos = firePoint != null ? firePoint.position : transform.position;
            scheduler.ScheduleHit(_networkObject, _pendingProjectileAttack.Target, firePos, _pendingProjectileAttack.Damage,
                _pendingProjectileAttack.DamageType, true, true, _pendingProjectileAttack.ProjectileSpeed);
        }
        else if (_pendingProjectileAttack.TargetEnemy != null)
        {
            _pendingProjectileAttack.TargetEnemy.TakeDamage(_pendingProjectileAttack.Damage, _pendingProjectileAttack.DamageType);
        }

        _hasPendingProjectileAttack = false;
    }

    public void AnimEvent_SkillEnd()
    {
        if (!blockAttacksDuringSkill)
        {
            return;
        }

        _isSkillCasting = false;
        if (_skillCastingRoutine != null)
        {
            StopCoroutine(_skillCastingRoutine);
            _skillCastingRoutine = null;
        }
    }
    #endregion

    #region 저지, 스킬 UI, IEnemy 구현 등 (이하 동일)
    private void OnTriggerEnter(Collider other)
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

        currentHP += amount;
        if (currentHP > maxHP)
        {
            currentHP = maxHP;
        }
        
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
