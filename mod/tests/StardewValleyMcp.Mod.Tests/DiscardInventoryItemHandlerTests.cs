using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class DiscardInventoryItemHandlerTests
{
    private const string InstanceId = "91919191-1919-4919-8919-191919191919";

    [Test]
    public void ValidationRequiresRefPositiveQuantityAndRevision()
    {
        var fixture = NewFixture();
        var valid = ValidRequest(fixture, 2);
        Assert.That(fixture.Handler.Validate(new CommandRequest
        {
            DiscardInventoryItem = new DiscardInventoryItemRequest(),
        })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
        valid.Quantity = 0;
        Assert.That(fixture.Handler.Validate(new CommandRequest
        {
            DiscardInventoryItem = valid,
        })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
        valid = ValidRequest(fixture, 1);
        valid.Quantity = (uint)int.MaxValue + 1u;
        var capturesBefore = fixture.Runtime.Captures;
        Assert.That(fixture.Handler.Validate(new CommandRequest
        {
            DiscardInventoryItem = valid,
        })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
        Assert.That(fixture.Runtime.Captures, Is.EqualTo(capturesBefore));
        valid = ValidRequest(fixture, 2);
        valid.PlayerInventoryRevision = "bad";
        Assert.That(fixture.Handler.Validate(new CommandRequest
        {
            DiscardInventoryItem = valid,
        })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
    }

    [Test]
    public void PartialStackUsesCloneAndNativeTrashExactlyOnce()
    {
        var fixture = NewFixture(stack: 10, currentSlot: 1, refund: 4);
        var source = fixture.Runtime.Items[0]!;
        var continuation = Start(fixture, ValidRequest(fixture, 3));

        Assert.That(continuation.Tick(ContinuationStopSignal.None),
            Is.TypeOf<ContinuationStep.Pending>());
        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Runtime.Items[0], Is.SameAs(source));
            Assert.That(source.Stack, Is.EqualTo(7));
            Assert.That(fixture.Runtime.Trashed, Has.Count.EqualTo(1));
            Assert.That(fixture.Runtime.Trashed[0], Is.Not.SameAs(source));
            Assert.That(fixture.Runtime.Trashed[0].Stack, Is.EqualTo(3));
            Assert.That(fixture.Runtime.TrashCalls, Is.EqualTo(1));
            Assert.That(succeeded.Result.DiscardInventoryItem.DiscardedQuantity,
                Is.EqualTo(3));
            Assert.That(succeeded.Result.DiscardInventoryItem.SourceRemainingQuantity,
                Is.EqualTo(7));
            Assert.That(succeeded.Result.DiscardInventoryItem.MoneyRefunded,
                Is.EqualTo(4));
        });
    }

    [Test]
    public void FullSelectedStackRunsStopLifecycleOnceAndClearsSlot()
    {
        var fixture = NewFixture(stack: 2, currentSlot: 0);
        var continuation = Start(fixture, ValidRequest(fixture, 2));
        continuation.Tick(ContinuationStopSignal.None);

        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Runtime.Items[0], Is.Null);
            Assert.That(fixture.Runtime.HeldEvents, Is.EqualTo(new[] { "stop:wood" }));
            Assert.That(fixture.Runtime.CurrentToolIndex, Is.Zero);
            Assert.That(fixture.Runtime.TrashCalls, Is.EqualTo(1));
            Assert.That(succeeded.Result.DiscardInventoryItem.SourceRemainingQuantity,
                Is.Zero);
        });
    }

    [TestCase(3u, 0, "")]
    [TestCase(10u, 1, "")]
    [TestCase(10u, 0, "stop:wood")]
    public void HeldLifecycleOnlyStopsASelectedFullyRemovedStack(
        uint quantity,
        int selectedSlot,
        string expectedEvents
    )
    {
        var fixture = NewFixture(stack: 10, currentSlot: selectedSlot);
        var continuation = Start(fixture, ValidRequest(fixture, quantity));
        continuation.Tick(ContinuationStopSignal.None);

        var result = continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<ContinuationStep.Succeeded>());
            Assert.That(string.Join(',', fixture.Runtime.HeldEvents),
                Is.EqualTo(expectedEvents));
            Assert.That(fixture.Runtime.CurrentToolIndex, Is.EqualTo(selectedSlot));
        });
    }

    [Test]
    public void NativeNoPriceSentinelSucceedsWithZeroRefund()
    {
        var fixture = NewFixture(stack: 1, refund: -1);
        var continuation = Start(fixture, ValidRequest(fixture, 1));
        continuation.Tick(ContinuationStopSignal.None);

        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(succeeded.Result.DiscardInventoryItem.MoneyBefore,
                Is.EqualTo(100));
            Assert.That(succeeded.Result.DiscardInventoryItem.MoneyAfter,
                Is.EqualTo(100));
            Assert.That(succeeded.Result.DiscardInventoryItem.MoneyRefunded,
                Is.Zero);
            Assert.That(fixture.Runtime.TrashCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void NativeSpecialItemSideEffectIsProjectedAndVerified()
    {
        var fixture = NewFixture(stack: 1, specialItemId: "wood");
        fixture.Runtime.SpecialItems.AddRange(new[] { "other", "wood", "wood" });
        var continuation = Start(fixture, ValidRequest(fixture, 1));
        continuation.Tick(ContinuationStopSignal.None);

        var succeeded = continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.TypeOf<ContinuationStep.Succeeded>());
            Assert.That(fixture.Runtime.SpecialItems,
                Is.EqualTo(new[] { "other", "wood" }));
            Assert.That(fixture.Runtime.TrashCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void NativeRejectionIsZeroSideEffect()
    {
        var fixture = NewFixture(canBeTrashed: false);

        var failed = (ContinuationStep.Failed)Start(fixture, ValidRequest(fixture, 1))
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ItemNotDiscardable));
            Assert.That(fixture.Runtime.Items[0]!.Stack, Is.EqualTo(10));
            Assert.That(fixture.Runtime.TrashCalls, Is.Zero);
        });
    }

    [Test]
    public void QuantityBeyondStackIsRejectedBeforeSecondTick()
    {
        var fixture = NewFixture(stack: 2);

        var failed = (ContinuationStep.Failed)Start(fixture, ValidRequest(fixture, 3))
            .Tick(ContinuationStopSignal.None);

        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.OutOfRange));
        Assert.That(fixture.Runtime.TrashCalls, Is.Zero);
    }

    [Test]
    public void StaleRevisionAndContainerRefAreRejectedBeforeMutation()
    {
        var fixture = NewFixture();
        var stale = ValidRequest(fixture, 1);
        stale.PlayerInventoryRevision = Revision(99);
        var staleFailure = (ContinuationStep.Failed)Start(fixture, stale)
            .Tick(ContinuationStopSignal.None);
        var foreign = ValidRequest(fixture, 1);
        foreign.ItemRef = fixture.Runtime.ContainerItemRef(0);
        var foreignFailure = (ContinuationStep.Failed)Start(fixture, foreign)
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(staleFailure.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(foreignFailure.Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Runtime.TrashCalls, Is.Zero);
        });
    }

    [TestCase("stack")]
    [TestCase("money")]
    [TestCase("special")]
    [TestCase("selected")]
    public void StateChangeBetweenTicksIsStale(string mutation)
    {
        var fixture = NewFixture();
        var continuation = Start(fixture, ValidRequest(fixture, 1));
        continuation.Tick(ContinuationStopSignal.None);
        switch (mutation)
        {
            case "stack": fixture.Runtime.Items[0]!.Stack++; break;
            case "money": fixture.Runtime.Money++; break;
            case "special": fixture.Runtime.SpecialItems.Add("new"); break;
            case "selected": fixture.Runtime.CurrentToolIndex = 0; break;
        }

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
        Assert.That(fixture.Runtime.TrashCalls, Is.Zero);
    }

    [Test]
    public void CancellationBetweenTicksDoesNotDetachOrTrash()
    {
        var fixture = NewFixture();
        var continuation = Start(fixture, ValidRequest(fixture, 1));
        continuation.Tick(ContinuationStopSignal.None);

        var stopped = continuation.Tick(ContinuationStopSignal.CancelRequested);

        Assert.That(stopped, Is.TypeOf<ContinuationStep.Stopped>());
        Assert.That(fixture.Runtime.Items[0]!.Stack, Is.EqualTo(10));
        Assert.That(fixture.Runtime.TrashCalls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PreTrashDetachFailureRollsBackAndNeverCallsTrash(bool fullStack)
    {
        var fixture = NewFixture(stack: fullStack ? 1 : 10, currentSlot: 0);
        var source = fixture.Runtime.Items[0]!;
        fixture.Runtime.ThrowAfterDetach = true;
        var continuation = Start(fixture, ValidRequest(fixture, 1));
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(fixture.Runtime.Items[0], Is.SameAs(source));
            Assert.That(source.Stack, Is.EqualTo(fullStack ? 1 : 10));
            Assert.That(fixture.Runtime.TrashCalls, Is.Zero);
            if (fullStack)
                Assert.That(fixture.Runtime.HeldEvents,
                    Is.EqualTo(new[] { "stop:wood", "start:wood" }));
            else
                Assert.That(fixture.Runtime.HeldEvents, Is.Empty);
        });
    }

    [Test]
    public void TrashExceptionReturnsUnknownWithoutSurfaceRollbackOrReplay()
    {
        var fixture = NewFixture(stack: 10, refund: 5, specialItemId: "wood");
        fixture.Runtime.SpecialItems.Add("wood");
        fixture.Runtime.ThrowAfterTrashEffects = true;
        var continuation = Start(fixture, ValidRequest(fixture, 3));
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.CommitOutcomeUnknown));
            Assert.That(fixture.Runtime.Items[0]!.Stack, Is.EqualTo(7));
            Assert.That(fixture.Runtime.Money, Is.EqualTo(105));
            Assert.That(fixture.Runtime.SpecialItems, Is.Empty);
            Assert.That(fixture.Runtime.TrashCalls, Is.EqualTo(1));
            Assert.That(continuation.CanCancel, Is.False);
        });
    }

    [Test]
    public void PostconditionFailureAfterNativeReturnIsUnknownAndNotCompensated()
    {
        var fixture = NewFixture(stack: 10, refund: 5);
        fixture.Runtime.CorruptMoneyAfterTrash = true;
        var continuation = Start(fixture, ValidRequest(fixture, 2));
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.CommitOutcomeUnknown));
            Assert.That(fixture.Runtime.Items[0]!.Stack, Is.EqualTo(8));
            Assert.That(fixture.Runtime.TrashCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void OtherSlotFactMutationAfterTrashIsUnknown()
    {
        var fixture = NewFixture(stack: 10);
        fixture.Runtime.MutateOtherItemQualityAfterTrash = true;
        var continuation = Start(fixture, ValidRequest(fixture, 2));
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.CommitOutcomeUnknown));
            Assert.That(fixture.Runtime.Items[1]!.Quality, Is.EqualTo(1));
            Assert.That(fixture.Runtime.TrashCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void CommitterDoesNotCallTrashWhenDetachCannotBeRestored()
    {
        var source = new FakeItem("source", 2);
        var unknown = new FakeItem("unknown", 1);
        var backend = new PrimitiveBackend(new FakeItem?[] { source }, 0)
        {
            ThrowAfterDetach = true,
            ReplaceWithOnDetachFailure = unknown,
        };
        var plan = Plan(source, quantity: 2, current: 0);

        var error = Assert.Throws<DiscardInventoryBeforeTrashException>(() =>
            DiscardInventoryItemCommitter.Commit(backend, plan));

        Assert.Multiple(() =>
        {
            Assert.That(error!.RollbackConfirmed, Is.False);
            Assert.That(backend.Items[0], Is.SameAs(unknown));
            Assert.That(backend.TrashCalls, Is.Zero);
        });
    }

    [Test]
    public void UnconfirmedPreTrashRollbackIsUnknown()
    {
        var fixture = NewFixture(stack: 1, currentSlot: 0);
        fixture.Runtime.ThrowAfterDetach = true;
        fixture.Runtime.ReplaceSourceOnDetachFailure = new FakeItem("unknown", 1);
        var continuation = Start(fixture, ValidRequest(fixture, 1));
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.CommitOutcomeUnknown));
            Assert.That(fixture.Runtime.TrashCalls, Is.Zero);
            Assert.That(fixture.Runtime.Items[0]!.Id, Is.EqualTo("unknown"));
        });
    }

    private static Fixture NewFixture(
        int stack = 10,
        int currentSlot = 1,
        bool canBeTrashed = true,
        int refund = 0,
        string? specialItemId = null
    )
    {
        var refs = new OpaqueRefStore(InstanceId);
        var runtime = new FakeRuntime(refs, new FakeItem?[]
        {
            new("wood", stack),
            new("tool", 1),
            null,
        }, currentSlot)
        {
            CanTrash = canBeTrashed,
            Refund = refund,
            SpecialItemId = specialItemId,
        };
        return new Fixture(runtime, new DiscardInventoryItemHandler(refs, runtime));
    }

    private static DiscardInventoryItemRequest ValidRequest(
        Fixture fixture,
        uint quantity
    ) => new()
    {
        ItemRef = fixture.Runtime.ItemRef(0),
        Quantity = quantity,
        PlayerInventoryRevision = fixture.Runtime.Capture().PlayerSnapshot!.InventoryRevision,
    };

    private static ICommandContinuation Start(
        Fixture fixture,
        DiscardInventoryItemRequest request
    )
    {
        var command = new CommandRequest { DiscardInventoryItem = request };
        Assert.That(fixture.Handler.Validate(command), Is.Null);
        return fixture.Handler.Start("command", command);
    }

    private static string Revision(int value) => value.ToString("x").PadLeft(64, '0');

    private static DiscardInventoryPlan Plan(
        FakeItem source,
        int quantity,
        int current
    ) => new(
        0,
        source,
        source.Stack,
        quantity,
        source,
        source.Stack - quantity,
        current,
        source,
        100,
        100,
        Array.Empty<string>(),
        Array.Empty<string>()
    );

    private sealed record Fixture(
        FakeRuntime Runtime,
        DiscardInventoryItemHandler Handler
    );

    private sealed class FakeItem
    {
        public FakeItem(string id, int stack, int quality = 0)
        {
            Id = id;
            Stack = stack;
            Quality = quality;
        }
        public string Id { get; }
        public int Stack { get; set; }
        public int Quality { get; set; }
    }

    private sealed class FakeRuntime
        : IDiscardInventoryItemRuntimeAdapter,
            ITrashSemantics
    {
        private readonly OpaqueRefStore _refs;
        private readonly object _player = new();
        private readonly object _backing = new();
        private readonly FakeInventoryOwner _owner;
        private readonly FakeInventoryOwner _containerOwner;
        private int _version;

        public FakeRuntime(OpaqueRefStore refs, FakeItem?[] items, int currentToolIndex)
        {
            _refs = refs;
            Items = items;
            CurrentToolIndex = currentToolIndex;
            _owner = new FakeInventoryOwner(items);
            _containerOwner = new FakeInventoryOwner(
                items,
                InventoryItemProvenance.Container
            );
        }

        public FakeItem?[] Items { get; }
        public int CurrentToolIndex { get; set; }
        public int Money { get; set; } = 100;
        public int TrashCanLevel { get; set; } = 2;
        public List<string> SpecialItems { get; } = new();
        public bool CanTrash { get; set; } = true;
        public int Refund { get; set; }
        public string? SpecialItemId { get; set; }
        public bool ThrowAfterDetach { get; set; }
        public FakeItem? ReplaceSourceOnDetachFailure { get; set; }
        public bool ThrowAfterTrashEffects { get; set; }
        public bool CorruptMoneyAfterTrash { get; set; }
        public bool MutateOtherItemQualityAfterTrash { get; set; }
        public int TrashCalls { get; private set; }
        public List<FakeItem> Trashed { get; } = new();
        public List<string> HeldEvents { get; } = new();
        public int Captures { get; private set; }

        public Ref ItemRef(int slot)
        {
            var item = Items[slot]!;
            return _refs.ObserveInventoryItem(_owner, slot, item, item.Id);
        }

        public Ref ContainerItemRef(int slot)
        {
            var item = Items[slot]!;
            return _refs.ObserveInventoryItem(_containerOwner, slot, item, item.Id);
        }

        public DiscardInventoryCapture Capture()
        {
            Captures++;
            var backpack = Items.Select(item => item is null
                ? null
                : new DiscardInventoryItemRuntimeFact(item, item.Stack, item.Id))
                .ToArray();
            var snapshot = new InventorySnapshot
            {
                ContainerKind = "player",
                SlotCount = checked((uint)Items.Length),
                InventoryRevision = Revision(_version),
            };
            snapshot.Slots.Add(Items.Select((item, index) => new InventorySlot
            {
                Index = checked((uint)index),
                Item = item is null ? null : new ItemFact
                {
                    QualifiedItemId = item.Id,
                    DisplayName = item.Id,
                    Stack = checked((uint)item.Stack),
                    Quality = checked((uint)item.Quality),
                    Category = "0",
                },
            }));
            return new DiscardInventoryCapture(
                DiscardInventoryCaptureStatus.Ready,
                null,
                null,
                _player,
                _backing,
                snapshot,
                backpack,
                CurrentToolIndex,
                Items[CurrentToolIndex],
                Money,
                TrashCanLevel,
                SpecialItems.ToArray(),
                this
            );
        }

        public bool CanBeTrashed(DiscardInventoryCapture capture, int sourceSlot) =>
            CanBeTrashed(capture.Backpack[sourceSlot]!.Identity);

        public DiscardInventoryPlan PrepareCommit(
            DiscardInventoryCapture capture,
            int sourceSlot,
            int quantity
        ) => DiscardInventoryPlanBuilder.Prepare(capture, sourceSlot, quantity, this);

        public void Commit(DiscardInventoryCapture capture, DiscardInventoryPlan plan)
        {
            DiscardInventoryItemCommitter.Commit(new RuntimeBackend(this), plan);
            _version++;
        }

        bool ITrashSemantics.CanBeTrashed(object item) => CanTrash;
        public bool CanBeTrashed(object item) => CanTrash;
        public object CloneForQuantity(object item, int quantity) =>
            new FakeItem(((FakeItem)item).Id, quantity);
        public int GetReclamationPrice(object item, object player) => Refund;
        public string? GetSpecialItemId(object item) => SpecialItemId;
        public void Trash(object item)
        {
            TrashCalls++;
            var value = (FakeItem)item;
            Trashed.Add(value);
            Money += Math.Max(0, Refund);
            if (SpecialItemId is not null)
                SpecialItems.Remove(SpecialItemId);
            if (CorruptMoneyAfterTrash)
                Money++;
            if (MutateOtherItemQualityAfterTrash)
                Items[1]!.Quality++;
            if (ThrowAfterTrashEffects)
                throw new InjectedFailure();
        }

        private sealed class RuntimeBackend : IDiscardInventoryItemBackend
        {
            private readonly FakeRuntime _owner;
            public RuntimeBackend(FakeRuntime owner) => _owner = owner;
            public int CurrentToolIndex => _owner.CurrentToolIndex;
            public object? CurrentItem => _owner.Items[CurrentToolIndex];
            public int Money => _owner.Money;
            public object? ReadSlot(int slot) => _owner.Items[slot];
            public int ReadStack(object item) => ((FakeItem)item).Stack;
            public void WriteStack(object item, int stack)
            {
                ((FakeItem)item).Stack = stack;
                MaybeThrow();
            }
            public void WriteSlot(int slot, object? item)
            {
                _owner.Items[slot] = (FakeItem?)item;
                MaybeThrow();
            }
            public IReadOnlyList<string> ReadSpecialItems() =>
                _owner.SpecialItems.ToArray();
            public void StopBeingHeld(object item) =>
                _owner.HeldEvents.Add($"stop:{((FakeItem)item).Id}");
            public void StartBeingHeld(object item) =>
                _owner.HeldEvents.Add($"start:{((FakeItem)item).Id}");
            public void Trash(object item) => _owner.Trash(item);
            private void MaybeThrow()
            {
                if (_owner.ThrowAfterDetach)
                {
                    _owner.ThrowAfterDetach = false;
                    if (_owner.ReplaceSourceOnDetachFailure is not null)
                        _owner.Items[0] = _owner.ReplaceSourceOnDetachFailure;
                    throw new InjectedFailure();
                }
            }
        }
    }

    private sealed class FakeInventoryOwner : IInventoryRefOwner
    {
        private readonly object _identity = new();
        private readonly FakeItem?[] _items;
        public FakeInventoryOwner(
            FakeItem?[] items,
            InventoryItemProvenance provenance = InventoryItemProvenance.Player
        )
        {
            _items = items;
            Provenance = provenance;
        }
        public InventoryItemProvenance Provenance { get; }
        public bool TryGetIdentity(out object identity)
        {
            identity = _identity;
            return true;
        }
        public InventorySlotLookup ResolveCurrentSlot(int slot)
        {
            if (slot < 0 || slot >= _items.Length)
                return new InventorySlotLookup(InventorySlotLookupStatus.Stale);
            var item = _items[slot];
            return new InventorySlotLookup(
                InventorySlotLookupStatus.Resolved,
                item,
                item?.Id ?? ""
            );
        }
    }

    private sealed class PrimitiveBackend : IDiscardInventoryItemBackend
    {
        public PrimitiveBackend(FakeItem?[] items, int current)
        {
            Items = items;
            CurrentToolIndex = current;
        }
        public FakeItem?[] Items { get; }
        public int CurrentToolIndex { get; }
        public object? CurrentItem => Items[CurrentToolIndex];
        public int Money => 100;
        public int TrashCalls { get; private set; }
        public bool ThrowAfterDetach { get; set; }
        public FakeItem? ReplaceWithOnDetachFailure { get; set; }
        public object? ReadSlot(int slot) => Items[slot];
        public int ReadStack(object item) => ((FakeItem)item).Stack;
        public void WriteStack(object item, int stack) => ((FakeItem)item).Stack = stack;
        public void WriteSlot(int slot, object? item)
        {
            Items[slot] = (FakeItem?)item;
            if (ThrowAfterDetach)
            {
                Items[slot] = ReplaceWithOnDetachFailure;
                throw new InjectedFailure();
            }
        }
        public IReadOnlyList<string> ReadSpecialItems() => Array.Empty<string>();
        public void StopBeingHeld(object item) { }
        public void StartBeingHeld(object item) { }
        public void Trash(object item) => TrashCalls++;
    }

    private sealed class InjectedFailure : Exception { }

}
