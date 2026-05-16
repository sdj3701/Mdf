using UnityEngine;

public sealed class UnitAttackVfxInstance : MonoBehaviour
{
    private UnitAttackVfxPresenter _owner;

    public void Initialize(UnitAttackVfxPresenter owner)
    {
        _owner = owner;
    }

    public bool IsOwnedBy(UnitAttackVfxPresenter owner)
    {
        return _owner == owner;
    }

    public void ClearOwner()
    {
        _owner = null;
    }
}
