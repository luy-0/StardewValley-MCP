using NUnit.Framework;

namespace StardewValleyMcp.Mod.Tests;

public sealed class InventoryTransferPlannerTests
{
    [Test]
    public void FillsCompatibleStacksBeforeFirstEmptySlotInIndexOrder()
    {
        var source = Item("wood", 12, 99);
        var target0 = Item("wood", 97, 99);
        var target2 = Item("wood", 95, 99);
        var result = InventoryTransferPlanner.Plan(
            0,
            new InventoryTransferItem?[] { source },
            new InventoryTransferItem?[] { target0, null, target2, null },
            10
        );

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(InventoryTransferPlanStatus.Success));
            Assert.That(result.Value!.SourceRemaining, Is.EqualTo(2));
            Assert.That(result.Value.Writes.Select(write => (write.Slot, write.Quantity)),
                Is.EqualTo(new[] { (0, 2), (2, 4), (1, 4) }));
        });
    }

    [Test]
    public void UsesFirstEmptyForUnstackableAndSupportsExactCapacity()
    {
        var tool = Item("axe", 1, 1);
        var result = InventoryTransferPlanner.Plan(
            0,
            new InventoryTransferItem?[] { tool },
            new InventoryTransferItem?[] { Item("stone", 1, 99), null },
            1
        );

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(InventoryTransferPlanStatus.Success));
            Assert.That(result.Value!.Writes.Single().Slot, Is.EqualTo(1));
            Assert.That(result.Value.SourceRemaining, Is.Zero);
        });
    }

    [Test]
    public void CapacityShortageAndSpecialItemAreRejectedWithoutAPlan()
    {
        var shortage = InventoryTransferPlanner.Plan(
            0,
            new InventoryTransferItem?[] { Item("wood", 5, 99) },
            new InventoryTransferItem?[] { Item("stone", 99, 99) },
            5
        );
        var special = InventoryTransferPlanner.Plan(
            0,
            new InventoryTransferItem?[] { Item("recipe", 1, 1, special: true) },
            new InventoryTransferItem?[1],
            1
        );

        Assert.Multiple(() =>
        {
            Assert.That(shortage.Status, Is.EqualTo(InventoryTransferPlanStatus.NotReady));
            Assert.That(shortage.Message, Does.Contain("重新查询"));
            Assert.That(special.Status, Is.EqualTo(InventoryTransferPlanStatus.Invalid));
        });
    }

    [Test]
    public void QuantityAboveSourceStackIsOutOfRange()
    {
        var result = InventoryTransferPlanner.Plan(
            0,
            new InventoryTransferItem?[] { Item("wood", 2, 99) },
            new InventoryTransferItem?[1],
            3
        );
        Assert.That(result.Status, Is.EqualTo(InventoryTransferPlanStatus.OutOfRange));
    }

    private static InventoryTransferItem Item(string kind, int stack, int maximum, bool special = false)
    {
        var identity = new FakeIdentity(kind);
        return new InventoryTransferItem(
            identity,
            stack,
            maximum,
            special,
            kind,
            other => maximum > 1 && other.Identity is FakeIdentity candidate && candidate.Kind == kind,
            _ => new FakeIdentity(kind)
        );
    }

    private sealed record FakeIdentity(string Kind);
}
