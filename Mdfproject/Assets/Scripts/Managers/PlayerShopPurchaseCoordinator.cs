using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Owns the asynchronous reserve, spawn, commit, and rollback lifecycle for shop purchases.
/// PlayerManager remains the networked state owner; this coordinator contains no durable state.
/// </summary>
internal sealed class PlayerShopPurchaseCoordinator
{
    private readonly PlayerManager _player;
    private readonly HashSet<int> _pendingSlots = new HashSet<int>();

    internal PlayerShopPurchaseCoordinator(PlayerManager player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    internal bool IsPending(int shopSlotIndex)
    {
        return _pendingSlots.Contains(shopSlotIndex);
    }

    internal async UniTask<PlayerManager.PurchaseUnitResult> ExecuteAsync(
        int shopSlotIndex,
        CancellationToken cancellationToken)
    {
        GameManagers gameManagers = GameManagers.Instance;
        bool networkSessionExpected =
            (gameManagers != null && gameManagers.Runner != null && gameManagers.Runner.IsRunning) ||
            (_player.Object != null && _player.Object.IsValid);
        if (networkSessionExpected &&
            (_player.Runner == null || !_player.Runner.IsRunning || gameManagers == null ||
             gameManagers.Runner != _player.Runner || _player.Object == null ||
             !_player.Object.IsValid || !_player.Object.HasStateAuthority))
        {
            return Failure(shopSlotIndex, "state_authority_required");
        }

        ShopManager shopManager = _player.shopManager;
        FieldManager fieldManager = _player.fieldManager;
        if (shopManager == null || !shopManager.IsDatabaseLoaded)
        {
            return Failure(shopSlotIndex, "shop_not_ready");
        }
        if (fieldManager == null)
        {
            return Failure(shopSlotIndex, "field_not_ready");
        }
        if (gameManagers == null || gameManagers.currentState != GameManagers.GameState.Prepare ||
            gameManagers.IsSequenceTransitioning)
        {
            return Failure(shopSlotIndex, "command_requires_stable_prepare_phase");
        }
        if (!_pendingSlots.Add(shopSlotIndex))
        {
            return Failure(shopSlotIndex, "shop_slot_purchase_pending");
        }

        Unit placedUnit = null;
        int committedCost = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ShopItem> items = shopManager.GetCurrentShopItems();
            if (shopSlotIndex < 0 || shopSlotIndex >= items.Count)
            {
                return Failure(shopSlotIndex, "shop_slot_out_of_range");
            }
            if (shopManager.IsSlotSold(shopSlotIndex))
            {
                return Failure(shopSlotIndex, "shop_slot_already_sold");
            }

            ShopItem observedItem = items[shopSlotIndex];
            if (observedItem.UnitData == null)
            {
                return Failure(shopSlotIndex, "shop_item_missing_unit_data");
            }

            int observedRevision = _player.CurrentShopSnapshotRevision;
            int observedCost = observedItem.CalculatedCost;
            UnitData observedUnitData = observedItem.UnitData;
            int observedStarLevel = observedItem.StarLevel;
            if (observedCost < 0 || _player.GetGold() < observedCost)
            {
                return Failure(shopSlotIndex, "insufficient_gold");
            }

            bool markAsAIPurchased = ComponentRegistry.Has<AIPlayerController>(_player.playerId.ToString());
            FieldManager.UnitPlacementResult placement = await fieldManager.TryCreateAndPlaceUnitOnFieldAsync(
                observedUnitData,
                observedStarLevel,
                markAsAIPurchased,
                suppressCombination: true,
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!placement.Succeeded)
            {
                return Failure(
                    shopSlotIndex,
                    string.IsNullOrEmpty(placement.FailureReason)
                        ? "unit_placement_failed"
                        : placement.FailureReason);
            }
            placedUnit = placement.Unit;

            items = shopManager.GetCurrentShopItems();
            bool transactionStillCurrent =
                gameManagers == GameManagers.Instance &&
                gameManagers.currentState == GameManagers.GameState.Prepare &&
                !gameManagers.IsSequenceTransitioning &&
                _player.CurrentShopSnapshotRevision == observedRevision &&
                shopSlotIndex >= 0 && shopSlotIndex < items.Count &&
                !shopManager.IsSlotSold(shopSlotIndex) &&
                ReferenceEquals(items[shopSlotIndex].UnitData, observedUnitData) &&
                items[shopSlotIndex].StarLevel == observedStarLevel &&
                items[shopSlotIndex].CalculatedCost == observedCost &&
                _player.GetGold() >= observedCost;

            if (!transactionStillCurrent)
            {
                fieldManager.TryRollbackPlacedUnit(placement.Unit, "PurchaseStateChanged");
                return Failure(shopSlotIndex, "purchase_state_changed_during_spawn");
            }
            if (!_player.SpendGold(observedCost))
            {
                fieldManager.TryRollbackPlacedUnit(placement.Unit, "PurchaseGoldCommitFailed");
                return Failure(shopSlotIndex, "gold_commit_failed");
            }
            committedCost = observedCost;

            shopManager.MarkSlotAsPurchased(shopSlotIndex);
            fieldManager.CheckForCombination();
            return PlayerManager.PurchaseUnitResult.Success(shopSlotIndex, observedCost, placement.Unit);
        }
        catch (OperationCanceledException)
        {
            Rollback(fieldManager, placedUnit, committedCost, "PurchaseCancelled");
            throw;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, _player);
            Rollback(fieldManager, placedUnit, committedCost, "PurchaseException");
            return Failure(shopSlotIndex, "purchase_transaction_failed");
        }
        finally
        {
            _pendingSlots.Remove(shopSlotIndex);
        }
    }

    private void Rollback(FieldManager fieldManager, Unit placedUnit, int committedCost, string context)
    {
        if (placedUnit != null)
        {
            fieldManager.TryRollbackPlacedUnit(placedUnit, context);
        }
        if (committedCost > 0)
        {
            _player.AddGold(committedCost);
        }
    }

    private static PlayerManager.PurchaseUnitResult Failure(int shopSlotIndex, string reason)
    {
        return PlayerManager.PurchaseUnitResult.Failure(shopSlotIndex, reason);
    }
}
