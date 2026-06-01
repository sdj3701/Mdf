// Assets/Scripts/Game/Skills/Effects/AreaDamageEffect.cs
using UnityEngine;
using System.Collections.Generic;
// using Fusion;

[CreateAssetMenu(fileName = "New AreaDamageEffect", menuName = "Game/Skills/Effects/Area Damage")]
public class AreaDamageEffect : SkillEffect
{
    [Header("데미지 설정")]
    public float damageAmount;
    public DamageType damageType;

    // public override void ApplyEffect(NetworkRunner runner, GameObject caster, List<GameObject> targets)
    public override void ApplyEffect(MonoBehaviour runner, GameObject caster, List<GameObject> targets, float skillRange, TargetingStrategy targetingStrategy)
    {
        // if (runner != null && !runner.IsServer) return; // 네트워크 모드에서는 이 라인이 필요합니다.

        var damagedTargets = new HashSet<int>();
        foreach (var target in targets)
        {
            if (TryResolveUniqueEnemy(target, damagedTargets, out var enemy))
            {
                enemy.TakeDamage(damageAmount, damageType);
            }
        }
    }

    private static bool TryResolveUniqueEnemy(GameObject target, HashSet<int> damagedTargets, out IEnemy enemy)
    {
        enemy = null;
        if (target == null || damagedTargets == null)
        {
            return false;
        }

        enemy = target.GetComponentInParent<IEnemy>();
        if (enemy == null)
        {
            return false;
        }

        int key = enemy is MonoBehaviour behaviour
            ? behaviour.GetInstanceID()
            : target.GetInstanceID();
        return damagedTargets.Add(key);
    }
}
