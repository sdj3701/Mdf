// Assets/Scripts/Game/Monsters/MonsterAnimationEventProxy.cs
using UnityEngine;

/// <summary>
/// Animator가 붙은 자식 오브젝트에서 애니메이션 이벤트를 수신하여 부모 Monster에 전달합니다.
/// UnitAnimationEventProxy와 동일한 패턴을 사용합니다.
/// </summary>
public class MonsterAnimationEventProxy : MonoBehaviour
{
    private Monster _monster;

    /// <summary>
    /// Monster 참조를 설정합니다. Monster.Initialize에서 호출됩니다.
    /// </summary>
    public void Initialize(Monster monster)
    {
        _monster = monster;
    }

    /// <summary>
    /// 공격 애니메이션의 타격 시점에서 호출됩니다.
    /// Animation Event로 설정해야 합니다.
    /// </summary>
    public void AnimEvent_AttackImpact()
    {
        if (_monster != null)
        {
            _monster.AnimEvent_AttackImpact();
        }
    }
}
