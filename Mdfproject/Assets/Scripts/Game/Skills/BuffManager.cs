// Assets/Scripts/Game/Skills/BuffManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class BuffManager : MonoBehaviour
{
    private readonly List<ActiveBuff> activeBuffs = new List<ActiveBuff>();

    private Unit unit;
    private Monster monster;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        monster = GetComponent<Monster>();
    }

    // ✅ [수정] 게임 상태 변경 이벤트를 구독/해지하는 로직 추가
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
        if (activeBuffs.Count == 0) return;

        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            var buff = activeBuffs[i];
            buff.timer -= Time.deltaTime;
            if (buff.timer <= 0)
            {
                activeBuffs.RemoveAt(i);
                RecalculateStats();
            }
        }
    }
    
    // ✅ [수정] 전투 종료 시 모든 버프를 제거하는 이벤트 핸들러 추가
    private void HandleGameStateChange(GameManagers.GameState newState)
    {
        // 새로운 상태가 'Prepare' 단계라면 모든 버프를 제거합니다.
        if (newState == GameManagers.GameState.Prepare)
        {
            ClearAllBuffs();
        }
    }

    /// <summary>
    /// 이 컴포넌트가 관리하는 모든 버프와 디버프를 제거하고 스탯을 초기화합니다.
    /// </summary>
    public void ClearAllBuffs()
    {
        if (activeBuffs.Count > 0)
        {
            activeBuffs.Clear();
            RecalculateStats();
            Debug.Log($"<color=orange>{gameObject.name}의 모든 버프/디버프 효과가 제거되었습니다.</color>");
        }
    }


    public void ApplyBuff(BuffStatEffect buffEffect, GameObject caster)
    {
        if (caster == null)
        {
            Debug.LogError("버프 시전자(caster)가 null입니다. 버프를 적용할 수 없습니다.");
            return;
        }
        
        var existingBuff = activeBuffs.FirstOrDefault(b => b.Source == buffEffect && b.Caster == caster);

        if (existingBuff != null)
        {
            existingBuff.timer = buffEffect.duration;
            Debug.Log($"{caster.name}가 {gameObject.name}에게 건 {buffEffect.name} 버프 지속시간 갱신!");
        }
        else
        {
            activeBuffs.Add(new ActiveBuff(buffEffect, buffEffect.duration, caster));
            Debug.Log($"{caster.name}가 {gameObject.name}에게 {buffEffect.name} 버프 신규 적용!");
        }
        
        RecalculateStats();
    }
    
    public void ApplyDebuff(SlowDebuffEffect debuffEffect, GameObject caster)
    {
        if (caster == null) return;

        var existingDebuff = activeBuffs.FirstOrDefault(b => b.Source == debuffEffect && b.Caster == caster);

        if (existingDebuff != null)
        {
            existingDebuff.timer = debuffEffect.duration;
        }
        else
        {
            activeBuffs.Add(new ActiveBuff(debuffEffect, debuffEffect.duration, caster));
        }
        
        RecalculateStats();
    }

    public void RecalculateStats()
    {
        if (unit != null)
        {
            float baseAttackDamage = unit.GetPermanentAdjustedBaseAttackDamage();
            float baseAttackSpeed = unit.GetPermanentAdjustedBaseAttackSpeed();

            float attackDamageBonus = 0;
            float attackSpeedBonusPercent = 0;

            foreach (var buff in activeBuffs)
            {
                if (buff.Source is BuffStatEffect buffEffect)
                {
                    if (buffEffect.statToBuff == StatType.AttackDamage)
                    {
                        attackDamageBonus += buffEffect.value;
                    }
                    else if (buffEffect.statToBuff == StatType.AttackSpeed && buffEffect.isPercentage)
                    {
                        attackSpeedBonusPercent += buffEffect.value;
                    }
                }
            }

            float finalAttackDamage = baseAttackDamage + attackDamageBonus;
            float finalAttackSpeed = baseAttackSpeed * (1 + attackSpeedBonusPercent);
            
            unit.ApplyStatModifiers(finalAttackDamage, finalAttackSpeed);
            
            Debug.Log($"<color=cyan>{gameObject.name} 스탯 재계산 완료: ATK {finalAttackDamage:F1}, ASPD {finalAttackSpeed:F2}</color>");
        }
        
        if (monster != null)
        {
            // 슬로우 디버프 효과 계산
            float moveSpeedMultiplier = 1f;
            
            foreach (var buff in activeBuffs)
            {
                if (buff.Source is SlowDebuffEffect slowEffect)
                {
                    // 슬로우 효과 누적 (곱연산)
                    moveSpeedMultiplier *= slowEffect.moveSpeedMultiplier;
                }
            }
            
            // 최소 이동속도 10%로 제한
            moveSpeedMultiplier = Mathf.Max(0.1f, moveSpeedMultiplier);
            
            monster.ApplyMoveSpeedModifier(moveSpeedMultiplier);
            
            if (moveSpeedMultiplier < 1f)
            {
                Debug.Log($"<color=purple>{gameObject.name} 슬로우 적용: 이동속도 x{moveSpeedMultiplier:F2}</color>");
            }
        }
    }
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
