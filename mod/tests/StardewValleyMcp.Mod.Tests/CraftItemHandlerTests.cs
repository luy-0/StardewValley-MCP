using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class CraftItemHandlerTests
{
    private const string InstanceId = "95555555-5555-4555-8555-555555555555";

    [Test]
    public void ValidationRequiresRecipeCountAndRevision()
    {
        var fixture = NewFixture();
        var invalidCount = fixture.Request();
        invalidCount.CraftCount = 0;
        var invalidRevision = fixture.Request();
        invalidRevision.UiRevision = "bad";

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Handler.Validate(new CommandRequest
            {
                CraftItem = new CraftItemRequest(),
            })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Handler.Validate(new CommandRequest
            {
                CraftItem = invalidCount,
            })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Handler.Validate(new CommandRequest
            {
                CraftItem = invalidRevision,
            })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
        });
    }

    [Test]
    public void TwoTickSuccessReturnsActualOutputsConsumptionAndRevisions()
    {
        var fixture = NewFixture();
        var continuation = fixture.Start(craftCount: 2);

        Assert.That(continuation.Tick(ContinuationStopSignal.None),
            Is.TypeOf<ContinuationStep.Pending>());
        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(
            ContinuationStopSignal.None
        );
        var result = succeeded.Result.CraftItem;

        Assert.Multiple(() =>
        {
            Assert.That(result.RequestedCraftCount, Is.EqualTo(2));
            Assert.That(result.CompletedCraftCount, Is.EqualTo(2));
            Assert.That(result.StopReason, Is.EqualTo(CraftItemStopReason.Completed));
            Assert.That(result.Outputs.Single().QualifiedItemId, Is.EqualTo("(O)322"));
            Assert.That(result.Outputs.Single().Quantity, Is.EqualTo(2));
            Assert.That(result.MaterialsConsumed.Single().IngredientKey, Is.EqualTo("388"));
            Assert.That(result.MaterialsConsumed.Single().Quantity, Is.EqualTo(4));
            Assert.That(result.PlayerInventoryRevision, Is.EqualTo(Revision(2)));
            Assert.That(result.UiRevision, Is.EqualTo(Revision(3)));
            Assert.That(fixture.Runtime.CommitCalls, Is.EqualTo(1));
        });
    }

    [TestCase(1, CraftItemStopReason.MaterialsInsufficient)]
    [TestCase(2, CraftItemStopReason.InventoryFull)]
    public void PartialCompletionReturnsStableStopReason(
        int runtimeReasonValue,
        CraftItemStopReason publicReason
    )
    {
        var runtimeReason = (CraftItemRuntimeStopReason)runtimeReasonValue;
        var fixture = NewFixture(completed: 1, stopReason: runtimeReason);
        var continuation = fixture.Start(craftCount: 3);
        continuation.Tick(ContinuationStopSignal.None);

        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(succeeded.Result.CraftItem.CompletedCraftCount, Is.EqualTo(1));
            Assert.That(succeeded.Result.CraftItem.StopReason, Is.EqualTo(publicReason));
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public void NoCompletedCraftFailsNotReady(int reasonValue)
    {
        var reason = (CraftItemRuntimeStopReason)reasonValue;
        var fixture = NewFixture(completed: 0, stopReason: reason);
        var continuation = fixture.Start();
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.NotReady));
    }

    [Test]
    public void StaleRevisionFailsBeforeCommit()
    {
        var fixture = NewFixture();
        var request = fixture.Request();
        request.UiRevision = Revision(9);

        var failed = (ContinuationStep.Failed)fixture.Start(request)
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(fixture.Runtime.CommitCalls, Is.Zero);
        });
    }

    [Test]
    public void ContextChangeBetweenTicksFailsBeforeCommit()
    {
        var fixture = NewFixture();
        var continuation = fixture.Start();
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Runtime.Page = new object();

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(fixture.Runtime.CommitCalls, Is.Zero);
        });
    }

    [Test]
    public void RecipeRefMustHaveCraftingRecipeKind()
    {
        var fixture = NewFixture(kind: UiElementKind.ItemSlot);
        var failed = (ContinuationStep.Failed)fixture.Start()
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Runtime.CommitCalls, Is.Zero);
        });
    }

    [Test]
    public void RecipeFromAlreadyProjectedHiddenPageCanCommitWithoutChangingPage()
    {
        var fixture = NewFixture(sourcePage: 2);
        var continuation = fixture.Start();
        continuation.Tick(ContinuationStopSignal.None);

        Assert.That(continuation.Tick(ContinuationStopSignal.None),
            Is.TypeOf<ContinuationStep.Succeeded>());
        Assert.That(fixture.Runtime.LastRecipe!.SourcePage, Is.EqualTo(2));
    }

    [Test]
    public void CancellationBeforeCommitDoesNotCallRuntimeCommit()
    {
        var fixture = NewFixture();
        var continuation = fixture.Start();
        continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(continuation.Tick(ContinuationStopSignal.CancelRequested),
                Is.TypeOf<ContinuationStep.Stopped>());
            Assert.That(fixture.Runtime.CommitCalls, Is.Zero);
        });
    }

    private static Fixture NewFixture(
        int completed = 2,
        CraftItemRuntimeStopReason stopReason = CraftItemRuntimeStopReason.Completed,
        UiElementKind kind = UiElementKind.CraftingRecipe,
        int sourcePage = 0
    )
    {
        var refs = new OpaqueRefStore(InstanceId);
        var runtime = new FakeCraftItemRuntime(completed, stopReason, sourcePage);
        var owner = new FakeRecipeOwner(
            runtime.Menu,
            runtime.Component,
            runtime.Recipe,
            kind
        );
        var session = refs.BeginUiProjection(runtime.Menu);
        var recipeRef = refs.ObserveUiElement(
            session,
            owner,
            new UiElementBindingIdentity(
                UiExtractorKind.GameMenu,
                kind,
                UiInventorySide.Unspecified,
                UiEquipmentSlotKind.Unspecified,
                0,
                runtime.Component,
                runtime.Recipe,
                "crafting-recipe:0"
            )
        );
        refs.CompleteUiProjection(session);
        return new Fixture(
            new CraftItemHandler(refs, runtime),
            runtime,
            recipeRef
        );
    }

    private static string Revision(int value) => value.ToString("x64");

    private sealed record Fixture(
        CraftItemHandler Handler,
        FakeCraftItemRuntime Runtime,
        Ref RecipeRef
    )
    {
        public CraftItemRequest Request(uint craftCount = 2) => new()
        {
            RecipeRef = RecipeRef.Clone(),
            CraftCount = craftCount,
            UiRevision = Revision(1),
        };

        public ICommandContinuation Start(uint craftCount = 2) => Start(Request(craftCount));

        public ICommandContinuation Start(CraftItemRequest request)
        {
            var command = new CommandRequest { CraftItem = request };
            Assert.That(Handler.Validate(command), Is.Null);
            return Handler.Start("craft-test", command);
        }
    }

    private sealed class FakeCraftItemRuntime : ICraftItemRuntimeAdapter
    {
        private readonly int _completed;
        private readonly CraftItemRuntimeStopReason _stopReason;
        private readonly int _sourcePage;

        public FakeCraftItemRuntime(
            int completed,
            CraftItemRuntimeStopReason stopReason,
            int sourcePage
        )
        {
            _completed = completed;
            _stopReason = stopReason;
            _sourcePage = sourcePage;
        }

        public object Menu { get; } = new();
        public object Page { get; set; } = new();
        public object Player { get; } = new();
        public object Component { get; } = new();
        public object Recipe { get; } = new();
        public int CommitCalls { get; private set; }
        public CraftItemRecipeBinding? LastRecipe { get; private set; }

        public CraftItemCapture Capture() => new(
            CraftItemCaptureStatus.Ready,
            Menu,
            Page,
            Player,
            Revision(1),
            new[] { new CraftItemRecipeBinding(_sourcePage, 0, Component, Recipe) },
            new object()
        );

        public CraftItemCommitResult Commit(
            CraftItemCapture capture,
            CraftItemRecipeBinding recipe,
            int craftCount
        )
        {
            CommitCalls++;
            LastRecipe = recipe;
            var completed = Math.Min(_completed, craftCount);
            return new CraftItemCommitResult(
                completed,
                completed == craftCount ? CraftItemRuntimeStopReason.Completed : _stopReason,
                completed == 0
                    ? Array.Empty<CraftingOutputFact>()
                    : new[]
                    {
                        new CraftingOutputFact
                        {
                            QualifiedItemId = "(O)322",
                            DisplayName = "木围栏",
                            Quantity = checked((uint)completed),
                        },
                    },
                completed == 0
                    ? Array.Empty<CraftingMaterialConsumption>()
                    : new[]
                    {
                        new CraftingMaterialConsumption
                        {
                            IngredientKey = "388",
                            Quantity = checked((uint)(completed * 2)),
                        },
                    },
                Revision(2),
                Revision(3)
            );
        }
    }

    private sealed class FakeRecipeOwner : IUiElementRefOwner
    {
        private readonly object _menu;
        private readonly object _component;
        private readonly object _recipe;
        private readonly UiElementKind _kind;

        public FakeRecipeOwner(
            object menu,
            object component,
            object recipe,
            UiElementKind kind
        )
        {
            _menu = menu;
            _component = component;
            _recipe = recipe;
            _kind = kind;
        }

        public bool TryGetMenuIdentity(out object menu)
        {
            menu = _menu;
            return true;
        }

        public UiElementLookup ResolveCurrentElement(UiElementBindingIdentity identity) =>
            identity.Extractor == UiExtractorKind.GameMenu
            && identity.PublicKind == _kind
            && identity.Index == 0
            && ReferenceEquals(identity.Component, _component)
            && ReferenceEquals(identity.SemanticTarget, _recipe)
                ? new UiElementLookup(
                    UiElementLookupStatus.Resolved,
                    _component,
                    _recipe,
                    identity.Guard
                )
                : new UiElementLookup(UiElementLookupStatus.Stale);
    }
}
