using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class QueryInventoryModContractTests
{
    [Test]
    public void ValidatorAcceptsDefaultPlayerAndValidContainerSelectors()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QueryInventoryRequestValidator.Validate(Request(new QueryInventoryRequest())), Is.Null);
            Assert.That(
                QueryInventoryRequestValidator.Validate(Request(new QueryInventoryRequest
                {
                    PlayerInventory = new PlayerInventorySelector(),
                })),
                Is.Null
            );
            Assert.That(
                QueryInventoryRequestValidator.Validate(Request(new QueryInventoryRequest
                {
                    ContainerRef = new Ref { Value = string.Concat(Enumerable.Repeat("😀", 512)) },
                })),
                Is.Null
            );
        });
    }

    [Test]
    public void ValidatorRejectsInvalidOperationAndContainerRefEnvelope()
    {
        var wrongOperation = new CommandRequest { QueryRuntime = new QueryRuntimeRequest() };
        var empty = Request(new QueryInventoryRequest { ContainerRef = new Ref() });
        var nul = Request(new QueryInventoryRequest
        {
            ContainerRef = new Ref { Value = "opaque\0ref" },
        });
        var tooLong = Request(new QueryInventoryRequest
        {
            ContainerRef = new Ref { Value = string.Concat(Enumerable.Repeat("😀", 513)) },
        });

        Assert.Multiple(() =>
        {
            Assert.That(QueryInventoryRequestValidator.Validate(wrongOperation)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(QueryInventoryRequestValidator.Validate(empty)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(QueryInventoryRequestValidator.Validate(nul)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(QueryInventoryRequestValidator.Validate(tooLong)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
        });
    }

    [Test]
    public void RevisionUsesCompleteFactsAndPlayerSelectedSlot()
    {
        var snapshot = CompleteSnapshot();
        var first = InventorySnapshotContract.ComputeRevision(snapshot, 0);
        snapshot.InventoryRevision = "ignored-existing-value";
        var repeated = InventorySnapshotContract.ComputeRevision(snapshot, 0);
        snapshot.Slots[0].Item.Stack++;
        var changedFact = InventorySnapshotContract.ComputeRevision(snapshot, 0);
        snapshot.Slots[0].Item.Stack--;
        var changedSelection = InventorySnapshotContract.ComputeRevision(snapshot, 1);

        Assert.Multiple(() =>
        {
            Assert.That(first, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(repeated, Is.EqualTo(first));
            Assert.That(changedFact, Is.Not.EqualTo(first));
            Assert.That(changedSelection, Is.Not.EqualTo(first));
        });
    }

    [Test]
    public void EmptySlotFilteringHappensAfterRevisionAndPreservesCapacityIndices()
    {
        var complete = CompleteSnapshot();
        var withEmpty = InventoryProjector.AssembleCompleteSnapshot(
            complete.ContainerKind,
            null,
            3,
            complete.Slots.ToArray(),
            int.MinValue,
            true
        );
        var withoutEmpty = InventoryProjector.AssembleCompleteSnapshot(
            complete.ContainerKind,
            null,
            3,
            complete.Slots.ToArray(),
            int.MinValue,
            false
        );

        Assert.Multiple(() =>
        {
            Assert.That(withEmpty.Slots.Count, Is.EqualTo(3));
            Assert.That(withoutEmpty.Slots.Count, Is.EqualTo(1));
            Assert.That(withoutEmpty.Slots.Single().Index, Is.EqualTo(0));
            Assert.That(withoutEmpty.SlotCount, Is.EqualTo(3));
            Assert.That(withoutEmpty.InventoryRevision, Is.EqualTo(withEmpty.InventoryRevision));
            Assert.That(complete.InventoryRevision, Is.Empty);
            Assert.That(complete.Slots.Count, Is.EqualTo(3));
        });
    }

    [Test]
    public void InventoryProjectorRejectsNonCanonicalCompleteSlotAssembly()
    {
        var wrongCount = new[] { new InventorySlot { Index = 0 } };
        var wrongIndex = new[]
        {
            new InventorySlot { Index = 0 },
            new InventorySlot { Index = 2 },
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                () => InventoryProjector.AssembleCompleteSnapshot(
                    "player", null, 2, wrongCount, 0, false),
                Throws.TypeOf<InvalidOperationException>()
            );
            Assert.That(
                () => InventoryProjector.AssembleCompleteSnapshot(
                    "player", null, 2, wrongIndex, 0, false),
                Throws.TypeOf<InvalidOperationException>()
            );
        });
    }

    [Test]
    public void InventoryProjectorProductionPathSignsRefsBeforeRevisionAndFiltering()
    {
        const string instanceId = "11111111-1111-4111-8111-111111111111";
        var store = new OpaqueRefStore(instanceId);
        var owner = new ProjectionOwner(3);
        var first = new object();
        var second = new object();
        owner.Set(0, first, "first-guard");
        owner.Set(2, second, "second-guard");
        var captured = new[]
        {
            new CapturedInventorySlot(first, "first-guard"),
            new CapturedInventorySlot(null, ""),
            new CapturedInventorySlot(second, "second-guard"),
        };

        var withoutEmpty = InventoryProjector.ProjectCapturedSlots(
            owner,
            "player",
            null,
            captured,
            0,
            false,
            store,
            Project
        );
        var withEmpty = InventoryProjector.ProjectCapturedSlots(
            owner,
            "player",
            null,
            captured,
            0,
            true,
            store,
            Project
        );

        var firstRef = withEmpty.Slots[0].Item.Ref;
        var secondRef = withEmpty.Slots[2].Item.Ref;
        Assert.Multiple(() =>
        {
            Assert.That(withoutEmpty.Slots.Select(slot => slot.Index), Is.EqualTo(new uint[] { 0, 2 }));
            Assert.That(withEmpty.Slots.Select(slot => slot.Index), Is.EqualTo(new uint[] { 0, 1, 2 }));
            Assert.That(withoutEmpty.SlotCount, Is.EqualTo(3));
            Assert.That(withoutEmpty.InventoryRevision, Is.EqualTo(withEmpty.InventoryRevision));
            Assert.That(withoutEmpty.Slots[0].Item.Ref.Value, Is.EqualTo(firstRef.Value));
            Assert.That(withoutEmpty.Slots[1].Item.Ref.Value, Is.EqualTo(secondRef.Value));
            Assert.That(store.ResolveInventoryItem(firstRef).Target?.Target, Is.SameAs(first));
            Assert.That(store.ResolveInventoryItem(secondRef).Target?.Target, Is.SameAs(second));
        });

        ItemFact Project(object target, Ref reference) => new()
        {
            Ref = reference.Clone(),
            QualifiedItemId = ReferenceEquals(target, first) ? "first" : "second",
            DisplayName = "Test Item",
            Stack = 1,
            Category = "0",
        };
    }

    [Test]
    public void QueryInventoryHandlerProductionSeamsPreserveSelectorAndTerminalSemantics()
    {
        var omitted = new QueryInventoryRequest();
        var explicitPlayer = new QueryInventoryRequest
        {
            PlayerInventory = new PlayerInventorySelector(),
        };
        var container = new QueryInventoryRequest
        {
            ContainerRef = new Ref { Value = "opaque" },
        };
        var snapshot = CompleteSnapshot();
        var succeeded = QueryInventoryHandler.Succeeded("command", snapshot);
        var stale = QueryInventoryHandler.FailedFromResolution(
            "command",
            new RefResolution
            {
                Status = RefStatus.Stale,
                Kind = RefKind.Container,
                Error = new Error { Code = ErrorCode.StaleRef, Message = "Ref 已失效" },
            }
        );

        Assert.Multiple(() =>
        {
            Assert.That(QueryInventoryHandler.SelectsPlayerInventory(omitted), Is.True);
            Assert.That(QueryInventoryHandler.SelectsPlayerInventory(explicitPlayer), Is.True);
            Assert.That(QueryInventoryHandler.SelectsPlayerInventory(container), Is.False);
            Assert.That(succeeded.State, Is.EqualTo(CommandState.Succeeded));
            Assert.That(succeeded.Result.QueryInventory.Snapshot, Is.SameAs(snapshot));
            Assert.That(stale.State, Is.EqualTo(CommandState.Failed));
            Assert.That(stale.Phase, Is.EqualTo("stale_ref"));
            Assert.That(stale.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
        });
    }

    [Test]
    public void InventoryViewResolverSeparatesLifecycleFromInvalidCapacityAndBacking()
    {
        var current = InventoryViewResolver.ClassifyPlayerSlot(
            true, true, true, 12, 12, 11);
        var ownerStale = InventoryViewResolver.ClassifyPlayerSlot(
            true, true, false, 12, 12, 0);
        var invalidCapacity = InventoryViewResolver.ClassifyPlayerSlot(
            true, true, true, -1, 0, 0);
        var oversizedBacking = InventoryViewResolver.ClassifyPlayerSlot(
            true, true, true, 12, 13, 0);

        Assert.Multiple(() =>
        {
            Assert.That(current, Is.EqualTo(InventorySlotLookupStatus.Resolved));
            Assert.That(ownerStale, Is.EqualTo(InventorySlotLookupStatus.Stale));
            Assert.That(invalidCapacity, Is.EqualTo(InventorySlotLookupStatus.Unavailable));
            Assert.That(oversizedBacking, Is.EqualTo(InventorySlotLookupStatus.Unavailable));
            Assert.That(
                () => InventoryViewResolver.ValidateBounds(-1, 0),
                Throws.TypeOf<InventoryViewException>().And.Property("Code").EqualTo(ErrorCode.Internal)
            );
            Assert.That(
                () => InventoryViewResolver.ValidateBounds(12, 13),
                Throws.TypeOf<InventoryViewException>().And.Property("Code").EqualTo(ErrorCode.Internal)
            );
        });
    }

    private static InventorySnapshot CompleteSnapshot() => new()
    {
        ContainerKind = "player",
        SlotCount = 3,
        Slots =
        {
            new InventorySlot
            {
                Index = 0,
                Item = new ItemFact
                {
                    Ref = new Ref { Value = "opaque-item" },
                    QualifiedItemId = "(O)24",
                    DisplayName = "Parsnip",
                    Stack = 2,
                    Category = "-75",
                },
            },
            new InventorySlot { Index = 1 },
            new InventorySlot { Index = 2 },
        },
    };

    private static CommandRequest Request(QueryInventoryRequest query) => new()
    {
        QueryInventory = query,
    };

    private sealed class ProjectionOwner : IInventoryRefOwner
    {
        private readonly object _identity = new();
        private readonly object?[] _targets;
        private readonly string[] _guards;

        public ProjectionOwner(int capacity)
        {
            _targets = new object?[capacity];
            _guards = new string[capacity];
        }

        public InventoryItemProvenance Provenance => InventoryItemProvenance.Player;

        public void Set(int slot, object? target, string guard)
        {
            _targets[slot] = target;
            _guards[slot] = guard;
        }

        public bool TryGetIdentity(out object identity)
        {
            identity = _identity;
            return true;
        }

        public InventorySlotLookup ResolveCurrentSlot(int slot) =>
            slot < 0 || slot >= _targets.Length
                ? new InventorySlotLookup(InventorySlotLookupStatus.Stale)
                : new InventorySlotLookup(
                    InventorySlotLookupStatus.Resolved,
                    _targets[slot],
                    _guards[slot]
                );
    }
}
