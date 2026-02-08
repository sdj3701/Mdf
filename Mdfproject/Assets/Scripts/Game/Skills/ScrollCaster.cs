// Assets/Scripts/Game/Skills/ScrollCaster.cs
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 마법 스크롤 발동용 가상 시전자 컴포넌트.
/// 생성 시 Allegiance를 Monster 진영으로 설정하여 TargetingStrategy가 올바르게 작동합니다.
/// - AlliesInRadius → 아군 몬스터 탐색
/// - EnemiesInRadius → 적군 유닛 탐색
/// </summary>
public class ScrollCaster : MonoBehaviour
{
    private Allegiance _allegiance;

    #region 초기화
    /// <summary>
    /// ScrollCaster를 초기화합니다. 몬스터 진영으로 Allegiance를 설정합니다.
    /// </summary>
    public void Initialize()
    {
        _allegiance = GetComponent<Allegiance>();
        if (_allegiance == null)
        {
            _allegiance = gameObject.AddComponent<Allegiance>();
        }
        
        // 몬스터 진영으로 설정 (아군=몬스터, 적군=유닛)
        _allegiance.SetAsMonster();
    }
    #endregion

    #region 스킬 발동
    /// <summary>
    /// 스킬을 발동합니다. SkillData의 TargetingStrategy와 Effects를 사용합니다.
    /// </summary>
    public void CastSkill(SkillData skillData)
    {
        if (skillData == null)
        {
            Debug.LogError("[ScrollCaster] SkillData가 null입니다.");
            Destroy(gameObject);
            return;
        }

        if (skillData.targetingStrategy == null || skillData.effects == null || skillData.effects.Count == 0)
        {
            Debug.LogError($"[ScrollCaster] SkillData '{skillData.skillName}'의 TargetingStrategy 또는 Effects가 설정되지 않았습니다.");
            Destroy(gameObject);
            return;
        }

        // 타겟 탐색 (Allegiance 기반 - 아군=몬스터, 적=유닛)
        List<GameObject> targets = skillData.targetingStrategy.FindTargets(
            gameObject, transform.position, skillData.range);

        Debug.Log($"<color=magenta>[ScrollCaster] 스킬 '{skillData.skillName}' 발동! 타겟 수: {targets.Count}</color>");

        // 효과 적용
        foreach (var effect in skillData.effects)
        {
            if (effect != null)
            {
                effect.ApplyEffect(null, gameObject, targets, skillData.range, skillData.targetingStrategy);
            }
        }

        // VFX 생성
        if (skillData.vfxPrefab != null)
        {
            GameObject vfx = Instantiate(skillData.vfxPrefab, transform.position, Quaternion.identity);
            Destroy(vfx, 5f);
        }

        // ScrollCaster 오브젝트 제거 (약간의 딜레이 후)
        Destroy(gameObject, 0.1f);
    }
    #endregion
}
