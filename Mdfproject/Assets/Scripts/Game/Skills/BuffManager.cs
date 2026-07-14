// Assets/Scripts/Game/Skills/BuffManager.cs
using Fusion;
using UnityEngine;

public class BuffManager : MonoBehaviour
{
    private Unit _unit;
    private Monster _monster;

    private StatusEffectType _currentEffects = StatusEffectType.None;
    private bool _statusCacheFromScheduler;
    private float _schedulerSlowMultiplier = 1f;
    private bool _statBuffCacheFromScheduler;
    private float _schedulerAttackDamageFlatBonus;
    private float _schedulerAttackDamagePercentBonus;
    private float _schedulerAttackSpeedPercentBonus;
    private float _schedulerMoveSpeedMultiplier = 1f;

    public StatusEffectType CurrentEffects => _currentEffects;

    public bool HasEffect(StatusEffectType effect) => (_currentEffects & effect) != 0;

    public bool IsStunned => HasEffect(StatusEffectType.Stunned);

    public bool CanMove => !HasEffect(StatusEffectType.Stunned | StatusEffectType.Rooted);

    public bool CanAttack => !HasEffect(StatusEffectType.Stunned);

    public bool CanUseSkill => !HasEffect(StatusEffectType.Stunned | StatusEffectType.Silenced);

    private void Awake()
    {
        _unit = GetComponent<Unit>();
        _monster = GetComponent<Monster>();
    }

    private void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleGameStateChange;
    }

    private void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleGameStateChange;
    }

    private void HandleGameStateChange(GameManagers.GameState newState)
    {
        if (newState == GameManagers.GameState.Prepare)
        {
            ClearAllBuffs();
            ClearAllStatusEffects();
        }
    }

    #region Buff System

    public void ClearAllBuffs()
    {
        if (!HasStateAuthorityOrNoNetwork())
        {
            return;
        }

        if (IsNetworkStatBuffSchedulerActive())
        {
            CombatScheduler.Instance.ClearStatBuffsForTarget(this);
            return;
        }

        ApplyStatBuffSchedulerCache(0f, 0f, 0f, 1f);
    }

    public bool ApplyBuff(BuffStatEffect buffEffect, GameObject caster)
    {
        if (!HasStateAuthorityOrNoNetwork() || buffEffect == null || caster == null)
        {
            return false;
        }

        if (IsNetworkStatBuffSchedulerActive())
        {
            return CombatScheduler.Instance.ApplyStatBuff(this, buffEffect, caster);
        }

        Debug.LogWarning($"[BuffManager] Ignored stat buff without active CombatScheduler. target={name}, buff={buffEffect.name}");
        return false;
    }

    public void RecalculateStats()
    {
        if (!HasStateAuthorityOrNoNetwork())
        {
            return;
        }

        if (_unit != null)
        {
            float baseAttackDamage = _unit.PermanentAttackDamage;
            float baseAttackSpeed = _unit.PermanentAttackSpeed;
            float attackDamageBonus = _statBuffCacheFromScheduler ? _schedulerAttackDamageFlatBonus : 0f;
            float attackDamageBonusPercent = _statBuffCacheFromScheduler ? _schedulerAttackDamagePercentBonus : 0f;
            float attackSpeedBonusPercent = _statBuffCacheFromScheduler ? _schedulerAttackSpeedPercentBonus : 0f;

            float finalAttackDamage = baseAttackDamage * (1 + attackDamageBonusPercent) + attackDamageBonus;
            float finalAttackSpeed = baseAttackSpeed * (1 + attackSpeedBonusPercent);
            if (_unit.IsBerserkModeActive)
            {
                finalAttackDamage *= 1.5f;
                finalAttackSpeed *= 1.5f;
            }
            _unit.ApplyStatModifiers(finalAttackDamage, finalAttackSpeed);
        }

        if (_monster != null)
        {
            float moveSpeedMultiplier = CalculateTotalSlowMultiplier() * CalculateTotalStatMoveSpeedMultiplier();
            moveSpeedMultiplier = Mathf.Max(0.1f, moveSpeedMultiplier);
            float attackDamage = _monster.PermanentAttackDamage;
            float attackSpeed = _monster.PermanentAttackSpeed;
            if (_statBuffCacheFromScheduler)
            {
                attackDamage = attackDamage * (1 + _schedulerAttackDamagePercentBonus) + _schedulerAttackDamageFlatBonus;
                attackSpeed *= 1 + _schedulerAttackSpeedPercentBonus;
            }

            if (_monster.IsBerserkModeActive)
            {
                attackDamage *= 1.5f;
                attackSpeed *= 1.5f;
                moveSpeedMultiplier *= 2f;
            }

            _monster.ApplyStatModifiers(attackDamage, attackSpeed, moveSpeedMultiplier);
        }
    }

    #endregion

    #region Status Effect System

    public bool ApplyStatusEffect(
        StatusEffectType type,
        float duration,
        GameObject caster,
        float tickInterval = 0f,
        float damagePerTick = 0f,
        float slowMultiplier = 1f,
        DamageType damageType = DamageType.Physical)
    {
        if (!HasStateAuthorityOrNoNetwork())
        {
            return false;
        }

        if (IsNetworkStatusSchedulerActive())
        {
            return CombatScheduler.Instance.ApplyStatusEffect(
                this,
                type,
                duration,
                caster,
                tickInterval,
                damagePerTick,
                slowMultiplier,
                damageType);
        }

        Debug.LogWarning($"[BuffManager] Ignored status effect without active CombatScheduler. target={name}, type={type}");
        return false;
    }

    public void RemoveStatusEffect(StatusEffectType type)
    {
        if (!HasStateAuthorityOrNoNetwork())
        {
            return;
        }

        if (IsNetworkStatusSchedulerActive())
        {
            CombatScheduler.Instance.ClearStatusEffectType(this, type);
            return;
        }

        ApplyStatusSchedulerCache(StatusEffectType.None, 1f);
    }

    public void ClearAllStatusEffects()
    {
        if (!HasStateAuthorityOrNoNetwork())
        {
            return;
        }

        if (IsNetworkStatusSchedulerActive())
        {
            CombatScheduler.Instance.ClearStatusEffectsForTarget(this);
            return;
        }

        ApplyStatusSchedulerCache(StatusEffectType.None, 1f);
    }

    private float CalculateTotalSlowMultiplier()
    {
        return _statusCacheFromScheduler ? _schedulerSlowMultiplier : 1f;
    }

    private float CalculateTotalStatMoveSpeedMultiplier()
    {
        return _statBuffCacheFromScheduler ? _schedulerMoveSpeedMultiplier : 1f;
    }

    public void ApplyStatusSchedulerCache(StatusEffectType effects, float slowMultiplier)
    {
        _statusCacheFromScheduler = true;
        _currentEffects = effects;
        _schedulerSlowMultiplier = Mathf.Max(0.1f, slowMultiplier);
        RecalculateStats();
    }

    public void ApplyStatBuffSchedulerCache(
        float attackDamageFlatBonus,
        float attackDamagePercentBonus,
        float attackSpeedPercentBonus,
        float moveSpeedMultiplier)
    {
        _statBuffCacheFromScheduler = true;
        _schedulerAttackDamageFlatBonus = attackDamageFlatBonus;
        _schedulerAttackDamagePercentBonus = attackDamagePercentBonus;
        _schedulerAttackSpeedPercentBonus = attackSpeedPercentBonus;
        _schedulerMoveSpeedMultiplier = Mathf.Max(0.1f, moveSpeedMultiplier);
        RecalculateStats();
    }

    #endregion

    #region Convenience Methods

    public bool ApplyStun(float duration, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Stunned, duration, caster);

    public bool ApplySlow(float duration, float slowMultiplier, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Slowed, duration, caster, slowMultiplier: slowMultiplier);

    public bool ApplyRoot(float duration, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Rooted, duration, caster);

    public bool ApplySilence(float duration, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Silenced, duration, caster);

    public bool ApplyBurn(float duration, float damagePerTick, float tickInterval, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Burning, duration, caster, tickInterval, damagePerTick, 1f, DamageType.Magic);

    public bool ApplyFrostbite(float duration, float damagePerTick, float tickInterval, float slowMultiplier, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Frostbitten, duration, caster, tickInterval, damagePerTick, slowMultiplier, DamageType.Magic);

    public bool ApplyBleed(float duration, float damagePerTick, float tickInterval, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Bleeding, duration, caster, tickInterval, damagePerTick, 1f, DamageType.Physical);

    public bool ApplyPoison(float duration, float damagePerTick, float tickInterval, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Poisoned, duration, caster, tickInterval, damagePerTick, 1f, DamageType.Magic);

    #endregion

    private bool HasStateAuthorityOrNoNetwork()
    {
        if (_unit != null && _unit.Object != null && _unit.Object.IsValid && _unit.Runner != null && _unit.Runner.IsRunning)
        {
            return _unit.Object.HasStateAuthority;
        }

        if (_monster != null && _monster.Object != null && _monster.Object.IsValid && _monster.Runner != null && _monster.Runner.IsRunning)
        {
            return _monster.Object.HasStateAuthority;
        }

        return true;
    }

    private bool IsNetworkStatusSchedulerActive()
    {
        var scheduler = CombatScheduler.Instance;
        if (scheduler == null || !scheduler.IsStatusEffectSchedulerActive)
        {
            return false;
        }

        NetworkObject networkObject = GetComponentInParent<NetworkObject>();
        return networkObject != null &&
            networkObject.IsValid &&
            networkObject.Runner != null &&
            networkObject.Runner.IsRunning;
    }

    private bool IsNetworkStatBuffSchedulerActive()
    {
        var scheduler = CombatScheduler.Instance;
        if (scheduler == null || !scheduler.IsStatBuffSchedulerActive)
        {
            return false;
        }

        NetworkObject networkObject = GetComponentInParent<NetworkObject>();
        return networkObject != null &&
            networkObject.IsValid &&
            networkObject.Runner != null &&
            networkObject.Runner.IsRunning;
    }
}
