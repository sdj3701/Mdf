// Assets/Scripts/Game/Skills/BuffManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class BuffManager : MonoBehaviour
{
    private readonly List<ActiveBuff> _activeBuffs = new List<ActiveBuff>();
    private readonly List<ActiveStatusEffect> _activeStatusEffects = new List<ActiveStatusEffect>();
    
    private Unit _unit;
    private Monster _monster;
    
    private StatusEffectType _currentEffects = StatusEffectType.None;

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

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        UpdateBuffs(deltaTime);
        UpdateStatusEffects(deltaTime);
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
    
    private void UpdateBuffs(float deltaTime)
    {
        if (_activeBuffs.Count == 0) return;

        bool needsRecalc = false;
        for (int i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            _activeBuffs[i].timer -= deltaTime;
            if (_activeBuffs[i].timer <= 0)
            {
                _activeBuffs.RemoveAt(i);
                needsRecalc = true;
            }
        }
        
        if (needsRecalc) RecalculateStats();
    }

    public void ClearAllBuffs()
    {
        if (_activeBuffs.Count > 0)
        {
            _activeBuffs.Clear();
            RecalculateStats();
        }
    }

    public void ApplyBuff(BuffStatEffect buffEffect, GameObject caster)
    {
        if (caster == null) return;
        
        var existing = _activeBuffs.FirstOrDefault(b => b.Source == buffEffect && b.Caster == caster);
        if (existing != null)
        {
            existing.timer = buffEffect.duration;
        }
        else
        {
            _activeBuffs.Add(new ActiveBuff(buffEffect, buffEffect.duration, caster));
        }
        
        RecalculateStats();
    }

    public void RecalculateStats()
    {
        if (_unit != null)
        {
            float baseAttackDamage = _unit.PermanentAttackDamage;
            float baseAttackSpeed = _unit.PermanentAttackSpeed;
            float attackDamageBonus = 0;
            float attackSpeedBonusPercent = 0;

            foreach (var buff in _activeBuffs)
            {
                if (buff.Source is BuffStatEffect buffEffect)
                {
                    if (buffEffect.statToBuff == StatType.AttackDamage)
                        attackDamageBonus += buffEffect.value;
                    else if (buffEffect.statToBuff == StatType.AttackSpeed && buffEffect.isPercentage)
                        attackSpeedBonusPercent += buffEffect.value;
                }
            }

            float finalAttackDamage = baseAttackDamage + attackDamageBonus;
            float finalAttackSpeed = baseAttackSpeed * (1 + attackSpeedBonusPercent);
            _unit.ApplyStatModifiers(finalAttackDamage, finalAttackSpeed);
        }
        
        if (_monster != null)
        {
            float moveSpeedMultiplier = CalculateTotalSlowMultiplier();
            moveSpeedMultiplier = Mathf.Max(0.1f, moveSpeedMultiplier);
            _monster.ApplyMoveSpeedModifier(moveSpeedMultiplier);
        }
    }
    
    #endregion

    #region Status Effect System
    
    private void UpdateStatusEffects(float deltaTime)
    {
        if (_activeStatusEffects.Count == 0) return;
        
        bool needsRecalc = false;
        float currentTime = Time.time;
        
        for (int i = _activeStatusEffects.Count - 1; i >= 0; i--)
        {
            var effect = _activeStatusEffects[i];
            
            if (effect.TickInterval > 0 && effect.DamagePerTick > 0 && currentTime >= effect.NextTickTime)
            {
                ApplyDotDamage(effect);
                effect.NextTickTime = currentTime + effect.TickInterval;
            }
            
            effect.RemainingDuration -= deltaTime;
            
            if (effect.RemainingDuration <= 0)
            {
                _activeStatusEffects.RemoveAt(i);
                needsRecalc = true;
            }
        }
        
        if (needsRecalc)
        {
            RefreshEffectFlags();
            RecalculateStats();
        }
    }
    
    private void ApplyDotDamage(ActiveStatusEffect effect)
    {
        if (TryGetComponent<IEnemy>(out var enemy))
        {
            enemy.TakeDamage(effect.DamagePerTick, effect.DamageType);
        }
    }
    
    public void ApplyStatusEffect(
        StatusEffectType type,
        float duration,
        GameObject caster,
        float tickInterval = 0f,
        float damagePerTick = 0f,
        float slowMultiplier = 1f,
        DamageType damageType = DamageType.Physical)
    {
        var existing = _activeStatusEffects.FirstOrDefault(e => e.Type == type && e.Caster == caster);
        
        if (existing != null)
        {
            existing.RemainingDuration = Mathf.Max(existing.RemainingDuration, duration);
        }
        else
        {
            var newEffect = new ActiveStatusEffect(
                type, duration, caster, 
                tickInterval, damagePerTick, slowMultiplier, damageType);
            _activeStatusEffects.Add(newEffect);
        }
        
        RefreshEffectFlags();
        RecalculateStats();
    }
    
    public void RemoveStatusEffect(StatusEffectType type)
    {
        int removed = _activeStatusEffects.RemoveAll(e => e.Type == type);
        if (removed > 0)
        {
            RefreshEffectFlags();
            RecalculateStats();
        }
    }
    
    public void ClearAllStatusEffects()
    {
        if (_activeStatusEffects.Count > 0)
        {
            _activeStatusEffects.Clear();
            RefreshEffectFlags();
            RecalculateStats();
        }
    }
    
    private void RefreshEffectFlags()
    {
        _currentEffects = StatusEffectType.None;
        foreach (var effect in _activeStatusEffects)
        {
            _currentEffects |= effect.Type;
        }
    }
    
    private float CalculateTotalSlowMultiplier()
    {
        float multiplier = 1f;
        
        foreach (var effect in _activeStatusEffects)
        {
            if (effect.SlowMultiplier < 1f)
            {
                multiplier *= effect.SlowMultiplier;
            }
        }
        
        return multiplier;
    }
    
    #endregion

    #region Convenience Methods

    public void ApplyStun(float duration, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Stunned, duration, caster);

    public void ApplySlow(float duration, float slowMultiplier, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Slowed, duration, caster, slowMultiplier: slowMultiplier);

    public void ApplyRoot(float duration, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Rooted, duration, caster);

    public void ApplySilence(float duration, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Silenced, duration, caster);

    public void ApplyBurn(float duration, float damagePerTick, float tickInterval, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Burning, duration, caster, tickInterval, damagePerTick, 1f, DamageType.Magic);

    public void ApplyFrostbite(float duration, float damagePerTick, float tickInterval, float slowMultiplier, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Frostbitten, duration, caster, tickInterval, damagePerTick, slowMultiplier, DamageType.Magic);

    public void ApplyBleed(float duration, float damagePerTick, float tickInterval, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Bleeding, duration, caster, tickInterval, damagePerTick, 1f, DamageType.Physical);

    public void ApplyPoison(float duration, float damagePerTick, float tickInterval, GameObject caster)
        => ApplyStatusEffect(StatusEffectType.Poisoned, duration, caster, tickInterval, damagePerTick, 1f, DamageType.Magic);

    #endregion
}

public class ActiveBuff
{
    public ScriptableObject Source { get; }
    public float timer;
    public GameObject Caster { get; }

    public ActiveBuff(ScriptableObject source, float duration, GameObject caster)
    {
        Source = source;
        timer = duration;
        Caster = caster;
    }
}
