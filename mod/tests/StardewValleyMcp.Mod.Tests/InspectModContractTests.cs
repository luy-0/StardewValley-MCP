using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class InspectModContractTests
{
    [Test]
    public void ValidatorEnforcesCountUniquenessAndPublicStrings()
    {
        var valid = Request("a", string.Concat(Enumerable.Repeat("😀", 512)));
        var empty = new CommandRequest { Inspect = new InspectRequest() };
        var duplicate = Request("same", "same");
        var nul = Request("bad\0ref");
        var tooLong = Request(string.Concat(Enumerable.Repeat("😀", 513)));
        var tooMany = Request(Enumerable.Range(0, 65).Select(index => $"r-{index}").ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(InspectRequestValidator.Validate(valid), Is.Null);
            Assert.That(InspectRequestValidator.Validate(empty)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(InspectRequestValidator.Validate(duplicate)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(InspectRequestValidator.Validate(nul)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(InspectRequestValidator.Validate(tooLong)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(InspectRequestValidator.Validate(tooMany)?.Code, Is.EqualTo(ErrorCode.OutOfRange));
        });
    }

    [Test]
    public void BatchPreservesOrderLengthAndPerItemFailureIsolation()
    {
        var request = Request("ok-1", "missing", "broken", "ok-2").Inspect;
        var lookupCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = InspectHandler.Assemble(
            request,
            reference =>
            {
                lookupCount[reference.Value] = lookupCount.GetValueOrDefault(reference.Value) + 1;
                return reference.Value == "missing"
                    ? Failed(reference, RefStatus.NotFound, RefKind.Unspecified, ErrorCode.NotFound)
                    : Resolved(reference, RefKind.InventoryItem, ItemTarget());
            },
            (reference, _) =>
            {
                if (reference.Value == "broken")
                    throw new InvalidOperationException("temporary getter failure");
                return ItemProjection(reference);
            }
        );

        Assert.Multiple(() =>
        {
            Assert.That(result.Items.Count, Is.EqualTo(request.Refs.Count));
            Assert.That(
                result.Items.Select(item => item.Resolution.Ref.Value),
                Is.EqualTo(request.Refs.Select(reference => reference.Value))
            );
            Assert.That(result.Items.Select(item => item.Resolution.Status), Is.EqualTo(new[]
            {
                RefStatus.Resolved,
                RefStatus.NotFound,
                RefStatus.FactUnavailable,
                RefStatus.Resolved,
            }));
            Assert.That(lookupCount.Values, Is.All.EqualTo(1));
            Assert.That(result.Items[0].InventoryItem.Ref.Value, Is.EqualTo("ok-1"));
            Assert.That(result.Items[3].InventoryItem.Ref.Value, Is.EqualTo("ok-2"));
        });
    }

    [Test]
    public void ExplicitFiveKindDispatchKeepsInputRefs()
    {
        var kinds = new[]
        {
            RefKind.WorldEntity,
            RefKind.Character,
            RefKind.InventoryItem,
            RefKind.Container,
            RefKind.UiElement,
        };
        var request = Request(kinds.Select((_, index) => $"r-{index}").ToArray()).Inspect;
        var byRef = request.Refs.Select((reference, index) =>
                (reference.Value, Target: Target(kinds[index])))
            .ToDictionary(item => item.Value, item => item.Target, StringComparer.Ordinal);
        var result = InspectHandler.Assemble(
            request,
            reference => Resolved(reference, byRef[reference.Value].Kind, byRef[reference.Value]),
            (reference, target) => Projection(reference, target.Kind)
        );

        Assert.Multiple(() =>
        {
            Assert.That(result.Items.Select(item => item.FactCase), Is.EqualTo(new[]
            {
                InspectedRef.FactOneofCase.WorldEntity,
                InspectedRef.FactOneofCase.Character,
                InspectedRef.FactOneofCase.InventoryItem,
                InspectedRef.FactOneofCase.Inventory,
                InspectedRef.FactOneofCase.UiElement,
            }));
            Assert.That(result.Items.All(item => item.Resolution.Status == RefStatus.Resolved), Is.True);
            Assert.That(result.Items.Select(FactRef), Is.EqualTo(request.Refs.Select(item => item.Value)));
        });
    }

    [Test]
    public void FactUnavailableBindingRecoversWithoutResigning()
    {
        const string instanceId = "11111111-1111-4111-8111-111111111111";
        var store = new OpaqueRefStore(instanceId);
        var owner = new RecoverableOwner();
        var target = new object();
        owner.Target = target;
        var reference = store.ObserveInventoryItem(owner, 0, target, "guard");

        owner.Status = InventorySlotLookupStatus.Unavailable;
        var unavailable = store.ResolveForInspect(reference);
        owner.Status = InventorySlotLookupStatus.Resolved;
        var recovered = store.ResolveForInspect(reference);

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.Resolution.Status, Is.EqualTo(RefStatus.FactUnavailable));
            Assert.That(unavailable.Resolution.Kind, Is.EqualTo(RefKind.InventoryItem));
            Assert.That(unavailable.Resolution.Error?.Code, Is.EqualTo(ErrorCode.Internal));
            Assert.That(recovered.Resolution.Status, Is.EqualTo(RefStatus.Resolved));
            Assert.That(recovered.Resolution.Ref.Value, Is.EqualTo(reference.Value));
            Assert.That(recovered.Target, Is.TypeOf<InventoryItemInspectTarget>());
        });
    }

    private static CommandRequest Request(params string[] refs)
    {
        var request = new CommandRequest { Inspect = new InspectRequest() };
        request.Inspect.Refs.AddRange(refs.Select(value => new Ref { Value = value }));
        return request;
    }

    private static InspectRefLookup Resolved(
        Ref reference,
        RefKind kind,
        InspectableRefTarget target
    ) => new(
        new RefResolution
        {
            Ref = reference.Clone(),
            Status = RefStatus.Resolved,
            Kind = kind,
        },
        target
    );

    private static InspectRefLookup Failed(
        Ref reference,
        RefStatus status,
        RefKind kind,
        ErrorCode code
    ) => new(
        new RefResolution
        {
            Ref = reference.Clone(),
            Status = status,
            Kind = kind,
            Error = new Error { Code = code, Message = "failed" },
        },
        null
    );

    private static InventoryItemInspectTarget ItemTarget() => new(
        new InventoryItemRefTarget(new object(), 0, InventoryItemProvenance.Player)
    );

    private static InspectableRefTarget Target(RefKind kind) => kind switch
    {
        RefKind.WorldEntity => new TestInspectTarget(kind),
        RefKind.Character => new TestInspectTarget(kind),
        RefKind.InventoryItem => new TestInspectTarget(kind),
        RefKind.Container => new TestInspectTarget(kind),
        RefKind.UiElement => new TestInspectTarget(kind),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static InspectProjectionResult Projection(Ref reference, RefKind kind)
    {
        var item = new InspectedRef();
        switch (kind)
        {
            case RefKind.WorldEntity:
                item.WorldEntity = new WorldEntityFact { Ref = reference.Clone() };
                break;
            case RefKind.Character:
                item.Character = new CharacterFact { Ref = reference.Clone() };
                break;
            case RefKind.InventoryItem:
                item.InventoryItem = new ItemFact { Ref = reference.Clone() };
                break;
            case RefKind.Container:
                item.Inventory = new InventorySnapshot { ContainerRef = reference.Clone() };
                break;
            case RefKind.UiElement:
                item.UiElement = new UiElementFact { Ref = reference.Clone() };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return new InspectProjectionResult(item, Array.Empty<QueryWarning>());
    }

    private static InspectProjectionResult ItemProjection(Ref reference) =>
        Projection(reference, RefKind.InventoryItem);

    private static string FactRef(InspectedRef item) => item.FactCase switch
    {
        InspectedRef.FactOneofCase.WorldEntity => item.WorldEntity.Ref.Value,
        InspectedRef.FactOneofCase.Character => item.Character.Ref.Value,
        InspectedRef.FactOneofCase.InventoryItem => item.InventoryItem.Ref.Value,
        InspectedRef.FactOneofCase.Inventory => item.Inventory.ContainerRef.Value,
        InspectedRef.FactOneofCase.UiElement => item.UiElement.Ref.Value,
        _ => "",
    };

    private sealed class RecoverableOwner : IInventoryRefOwner
    {
        private readonly object _identity = new();

        public object? Target { get; set; }
        public InventorySlotLookupStatus Status { get; set; }
        public InventoryItemProvenance Provenance => InventoryItemProvenance.Player;
        public bool TryGetIdentity(out object identity)
        {
            identity = _identity;
            return true;
        }

        public InventorySlotLookup ResolveCurrentSlot(int slot) =>
            new(Status, Target, "guard");
    }

    private sealed record TestInspectTarget(RefKind Value) : InspectableRefTarget(Value);
}
