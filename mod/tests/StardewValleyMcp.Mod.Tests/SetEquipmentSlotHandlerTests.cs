using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class SetEquipmentSlotHandlerTests
{
    private const string InstanceId = "92222222-2222-4222-8222-222222222222";
    private static readonly string[] SpecialIds =
    {
        "(T)Pan", "(T)SteelPan", "(T)GoldPan", "(T)IridiumPan", "(O)71",
        "(P)15", "(H)71", "(H)SteelPanHat", "(H)GoldPanHat", "(H)IridiumPanHat",
    };

    [Test]
    public void ValidationRequiresOneofTrueClearAndBothRevisions()
    {
        var fixture = NewFixture(null);
        var missing = new CommandRequest
        {
            SetEquipmentSlot = new SetEquipmentSlotRequest
            {
                EquipmentSlotRef = fixture.Runtime.EquipmentRef(),
            },
        };
        var falseClear = ValidClear(fixture);
        falseClear.Clear = false;

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Handler.Validate(missing)!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Handler.Validate(new CommandRequest { SetEquipmentSlot = falseClear })!.Code,
                Is.EqualTo(ErrorCode.InvalidArgument));
        });
    }

    [Test]
    public void EmptyClearSucceedsAcrossTwoTicksWithoutRevisionChangeOrItem()
    {
        var fixture = NewFixture(null);
        var request = ValidClear(fixture);
        var continuation = Start(fixture, request);

        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);
        var result = succeeded.Result.SetEquipmentSlot;

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Item, Is.Null);
            Assert.That(result.PlayerInventoryRevision, Is.EqualTo(request.PlayerInventoryRevision));
            Assert.That(fixture.Runtime.CommitCalls, Is.EqualTo(1));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void WearAndReplaceReturnTheExactNewEquipment(bool replace)
    {
        var old = replace ? Item("old-hat") : null;
        var fixture = NewFixture(old);
        var source = fixture.Runtime.Backpack[0]!;
        var continuation = Start(fixture, ValidWear(fixture));
        continuation.Tick(ContinuationStopSignal.None);

        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Runtime.Equipment, Is.SameAs(source));
            Assert.That(fixture.Runtime.Backpack[0], Is.SameAs(old));
            Assert.That(succeeded.Result.SetEquipmentSlot.Item.QualifiedItemId, Is.EqualTo("source-hat"));
            Assert.That(succeeded.Result.SetEquipmentSlot.Changed, Is.True);
        });
    }

    [Test]
    public void SuccessfulWearStalesSourceItemRefButKeepsEquipmentSlotRefResolved()
    {
        var fixture = NewFixture(null);
        var request = ValidWear(fixture);
        var sourceRef = request.ItemRef.Clone();
        var equipmentRef = request.EquipmentSlotRef.Clone();
        var continuation = Start(fixture, request);
        continuation.Tick(ContinuationStopSignal.None);
        continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Runtime.ResolveItem(sourceRef).Status,
                Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(fixture.Runtime.ResolveEquipment(equipmentRef).Status,
                Is.EqualTo(UiElementResolveStatus.Resolved));
        });
    }

    [Test]
    public void ClearUsesLowestUnlockedEmptySlotAndOmitsResultItem()
    {
        var old = Item("old-hat");
        var fixture = NewFixture(old);
        fixture.Runtime.Backpack[1] = null;
        var continuation = Start(fixture, ValidClear(fixture));
        continuation.Tick(ContinuationStopSignal.None);

        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Runtime.Equipment, Is.Null);
            Assert.That(fixture.Runtime.Backpack[1], Is.SameAs(old));
            Assert.That(succeeded.Result.SetEquipmentSlot.Item, Is.Null);
            Assert.That(succeeded.Result.SetEquipmentSlot.Changed, Is.True);
        });
    }

    [TestCaseSource(nameof(SpecialIds))]
    public void EverySpecialIdIsRejectedAsSourceAndOldTarget(string qualifiedItemId)
    {
        var sourceFixture = NewFixture(null);
        sourceFixture.Runtime.Backpack[0] = Item(qualifiedItemId);
        var sourceFailed = (ContinuationStep.Failed)Start(sourceFixture, ValidWear(sourceFixture))
            .Tick(ContinuationStopSignal.None);

        var targetFixture = NewFixture(Item(qualifiedItemId));
        var targetFailed = (ContinuationStep.Failed)Start(targetFixture, ValidClear(targetFixture))
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(sourceFailed.Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(targetFailed.Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(sourceFixture.Runtime.CommitCalls, Is.Zero);
            Assert.That(targetFixture.Runtime.CommitCalls, Is.Zero);
        });
    }

    [Test]
    public void RevisionChangeBetweenTicksIsStaleWithoutMutation()
    {
        var fixture = NewFixture(null);
        var continuation = Start(fixture, ValidWear(fixture));
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Runtime.AdvanceRevision();

        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(fixture.Runtime.CommitCalls, Is.Zero);
        });
    }

    [Test]
    public void ComponentSourceOldEquipmentAndClearDestinationAreRevalidated()
    {
        static ErrorCode SecondTickAfter(Action<FakeRuntime> mutate, bool clear = false)
        {
            var fixture = NewFixture(clear ? Item("old-hat") : null);
            if (clear)
                fixture.Runtime.Backpack[1] = null;
            var continuation = Start(
                fixture,
                clear ? ValidClear(fixture) : ValidWear(fixture)
            );
            continuation.Tick(ContinuationStopSignal.None);
            mutate(fixture.Runtime);
            return ((ContinuationStep.Failed)continuation.Tick(
                ContinuationStopSignal.None
            )).Error.Code;
        }

        Assert.Multiple(() =>
        {
            Assert.That(SecondTickAfter(runtime => runtime.ReplaceComponent()), Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(SecondTickAfter(runtime => runtime.Backpack[0] = Item("other-source")), Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(SecondTickAfter(runtime => runtime.Equipment = Item("other-old"), clear: true), Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(SecondTickAfter(runtime => runtime.Backpack[1] = Item("occupied-now"), clear: true), Is.EqualTo(ErrorCode.StaleRef));
        });
    }

    [Test]
    public void CancelBeforeCommitStopsWithoutMutation()
    {
        var fixture = NewFixture(null);
        var source = fixture.Runtime.Backpack[0];
        var continuation = Start(fixture, ValidWear(fixture));
        continuation.Tick(ContinuationStopSignal.None);

        var stopped = continuation.Tick(ContinuationStopSignal.CancelRequested);

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.TypeOf<ContinuationStep.Stopped>());
            Assert.That(fixture.Runtime.Backpack[0], Is.SameAs(source));
            Assert.That(fixture.Runtime.Equipment, Is.Null);
            Assert.That(fixture.Runtime.CommitCalls, Is.Zero);
        });
    }

    [Test]
    public void ContainerItemRefIsRejectedByProvenance()
    {
        var fixture = NewFixture(null);
        var request = ValidWear(fixture);
        request.ItemRef = fixture.Runtime.ForeignItemRef();

        var failed = (ContinuationStep.Failed)Start(fixture, request)
            .Tick(ContinuationStopSignal.None);

        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
    }

    [Test]
    public void PostconditionFailureRollsBackAndConfirmsRestoredContent()
    {
        var fixture = NewFixture(null);
        var source = fixture.Runtime.Backpack[0];
        fixture.Runtime.KeepRevisionAfterCommit = true;
        var continuation = Start(fixture, ValidWear(fixture));
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(failed.Error.Message, Does.Not.Contain("无法确认"));
            Assert.That(fixture.Runtime.Backpack[0], Is.SameAs(source));
            Assert.That(fixture.Runtime.Equipment, Is.Null);
            Assert.That(fixture.Runtime.RollbackCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void InternalCommitFailureWithAutomaticRollbackIsReportedAsConfirmed()
    {
        var fixture = NewFixture(null);
        var source = fixture.Runtime.Backpack[0];
        fixture.Runtime.FailCommitAfterSourceRemoval = true;
        var continuation = Start(fixture, ValidWear(fixture));
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(failed.Error.Message, Does.Not.Contain("无法确认"));
            Assert.That(fixture.Runtime.Backpack[0], Is.SameAs(source));
            Assert.That(fixture.Runtime.Equipment, Is.Null);
        });
    }

    [Test]
    public void AdapterCompatibilityRejectionIsInvalidArgument()
    {
        var fixture = NewFixture(null);
        fixture.Runtime.SupportItems = false;

        var failed = (ContinuationStep.Failed)Start(fixture, ValidWear(fixture))
            .Tick(ContinuationStopSignal.None);

        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
    }

    private static Fixture NewFixture(FakeItem? equipment)
    {
        var refs = new OpaqueRefStore(InstanceId);
        var runtime = new FakeRuntime(refs, new[]
        {
            Item("source-hat"),
            Item("occupied"),
            (FakeItem?)null,
        }, equipment);
        return new Fixture(runtime, new SetEquipmentSlotHandler(refs, runtime));
    }

    private static SetEquipmentSlotRequest ValidWear(Fixture fixture)
    {
        var capture = fixture.Runtime.Capture();
        return new SetEquipmentSlotRequest
        {
            EquipmentSlotRef = fixture.Runtime.EquipmentRef(),
            ItemRef = fixture.Runtime.ItemRef(0),
            UiRevision = capture.UiRevision,
            PlayerInventoryRevision = capture.PlayerSnapshot!.InventoryRevision,
        };
    }

    private static SetEquipmentSlotRequest ValidClear(Fixture fixture)
    {
        var capture = fixture.Runtime.Capture();
        return new SetEquipmentSlotRequest
        {
            EquipmentSlotRef = fixture.Runtime.EquipmentRef(),
            Clear = true,
            UiRevision = capture.UiRevision,
            PlayerInventoryRevision = capture.PlayerSnapshot!.InventoryRevision,
        };
    }

    private static SetEquipmentSlotContinuation Start(
        Fixture fixture,
        SetEquipmentSlotRequest request
    )
    {
        var command = new CommandRequest { SetEquipmentSlot = request };
        Assert.That(fixture.Handler.Validate(command), Is.Null);
        return (SetEquipmentSlotContinuation)fixture.Handler.Start("command", command);
    }

    private static FakeItem Item(string id) => new(id);
    private sealed record Fixture(FakeRuntime Runtime, SetEquipmentSlotHandler Handler);
    private sealed record FakeItem(string Id);

    private sealed class FakeRuntime : IEquipmentSlotRuntimeAdapter
    {
        private readonly OpaqueRefStore _refs;
        private readonly FakeInventoryOwner _inventoryOwner;
        private readonly FakeUiOwner _uiOwner;
        private readonly object _menu = new();
        private readonly object _page = new();
        private readonly object _player = new();
        private object _component = new();
        private readonly FakeInventoryOwner _foreignOwner;
        private int _version;

        public FakeRuntime(OpaqueRefStore refs, FakeItem?[] backpack, FakeItem? equipment)
        {
            _refs = refs;
            Backpack = backpack;
            Equipment = equipment;
            _inventoryOwner = new FakeInventoryOwner(Backpack);
            _foreignOwner = new FakeInventoryOwner(
                new[] { Backpack[0] },
                InventoryItemProvenance.Container
            );
            _uiOwner = new FakeUiOwner(_menu, _component);
        }

        public FakeItem?[] Backpack { get; }
        public FakeItem? Equipment { get; set; }
        public bool SupportItems { get; set; } = true;
        public bool KeepRevisionAfterCommit { get; set; }
        public bool FailCommitAfterSourceRemoval { get; set; }
        public int CommitCalls { get; private set; }
        public int RollbackCalls { get; private set; }

        public void AdvanceRevision() => _version++;
        public InventoryItemResolveResult ResolveItem(Ref reference) =>
            _refs.ResolveInventoryItem(reference);
        public UiElementResolveResult ResolveEquipment(Ref reference) =>
            _refs.ResolveUiElement(reference);
        public void ReplaceComponent()
        {
            _component = new object();
            _uiOwner.Component = _component;
        }

        public Ref ItemRef(int slot)
        {
            var item = Backpack[slot]!;
            return _refs.ObserveInventoryItem(_inventoryOwner, slot, item, item.Id);
        }

        public Ref ForeignItemRef()
        {
            var item = Backpack[0]!;
            return _refs.ObserveInventoryItem(_foreignOwner, 0, item, item.Id);
        }

        public Ref EquipmentRef()
        {
            var session = _refs.BeginUiProjection(_menu);
            var reference = _refs.ObserveUiElement(
                session,
                _uiOwner,
                new UiElementBindingIdentity(
                    UiExtractorKind.GameMenu,
                    UiElementKind.EquipmentSlot,
                    UiInventorySide.Unspecified,
                    UiEquipmentSlotKind.Hat,
                    0,
                    _component,
                    _component,
                    "hat"
                )
            );
            _refs.CompleteUiProjection(session);
            return reference;
        }

        public EquipmentSlotCapture Capture()
        {
            var revision = _version.ToString("x").PadLeft(64, '0');
            var items = Backpack.Select(RuntimeItem).ToArray();
            var slots = items.Select((item, index) => new InventorySlot
            {
                Index = checked((uint)index),
                Item = item?.PublicFact.Clone(),
            }).ToArray();
            var snapshot = new InventorySnapshot
            {
                ContainerKind = "player",
                SlotCount = checked((uint)Backpack.Length),
                InventoryRevision = revision,
            };
            snapshot.Slots.Add(slots);
            return new EquipmentSlotCapture(
                EquipmentSlotCaptureStatus.Ready,
                _menu,
                _page,
                _player,
                revision,
                snapshot,
                items,
                new[]
                {
                    new EquipmentRuntimeSlot(
                        UiEquipmentSlotKind.Hat,
                        0,
                        _component,
                        RuntimeItem(Equipment)
                    ),
                },
                0,
                this
            );
        }

        public bool IsSupported(EquipmentRuntimeItem item, UiEquipmentSlotKind kind) =>
            SupportItems && kind == UiEquipmentSlotKind.Hat;

        public IEquipmentSlotMutationCommit Commit(
            EquipmentSlotCapture capture,
            UiEquipmentSlotKind kind,
            int index,
            EquipmentSlotMutationPlan plan
        )
        {
            CommitCalls++;
            var inner = EquipmentSlotMutationExecutor.Commit(
                new Backend(this),
                plan,
                point =>
                {
                    if (FailCommitAfterSourceRemoval
                        && point == EquipmentSlotMutationPoint.SourceRemoved)
                        throw new InjectedCommitException();
                }
            );
            if (plan.Changed && !KeepRevisionAfterCommit)
                _version++;
            return new CommitScope(this, inner, plan.Changed);
        }

        private static EquipmentRuntimeItem? RuntimeItem(FakeItem? item) => item is null
            ? null
            : new EquipmentRuntimeItem(item, item.Id, 1, 1, new ItemFact
            {
                QualifiedItemId = item.Id,
                DisplayName = item.Id,
                Stack = 1,
                Category = "0",
            });

        private sealed class Backend : IEquipmentSlotMutationBackend
        {
            private readonly FakeRuntime _owner;
            public Backend(FakeRuntime owner) => _owner = owner;
            public object? ReadBackpack(int slot) => _owner.Backpack[slot];
            public void WriteBackpack(int slot, object? item) =>
                _owner.Backpack[slot] = (FakeItem?)item;
            public object? ReadEquipment() => _owner.Equipment;
            public object? ExchangeEquipment(object? item)
            {
                var old = _owner.Equipment;
                _owner.Equipment = (FakeItem?)item;
                return old;
            }
            public void RestoreShape() { }
        }

        private sealed class CommitScope : IEquipmentSlotMutationCommit
        {
            private readonly FakeRuntime _owner;
            private readonly IEquipmentSlotMutationCommit _inner;
            private readonly bool _changed;
            public CommitScope(FakeRuntime owner, IEquipmentSlotMutationCommit inner, bool changed)
            {
                _owner = owner;
                _inner = inner;
                _changed = changed;
            }
            public void Complete() => _inner.Complete();
            public void Rollback()
            {
                _inner.Rollback();
                _owner.RollbackCalls++;
                if (_changed)
                    _owner._version++;
            }
        }

        private sealed class InjectedCommitException : Exception { }
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
        public object Component { get; set; }
        public FakeUiOwner(object menu, object component)
        {
            _menu = menu;
            Component = component;
        }
        public bool TryGetMenuIdentity(out object menu)
        {
            menu = _menu;
            return true;
        }
        public UiElementLookup ResolveCurrentElement(UiElementBindingIdentity identity) =>
            new(
                UiElementLookupStatus.Resolved,
                Component,
                Component,
                "hat"
            );
    }
}
