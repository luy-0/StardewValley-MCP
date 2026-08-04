using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class TransferInventoryItemHandlerTests
{
    private const string InstanceId = "91111111-1111-4111-8111-111111111111";

    [Test]
    public void ValidationRequiresAllConcurrencyTokensAndPositiveQuantity()
    {
        var fixture = NewFixture();
        var missing = new CommandRequest { TransferInventoryItem = new TransferInventoryItemRequest() };
        var zero = ValidRequest(fixture);
        zero.Quantity = 0;

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Handler.Validate(missing)!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Handler.Validate(new CommandRequest { TransferInventoryItem = zero })!.Code,
                Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Inventory.CommitCalls, Is.Zero);
        });
    }

    [Test]
    public void TransfersPartialStackAcrossTwoTicksAndReturnsNewRevisions()
    {
        var fixture = NewFixture();
        var request = ValidRequest(fixture, quantity: 6);
        var beforePlayer = request.PlayerInventoryRevision;
        var beforeContainer = request.ContainerInventoryRevision;
        var continuation = Start(fixture, request);

        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        var completed = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);
        var result = completed.Result.TransferInventoryItem;

        Assert.Multiple(() =>
        {
            Assert.That(result.TransferredQuantity, Is.EqualTo(6));
            Assert.That(result.SourceSlotIndex, Is.Zero);
            Assert.That(result.SourceRemainingQuantity, Is.EqualTo(4));
            Assert.That(result.PlayerInventoryRevision, Is.Not.EqualTo(beforePlayer));
            Assert.That(result.ContainerInventoryRevision, Is.Not.EqualTo(beforeContainer));
            Assert.That(fixture.Inventory.Player[0]!.Stack, Is.EqualTo(4));
            Assert.That(fixture.Inventory.Container[0]!.Stack, Is.EqualTo(99));
            Assert.That(fixture.Inventory.Container[1]!.Stack, Is.EqualTo(2));
            Assert.That(fixture.Inventory.CommitCalls, Is.EqualTo(1));
            Assert.That(continuation.CanCancel, Is.False);
        });
    }

    [Test]
    public void DirectionMustMatchItemRefProvenance()
    {
        var fixture = NewFixture();
        var request = ValidRequest(fixture);
        request.Direction = InventoryTransferDirection.ContainerToPlayer;

        var failed = (ContinuationStep.Failed)Start(fixture, request)
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Inventory.CommitCalls, Is.Zero);
        });
    }

    [Test]
    public void TransfersWholeContainerStackBackToPlayer()
    {
        var fixture = NewFixture();
        var capture = fixture.Inventory.Capture();
        var request = new TransferInventoryItemRequest
        {
            Direction = InventoryTransferDirection.ContainerToPlayer,
            ItemRef = fixture.Inventory.RefAt(InventoryItemProvenance.Container, 0),
            Quantity = 95,
            UiRevision = capture.UiRevision,
            PlayerInventoryRevision = capture.PlayerSnapshot!.InventoryRevision,
            ContainerInventoryRevision = capture.ContainerSnapshot!.InventoryRevision,
        };
        var continuation = Start(fixture, request);
        continuation.Tick(ContinuationStopSignal.None);

        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded.Result.TransferInventoryItem.SourceRemainingQuantity, Is.Zero);
            Assert.That(fixture.Inventory.Container[0], Is.Null);
            Assert.That(fixture.Inventory.Player[0]!.Stack, Is.EqualTo(99));
            Assert.That(fixture.Inventory.Player[1]!.Stack, Is.EqualTo(6));
        });
    }

    [Test]
    public void RevisionsAndSourceIdentityAreRevalidatedBeforeCommit()
    {
        var fixture = NewFixture();
        var request = ValidRequest(fixture);
        var continuation = Start(fixture, request);
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Inventory.Player[0] = new FakeItem("wood", 10, 99);

        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(fixture.Inventory.CommitCalls, Is.Zero);
        });
    }

    [Test]
    public void CancelBeforeCommitStopsWithoutMutation()
    {
        var fixture = NewFixture();
        var continuation = Start(fixture, ValidRequest(fixture));
        continuation.Tick(ContinuationStopSignal.None);

        var stopped = continuation.Tick(ContinuationStopSignal.CancelRequested);

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.TypeOf<ContinuationStep.Stopped>());
            Assert.That(fixture.Inventory.Player[0]!.Stack, Is.EqualTo(10));
            Assert.That(fixture.Inventory.CommitCalls, Is.Zero);
        });
    }

    [Test]
    public void CapacityShortageIsNotReadyAndDoesNotMutate()
    {
        var fixture = NewFixture(container: new[]
        {
            new FakeItem("stone", 99, 99),
            new FakeItem("stone", 99, 99),
            new FakeItem("stone", 99, 99),
        });
        var failed = (ContinuationStep.Failed)Start(fixture, ValidRequest(fixture, quantity: 2))
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.NotReady));
            Assert.That(failed.Error.Message, Does.Contain("重新查询"));
            Assert.That(fixture.Inventory.Player[0]!.Stack, Is.EqualTo(10));
        });
    }

    [Test]
    public void WholeStackPostconditionFailureRollsBackContentButDoesNotReviveStaleSourceRef()
    {
        var fixture = NewFixture();
        var request = ValidRequest(fixture, quantity: 10);
        var beforeUi = request.UiRevision;
        var beforePlayer = request.PlayerInventoryRevision;
        var beforeContainer = request.ContainerInventoryRevision;
        fixture.Inventory.KeepUiRevisionAfterCommit = true;
        var continuation = Start(fixture, request);
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);
        var restored = fixture.Inventory.Capture();

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(fixture.Inventory.Player[0]!.Stack, Is.EqualTo(10));
            Assert.That(fixture.Inventory.Container[0]!.Stack, Is.EqualTo(95));
            Assert.That(fixture.Inventory.Container[1], Is.Null);
            Assert.That(restored.UiRevision, Is.Not.EqualTo(beforeUi));
            Assert.That(restored.PlayerSnapshot!.InventoryRevision, Is.Not.EqualTo(beforePlayer));
            Assert.That(restored.ContainerSnapshot!.InventoryRevision, Is.EqualTo(beforeContainer));
            Assert.That(fixture.Inventory.RollbackCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void MenuReplacementBeforeCommitIsStale()
    {
        var fixture = NewFixture();
        var continuation = Start(fixture, ValidRequest(fixture));
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Inventory.MenuIdentity = new object();

        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);
        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
    }

    [Test]
    public void WrongDestinationSlotWithConservedTotalsIsDetectedAndRolledBack()
    {
        var fixture = NewFixture();
        fixture.Inventory.MisapplyEmptyWrite = true;
        var continuation = Start(fixture, ValidRequest(fixture, quantity: 6));
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(fixture.Inventory.Player[0]!.Stack, Is.EqualTo(10));
            Assert.That(fixture.Inventory.Container[0]!.Stack, Is.EqualTo(95));
            Assert.That(fixture.Inventory.Container[1], Is.Null);
            Assert.That(fixture.Inventory.Container[2], Is.Null);
            Assert.That(fixture.Inventory.RollbackCalls, Is.EqualTo(1));
        });
    }

    private static TransferInventoryItemRequest ValidRequest(TransferFixture fixture, uint quantity = 2)
    {
        var capture = fixture.Inventory.Capture();
        return new TransferInventoryItemRequest
        {
            Direction = InventoryTransferDirection.PlayerToContainer,
            ItemRef = fixture.Inventory.RefAt(InventoryItemProvenance.Player, 0),
            Quantity = quantity,
            UiRevision = capture.UiRevision,
            PlayerInventoryRevision = capture.PlayerSnapshot!.InventoryRevision,
            ContainerInventoryRevision = capture.ContainerSnapshot!.InventoryRevision,
        };
    }

    private static InventoryTransferContinuation Start(
        TransferFixture fixture,
        TransferInventoryItemRequest request
    )
    {
        var command = new CommandRequest { TransferInventoryItem = request };
        Assert.That(fixture.Handler.Validate(command), Is.Null);
        return (InventoryTransferContinuation)fixture.Handler.Start("command", command);
    }

    private static TransferFixture NewFixture(FakeItem?[]? container = null)
    {
        var store = new OpaqueRefStore(InstanceId);
        var inventory = new FakeTransferAdapter(
            store,
            new FakeItem?[] { new("wood", 10, 99), null, new("axe", 1, 1) },
            container ?? new FakeItem?[] { new("wood", 95, 99), null, null }
        );
        return new TransferFixture(inventory, new TransferInventoryItemHandler(store, inventory));
    }

    private sealed record TransferFixture(FakeTransferAdapter Inventory, TransferInventoryItemHandler Handler);

    private sealed class FakeTransferAdapter : IInventoryTransferAdapter
    {
        private readonly OpaqueRefStore _store;
        private readonly FakeOwner _playerOwner = new(InventoryItemProvenance.Player);
        private readonly FakeOwner _containerOwner = new(InventoryItemProvenance.Container);
        private readonly object _containerIdentity = new();
        private string? _stableUiRevision;
        private bool _forceStableUiOnce;

        public FakeTransferAdapter(OpaqueRefStore store, FakeItem?[] player, FakeItem?[] container)
        {
            _store = store;
            Player = player;
            Container = container;
        }

        public FakeItem?[] Player { get; }
        public FakeItem?[] Container { get; }
        public object MenuIdentity { get; set; } = new();
        public bool KeepUiRevisionAfterCommit { get; set; }
        public bool MisapplyEmptyWrite { get; set; }
        public int CommitCalls { get; private set; }
        public int RollbackCalls { get; private set; }

        public InventoryTransferCapture Capture()
        {
            _playerOwner.Set(Player);
            _containerOwner.Set(Container);
            var player = Snapshot(Player, _playerOwner, "player");
            var container = Snapshot(Container, _containerOwner, "chest");
            var computedUi = Revision($"ui:{player.InventoryRevision}:{container.InventoryRevision}");
            _stableUiRevision ??= computedUi;
            var ui = _forceStableUiOnce ? _stableUiRevision : computedUi;
            _forceStableUiOnce = false;
            return new InventoryTransferCapture(
                InventoryTransferCaptureStatus.Ready,
                MenuIdentity,
                _containerIdentity,
                ui,
                player,
                container,
                Wrap(Player),
                Wrap(Container),
                new object()
            );
        }

        public IInventoryTransferCommit Commit(
            InventoryTransferCapture capture,
            InventoryTransferDirection direction,
            InventoryTransferPlan plan
        )
        {
            CommitCalls++;
            _forceStableUiOnce = KeepUiRevisionAfterCommit;
            var source = direction == InventoryTransferDirection.PlayerToContainer ? Player : Container;
            var target = direction == InventoryTransferDirection.PlayerToContainer ? Container : Player;
            var sourceBackup = source.Select(item => new FakeSlotBackup(item, item?.Stack)).ToArray();
            var targetBackup = target.Select(item => new FakeSlotBackup(item, item?.Stack)).ToArray();
            foreach (var write in plan.Writes)
            {
                if (target[write.Slot] is { } existing)
                    existing.Stack += write.Quantity;
                else
                    target[write.Slot] = (FakeItem)write.NewIdentity!;
            }
            if (plan.SourceRemaining == 0)
                source[plan.SourceSlot] = null;
            else
                source[plan.SourceSlot]!.Stack = plan.SourceRemaining;
            if (MisapplyEmptyWrite)
            {
                var emptyWrite = plan.Writes.First(write => write.ExistingIdentity is null);
                var wrongSlot = emptyWrite.Slot == target.Length - 1
                    ? emptyWrite.Slot - 1
                    : emptyWrite.Slot + 1;
                target[wrongSlot] = target[emptyWrite.Slot];
                target[emptyWrite.Slot] = null;
            }
            return new FakeCommit(this, source, target, sourceBackup, targetBackup);
        }

        public Ref RefAt(InventoryItemProvenance provenance, int slot)
        {
            var owner = provenance == InventoryItemProvenance.Player ? _playerOwner : _containerOwner;
            var items = provenance == InventoryItemProvenance.Player ? Player : Container;
            owner.Set(items);
            return _store.ObserveInventoryItem(owner, slot, items[slot]!, items[slot]!.Kind);
        }

        private InventorySnapshot Snapshot(FakeItem?[] items, FakeOwner owner, string kind)
        {
            var materials = new List<string>();
            var snapshot = new InventorySnapshot
            {
                ContainerKind = kind,
                SlotCount = checked((uint)items.Length),
            };
            for (var slot = 0; slot < items.Length; slot++)
            {
                var fact = new InventorySlot { Index = checked((uint)slot) };
                if (items[slot] is { } item)
                {
                    var reference = _store.ObserveInventoryItem(owner, slot, item, item.Kind);
                    fact.Item = new ItemFact
                    {
                        Ref = reference,
                        QualifiedItemId = item.Kind,
                        DisplayName = item.Kind,
                        Stack = checked((uint)item.Stack),
                        Category = "0",
                    };
                    materials.Add($"{slot}:{reference.Value}:{item.Kind}:{item.Stack}:{item.Maximum}");
                }
                else
                {
                    _store.ObserveEmptyInventorySlot(owner, slot);
                    materials.Add($"{slot}:empty");
                }
                snapshot.Slots.Add(fact);
            }
            _store.CompleteInventoryObservation(owner, items.Length);
            snapshot.InventoryRevision = Revision(string.Join("|", materials));
            return snapshot;
        }

        private static IReadOnlyList<InventoryTransferItem?> Wrap(FakeItem?[] items) => items
            .Select(item => item is null ? null : new InventoryTransferItem(
                item,
                item.Stack,
                item.Maximum,
                item.Special,
                item.Kind,
                other => item.Maximum > 1
                    && other.Identity is FakeItem candidate
                    && candidate.Kind == item.Kind,
                quantity => item.Copy(quantity)
            )).ToArray();

        private static string Revision(string material) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();

        private sealed class FakeCommit : IInventoryTransferCommit
        {
            private readonly FakeTransferAdapter _owner;
            private readonly FakeItem?[] _source;
            private readonly FakeItem?[] _target;
            private readonly FakeSlotBackup[] _sourceBackup;
            private readonly FakeSlotBackup[] _targetBackup;
            private bool _finished;

            public FakeCommit(
                FakeTransferAdapter owner,
                FakeItem?[] source,
                FakeItem?[] target,
                FakeSlotBackup[] sourceBackup,
                FakeSlotBackup[] targetBackup
            )
            {
                _owner = owner;
                _source = source;
                _target = target;
                _sourceBackup = sourceBackup;
                _targetBackup = targetBackup;
            }

            public void Complete() => _finished = true;

            public void Rollback()
            {
                if (_finished)
                    return;
                Restore(_source, _sourceBackup);
                Restore(_target, _targetBackup);
                _owner.RollbackCalls++;
                _finished = true;
            }

            private static void Restore(FakeItem?[] target, FakeSlotBackup[] backup)
            {
                for (var index = 0; index < target.Length; index++)
                {
                    target[index] = backup[index].Item;
                    if (backup[index].Item is not null && backup[index].Stack.HasValue)
                        backup[index].Item!.Stack = backup[index].Stack!.Value;
                }
            }
        }

        private sealed record FakeSlotBackup(FakeItem? Item, int? Stack);
    }

    private sealed class FakeOwner : IInventoryRefOwner
    {
        private readonly object _identity = new();
        private FakeItem?[] _items = Array.Empty<FakeItem?>();
        public FakeOwner(InventoryItemProvenance provenance) => Provenance = provenance;
        public InventoryItemProvenance Provenance { get; }
        public void Set(FakeItem?[] items) => _items = items;
        public bool TryGetIdentity(out object identity) { identity = _identity; return true; }
        public InventorySlotLookup ResolveCurrentSlot(int slot) => slot < 0 || slot >= _items.Length
            ? new InventorySlotLookup(InventorySlotLookupStatus.Stale)
            : new InventorySlotLookup(
                InventorySlotLookupStatus.Resolved,
                _items[slot],
                _items[slot]?.Kind ?? ""
            );
    }

    private sealed class FakeItem
    {
        public FakeItem(string kind, int stack, int maximum, bool special = false)
        {
            Kind = kind;
            Stack = stack;
            Maximum = maximum;
            Special = special;
        }
        public string Kind { get; }
        public int Stack { get; set; }
        public int Maximum { get; set; }
        public bool Special { get; }
        public FakeItem Copy(int? stack = null) => new(Kind, stack ?? Stack, Maximum, Special);
    }
}
