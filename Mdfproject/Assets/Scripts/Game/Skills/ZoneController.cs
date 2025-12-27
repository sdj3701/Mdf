// Assets/Scripts/Game/Skills/ZoneController.cs
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 장판 오브젝트의 런타임 동작을 제어하는 컴포넌트입니다.
/// ZoneEffect에 의해 자동으로 추가되고 초기화됩니다.
/// </summary>
public class ZoneController : MonoBehaviour
{
    private ZoneEffect zoneEffect;
    private GameObject caster;
    private MonoBehaviour runner;
    private float skillRange;
    private TargetingStrategy targetingStrategy;

    private float remainingDuration;
    private float tickTimer;
    private bool isInitialized = false;

    /// <summary>
    /// 장판을 초기화합니다. ZoneEffect에서 호출됩니다.
    /// </summary>
    public void Initialize(ZoneEffect effect, GameObject caster, MonoBehaviour runner, float skillRange, TargetingStrategy targetingStrategy)
    {
        this.zoneEffect = effect;
        this.caster = caster;
        this.runner = runner;
        this.skillRange = skillRange;
        this.targetingStrategy = targetingStrategy;

        this.remainingDuration = effect.zoneDuration;
        this.tickTimer = 0f; // 즉시 첫 틱 적용
        this.isInitialized = true;

        Debug.Log($"<color=magenta>[Zone] {effect.name} 장판 생성! 지속시간: {effect.zoneDuration}초, 범위: {skillRange}</color>");
    }

    private void Update()
    {
        if (!isInitialized) return;

        // 지속시간 체크
        remainingDuration -= Time.deltaTime;
        if (remainingDuration <= 0)
        {
            Debug.Log($"<color=magenta>[Zone] {zoneEffect.name} 장판 종료!</color>");
            Destroy(gameObject);
            return;
        }

        // 틱 타이머
        tickTimer -= Time.deltaTime;
        if (tickTimer <= 0)
        {
            ApplyTickEffects();
            tickTimer = zoneEffect.tickInterval;
        }
    }

    /// <summary>
    /// 범위 내 대상에게 효과를 적용합니다.
    /// </summary>
    private void ApplyTickEffects()
    {
        if (zoneEffect.effectsPerTick == null || zoneEffect.effectsPerTick.Count == 0)
        {
            Debug.LogWarning($"[Zone] {zoneEffect.name}에 적용할 효과가 설정되지 않았습니다.");
            return;
        }

        // TargetingStrategy를 사용하여 범위 내 대상 탐색
        List<GameObject> targetsInZone = FindTargetsInZone();

        if (targetsInZone.Count == 0) return;

        Debug.Log($"<color=magenta>[Zone] {zoneEffect.name} 틱! 범위 내 대상 {targetsInZone.Count}명에게 효과 적용</color>");

        // 각 효과를 대상들에게 적용
        foreach (var effect in zoneEffect.effectsPerTick)
        {
            if (effect != null)
            {
                effect.ApplyEffect(runner, caster, targetsInZone, skillRange, targetingStrategy);
            }
        }
    }

    /// <summary>
    /// 장판 범위 내의 대상들을 찾습니다.
    /// </summary>
    private List<GameObject> FindTargetsInZone()
    {
        // SkillData의 TargetingStrategy를 사용하여 대상 탐색
        if (targetingStrategy != null)
        {
            return targetingStrategy.FindTargets(caster, transform.position, skillRange);
        }

        // TargetingStrategy가 없으면 빈 리스트 반환 (설정 오류 경고)
        Debug.LogWarning($"[Zone] TargetingStrategy가 전달되지 않았습니다.");
        return new List<GameObject>();
    }

    // 에디터에서 범위 시각화 (Scene 뷰)
    private void OnDrawGizmosSelected()
    {
        if (isInitialized)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawSphere(transform.position, skillRange);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, skillRange);
        }
    }
}
