// Assets/Scripts/Commands/Sync/NotifyPurchaseSucceededCommand.cs

using UnityEngine;

/// <summary>
/// 유닛 구매 성공을 클라이언트에 알리는 커맨드
/// </summary>
public class NotifyPurchaseSucceededCommand : ICommand
{
    public int PlayerId { get; set; }
    public int SlotIndex { get; private set; }

    public NotifyPurchaseSucceededCommand(int playerId, int slotIndex)
    {
        PlayerId = playerId;
        SlotIndex = slotIndex;
    }

    public void Execute()
    {
        // UI 업데이트를 위한 이벤트 트리거
        GameEvents.TriggerUnitPurchaseSucceeded(PlayerId, default(ShopItem), SlotIndex);
        Debug.Log($"<color=green>[NotifyPurchaseSucceededCommand] Player {PlayerId}: 슬롯 {SlotIndex} 구매 성공 알림</color>");
    }
}
