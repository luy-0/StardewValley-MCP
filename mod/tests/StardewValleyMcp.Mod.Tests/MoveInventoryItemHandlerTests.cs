using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class MoveInventoryItemHandlerTests
{
    private const string InstanceId = "93333333-3333-4333-8333-333333333333";

    [Test]
    public void ValidationRequiresBothRefsAndBothRevisions()
    {
        var fixture = NewFixture();
        var request = ValidRequest(fixture, 1);
        request.UiRevision = "bad";

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Handler.Validate(new CommandRequest
            {
                MoveInventoryItem = new MoveInventoryItemRequest(),
            })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Handler.Validate(new CommandRequest
            {
                MoveInventoryItem = request,
            })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MoveAndSwapPreserveObjectIdentityAndStacks(bool swap)
    {
        var fixture = NewFixture(destination: swap ? Item("wood", 30) : null);
        var source = fixture.Runtime.Backpack[0]!;
        var destination = fixture.Runtime.Backpack[1];
        var continuation = Start(fixture, ValidRequest(fixture, 1));

        Assert.That(continuation.Tick(ContinuationStopSignal.None),
            Is.TypeOf<ContinuationStep.Pending>());
        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Runtime.Backpack[0], Is.SameAs(destination));
            Assert.That(fixture.Runtime.Backpack[1], Is.SameAs(source));
            Assert.That(source.Stack, Is.EqualTo(20));
            Assert.That(destination?.Stack, Is.EqualTo(swap ? 30 : null));
            Assert.That(succeeded.Result.MoveInventoryItem.Changed, Is.True);
            Assert.That(succeeded.Result.MoveInventoryItem.Swapped, Is.EqualTo(swap));
            Assert.That(fixture.Runtime.CurrentToolIndex, Is.EqualTo(2));
        });
    }

    [Test]
    public void SameSlotIsIdempotentWithoutWritesCallbacksOrRevisionChange()
    {
        var fixture = NewFixture();
        var request = ValidRequest(fixture, 0);
        var itemRef = request.ItemRef.Clone();
        var slotRef = request.DestinationSlotRef.Clone();
        var continuation = Start(fixture, request);
        continuation.Tick(ContinuationStopSignal.None);

        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(succeeded.Result.MoveInventoryItem.Changed, Is.False);
            Assert.That(succeeded.Result.MoveInventoryItem.Swapped, Is.False);
            Assert.That(succeeded.Result.MoveInventoryItem.PlayerInventoryRevision,
                Is.EqualTo(request.PlayerInventoryRevision));
            Assert.That(fixture.Runtime.Writes, Is.Empty);
            Assert.That(fixture.Runtime.HeldEvents, Is.Empty);
            Assert.That(fixture.Runtime.ResolveItem(itemRef).Status,
                Is.EqualTo(InventoryItemResolveStatus.Resolved));
            Assert.That(fixture.Runtime.ResolveSlot(slotRef).Status,
                Is.EqualTo(UiElementResolveStatus.Resolved));
        });
    }

    [TestCase(UiInventorySide.Container, UiElementKind.ItemSlot, UiEquipmentSlotKind.Unspecified)]
    [TestCase(UiInventorySide.Unspecified, UiElementKind.EquipmentSlot, UiEquipmentSlotKind.Hat)]
    public void DestinationMustBePlayerBackpackItemSlot(
        UiInventorySide side,
        UiElementKind kind,
        UiEquipmentSlotKind equipment
    )
    {
        var fixture = NewFixture();
        var request = ValidRequest(fixture, 1);
        request.DestinationSlotRef = fixture.Runtime.SlotRef(1, side, kind, equipment);

        var failed = (ContinuationStep.Failed)Start(fixture, request)
            .Tick(ContinuationStopSignal.None);

        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
        Assert.That(fixture.Runtime.Writes, Is.Empty);
    }

    [Test]
    public void OldComponentRefIsStale()
    {
        var fixture = NewFixture();
        var request = ValidRequest(fixture, 1);
        fixture.Runtime.ReplaceComponent(1);

        var failed = (ContinuationStep.Failed)Start(fixture, request)
            .Tick(ContinuationStopSignal.None);

        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
    }

    [Test]
    public void SourceMustBelongToPlayerBackpack()
    {
        var fixture = NewFixture();
        var request = ValidRequest(fixture, 1);
        request.ItemRef = fixture.Runtime.ContainerItemRef(0);

        var failed = (ContinuationStep.Failed)Start(fixture, request)
            .Tick(ContinuationStopSignal.None);

        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
        Assert.That(fixture.Runtime.Writes, Is.Empty);
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void EachRevisionIsIndependentlyProtected(bool ui, bool inventory)
    {
        var fixture = NewFixture();
        var request = ValidRequest(fixture, 1);
        if (ui)
            request.UiRevision = FakeRuntime.Revision(99);
        if (inventory)
            request.PlayerInventoryRevision = FakeRuntime.Revision(99);

        var failed = (ContinuationStep.Failed)Start(fixture, request)
            .Tick(ContinuationStopSignal.None);

        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void StackChangesBetweenTicksAreStale(bool source)
    {
        var fixture = NewFixture(destination: Item("stone", 5));
        var continuation = Start(fixture, ValidRequest(fixture, 1));
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Runtime.Backpack[source ? 0 : 1]!.Stack++;

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
        Assert.That(fixture.Runtime.Writes, Is.Empty);
    }

    [Test]
    public void CurrentToolIndexChangeBetweenTicksIsStale()
    {
        var fixture = NewFixture();
        var continuation = Start(fixture, ValidRequest(fixture, 1));
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Runtime.CurrentToolIndex = 1;

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
        Assert.That(fixture.Runtime.Writes, Is.Empty);
    }

    [Test]
    public void PostconditionFailureRollsBackWithConfirmedMessage()
    {
        var fixture = NewFixture();
        var source = fixture.Runtime.Backpack[0];
        fixture.Runtime.KeepRevisionAfterCommit = true;
        var continuation = Start(fixture, ValidRequest(fixture, 1));
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(failed.Error.Message, Does.Not.Contain("无法确认"));
            Assert.That(fixture.Runtime.Backpack[0], Is.SameAs(source));
            Assert.That(fixture.Runtime.Backpack[1], Is.Null);
        });
    }

    [Test]
    public void InternalCommitAutoRollbackIsRecognizedAsRestored()
    {
        var fixture = NewFixture();
        var source = fixture.Runtime.Backpack[0];
        fixture.Runtime.FailWriteAtOrdinal = 2;
        var continuation = Start(fixture, ValidRequest(fixture, 1));
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(failed.Error.Message, Does.Not.Contain("无法确认"));
            Assert.That(fixture.Runtime.Backpack[0], Is.SameAs(source));
            Assert.That(fixture.Runtime.Backpack[1], Is.Null);
        });
    }

    [Test]
    public void SuccessStalesBothOldItemRefsButKeepsSlotRefStable()
    {
        var fixture = NewFixture(destination: Item("stone", 5));
        var request = ValidRequest(fixture, 1);
        var sourceRef = request.ItemRef.Clone();
        var destinationItemRef = fixture.Runtime.ItemRef(1);
        var slotRef = request.DestinationSlotRef.Clone();
        var continuation = Start(fixture, request);
        continuation.Tick(ContinuationStopSignal.None);
        continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Runtime.ResolveItem(sourceRef).Status,
                Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(fixture.Runtime.ResolveItem(destinationItemRef).Status,
                Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(fixture.Runtime.ResolveSlot(slotRef).Status,
                Is.EqualTo(UiElementResolveStatus.Resolved));
        });
    }

    [Test]
    public void CancellationBeforeCommitWritesNothing()
    {
        var fixture = NewFixture();
        var continuation = Start(fixture, ValidRequest(fixture, 1));
        continuation.Tick(ContinuationStopSignal.None);

        var stopped = continuation.Tick(ContinuationStopSignal.CancelRequested);

        Assert.That(stopped, Is.TypeOf<ContinuationStep.Stopped>());
        Assert.That(fixture.Runtime.Writes, Is.Empty);
    }

    [TestCase(2, 0, 1, "")]
    [TestCase(0, 0, 1, "stop:source")]
    [TestCase(0, 0, 1, "stop:source,start:destination")]
    [TestCase(1, 0, 1, "start:source")]
    [TestCase(1, 0, 1, "stop:destination,start:source")]
    public void HeldLifecycleRunsOnceAroundWholeTransaction(
        int current,
        int sourceSlot,
        int destinationSlot,
        string expected
    )
    {
        var withDestination = expected.Contains("destination", StringComparison.Ordinal);
        var source = Item("source", 1);
        var destination = withDestination ? Item("destination", 1) : null;
        var slots = new FakeItem?[] { source, destination, Item("other", 1) };
        var backend = new HeldBackend(slots, current);
        var plan = InventorySlotMutationPlanner.Plan(
            sourceSlot, source, destinationSlot, destination, slots
        ).Plan!;

        var commit = InventorySlotMoveCommitter.Commit(backend, plan);
        commit.Complete();

        Assert.Multiple(() =>
        {
            Assert.That(string.Join(',', backend.Events), Is.EqualTo(expected));
            Assert.That(backend.CurrentToolIndex, Is.EqualTo(current));
        });
    }

    [Test]
    public void StartAfterMutationFailureStopsNewHeldItemBeforeRestoringOldHeldItem()
    {
        var source = Item("source", 1);
        var destination = Item("destination", 1);
        var slots = new FakeItem?[] { source, destination };
        var backend = new HeldBackend(slots, current: 0)
        {
            ThrowAfterFirstStart = true,
        };
        var plan = InventorySlotMutationPlanner.Plan(
            0, source, 1, destination, slots
        ).Plan!;

        Assert.Throws<InjectedHeldException>(() =>
            InventorySlotMoveCommitter.Commit(backend, plan));

        Assert.Multiple(() =>
        {
            Assert.That(backend.Events, Is.EqualTo(new[]
            {
                "stop:source",
                "start:destination",
                "stop:destination",
                "start:source",
            }));
            Assert.That(slots[0], Is.SameAs(source));
            Assert.That(slots[1], Is.SameAs(destination));
            Assert.That(backend.HeldItem, Is.SameAs(source));
            Assert.That(backend.CurrentToolIndex, Is.Zero);
        });
    }

    [Test]
    public void ReturnedCommitRollbackDoesNotTouchOrOverwriteUnknownCurrentOccupant()
    {
        var source = Item("source", 1);
        var destination = Item("destination", 1);
        var unknown = Item("unknown", 1);
        var slots = new FakeItem?[] { source, destination };
        var backend = new HeldBackend(slots, current: 0);
        var plan = InventorySlotMutationPlanner.Plan(
            0, source, 1, destination, slots
        ).Plan!;
        var commit = InventorySlotMoveCommitter.Commit(backend, plan);
        slots[0] = unknown;

        Assert.Throws<InvalidOperationException>(() => commit.Rollback());

        Assert.Multiple(() =>
        {
            Assert.That(slots[0], Is.SameAs(unknown));
            Assert.That(backend.Events, Is.EqualTo(new[]
            {
                "stop:source",
                "start:destination",
            }));
            Assert.That(backend.Events.Any(value => value.Contains("unknown")), Is.False);
        });
    }

    [Test]
    public void CommitCatchDoesNotTouchOrOverwriteUnknownCurrentOccupant()
    {
        var source = Item("source", 1);
        var destination = Item("destination", 1);
        var unknown = Item("unknown", 1);
        var slots = new FakeItem?[] { source, destination };
        var backend = new HeldBackend(slots, current: 0)
        {
            ReplaceCurrentThenThrowOnFirstStart = unknown,
        };
        var plan = InventorySlotMutationPlanner.Plan(
            0, source, 1, destination, slots
        ).Plan!;

        Assert.Throws<InventorySlotMoveRecoveryException>(() =>
            InventorySlotMoveCommitter.Commit(backend, plan));

        Assert.Multiple(() =>
        {
            Assert.That(slots[0], Is.SameAs(unknown));
            Assert.That(backend.Events, Is.EqualTo(new[]
            {
                "stop:source",
                "start:destination",
            }));
            Assert.That(backend.Events.Any(value => value.Contains("unknown")), Is.False);
        });
    }

    private static Fixture NewFixture(FakeItem? destination = null)
    {
        var refs = new OpaqueRefStore(InstanceId);
        var runtime = new FakeRuntime(refs, new[]
        {
            Item("wood", 20),
            destination,
            Item("tool", 1),
        });
        return new Fixture(runtime, new MoveInventoryItemHandler(refs, runtime));
    }

    private static MoveInventoryItemRequest ValidRequest(Fixture fixture, int destination)
    {
        var capture = fixture.Runtime.Capture();
        return new MoveInventoryItemRequest
        {
            ItemRef = fixture.Runtime.ItemRef(0),
            DestinationSlotRef = fixture.Runtime.SlotRef(destination),
            UiRevision = capture.UiRevision,
            PlayerInventoryRevision = capture.PlayerSnapshot!.InventoryRevision,
        };
    }

    private static MoveInventoryItemContinuation Start(
        Fixture fixture,
        MoveInventoryItemRequest request
    )
    {
        var command = new CommandRequest { MoveInventoryItem = request };
        Assert.That(fixture.Handler.Validate(command), Is.Null);
        return (MoveInventoryItemContinuation)fixture.Handler.Start("command", command);
    }

    private static FakeItem Item(string id, int stack) => new(id, stack);
    private sealed record Fixture(FakeRuntime Runtime, MoveInventoryItemHandler Handler);
    private sealed class FakeItem
    {
        public FakeItem(string id, int stack)
        {
            Id = id;
            Stack = stack;
        }
        public string Id { get; }
        public int Stack { get; set; }
    }

    private sealed class FakeRuntime : IInventorySlotMoveRuntimeAdapter
    {
        private readonly OpaqueRefStore _refs;
        private readonly object _menu = new();
        private object _page = new();
        private readonly object _player = new();
        private readonly object _backing = new();
        private readonly FakeInventoryOwner _inventoryOwner;
        private readonly FakeInventoryOwner _containerOwner;
        private readonly FakeUiOwner _uiOwner;
        private readonly object[] _components;
        private int _version;
        private int _writeOrdinal;

        public FakeRuntime(OpaqueRefStore refs, FakeItem?[] backpack)
        {
            _refs = refs;
            Backpack = backpack;
            _components = backpack.Select(_ => new object()).ToArray();
            _inventoryOwner = new FakeInventoryOwner(Backpack);
            _containerOwner = new FakeInventoryOwner(
                Backpack,
                InventoryItemProvenance.Container
            );
            _uiOwner = new FakeUiOwner(_menu, _components);
        }

        public FakeItem?[] Backpack { get; }
        public int CurrentToolIndex { get; set; } = 2;
        public bool KeepRevisionAfterCommit { get; set; }
        public int? FailWriteAtOrdinal { get; set; }
        public List<string> Writes { get; } = new();
        public List<string> HeldEvents { get; } = new();

        public static string Revision(int value) => value.ToString("x").PadLeft(64, '0');
        public InventoryItemResolveResult ResolveItem(Ref reference) =>
            _refs.ResolveInventoryItem(reference);
        public UiElementResolveResult ResolveSlot(Ref reference) =>
            _refs.ResolveUiElement(reference);

        public Ref ItemRef(int slot)
        {
            var item = Backpack[slot]!;
            return _refs.ObserveInventoryItem(_inventoryOwner, slot, item, item.Id);
        }

        public Ref ContainerItemRef(int slot)
        {
            var item = Backpack[slot]!;
            return _refs.ObserveInventoryItem(_containerOwner, slot, item, item.Id);
        }

        public Ref SlotRef(
            int slot,
            UiInventorySide side = UiInventorySide.Player,
            UiElementKind kind = UiElementKind.ItemSlot,
            UiEquipmentSlotKind equipment = UiEquipmentSlotKind.Unspecified
        )
        {
            var session = _refs.BeginUiProjection(_menu);
            var component = _components[slot];
            var reference = _refs.ObserveUiElement(
                session,
                _uiOwner,
                new UiElementBindingIdentity(
                    UiExtractorKind.GameMenu,
                    kind,
                    side,
                    equipment,
                    slot,
                    component,
                    component,
                    $"slot:{slot}"
                )
            );
            _refs.CompleteUiProjection(session);
            return reference;
        }

        public void ReplaceComponent(int slot)
        {
            _components[slot] = new object();
            _uiOwner.Components = _components;
        }

        public InventorySlotMoveCapture Capture()
        {
            var revision = Revision(_version);
            var items = Backpack.Select(RuntimeItem).ToArray();
            var snapshot = new InventorySnapshot
            {
                ContainerKind = "player",
                SlotCount = checked((uint)Backpack.Length),
                InventoryRevision = revision,
            };
            snapshot.Slots.Add(items.Select((item, index) => new InventorySlot
            {
                Index = checked((uint)index),
                Item = item?.PublicFact.Clone(),
            }));
            return new InventorySlotMoveCapture(
                InventorySlotMoveCaptureStatus.Ready,
                _menu,
                _page,
                _player,
                _backing,
                revision,
                snapshot,
                items,
                _components,
                CurrentToolIndex,
                this
            );
        }

        public IInventorySlotMutationCommit Commit(
            InventorySlotMoveCapture capture,
            InventorySlotMutationPlan plan
        )
        {
            var inner = InventorySlotMoveCommitter.Commit(new Backend(this), plan);
            if (plan.Changed && !KeepRevisionAfterCommit)
                _version++;
            return new CommitScope(this, inner, plan.Changed);
        }

        private static InventorySlotMoveItem? RuntimeItem(FakeItem? item) => item is null
            ? null
            : new InventorySlotMoveItem(item, item.Stack, new ItemFact
            {
                QualifiedItemId = item.Id,
                DisplayName = item.Id,
                Stack = checked((uint)item.Stack),
                Category = "0",
            });

        private sealed class Backend : IInventorySlotMoveBackend
        {
            private readonly FakeRuntime _owner;
            public Backend(FakeRuntime owner) => _owner = owner;
            public int CurrentToolIndex => _owner.CurrentToolIndex;
            public object? CurrentItem => _owner.Backpack[CurrentToolIndex];
            public object? ReadSlot(int slot) => _owner.Backpack[slot];
            public void WriteSlot(int slot, object? item)
            {
                _owner._writeOrdinal++;
                if (_owner.FailWriteAtOrdinal == _owner._writeOrdinal)
                    throw new InjectedCommitException();
                _owner.Backpack[slot] = (FakeItem?)item;
                _owner.Writes.Add($"{slot}:{((FakeItem?)item)?.Id ?? "null"}");
            }
            public void StopBeingHeld(object item) =>
                _owner.HeldEvents.Add($"stop:{((FakeItem)item).Id}");
            public void StartBeingHeld(object item) =>
                _owner.HeldEvents.Add($"start:{((FakeItem)item).Id}");
            public void RestoreShape() { }
        }

        private sealed class CommitScope : IInventorySlotMutationCommit
        {
            private readonly FakeRuntime _owner;
            private readonly IInventorySlotMutationCommit _inner;
            private readonly bool _changed;
            public CommitScope(
                FakeRuntime owner,
                IInventorySlotMutationCommit inner,
                bool changed
            )
            {
                _owner = owner;
                _inner = inner;
                _changed = changed;
            }
            public void Complete() => _inner.Complete();
            public void Rollback()
            {
                _inner.Rollback();
                if (_changed && !_owner.KeepRevisionAfterCommit)
                    _owner._version++;
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

    private sealed class FakeUiOwner : IUiElementRefOwner
    {
        private readonly object _menu;
        public FakeUiOwner(object menu, object[] components)
        {
            _menu = menu;
            Components = components;
        }
        public object[] Components { get; set; }
        public bool TryGetMenuIdentity(out object menu)
        {
            menu = _menu;
            return true;
        }
        public UiElementLookup ResolveCurrentElement(UiElementBindingIdentity identity)
        {
            if (identity.Index < 0 || identity.Index >= Components.Length)
                return new UiElementLookup(UiElementLookupStatus.Stale);
            var component = Components[identity.Index];
            return new UiElementLookup(
                UiElementLookupStatus.Resolved,
                component,
                component,
                $"slot:{identity.Index}"
            );
        }
    }

    private sealed class HeldBackend : IInventorySlotMoveBackend
    {
        private readonly FakeItem?[] _slots;
        public HeldBackend(FakeItem?[] slots, int current)
        {
            _slots = slots;
            CurrentToolIndex = current;
            HeldItem = slots[current];
        }
        public int CurrentToolIndex { get; }
        public object? CurrentItem => _slots[CurrentToolIndex];
        public List<string> Events { get; } = new();
        public bool ThrowAfterFirstStart { get; set; }
        public FakeItem? ReplaceCurrentThenThrowOnFirstStart { get; set; }
        public object? HeldItem { get; private set; }
        public object? ReadSlot(int slot) => _slots[slot];
        public void WriteSlot(int slot, object? item) => _slots[slot] = (FakeItem?)item;
        public void StopBeingHeld(object item)
        {
            Events.Add($"stop:{((FakeItem)item).Id}");
            if (ReferenceEquals(HeldItem, item))
                HeldItem = null;
        }
        public void StartBeingHeld(object item)
        {
            Events.Add($"start:{((FakeItem)item).Id}");
            HeldItem = item;
            if (ReplaceCurrentThenThrowOnFirstStart is { } replacement)
            {
                ReplaceCurrentThenThrowOnFirstStart = null;
                _slots[CurrentToolIndex] = replacement;
                HeldItem = replacement;
                throw new InjectedHeldException();
            }
            if (ThrowAfterFirstStart)
            {
                ThrowAfterFirstStart = false;
                throw new InjectedHeldException();
            }
        }
        public void RestoreShape() { }
    }

    private sealed class InjectedCommitException : Exception { }
    private sealed class InjectedHeldException : Exception { }
}
