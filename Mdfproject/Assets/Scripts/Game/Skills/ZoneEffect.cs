// Assets/Scripts/Game/Skills/ZoneEffect.cs
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 장판형 지속 이펙트 - 일정 시간 동안 범위 내 대상에게 주기적으로 효과를 적용합니다.
/// </summary>
[CreateAssetMenu(fileName = "New ZoneEffect", menuName = "Game/Skills/Effects/Zone Effect")]
public class ZoneEffect : SkillEffect, IDurationEffect
{
    [Header("장판 설정")]
    [Tooltip("장판이 지속되는 시간(초)입니다.")]
    public float zoneDuration = 5f;

    [Tooltip("효과가 적용되는 주기(초)입니다. 예: 1초마다 데미지")]
    public float tickInterval = 1f;

    [Header("적용 효과")]
    [Tooltip("매 틱마다 범위 내 대상에게 적용할 효과들입니다.")]
    public List<SkillEffect> effectsPerTick;

    [Header("시각 효과")]
    [Tooltip("장판 시각 효과 프리팹입니다. ZoneController가 자동으로 추가됩니다.")]
    public GameObject zonePrefab;

    // IDurationEffect 인터페이스 구현
    public float Duration => zoneDuration;

    public override void ApplyEffect(MonoBehaviour runner, GameObject caster, List<GameObject> targets, float skillRange, TargetingStrategy targetingStrategy)
    {
        // 장판은 시전자 위치에 생성됩니다 (게임 설계상 항상 시전자 중심)
        Vector3 spawnPosition = caster.transform.position;

        // 장판 오브젝트 생성
        GameObject zoneObject;
        
        if (zonePrefab != null)
        {
            zoneObject = Object.Instantiate(zonePrefab, spawnPosition, Quaternion.identity);
        }
        else
        {
            // 프리팹이 없으면 빈 오브젝트 생성
            zoneObject = new GameObject($"Zone_{name}");
            zoneObject.transform.position = spawnPosition;
        }

        // ZoneController 추가 및 초기화
        var controller = zoneObject.GetComponent<ZoneController>();
        if (controller == null)
        {
            controller = zoneObject.AddComponent<ZoneController>();
        }

        controller.Initialize(this, caster, runner, skillRange, targetingStrategy);
    }
}
