using System.IO;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

public sealed class PurchaseAndCombinationTransactionEditModeTests
{
    [Test]
    public void InvalidPlacementReturnsFailureWithoutLeavingCellReserved()
    {
        var root = new GameObject("placement-transaction-test");
        try
        {
            var field = root.AddComponent<FieldManager>();
            field.gridSize = new Vector2Int(2, 2);
            var position = new Vector3Int(0, 0, 0);

            FieldManager.UnitPlacementResult result = field
                .TryCreateUnitAtAsync(null, position, 1)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo("unit_data_missing"));
            Assert.That(field.HasPendingUnitAt(position), Is.False);
            Assert.That(field.GetUnitAt(position), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void InvalidStarLevelReturnsFailureWithoutLeavingCellReserved()
    {
        var root = new GameObject("placement-star-transaction-test");
        var data = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            var field = root.AddComponent<FieldManager>();
            field.gridSize = new Vector2Int(2, 2);
            data.prefabsByStarLevel = new[] { "Unit_Test" };
            var position = new Vector3Int(1, 1, 0);

            FieldManager.UnitPlacementResult result = field
                .TryCreateUnitAtAsync(data, position, 2)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo("unit_star_level_invalid"));
            Assert.That(field.HasPendingUnitAt(position), Is.False);
            Assert.That(field.GetUnitAt(position), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(data);
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PurchaseCommitsEconomyOnlyAfterPlacementAndRollsBackStaleState()
    {
        string source = File.ReadAllText("Assets/Scripts/Managers/PlayerManager.cs");

        int placementIndex = source.IndexOf("await fieldManager.TryCreateAndPlaceUnitOnFieldAsync", System.StringComparison.Ordinal);
        int spendIndex = source.IndexOf("SpendGold(observedCost)", System.StringComparison.Ordinal);
        int soldIndex = source.IndexOf("shopManager.MarkSlotAsPurchased(shopSlotIndex)", System.StringComparison.Ordinal);

        Assert.That(placementIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(spendIndex, Is.GreaterThan(placementIndex));
        Assert.That(soldIndex, Is.GreaterThan(spendIndex));
        Assert.That(source, Does.Contain("_pendingShopPurchaseSlots.Add(shopSlotIndex)"));
        Assert.That(source, Does.Contain("TryRollbackPlacedUnit(placement.Unit"));
        Assert.That(source, Does.Contain("cancellationToken: cancellationToken"));
    }

    [Test]
    public void CombinationPromotesBeforeConsumingIngredients()
    {
        string source = File.ReadAllText("Assets/Scripts/Managers/FieldManager.cs");
        int transactionStart = source.IndexOf("TryCombineUnitsTransactionAsync", System.StringComparison.Ordinal);
        int promotionIndex = source.IndexOf("await baseUnit.Initialize(unitData, newStarLevel", transactionStart, System.StringComparison.Ordinal);
        int consumeIndex = source.IndexOf("RemoveCombinedUnit(unitsToCombine[0]", transactionStart, System.StringComparison.Ordinal);

        Assert.That(transactionStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(promotionIndex, Is.GreaterThan(transactionStart));
        Assert.That(consumeIndex, Is.GreaterThan(promotionIndex));
        Assert.That(source, Does.Contain("TryStageCombinedReplacementAsync"));
        Assert.That(source, Does.Contain("_combinationInProgress"));
        Assert.That(source, Does.Contain("_combinationReservedUnits"));
        Assert.That(source, Does.Contain("AreCombinationInputsCurrent"));
        Assert.That(source, Does.Contain("runner_or_authority_changed_during_load"));
        Assert.That(source, Does.Contain("RemoveOwnedUnitReference(uncommittedUnit)"));
    }
}
