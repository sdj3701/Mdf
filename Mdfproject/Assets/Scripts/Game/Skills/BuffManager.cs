// Assets/Scripts/Game/BuffManager.cs (새 파일)
using UnityEngine;
using System.Collections.Generic;
// using Fusion; // 네트워크 도입 시 주석 해제

// public class BuffManager : NetworkBehaviour // 네트워크 도입 시 NetworkBehaviour로 변경
public class BuffManager : MonoBehaviour
{
    // 현재 적용중인 버프/디버프 목록
    private readonly List<ActiveBuff> activeBuffs = new List<ActiveBuff>();

    // 참조 컴포넌트들
    private Unit unit;
    private Monster monster;
    
    // 원본 스탯을 저장해 둘 변수들 (더 많은 스탯 추가 가능)
    private float originalAttackDamage;
    private float originalAttackSpeed;
    private float originalMoveSpeed;

    private void Awake()
    {
        // 이 컴포넌트가 Unit에 붙어있는지, Monster에 붙어있는지 확인
        unit = GetComponent<Unit>();
        monster = GetComponent<Monster>();

        // 초기 원본 스탯 저장
        if (unit != null)
        {
            // UnitData로부터 원본 스탯을 가져옵니다.
            // 주의: Unit.cs의 InitializeStats()가 먼저 호출되어야 정확한 값을 가져올 수 있습니다.
            // 이 부분은 Start()에서 처리하는 것이 더 안전할 수 있습니다.
        }
        else if (monster != null)
        {
            originalMoveSpeed = monster.monsterData.moveSpeed;
        }
    }
    
    // 네트워크가 없을 때는 Unity의 기본 Update 루프를 사용합니다.
    private void Update()
    {
        // 버프/디버프의 지속시간을 관리합니다.
        if (activeBuffs.Count == 0) return;

        // 뒤에서부터 순회해야 리스트에서 아이템을 제거할 때 인덱스 문제가 발생하지 않습니다.
        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            var buff = activeBuffs[i];
            buff.timer -= Time.deltaTime;
            if (buff.timer <= 0)
            {
                activeBuffs.RemoveAt(i);
                // 버프가 끝났으므로 스탯을 다시 계산합니다.
                RecalculateStats();
            }
        }
    }
    /*
    // 네트워크 도입 시 아래 FixedUpdateNetwork()를 사용하고 위 Update()는 제거합니다.
    public override void FixedUpdateNetwork()
    {
        // 서버에서만 버프/디버프의 지속시간을 관리합니다.
        if (!Runner.IsServer) return;

        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            var buff = activeBuffs[i];
            buff.timer -= Runner.DeltaTime;
            if (buff.timer <= 0)
            {
                activeBuffs.RemoveAt(i);
                RecalculateStats();
            }
        }
    }
    */

    // SkillEffect에서 이 메서드를 호출합니다.
    public void ApplyBuff(BuffStatEffect buffEffect)
    {
        // TODO: 동일한 종류의 버프가 이미 있다면 어떻게 처리할지 정책 결정 (중첩, 시간 갱신 등)
        activeBuffs.Add(new ActiveBuff(buffEffect, buffEffect.duration));
        RecalculateStats();
        Debug.Log($"{gameObject.name}에게 {buffEffect.name} 버프 적용!");
    }
    
    public void ApplyDebuff(SlowDebuffEffect debuffEffect)
    {
        activeBuffs.Add(new ActiveBuff(debuffEffect, debuffEffect.duration));
        RecalculateStats();
        Debug.Log($"{gameObject.name}에게 {debuffEffect.name} 디버프 적용!");
    }

    private void RecalculateStats()
    {
        // 이 로직은 Unit과 Monster의 스탯 시스템과 긴밀하게 연동되어야 합니다.
        // 현재 Unit.cs에 스탯 변수들이 private으로 선언되어 있어 직접 수정이 어렵습니다.
        // Unit.cs와 Monster.cs에 스탯을 수정할 수 있는 public 메서드를 만들어야 합니다.
        // 예시: public void ApplyStatModifiers(float attackDamageMod, float attackSpeedMod, float moveSpeedMod)
        
        Debug.LogWarning($"'{gameObject.name}'의 스탯 재계산이 필요합니다. 이 부분은 Unit/Monster 스크립트와 연동하여 구현해야 합니다.");

        // --- 스탯 재계산 로직 예시 ---
        // 1. 모든 스탯을 원본 값으로 초기화합니다.
        //    (예: unit.SetAttackSpeed(originalAttackSpeed); monster.SetMoveSpeed(originalMoveSpeed);)

        // 2. 현재 활성화된 모든 버프/디버프를 순회하며 스탯 수정치를 계산합니다.
        //    float totalAttackSpeedBonus = 0f;
        //    float finalMoveSpeedMultiplier = 1f;
        //    foreach (var buff in activeBuffs)
        //    {
        //        if (buff.Source is BuffStatEffect buffEffect && buffEffect.statToBuff == StatType.AttackSpeed)
        //        {
        //            totalAttackSpeedBonus += buffEffect.value; // (isPercentage 고려 필요)
        //        }
        //        else if (buff.Source is SlowDebuffEffect slowEffect)
        //        {
        //            finalMoveSpeedMultiplier *= slowEffect.moveSpeedMultiplier;
        //        }
        //    }

        // 3. 최종 계산된 수정치를 유닛/몬스터에 적용합니다.
        //    (예: unit.SetAttackSpeed(originalAttackSpeed * (1 + totalAttackSpeedBonus));
        //         monster.SetMoveSpeed(originalMoveSpeed * finalMoveSpeedMultiplier);)
    }
}

/// <summary>
/// 활성화된 버프/디버프의 상태를 추적하기 위한 내부 클래스
/// </summary>
public class ActiveBuff
{
    public ScriptableObject Source { get; }
    public float timer;

    public ActiveBuff(ScriptableObject source, float duration)
    {
        Source = source;
        timer = duration;
    }
}