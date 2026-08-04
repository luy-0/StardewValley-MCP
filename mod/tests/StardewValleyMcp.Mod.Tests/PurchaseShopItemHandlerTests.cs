using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class PurchaseShopItemHandlerTests
{
    private const string InstanceId = "96666666-6666-4666-8666-666666666666";

    [Test]
    public void ValidationRequiresSaleCountAndRevision()
    {
        var fixture = NewFixture();
        var invalidCount = fixture.Request();
        invalidCount.PurchaseCount = 0;
        var invalidRevision = fixture.Request();
        invalidRevision.UiRevision = "bad";

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Handler.Validate(new CommandRequest
            {
                PurchaseShopItem = new PurchaseShopItemRequest(),
            })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Handler.Validate(new CommandRequest
            {
                PurchaseShopItem = invalidCount,
            })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Handler.Validate(new CommandRequest
            {
                PurchaseShopItem = invalidRevision,
            })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
        });
    }

    [Test]
    public void TwoTickSuccessReturnsActualItemMoneyStockAndRevisions()
    {
        var fixture = NewFixture();
        var continuation = fixture.Start(purchaseCount: 2);

        Assert.That(continuation.Tick(ContinuationStopSignal.None),
            Is.TypeOf<ContinuationStep.Pending>());
        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(
            ContinuationStopSignal.None
        );
        var result = succeeded.Result.PurchaseShopItem;

        Assert.Multiple(() =>
        {
            Assert.That(result.PurchaseCount, Is.EqualTo(2));
            Assert.That(result.Item.QualifiedItemId, Is.EqualTo("(O)472"));
            Assert.That(result.Item.Stack, Is.EqualTo(2));
            Assert.That(result.TotalPrice, Is.EqualTo(40));
            Assert.That(result.MoneyBefore, Is.EqualTo(100));
            Assert.That(result.MoneyAfter, Is.EqualTo(60));
            Assert.That(result.HasStockRemaining, Is.True);
            Assert.That(result.StockRemaining, Is.EqualTo(8));
            Assert.That(result.PlayerInventoryRevision, Is.EqualTo(Revision(3)));
            Assert.That(result.UiRevision, Is.EqualTo(Revision(4)));
            Assert.That(fixture.Runtime.CommitCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void InfiniteStockOmitsRemainingStock()
    {
        var fixture = NewFixture(unlimited: true);
        var continuation = fixture.Start();
        continuation.Tick(ContinuationStopSignal.None);

        var result = ((ContinuationStep.Succeeded)continuation.Tick(
            ContinuationStopSignal.None
        )).Result.PurchaseShopItem;

        Assert.That(result.HasStockRemaining, Is.False);
    }

    [Test]
    public void NotReadyPlanFailsWithoutCommit()
    {
        var fixture = NewFixture(planStatus: ShopPurchasePlanStatus.NotReady);

        var failed = (ContinuationStep.Failed)fixture.Start()
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.NotReady));
            Assert.That(fixture.Runtime.CommitCalls, Is.Zero);
        });
    }

    [Test]
    public void StaleRevisionFailsBeforePlanning()
    {
        var fixture = NewFixture();
        var request = fixture.Request();
        request.UiRevision = Revision(9);

        var failed = (ContinuationStep.Failed)fixture.Start(request)
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(fixture.Runtime.PlanCalls, Is.Zero);
        });
    }

    [Test]
    public void ContextChangeBetweenTicksFailsBeforeCommit()
    {
        var fixture = NewFixture();
        var continuation = fixture.Start();
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Runtime.Menu = new object();

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
    public void MoneyOrInventoryPlanChangeBetweenTicksFailsBeforeCommit()
    {
        var fixture = NewFixture();
        var continuation = fixture.Start();
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Runtime.Money = 99;

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
    public void SaleRefMustBeShopItemSlot()
    {
        var fixture = NewFixture(kind: UiElementKind.DialogueResponse);

        var failed = (ContinuationStep.Failed)fixture.Start()
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(fixture.Runtime.PlanCalls, Is.Zero);
        });
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

    [Test]
    public void CommitExceptionReturnsExecutionFailedAndRequiresRequery()
    {
        var fixture = NewFixture(throwOnCommit: true);
        var continuation = fixture.Start();
        continuation.Tick(ContinuationStopSignal.None);

        var failed = (ContinuationStep.Failed)continuation.Tick(
            ContinuationStopSignal.None
        );

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(failed.Error.Message, Does.Contain("重新查询"));
        });
    }

    private static Fixture NewFixture(
        ShopPurchasePlanStatus planStatus = ShopPurchasePlanStatus.Ready,
        bool unlimited = false,
        bool throwOnCommit = false,
        UiElementKind kind = UiElementKind.ItemSlot
    )
    {
        var refs = new OpaqueRefStore(InstanceId);
        var runtime = new FakeShopPurchaseRuntime(planStatus, unlimited, throwOnCommit);
        var owner = new FakeSaleOwner(
            runtime.Menu,
            runtime.Component,
            runtime.Salable,
            kind
        );
        var session = refs.BeginUiProjection(runtime.Menu);
        var saleRef = refs.ObserveUiElement(
            session,
            owner,
            new UiElementBindingIdentity(
                UiExtractorKind.ShopSaleRow,
                kind,
                UiInventorySide.Unspecified,
                UiEquipmentSlotKind.Unspecified,
                0,
                runtime.Component,
                runtime.Salable,
                "shop-sale-row:0"
            )
        );
        refs.CompleteUiProjection(session);
        return new Fixture(new PurchaseShopItemHandler(refs, runtime), runtime, saleRef);
    }

    private static string Revision(int value) => value.ToString("x64");

    private sealed record Fixture(
        PurchaseShopItemHandler Handler,
        FakeShopPurchaseRuntime Runtime,
        Ref SaleRef
    )
    {
        public PurchaseShopItemRequest Request(uint purchaseCount = 2) => new()
        {
            SaleRef = SaleRef.Clone(),
            PurchaseCount = purchaseCount,
            UiRevision = Revision(1),
        };

        public ICommandContinuation Start(uint purchaseCount = 2) =>
            Start(Request(purchaseCount));

        public ICommandContinuation Start(PurchaseShopItemRequest request)
        {
            var command = new CommandRequest { PurchaseShopItem = request };
            Assert.That(Handler.Validate(command), Is.Null);
            return Handler.Start("purchase-test", command);
        }
    }

    private sealed class FakeShopPurchaseRuntime : IShopPurchaseRuntimeAdapter
    {
        private readonly ShopPurchasePlanStatus _planStatus;
        private readonly bool _unlimited;
        private readonly bool _throwOnCommit;

        public FakeShopPurchaseRuntime(
            ShopPurchasePlanStatus planStatus,
            bool unlimited,
            bool throwOnCommit
        )
        {
            _planStatus = planStatus;
            _unlimited = unlimited;
            _throwOnCommit = throwOnCommit;
        }

        public object Menu { get; set; } = new();
        public object Player { get; } = new();
        public object Component { get; } = new();
        public object Salable { get; } = new();
        public int Money { get; set; } = 100;
        public int PlanCalls { get; private set; }
        public int CommitCalls { get; private set; }

        public ShopPurchaseCapture Capture() => new(
            ShopPurchaseCaptureStatus.Ready,
            Menu,
            Player,
            Revision(1),
            new[] { new ShopSaleBinding(0, Component, Salable, 20, 10) },
            new InventorySnapshot { InventoryRevision = Revision(2) },
            new object()
        );

        public ShopPurchasePlanResult Plan(
            ShopPurchaseCapture capture,
            ShopSaleBinding sale,
            int purchaseCount
        )
        {
            PlanCalls++;
            if (_planStatus != ShopPurchasePlanStatus.Ready)
                return new ShopPurchasePlanResult(_planStatus, null, "当前条件不允许购买");
            return new ShopPurchasePlanResult(
                ShopPurchasePlanStatus.Ready,
                new ShopPurchasePlan(
                    sale,
                    purchaseCount,
                    purchaseCount,
                    checked(20 * purchaseCount),
                    Money,
                    _unlimited,
                    capture.PlayerInventory!.InventoryRevision,
                    "StardewValley.Object:(O)472",
                    new object()
                ),
                ""
            );
        }

        public ShopPurchaseCommitResult Commit(
            ShopPurchaseCapture capture,
            ShopPurchasePlan plan
        )
        {
            CommitCalls++;
            if (_throwOnCommit)
                throw new InvalidOperationException("injected");
            return new ShopPurchaseCommitResult(
                checked((uint)plan.PurchaseCount),
                new ItemFact
                {
                    QualifiedItemId = "(O)472",
                    DisplayName = "防风草种子",
                    Stack = checked((uint)plan.OutputQuantity),
                    Category = "-74",
                },
                checked((uint)plan.TotalPrice),
                checked((uint)plan.MoneyBefore),
                checked((uint)(plan.MoneyBefore - plan.TotalPrice)),
                _unlimited ? null : 8u,
                Revision(3),
                Revision(4)
            );
        }
    }

    private sealed class FakeSaleOwner : IUiElementRefOwner
    {
        private readonly object _menu;
        private readonly object _component;
        private readonly object _salable;
        private readonly UiElementKind _kind;

        public FakeSaleOwner(
            object menu,
            object component,
            object salable,
            UiElementKind kind
        )
        {
            _menu = menu;
            _component = component;
            _salable = salable;
            _kind = kind;
        }

        public bool TryGetMenuIdentity(out object menu)
        {
            menu = _menu;
            return true;
        }

        public UiElementLookup ResolveCurrentElement(UiElementBindingIdentity identity) =>
            identity.Extractor == UiExtractorKind.ShopSaleRow
            && identity.PublicKind == _kind
            && identity.Index == 0
            && ReferenceEquals(identity.Component, _component)
            && ReferenceEquals(identity.SemanticTarget, _salable)
                ? new UiElementLookup(
                    UiElementLookupStatus.Resolved,
                    _component,
                    _salable,
                    identity.Guard
                )
                : new UiElementLookup(UiElementLookupStatus.Stale);
    }
}
