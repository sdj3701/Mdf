using UnityEngine;

public class UnitAnimationEventProxy : MonoBehaviour
{
    private Unit _unit;

    public void Initialize(Unit unit)
    {
        _unit = unit;
    }

    public void AnimEvent_AttackImpact()
    {
        if (_unit != null)
        {
            _unit.AnimEvent_AttackImpact();
        }
    }

    public void AnimEvent_SkillEnd()
    {
        if (_unit != null)
        {
            _unit.AnimEvent_SkillEnd();
        }
    }
}
