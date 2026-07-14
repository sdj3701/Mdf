using UnityEngine;

public readonly struct AttackMonsterCardState
{
    public bool HasPortrait { get; }
    public bool CanInteract { get; }
    public bool IsExhausted { get; }
    public bool IsUnaffordable { get; }
    public string CountText { get; }

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
        bool hasPortrait = entry != null && entry.MonsterData != null;
        if (!hasPortrait)
        {
            return new AttackMonsterCardState(false, false, false, false, string.Empty);
        }

        bool isExhausted = entry.IsEmpty;
        bool canAfford = entry.IsBoss ||
                         availableBlackMagic >= Mathf.Max(0, entry.MonsterData.blackMagicCost);
        bool canInteract = !isExhausted && canAfford;
        string countText = entry.IsBoss
            ? $"x{Mathf.Max(0, entry.RemainingCount)}"
            : Mathf.Max(0, entry.MonsterData.blackMagicCost).ToString();

        return new AttackMonsterCardState(
            true,
            canInteract,
            isExhausted,
            !isExhausted && !canAfford,
            countText);
    }
}
