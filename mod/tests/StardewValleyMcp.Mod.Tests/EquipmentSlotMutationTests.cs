using NUnit.Framework;

namespace StardewValleyMcp.Mod.Tests;

public sealed class EquipmentSlotMutationTests
{
    [Test]
    public void PlannerCoversWearReplaceClearAndIdempotentClear()
    {
        var source = new object();
        var old = new object();
        var backpack = new object?[] { source, new object(), null };

        var wear = EquipmentSlotMutationPlanner.Plan(false, 0, source, backpack, null);
        var replace = EquipmentSlotMutationPlanner.Plan(false, 0, source, backpack, old);
        var clear = EquipmentSlotMutationPlanner.Plan(true, null, null, backpack, old);
        var noChange = EquipmentSlotMutationPlanner.Plan(true, null, null, backpack, null);

        Assert.Multiple(() =>
        {
            Assert.That(wear.Plan!.Kind, Is.EqualTo(EquipmentSlotMutationKind.Wear));
            Assert.That(wear.Plan.BackpackDestinationSlot, Is.Null);
            Assert.That(replace.Plan!.Kind, Is.EqualTo(EquipmentSlotMutationKind.Replace));
            Assert.That(replace.Plan.BackpackDestinationSlot, Is.Zero);
            Assert.That(clear.Plan!.Kind, Is.EqualTo(EquipmentSlotMutationKind.Clear));
            Assert.That(clear.Plan.BackpackDestinationSlot, Is.EqualTo(2));
            Assert.That(noChange.Plan!.Kind, Is.EqualTo(EquipmentSlotMutationKind.NoChange));
            Assert.That(noChange.Plan.Changed, Is.False);
        });
    }

    [Test]
    public void FullBackpackAllowsReplaceButRejectsClear()
    {
        var source = new object();
        var old = new object();
        var backpack = new object?[] { source, new object() };

        var replace = EquipmentSlotMutationPlanner.Plan(false, 0, source, backpack, old);
        var clear = EquipmentSlotMutationPlanner.Plan(true, null, null, backpack, old);

        Assert.Multiple(() =>
        {
            Assert.That(replace.Status, Is.EqualTo(EquipmentSlotMutationPlanStatus.Ready));
            Assert.That(replace.Plan!.BackpackDestinationSlot, Is.Zero);
            Assert.That(clear.Status, Is.EqualTo(EquipmentSlotMutationPlanStatus.NotReady));
            Assert.That(clear.Message, Does.Contain("重新查询"));
        });
    }

    [TestCase(12)]
    [TestCase(24)]
    [TestCase(36)]
    public void ClearUsesLowestEmptySlotWithinCapturedUnlockedCapacity(int maxItems)
    {
        var old = new object();
        var backpack = Enumerable.Range(0, maxItems)
            .Select(_ => (object?)new object())
            .ToArray();
        backpack[maxItems - 2] = null;

        var clear = EquipmentSlotMutationPlanner.Plan(
            true,
            null,
            null,
            backpack,
            old
        );

        Assert.That(clear.Plan!.BackpackDestinationSlot, Is.EqualTo(maxItems - 2));
    }

    [TestCase((int)EquipmentSlotMutationKind.Wear)]
    [TestCase((int)EquipmentSlotMutationKind.Replace)]
    [TestCase((int)EquipmentSlotMutationKind.Clear)]
    public void ExecutorCommitsThreeOwnershipBranches(int kindValue)
    {
        var kind = (EquipmentSlotMutationKind)kindValue;
        var source = new object();
        var old = kind == EquipmentSlotMutationKind.Wear ? null : new object();
        var backpack = kind == EquipmentSlotMutationKind.Clear
            ? new object?[] { new object(), null }
            : new object?[] { source, new object() };
        var plan = EquipmentSlotMutationPlanner.Plan(
            kind == EquipmentSlotMutationKind.Clear,
            kind == EquipmentSlotMutationKind.Clear ? null : 0,
            kind == EquipmentSlotMutationKind.Clear ? null : source,
            backpack,
            old
        ).Plan!;
        var backend = new FakeMutationBackend(backpack, old);

        var commit = EquipmentSlotMutationExecutor.Commit(backend, plan);
        commit.Complete();

        Assert.Multiple(() =>
        {
            Assert.That(backend.Equipment, Is.SameAs(
                kind == EquipmentSlotMutationKind.Clear ? null : source));
            if (kind == EquipmentSlotMutationKind.Wear)
                Assert.That(backend.Backpack[0], Is.Null);
            else if (kind == EquipmentSlotMutationKind.Replace)
                Assert.That(backend.Backpack[0], Is.SameAs(old));
            else
                Assert.That(backend.Backpack[1], Is.SameAs(old));
        });
    }

    [TestCase((int)EquipmentSlotMutationKind.Wear, (int)EquipmentSlotMutationPoint.SourceRemoved)]
    [TestCase((int)EquipmentSlotMutationKind.Wear, (int)EquipmentSlotMutationPoint.EquipmentChanged)]
    [TestCase((int)EquipmentSlotMutationKind.Replace, (int)EquipmentSlotMutationPoint.SourceRemoved)]
    [TestCase((int)EquipmentSlotMutationKind.Replace, (int)EquipmentSlotMutationPoint.EquipmentChanged)]
    [TestCase((int)EquipmentSlotMutationKind.Replace, (int)EquipmentSlotMutationPoint.DestinationWritten)]
    [TestCase((int)EquipmentSlotMutationKind.Clear, (int)EquipmentSlotMutationPoint.EquipmentChanged)]
    [TestCase((int)EquipmentSlotMutationKind.Clear, (int)EquipmentSlotMutationPoint.DestinationWritten)]
    public void FaultAfterEachMutationRollsBackExactOwnership(
        int kindValue,
        int faultValue
    )
    {
        var kind = (EquipmentSlotMutationKind)kindValue;
        var fault = (EquipmentSlotMutationPoint)faultValue;
        var fixture = NewFixture(kind);

        Assert.Throws<InjectedMutationException>(() =>
            EquipmentSlotMutationExecutor.Commit(
                fixture.Backend,
                fixture.Plan,
                point =>
                {
                    if (point == fault)
                        throw new InjectedMutationException();
                }
            ));

        AssertRestored(fixture);
    }

    [TestCase((int)EquipmentSlotMutationKind.Wear)]
    [TestCase((int)EquipmentSlotMutationKind.Replace)]
    [TestCase((int)EquipmentSlotMutationKind.Clear)]
    public void PostconditionFailureCanRollbackReturnedCommit(
        int kindValue
    )
    {
        var kind = (EquipmentSlotMutationKind)kindValue;
        var fixture = NewFixture(kind);
        var commit = EquipmentSlotMutationExecutor.Commit(fixture.Backend, fixture.Plan);

        commit.Rollback();

        AssertRestored(fixture);
    }

    [TestCase((int)EquipmentSlotMutationKind.Replace)]
    [TestCase((int)EquipmentSlotMutationKind.Clear)]
    public void RollbackDetachesOldEquipmentFromBackpackBeforeReequipping(
        int kindValue
    )
    {
        var fixture = NewFixture((EquipmentSlotMutationKind)kindValue);
        var commit = EquipmentSlotMutationExecutor.Commit(fixture.Backend, fixture.Plan);
        var forwardCount = fixture.Backend.Operations.Count;

        commit.Rollback();

        var rollback = fixture.Backend.Operations.Skip(forwardCount).ToArray();
        var detach = Array.IndexOf(rollback, $"write:{fixture.Plan.BackpackDestinationSlot}:null");
        var exchange = Array.IndexOf(rollback, "exchange:item");
        Assert.Multiple(() =>
        {
            Assert.That(detach, Is.GreaterThanOrEqualTo(0));
            Assert.That(exchange, Is.GreaterThan(detach));
        });
        AssertRestored(fixture);
    }

    [Test]
    public void ExchangeFailureAfterMutationStillRollsBack()
    {
        var fixture = NewFixture(EquipmentSlotMutationKind.Replace);
        fixture.Backend.FailAfterNextExchange = true;

        Assert.Throws<InjectedMutationException>(() =>
            EquipmentSlotMutationExecutor.Commit(fixture.Backend, fixture.Plan));

        AssertRestored(fixture);
    }

    [Test]
    public void RollbackExchangeFailureStillRestoresBackpackAndShape()
    {
        var fixture = NewFixture(EquipmentSlotMutationKind.Replace);

        var error = Assert.Throws<EquipmentSlotMutationRollbackException>(() =>
            EquipmentSlotMutationExecutor.Commit(
                fixture.Backend,
                fixture.Plan,
                point =>
                {
                    if (point != EquipmentSlotMutationPoint.EquipmentChanged)
                        return;
                    fixture.Backend.FailAfterNextExchange = true;
                    throw new InjectedMutationException();
                }
            ));

        Assert.Multiple(() =>
        {
            Assert.That(error!.InnerException, Is.TypeOf<AggregateException>());
            Assert.That(fixture.Backend.Backpack[0], Is.SameAs(fixture.BackpackBefore[0]));
            Assert.That(fixture.Backend.Equipment, Is.SameAs(fixture.EquipmentBefore));
            Assert.That(fixture.Backend.RestoreShapeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void RollbackReadFailureStillRestoresBackpackAndShape()
    {
        var fixture = NewFixture(EquipmentSlotMutationKind.Wear);

        Assert.Throws<EquipmentSlotMutationRollbackException>(() =>
            EquipmentSlotMutationExecutor.Commit(
                fixture.Backend,
                fixture.Plan,
                point =>
                {
                    if (point != EquipmentSlotMutationPoint.EquipmentChanged)
                        return;
                    fixture.Backend.FailNextEquipmentRead = true;
                    throw new InjectedMutationException();
                }
            ));

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Backend.Backpack[0], Is.SameAs(fixture.BackpackBefore[0]));
            Assert.That(fixture.Backend.RestoreShapeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void RollbackBackpackFailureStillRestoresEquipmentAndShape()
    {
        var fixture = NewFixture(EquipmentSlotMutationKind.Replace);

        Assert.Throws<EquipmentSlotMutationRollbackException>(() =>
            EquipmentSlotMutationExecutor.Commit(
                fixture.Backend,
                fixture.Plan,
                point =>
                {
                    if (point != EquipmentSlotMutationPoint.EquipmentChanged)
                        return;
                    fixture.Backend.FailNextBackpackWrite = true;
                    throw new InjectedMutationException();
                }
            ));

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Backend.Equipment, Is.SameAs(fixture.EquipmentBefore));
            Assert.That(fixture.Backend.RestoreShapeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void RollbackShapeFailureOccursAfterContentRecovery()
    {
        var fixture = NewFixture(EquipmentSlotMutationKind.Clear);

        Assert.Throws<EquipmentSlotMutationRollbackException>(() =>
            EquipmentSlotMutationExecutor.Commit(
                fixture.Backend,
                fixture.Plan,
                point =>
                {
                    if (point != EquipmentSlotMutationPoint.DestinationWritten)
                        return;
                    fixture.Backend.FailRestoreShape = true;
                    throw new InjectedMutationException();
                }
            ));

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Backend.Equipment, Is.SameAs(fixture.EquipmentBefore));
            Assert.That(fixture.Backend.Backpack[1], Is.SameAs(fixture.BackpackBefore[1]));
            Assert.That(fixture.Backend.RestoreShapeCalls, Is.EqualTo(1));
        });
    }

    private static MutationFixture NewFixture(EquipmentSlotMutationKind kind)
    {
        var source = new object();
        var old = kind == EquipmentSlotMutationKind.Wear ? null : new object();
        var backpack = kind == EquipmentSlotMutationKind.Clear
            ? new object?[] { new object(), null, new object() }
            : new object?[] { source, new object(), new object() };
        var before = backpack.ToArray();
        var plan = EquipmentSlotMutationPlanner.Plan(
            kind == EquipmentSlotMutationKind.Clear,
            kind == EquipmentSlotMutationKind.Clear ? null : 0,
            kind == EquipmentSlotMutationKind.Clear ? null : source,
            backpack,
            old
        ).Plan!;
        return new MutationFixture(
            new FakeMutationBackend(backpack, old),
            plan,
            before,
            old
        );
    }

    private static void AssertRestored(MutationFixture fixture)
    {
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Backend.Equipment, Is.SameAs(fixture.EquipmentBefore));
            Assert.That(fixture.Backend.Backpack.Length, Is.EqualTo(fixture.BackpackBefore.Length));
            for (var index = 0; index < fixture.BackpackBefore.Length; index++)
            {
                Assert.That(
                    fixture.Backend.Backpack[index],
                    Is.SameAs(fixture.BackpackBefore[index]),
                    $"slot={index}"
                );
            }
            Assert.That(fixture.Backend.RestoreShapeCalls, Is.EqualTo(1));
        });
    }

    private sealed record MutationFixture(
        FakeMutationBackend Backend,
        EquipmentSlotMutationPlan Plan,
        object?[] BackpackBefore,
        object? EquipmentBefore
    );

    private sealed class FakeMutationBackend : IEquipmentSlotMutationBackend
    {
        public FakeMutationBackend(object?[] backpack, object? equipment)
        {
            Backpack = backpack;
            Equipment = equipment;
        }

        public object?[] Backpack { get; }
        public object? Equipment { get; private set; }
        public bool FailAfterNextExchange { get; set; }
        public bool FailNextEquipmentRead { get; set; }
        public bool FailNextBackpackWrite { get; set; }
        public bool FailRestoreShape { get; set; }
        public int RestoreShapeCalls { get; private set; }
        public List<string> Operations { get; } = new();

        public object? ReadBackpack(int slot)
        {
            Operations.Add($"read:{slot}");
            return Backpack[slot];
        }
        public void WriteBackpack(int slot, object? item)
        {
            Operations.Add($"write:{slot}:{(item is null ? "null" : "item")}");
            if (FailNextBackpackWrite)
            {
                FailNextBackpackWrite = false;
                throw new InjectedMutationException();
            }
            Backpack[slot] = item;
        }
        public object? ReadEquipment()
        {
            Operations.Add("read-equipment");
            if (FailNextEquipmentRead)
            {
                FailNextEquipmentRead = false;
                throw new InjectedMutationException();
            }
            return Equipment;
        }
        public object? ExchangeEquipment(object? item)
        {
            Operations.Add($"exchange:{(item is null ? "null" : "item")}");
            var old = Equipment;
            Equipment = item;
            if (FailAfterNextExchange)
            {
                FailAfterNextExchange = false;
                throw new InjectedMutationException();
            }
            return old;
        }
        public void RestoreShape()
        {
            Operations.Add("restore-shape");
            RestoreShapeCalls++;
            if (FailRestoreShape)
                throw new InjectedMutationException();
        }
    }

    private sealed class InjectedMutationException : Exception { }
}
