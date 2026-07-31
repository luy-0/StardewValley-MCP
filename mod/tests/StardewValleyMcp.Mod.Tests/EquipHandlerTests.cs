using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class EquipHandlerTests
{
    private const string InstanceId = "11111111-1111-4111-8111-111111111111";

    [Test]
    public void ValidateIsStructuralAndDoesNotReadInventory()
    {
        var fixture = NewFixture();
        var wrongOperation = new CommandRequest { QueryRuntime = new QueryRuntimeRequest() };
        var missingSelector = new CommandRequest { Equip = new EquipRequest() };
        var missingRevision = new CommandRequest
        {
            Equip = new EquipRequest { ItemRef = fixture.RefAt(0) },
        };
        var capturesBeforeValidation = fixture.Inventory.Captures;

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Handler.Validate(wrongOperation)!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Handler.Validate(missingSelector)!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Handler.Validate(missingRevision)!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Inventory.Captures, Is.EqualTo(capturesBeforeValidation));
        });
    }

    [Test]
    public void SlotSelectionEquipsAfterCommitRevalidationAndReturnsItemFact()
    {
        var fixture = NewFixture(selectedSlot: 0);
        var continuation = Start(fixture, new EquipRequest { SlotIndex = 1 });

        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        var completed = continuation.Tick(ContinuationStopSignal.None);

        var result = ((ContinuationStep.Succeeded)completed).Result.Equip;
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Inventory.CurrentToolIndex, Is.EqualTo(1));
            Assert.That(result.SlotIndex, Is.EqualTo(1));
            Assert.That(result.Item.QualifiedItemId, Is.EqualTo("item-1"));
            Assert.That(result.Changed, Is.True);
            Assert.That(fixture.Inventory.SetCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void ItemRefRequiresCurrentPlayerRevisionAndRejectsContainerRef()
    {
        var fixture = NewFixture();
        var staleRevision = Start(fixture, new EquipRequest
        {
            ItemRef = fixture.RefAt(1),
            InventoryRevision = new string('0', 64),
        });

        var stale = (ContinuationStep.Failed)staleRevision.Tick(ContinuationStopSignal.None);
        var containerRef = fixture.CreateContainerRefAt(1);
        var invalidSource = Start(fixture, new EquipRequest
        {
            ItemRef = containerRef,
            InventoryRevision = fixture.Inventory.CurrentRevision,
        });
        var invalid = (ContinuationStep.Failed)invalidSource.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(stale.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(invalid.Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Inventory.SetCalls, Is.Zero);
        });
    }

    [Test]
    public void PlayerItemRefSelectsTheBoundSlot()
    {
        var fixture = NewFixture();
        var continuation = Start(fixture, new EquipRequest
        {
            ItemRef = fixture.RefAt(1),
            InventoryRevision = fixture.Inventory.CurrentRevision,
        });

        continuation.Tick(ContinuationStopSignal.None);
        var result = ((ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None)).Result.Equip;

        Assert.Multiple(() =>
        {
            Assert.That(result.SlotIndex, Is.EqualTo(1));
            Assert.That(result.Changed, Is.True);
            Assert.That(fixture.Inventory.CurrentToolIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void IssuedItemRefBecomesStaleWhenItsSlotIdentityChanges()
    {
        var fixture = NewFixture();
        var staleRef = fixture.RefAt(1);
        fixture.Inventory.Replace(1, new object(), "replacement");
        var continuation = Start(fixture, new EquipRequest
        {
            ItemRef = staleRef,
            InventoryRevision = fixture.Inventory.CurrentRevision,
        });

        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(fixture.Inventory.SetCalls, Is.Zero);
        });
    }

    [Test]
    public void SlotErrorsMapToOutOfRangeAndNotFound()
    {
        var fixture = NewFixture();
        var outOfRange = Start(fixture, new EquipRequest { SlotIndex = 99 });
        var empty = Start(fixture, new EquipRequest { SlotIndex = 2 });

        Assert.Multiple(() =>
        {
            Assert.That(
                ((ContinuationStep.Failed)outOfRange.Tick(ContinuationStopSignal.None)).Error.Code,
                Is.EqualTo(ErrorCode.OutOfRange)
            );
            Assert.That(
                ((ContinuationStep.Failed)empty.Tick(ContinuationStopSignal.None)).Error.Code,
                Is.EqualTo(ErrorCode.NotFound)
            );
        });
    }

    [Test]
    public void RevisionOrItemChangeBeforeCommitIsStaleAndDoesNotMutate()
    {
        var fixture = NewFixture();
        var continuation = Start(fixture, new EquipRequest { SlotIndex = 1 });
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        fixture.Inventory.Replace(1, new object(), "replacement");

        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(fixture.Inventory.SetCalls, Is.Zero);
        });
    }

    [Test]
    public void AlreadyEquippedTargetIsSuccessfulNoOp()
    {
        var fixture = NewFixture(selectedSlot: 1);
        var continuation = Start(fixture, new EquipRequest { SlotIndex = 1 });
        continuation.Tick(ContinuationStopSignal.None);

        var result = ((ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None)).Result.Equip;
        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.False);
            Assert.That(fixture.Inventory.SetCalls, Is.Zero);
        });
    }

    [Test]
    public void CancelBeforeCommitStopsWithoutMutation()
    {
        var fixture = NewFixture();
        var continuation = Start(fixture, new EquipRequest { SlotIndex = 1 });
        continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(continuation.CanCancel, Is.True);
            Assert.That(continuation.Tick(ContinuationStopSignal.CancelRequested), Is.TypeOf<ContinuationStep.Stopped>());
            Assert.That(fixture.Inventory.SetCalls, Is.Zero);
        });
    }

    private static EquipContinuation Start(EquipFixture fixture, EquipRequest request)
    {
        var command = new CommandRequest { Equip = request };
        Assert.That(fixture.Handler.Validate(command), Is.Null);
        return (EquipContinuation)fixture.Handler.Start("command", command);
    }

    private static EquipFixture NewFixture(int selectedSlot = 0)
    {
        var store = new OpaqueRefStore(InstanceId);
        var inventory = new FakeEquipInventory(store, selectedSlot);
        return new EquipFixture(store, inventory, new EquipHandler(store, inventory));
    }

    private sealed record EquipFixture(
        OpaqueRefStore Store,
        FakeEquipInventory Inventory,
        EquipHandler Handler
    )
    {
        public Ref RefAt(int slot) => Inventory.RefAt(slot);
        public Ref CreateContainerRefAt(int slot) => Inventory.CreateContainerRefAt(slot);
    }

    private sealed class FakeEquipInventory : IEquipInventoryAdapter
    {
        private readonly OpaqueRefStore _store;
        private readonly FakeOwner _playerOwner = new(InventoryItemProvenance.Player);
        private readonly FakeOwner _containerOwner = new(InventoryItemProvenance.Container);
        private readonly object?[] _items = { new object(), new object(), null };
        private readonly string[] _guards = { "item-0", "item-1", "" };

        public FakeEquipInventory(OpaqueRefStore store, int selectedSlot)
        {
            _store = store;
            CurrentToolIndex = selectedSlot;
        }

        public int CurrentToolIndex { get; private set; }
        public int Captures { get; private set; }
        public int SetCalls { get; private set; }
        public string CurrentRevision => Capture().Snapshot!.InventoryRevision;

        public EquipInventoryCapture Capture()
        {
            Captures++;
            _playerOwner.Set(_items, _guards);
            var captured = _items.Select((item, slot) => new CapturedInventorySlot(item, _guards[slot])).ToArray();
            var snapshot = InventoryProjector.ProjectCapturedSlots(
                _playerOwner,
                "player",
                null,
                captured,
                CurrentToolIndex,
                includeEmptySlots: true,
                _store,
                (target, reference) => new ItemFact
                {
                    Ref = reference.Clone(),
                    QualifiedItemId = $"item-{Array.IndexOf(_items, target)}",
                    DisplayName = "Test Item",
                    Stack = 1,
                    Category = "0",
                }
            );
            return new EquipInventoryCapture(
                EquipInventoryCaptureStatus.Ready,
                snapshot,
                _items.ToArray(),
                CurrentToolIndex
            );
        }

        public void SetCurrentToolIndex(int slot)
        {
            SetCalls++;
            CurrentToolIndex = slot;
        }

        public void Replace(int slot, object? item, string guard)
        {
            _items[slot] = item;
            _guards[slot] = item is null ? "" : guard;
        }

        public Ref RefAt(int slot) => Capture().Snapshot!.Slots[slot].Item!.Ref.Clone();

        public Ref CreateContainerRefAt(int slot)
        {
            _containerOwner.Set(_items, _guards);
            return _store.ObserveInventoryItem(
                _containerOwner,
                slot,
                _items[slot]!,
                _guards[slot]
            );
        }
    }

    private sealed class FakeOwner : IInventoryRefOwner
    {
        private readonly object _identity = new();
        private object?[] _items = Array.Empty<object?>();
        private string[] _guards = Array.Empty<string>();

        public FakeOwner(InventoryItemProvenance provenance)
        {
            Provenance = provenance;
        }

        public InventoryItemProvenance Provenance { get; }

        public void Set(object?[] items, string[] guards)
        {
            _items = items;
            _guards = guards;
        }

        public bool TryGetIdentity(out object identity)
        {
            identity = _identity;
            return true;
        }

        public InventorySlotLookup ResolveCurrentSlot(int slot) => slot < 0 || slot >= _items.Length
            ? new InventorySlotLookup(InventorySlotLookupStatus.Stale)
            : new InventorySlotLookup(
                InventorySlotLookupStatus.Resolved,
                _items[slot],
                _guards[slot]
            );
    }
}
