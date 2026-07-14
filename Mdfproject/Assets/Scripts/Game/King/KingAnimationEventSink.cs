using UnityEngine;

/// <summary>
/// Receives animation events authored on base-unit clips used by the visual-only King clone.
/// King combat timing is authority-driven by PlayerManager, so these presentation events must not
/// apply gameplay a second time; the receiver only prevents Unity from reporting missing methods.
/// </summary>
public sealed class KingAnimationEventSink : MonoBehaviour
{
    public void AnimEvent_AttackImpact()
    {
    }

    public void AnimEvent_SkillEnd()
    {
    }
}
