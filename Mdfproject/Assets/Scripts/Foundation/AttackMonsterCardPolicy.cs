public readonly struct AttackMonsterCardPolicyInput
{
    public AttackMonsterCardPolicyInput(
        bool hasMonster,
        bool isBoss,
        int remainingCount,
        int blackMagicCost,
        int availableBlackMagic)
    {
        HasMonster = hasMonster;
        IsBoss = isBoss;
        RemainingCount = remainingCount;
        BlackMagicCost = blackMagicCost;
        AvailableBlackMagic = availableBlackMagic;
    }

    public bool HasMonster { get; }
    public bool IsBoss { get; }
    public int RemainingCount { get; }
    public int BlackMagicCost { get; }
    public int AvailableBlackMagic { get; }
}

public readonly struct AttackMonsterCardPolicyState
{
    public AttackMonsterCardPolicyState(
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

    public bool HasPortrait { get; }
    public bool CanInteract { get; }
    public bool IsExhausted { get; }
    public bool IsUnaffordable { get; }
    public string CountText { get; }
}

/// <summary>Single source of truth for legacy and UI Toolkit monster-card availability.</summary>
public static class AttackMonsterCardPolicy
{
    public static AttackMonsterCardPolicyState Resolve(AttackMonsterCardPolicyInput input)
    {
        if (!input.HasMonster)
        {
            return new AttackMonsterCardPolicyState(false, false, false, false, string.Empty);
        }

        int remaining = input.RemainingCount < 0 ? 0 : input.RemainingCount;
        int cost = input.BlackMagicCost < 0 ? 0 : input.BlackMagicCost;
        bool exhausted = remaining == 0;
        bool canAfford = input.IsBoss || input.AvailableBlackMagic >= cost;
        return new AttackMonsterCardPolicyState(
            true,
            !exhausted && canAfford,
            exhausted,
            !exhausted && !canAfford,
            input.IsBoss ? $"x{remaining}" : cost.ToString());
    }
}
