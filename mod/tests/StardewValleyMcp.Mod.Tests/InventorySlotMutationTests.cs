using NUnit.Framework;

namespace StardewValleyMcp.Mod.Tests;

public sealed class InventorySlotMutationTests
{
    [Test]
    public void PlannerCoversMoveSwapAndSameSlotNoChange()
    {
        var source = Item("wood", 20);
        var destination = Item("stone", 8);

        var move = InventorySlotMutationPlanner.Plan(
            0, source, 2, null, new object?[] { source, destination, null }
        );
        var swap = InventorySlotMutationPlanner.Plan(
            0, source, 1, destination, new object?[] { source, destination, null }
        );
        var noChange = InventorySlotMutationPlanner.Plan(
            0, source, 0, source, new object?[] { source, destination, null }
        );

        Assert.Multiple(() =>
        {
            Assert.That(move.Plan!.Kind, Is.EqualTo(InventorySlotMutationKind.Move));
            Assert.That(swap.Plan!.Kind, Is.EqualTo(InventorySlotMutationKind.Swap));
            Assert.That(noChange.Plan!.Kind, Is.EqualTo(InventorySlotMutationKind.NoChange));
            Assert.That(noChange.Plan.Changed, Is.False);
        });
    }

    [Test]
    public void PlannerRejectsOutOfRangeStaleAndDuplicateIdentity()
    {
        var source = new object();
        var other = new object();
        var backpack = new object?[] { source, other };

        var outOfRange = InventorySlotMutationPlanner.Plan(0, source, 2, null, backpack);
        var staleSource = InventorySlotMutationPlanner.Plan(0, other, 1, other, backpack);
        var staleDestination = InventorySlotMutationPlanner.Plan(0, source, 1, null, backpack);
        var duplicate = InventorySlotMutationPlanner.Plan(
            0, source, 1, source, new object?[] { source, source }
        );

        Assert.Multiple(() =>
        {
            Assert.That(outOfRange.Status, Is.EqualTo(InventorySlotMutationPlanStatus.Invalid));
            Assert.That(staleSource.Status, Is.EqualTo(InventorySlotMutationPlanStatus.Stale));
            Assert.That(staleDestination.Status, Is.EqualTo(InventorySlotMutationPlanStatus.Stale));
            Assert.That(duplicate.Status, Is.EqualTo(InventorySlotMutationPlanStatus.Unavailable));
        });
    }

    [Test]
    public void SamePlanIncludesBothSlotsAndBothObjectIdentities()
    {
        var source = new object();
        var destination = new object();
        var plan = Ready(0, source, 1, destination, new object?[] { source, destination });

        Assert.Multiple(() =>
        {
            Assert.That(InventorySlotMutationPlanner.SamePlan(plan, plan with { }), Is.True);
            Assert.That(InventorySlotMutationPlanner.SamePlan(
                plan, plan with { DestinationIdentity = new object() }), Is.False);
            Assert.That(InventorySlotMutationPlanner.SamePlan(
                plan, plan with { DestinationSlot = 2 }), Is.False);
        });
    }

    [Test]
    public void MoveWritesSourceNullBeforeDestinationWithoutDualParent()
    {
        var source = Item("wood", 20, new object());
        var backend = new FakeBackend(source, null);
        var plan = Ready(0, source, 1, null, backend.Slots);

        var commit = InventorySlotMutationExecutor.Commit(backend, plan);
        commit.Complete();

        Assert.Multiple(() =>
        {
            Assert.That(backend.Slots, Is.EqualTo(new object?[] { null, source }));
            Assert.That(backend.Writes, Is.EqualTo(new[]
            {
                "write:0:null",
                "write:1:source",
            }));
            Assert.That(backend.DuplicateObserved, Is.False);
            Assert.That(source.Attachment, Is.Not.Null);
            Assert.That(source.Stack, Is.EqualTo(20));
        });
    }

    [Test]
    public void SwapNeverMergesCompatibleStacksAndClearsBothSlotsFirst()
    {
        var source = Item("wood", 20);
        var destination = Item("wood", 30);
        var backend = new FakeBackend(source, destination);
        var plan = Ready(0, source, 1, destination, backend.Slots);

        var commit = InventorySlotMutationExecutor.Commit(backend, plan);
        commit.Complete();

        Assert.Multiple(() =>
        {
            Assert.That(backend.Slots[0], Is.SameAs(destination));
            Assert.That(backend.Slots[1], Is.SameAs(source));
            Assert.That(source.Stack, Is.EqualTo(20));
            Assert.That(destination.Stack, Is.EqualTo(30));
            Assert.That(backend.Writes, Is.EqualTo(new[]
            {
                "write:0:null",
                "write:1:null",
                "write:0:destination",
                "write:1:source",
            }));
            Assert.That(backend.DuplicateObserved, Is.False);
        });
    }

    [Test]
    public void SameSlotNoChangeDoesNotWriteAndRollbackIsAlsoNoOp()
    {
        var source = Item("wood", 20);
        var backend = new FakeBackend(source, null);
        var plan = Ready(0, source, 0, source, backend.Slots);

        var commit = InventorySlotMutationExecutor.Commit(backend, plan);
        commit.Rollback();

        Assert.Multiple(() =>
        {
            Assert.That(backend.Slots[0], Is.SameAs(source));
            Assert.That(backend.Writes, Is.Empty);
        });
    }

    [TestCase(false, (int)InventorySlotMutationPoint.SourceCleared)]
    [TestCase(false, (int)InventorySlotMutationPoint.SourceWrittenToDestination)]
    [TestCase(true, (int)InventorySlotMutationPoint.SourceCleared)]
    [TestCase(true, (int)InventorySlotMutationPoint.DestinationCleared)]
    [TestCase(true, (int)InventorySlotMutationPoint.DestinationWrittenToSource)]
    [TestCase(true, (int)InventorySlotMutationPoint.SourceWrittenToDestination)]
    public void FaultAfterEveryMutationPointRestoresExactTwoSlotOwnership(
        bool swap,
        int pointValue
    )
    {
        var fixture = Fixture(swap);
        var point = (InventorySlotMutationPoint)pointValue;

        Assert.Throws<InjectedMutationException>(() =>
            InventorySlotMutationExecutor.Commit(
                fixture.Backend,
                fixture.Plan,
                current =>
                {
                    if (current == point)
                        throw new InjectedMutationException();
                }
            ));

        AssertRestored(fixture);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReturnedCommitRollsBackAfterPostconditionFailure(bool swap)
    {
        var fixture = Fixture(swap);
        var commit = InventorySlotMutationExecutor.Commit(
            fixture.Backend,
            fixture.Plan
        );

        commit.Rollback();

        AssertRestored(fixture);
        var rollbackWrites = fixture.Backend.Writes.Skip(swap ? 4 : 2).ToArray();
        Assert.That(rollbackWrites.Take(2), Is.EqualTo(swap
            ? new[] { "write:1:null", "write:0:null" }
            : new[] { "write:1:null", "write:0:source" }));
    }

    [Test]
    public void RollbackWriteFailureStillAttemptsIndependentRecovery()
    {
        var fixture = Fixture(swap: true);

        var error = Assert.Throws<InventorySlotMutationRollbackException>(() =>
            InventorySlotMutationExecutor.Commit(
                fixture.Backend,
                fixture.Plan,
                point =>
                {
                    if (point != InventorySlotMutationPoint.SourceWrittenToDestination)
                        return;
                    fixture.Backend.FailNextWriteAtSlot = 1;
                    throw new InjectedMutationException();
                }
            ));

        Assert.Multiple(() =>
        {
            Assert.That(error!.InnerException, Is.TypeOf<AggregateException>());
            Assert.That(fixture.Backend.WritesAfterFailure,
                Does.Contain("write:0:null"));
            Assert.That(fixture.Backend.Slots[1], Is.SameAs(fixture.Source));
            Assert.That(fixture.Backend.WritesAfterFailure,
                Does.Not.Contain("write:1:destination"));
            Assert.That(fixture.Backend.DuplicateObserved, Is.False);
        });
    }

    [Test]
    public void RollbackReadFailureStillAttemptsOtherSlotRecovery()
    {
        var fixture = Fixture(swap: true);

        Assert.Throws<InventorySlotMutationRollbackException>(() =>
            InventorySlotMutationExecutor.Commit(
                fixture.Backend,
                fixture.Plan,
                point =>
                {
                    if (point != InventorySlotMutationPoint.SourceWrittenToDestination)
                        return;
                    fixture.Backend.FailNextReadAtSlot = 1;
                    throw new InjectedMutationException();
                }
            ));

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Backend.WritesAfterFailure,
                Does.Contain("write:0:null"));
            Assert.That(fixture.Backend.DuplicateObserved, Is.False);
        });
    }

    [Test]
    public void RollbackNeverOverwritesUnknownOccupant()
    {
        var fixture = Fixture(swap: false);
        var unknown = new object();

        var error = Assert.Throws<InventorySlotMutationRollbackException>(() =>
            InventorySlotMutationExecutor.Commit(
                fixture.Backend,
                fixture.Plan,
                point =>
                {
                    if (point != InventorySlotMutationPoint.SourceWrittenToDestination)
                        return;
                    fixture.Backend.WriteSlot(0, unknown);
                    throw new InjectedMutationException();
                }
            ));

        Assert.Multiple(() =>
        {
            Assert.That(error!.InnerException, Is.TypeOf<AggregateException>());
            Assert.That(fixture.Backend.Slots[0], Is.SameAs(unknown));
            Assert.That(fixture.Backend.Slots[1], Is.Null);
            Assert.That(fixture.Backend.Writes.Last(), Is.Not.EqualTo("write:0:source"));
            Assert.That(fixture.Backend.DuplicateObserved, Is.False);
        });
    }

    private static MutationFixture Fixture(bool swap)
    {
        var source = Item("source", 20);
        var destination = swap ? Item("destination", 30) : null;
        var backend = new FakeBackend(source, destination);
        return new MutationFixture(
            backend,
            Ready(0, source, 1, destination, backend.Slots),
            source,
            destination
        );
    }

    private static InventorySlotMutationPlan Ready(
        int sourceSlot,
        object source,
        int destinationSlot,
        object? destination,
        IReadOnlyList<object?> backpack
    ) => InventorySlotMutationPlanner.Plan(
        sourceSlot,
        source,
        destinationSlot,
        destination,
        backpack
    ).Plan!;

    private static void AssertRestored(MutationFixture fixture)
    {
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Backend.Slots[0], Is.SameAs(fixture.Source));
            Assert.That(fixture.Backend.Slots[1], Is.SameAs(fixture.Destination));
            Assert.That(fixture.Backend.DuplicateObserved, Is.False);
        });
    }

    private static FakeItem Item(string kind, int stack, object? attachment = null) =>
        new(kind, stack, attachment);
    private sealed record FakeItem(string Kind, int Stack, object? Attachment = null);
    private sealed record MutationFixture(
        FakeBackend Backend,
        InventorySlotMutationPlan Plan,
        FakeItem Source,
        FakeItem? Destination
    );

    private sealed class FakeBackend : IInventorySlotMutationBackend
    {
        private readonly object _source;
        private readonly object? _destination;
        private bool _failureStarted;

        public FakeBackend(object source, object? destination)
        {
            _source = source;
            _destination = destination;
            Slots = new object?[] { source, destination };
        }

        public object?[] Slots { get; }
        public List<string> Writes { get; } = new();
        public List<string> WritesAfterFailure { get; } = new();
        public int? FailNextWriteAtSlot { get; set; }
        public int? FailNextReadAtSlot { get; set; }
        public bool DuplicateObserved { get; private set; }

        public object? ReadSlot(int slot)
        {
            if (FailNextReadAtSlot == slot)
            {
                FailNextReadAtSlot = null;
                _failureStarted = true;
                throw new InjectedRollbackException();
            }
            return Slots[slot];
        }

        public void WriteSlot(int slot, object? item)
        {
            var operation = $"write:{slot}:{Label(item)}";
            Writes.Add(operation);
            if (_failureStarted)
                WritesAfterFailure.Add(operation);
            if (FailNextWriteAtSlot == slot)
            {
                FailNextWriteAtSlot = null;
                _failureStarted = true;
                throw new InjectedRollbackException();
            }
            Slots[slot] = item;
            var nonNull = Slots.Where(value => value is not null).ToArray();
            DuplicateObserved |= nonNull.Distinct(ReferenceEqualityComparer.Instance).Count()
                != nonNull.Length;
            if (DuplicateObserved)
                throw new InvalidOperationException("检测到同一对象同时挂在两个 Slot");
        }

        private string Label(object? item) => item is null
            ? "null"
            : ReferenceEquals(item, _source)
                ? "source"
                : ReferenceEquals(item, _destination)
                    ? "destination"
                    : "unknown";
    }

    private sealed class InjectedMutationException : Exception { }
    private sealed class InjectedRollbackException : Exception { }
}
