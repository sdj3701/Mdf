using System.Collections.Generic;
using UnityEngine;

public class ScrollCaster : MonoBehaviour
{
    private Allegiance _allegiance;

    public void Initialize()
    {
        _allegiance = GetComponent<Allegiance>();
        if (_allegiance == null)
        {
            _allegiance = gameObject.AddComponent<Allegiance>();
        }

        _allegiance.SetAsMonster();
    }

    public bool CastGameplay(SkillData skillData, out int targetCount)
    {
        targetCount = 0;
        var gm = GameManagers.Instance;
        if (gm != null &&
            gm.Runner != null &&
            gm.Runner.IsRunning &&
            gm.Object != null &&
            !gm.Object.HasStateAuthority)
        {
            Debug.LogError("[ScrollCaster] Gameplay scroll effects require State Authority.");
            Destroy(gameObject);
            return false;
        }

        if (skillData == null)
        {
            Debug.LogError("[ScrollCaster] SkillData is null.");
            Destroy(gameObject);
            return false;
        }

        if (skillData.targetingStrategy == null || skillData.effects == null || skillData.effects.Count == 0)
        {
            Debug.LogError($"[ScrollCaster] SkillData '{skillData.skillName}' is missing targeting or effects.");
            Destroy(gameObject);
            return false;
        }

        List<GameObject> targets = skillData.targetingStrategy.FindTargets(
            gameObject,
            transform.position,
            skillData.range);
        targetCount = targets.Count;

        foreach (var effect in skillData.effects)
        {
            if (effect != null)
            {
                effect.ApplyEffect(null, gameObject, targets, skillData.range, skillData.targetingStrategy);
            }
        }

        Destroy(gameObject, 0.1f);
        return true;
    }

    public void PlayPresentation(SkillData skillData)
    {
        if (skillData?.vfxPrefab != null)
        {
            GameObject vfx = Instantiate(skillData.vfxPrefab, transform.position, Quaternion.identity);
            Destroy(vfx, 5f);
        }

        Destroy(gameObject, 0.1f);
    }

    public void CastSkill(SkillData skillData)
    {
        CastGameplay(skillData, out _);
        PlayPresentation(skillData);
    }
}
