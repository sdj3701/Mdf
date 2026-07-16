public readonly struct AttackMonsterCardState
{
    public bool HasPortrait { get; }
    public bool CanInteract { get; }
    public bool IsExhausted { get; }
    public bool IsUnaffordable { get; }
    public string CountText { get; }
    public AttackMonsterCardPolicyState PolicyState => new AttackMonsterCardPolicyState(
        HasPortrait,
        CanInteract,
        IsExhausted,
        IsUnaffordable,
        CountText);

    public AttackMonsterCardState(
        bool hasPortrait,
        bool canInteract,
        bool isExhausted,
        bool isUnaffordable,
        string countText)
    {
        HasPortrait = hasPortrait;
        CanInteract = canInteract;
        IsExhausted = isExhausted;
        IsUnaffordable = isUnaffordable;
        CountText = countText ?? string.Empty;
    }
}

/// <summary>
/// Keeps legacy and UI Toolkit monster cards on the same availability rules.
/// Exhausted boss entitlements retain their portrait and x0 count, but cannot be selected.
/// </summary>
public static class AttackMonsterCardPresentation
{
    public static AttackMonsterCardState Resolve(MonsterPoolEntry entry, int availableBlackMagic)
    {
        AttackMonsterCardPolicyState state = AttackMonsterCardPolicy.Resolve(
            new AttackMonsterCardPolicyInput(
                entry != null && entry.MonsterData != null,
                entry != null && entry.IsBoss,
                entry != null ? entry.RemainingCount : 0,
                entry != null && entry.MonsterData != null ? entry.MonsterData.blackMagicCost : 0,
                availableBlackMagic));
        return new AttackMonsterCardState(
            state.HasPortrait,
            state.CanInteract,
            state.IsExhausted,
            state.IsUnaffordable,
            state.CountText);
    }
}
