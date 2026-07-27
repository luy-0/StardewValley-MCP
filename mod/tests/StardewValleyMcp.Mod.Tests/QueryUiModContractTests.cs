using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class QueryUiModContractTests
{
    private const string InstanceId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

    [Test]
    public void OpaqueRefStore_UiBindingRequiresMenuEpochComponentTargetAndIndex()
    {
        var epochs = new Queue<string>(new[] { "menu-a", "menu-b" });
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => epochs.Dequeue());
        var menu = new object();
        var component = new object();
        var target = new object();
        var owner = new FakeUiOwner(menu);
        owner.Set(3, component, target, "tab:3");

        var first = Project(store, menu, owner, Descriptor(3, component, target));
        var repeated = Project(store, menu, owner, Descriptor(3, component, target));
        var reference = first.Snapshot.Elements.Single().Ref;
        var resolved = store.ResolveUiElement(reference);

        Assert.Multiple(() =>
        {
            Assert.That(repeated.Snapshot.Elements.Single().Ref.Value, Is.EqualTo(reference.Value));
            Assert.That(repeated.Snapshot.UiRevision, Is.EqualTo(first.Snapshot.UiRevision));
            Assert.That(resolved.Status, Is.EqualTo(UiElementResolveStatus.Resolved));
            Assert.That(resolved.Kind, Is.EqualTo(RefKind.UiElement));
            Assert.That(resolved.Target?.Target, Is.SameAs(target));
            Assert.That(resolved.Target?.MenuEpoch, Is.EqualTo("menu-a"));
            Assert.That(resolved.Target?.Index, Is.EqualTo(3));
        });

        var newComponent = new object();
        owner.Set(3, newComponent, target, "tab:3");
        var replaced = Project(store, menu, owner, Descriptor(3, newComponent, target));
        Assert.Multiple(() =>
        {
            Assert.That(replaced.Snapshot.Elements.Single().Ref.Value, Is.Not.EqualTo(reference.Value));
            Assert.That(store.ResolveUiElement(reference).Status, Is.EqualTo(UiElementResolveStatus.Stale));
        });
    }

    [Test]
    public void OpaqueRefStore_StaleUiTokenNeverResurrectsAfterReappearance()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "menu-a");
        var menu = new object();
        var component = new object();
        var target = new object();
        var owner = new FakeUiOwner(menu);
        owner.Set(1, component, target, "tab:1");

        var first = Project(store, menu, owner, Descriptor(1, component, target));
        var oldRef = first.Snapshot.Elements.Single().Ref;
        owner.Remove(1);
        Project(store, menu, owner);
        owner.Set(1, component, target, "tab:1");
        var returned = Project(store, menu, owner, Descriptor(1, component, target));

        Assert.Multiple(() =>
        {
            Assert.That(store.ResolveUiElement(oldRef).Status, Is.EqualTo(UiElementResolveStatus.Stale));
            Assert.That(returned.Snapshot.Elements.Single().Ref.Value, Is.Not.EqualTo(oldRef.Value));
            Assert.That(store.ResolveUiElement(returned.Snapshot.Elements.Single().Ref).Status, Is.EqualTo(UiElementResolveStatus.Resolved));
        });
    }

    [Test]
    public void OpaqueRefStore_MenuReplacementAndNoMenuStalePreviousBindings()
    {
        var epochs = new Queue<string>(new[] { "menu-a", "menu-b" });
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => epochs.Dequeue());
        var firstMenu = new object();
        var firstOwner = new FakeUiOwner(firstMenu);
        var firstComponent = new object();
        var firstTarget = new object();
        firstOwner.Set(0, firstComponent, firstTarget, "tab:0");
        var first = Project(store, firstMenu, firstOwner, Descriptor(0, firstComponent, firstTarget));

        var secondMenu = new object();
        var secondOwner = new FakeUiOwner(secondMenu);
        var secondComponent = new object();
        var secondTarget = new object();
        secondOwner.Set(0, secondComponent, secondTarget, "tab:0");
        var second = Project(store, secondMenu, secondOwner, Descriptor(0, secondComponent, secondTarget));

        Assert.Multiple(() =>
        {
            Assert.That(first.Snapshot.UiRevision, Is.Not.EqualTo(second.Snapshot.UiRevision));
            Assert.That(store.ResolveUiElement(first.Snapshot.Elements.Single().Ref).Status, Is.EqualTo(UiElementResolveStatus.Stale));
        });
        UiProjector.ProjectNoMenu(store);
        Assert.That(store.ResolveUiElement(second.Snapshot.Elements.Single().Ref).Status, Is.EqualTo(UiElementResolveStatus.Stale));
    }

    [Test]
    public void OpaqueRefStore_UiUnavailableDoesNotPoisonLaterResolution()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "menu-a");
        var menu = new object();
        var owner = new FakeUiOwner(menu);
        var component = new object();
        var target = new object();
        owner.Set(2, component, target, "tab:2");
        var projected = Project(store, menu, owner, Descriptor(2, component, target));
        var reference = projected.Snapshot.Elements.Single().Ref;

        owner.Unavailable = true;
        var unavailable = store.ResolveUiElement(reference);
        owner.Unavailable = false;
        var recovered = store.ResolveUiElement(reference);

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.Status, Is.EqualTo(UiElementResolveStatus.Unavailable));
            Assert.That(unavailable.Error?.Code, Is.EqualTo(ErrorCode.Internal));
            Assert.That(recovered.Status, Is.EqualTo(UiElementResolveStatus.Resolved));
        });
    }

    [Test]
    public void UiProjector_DescriptorOrderIsCanonicalAndWarningsAreStable()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "menu-a");
        var menu = new object();
        var owner = new FakeUiOwner(menu);
        var componentA = new object();
        var componentB = new object();
        var targetA = new object();
        var targetB = new object();
        owner.Set(1, componentA, targetA, "tab:1");
        owner.Set(0, componentB, targetB, "tab:0");
        var a = Descriptor(1, componentA, targetA) with { Label = "技能" };
        var b = Descriptor(0, componentB, targetB) with { Label = "背包" };
        var warnings = new[]
        {
            new QueryWarning { Code = "Z", Message = "后" },
            new QueryWarning { Code = "A", Message = "前" },
        };

        var first = Project(store, menu, owner, new[] { a, b }, warnings);
        var repeated = Project(store, menu, owner, new[] { b, a }, warnings.Reverse());

        Assert.Multiple(() =>
        {
            Assert.That(first.Snapshot.Elements.Select(item => item.Index), Is.EqualTo(new uint[] { 0, 1 }));
            Assert.That(repeated.Snapshot.Elements.Select(item => item.Ref.Value), Is.EqualTo(first.Snapshot.Elements.Select(item => item.Ref.Value)));
            Assert.That(repeated.Snapshot.UiRevision, Is.EqualTo(first.Snapshot.UiRevision));
            Assert.That(repeated.Warnings.Select(item => item.Code), Is.EqualTo(new[] { "A", "Z" }));
        });
    }

    [Test]
    public void UiRevision_AllPublicFactsAndMenuEpochAffectCanonicalHash()
    {
        var baseSnapshot = Snapshot();
        var baseline = Hash(baseSnapshot, "epoch-a");
        var mutations = new Action<UiSnapshot>[]
        {
            value => value.Menu!.MenuType = "Other",
            value => value.Menu!.MenuKind = MenuKind.Skills,
            value => value.Menu!.Title = "Other",
            value => value.Menu!.Modal = true,
            value => value.Menu!.DialogueText = "Text",
            value => value.Elements[0].Ref = new Ref { Value = "other" },
            value => value.Elements[0].Kind = UiElementKind.ItemSlot,
            value => value.Elements[0].Label = "Other",
            value => value.Elements[0].Visible = false,
            value => value.Elements[0].Enabled = false,
            value => value.Elements[0].Center.X++,
            value => value.Elements[0].Index++,
            value => value.Elements[0].Item = new ItemFact { QualifiedItemId = "(O)24" },
            value => value.Elements[0].Price = 0,
            value => value.Elements[0].Stock = 0,
        };

        Assert.That(Hash(baseSnapshot, "epoch-b"), Is.Not.EqualTo(baseline));
        foreach (var mutate in mutations)
        {
            var changed = baseSnapshot.Clone();
            mutate(changed);
            Assert.That(Hash(changed, "epoch-a"), Is.Not.EqualTo(baseline));
        }
        Assert.That(UiRevision.CanonicalMaterial(baseSnapshot, "epoch-a", UiExtractorKind.GameMenuTab, "tab:0"), Is.EqualTo(UiRevision.CanonicalMaterial(baseSnapshot.Clone(), "epoch-a", UiExtractorKind.GameMenuTab, "tab:0")));
    }

    [Test]
    public void QueryUiClassifier_DerivedVanillaMenusAreUnsupportedShellOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UiProjectionPolicy.ClassifyExact(typeof(BaseGameMenu), typeof(BaseGameMenu), typeof(BaseDialogue), typeof(BaseShop)), Is.EqualTo(UiMenuClassification.GameMenu));
            Assert.That(UiProjectionPolicy.ClassifyExact(typeof(DerivedGameMenu), typeof(BaseGameMenu), typeof(BaseDialogue), typeof(BaseShop)), Is.EqualTo(UiMenuClassification.Unsupported));
            Assert.That(UiProjectionPolicy.IsExactModal(typeof(BaseDialogue), typeof(BaseDialogue), typeof(BaseLetter)), Is.True);
            Assert.That(UiProjectionPolicy.IsExactModal(typeof(DerivedDialogue), typeof(BaseDialogue), typeof(BaseLetter)), Is.False);
        });
    }

    [Test]
    public void UiGeometry_UsesPureBoundsWithoutCallingClickableHitTesting()
    {
        var viewport = new UiBounds(0, 0, 1280, 720);
        Assert.Multiple(() =>
        {
            Assert.That(UiProjectionPolicy.IsVisible(new UiBounds(10, 20, 64, 32), true, viewport), Is.True);
            Assert.That(UiProjectionPolicy.Center(new UiBounds(10, 20, 65, 33)), Is.EqualTo((42, 36)));
            Assert.That(UiProjectionPolicy.IsVisible(new UiBounds(-64, 20, 64, 32), true, viewport), Is.False);
            Assert.That(UiProjectionPolicy.IsVisible(new UiBounds(0, 0, 1, 1), false, viewport), Is.False);
        });
    }

    [Test]
    public void DialogueProjector_UnreadableOrUnpresentedTextCannotBeEnabled()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UiProjectionPolicy.DialogueEnabled(true, false, true, true, 4, 5), Is.True);
            Assert.That(UiProjectionPolicy.DialogueEnabled(true, false, true, false, 100, 0), Is.False);
            Assert.That(UiProjectionPolicy.DialogueEnabled(true, true, true, true, 4, 5), Is.False);
            Assert.That(UiProjectionPolicy.DialogueEnabled(true, false, false, true, 4, 5), Is.False);
            Assert.That(UiProjectionPolicy.DialogueEnabled(true, false, true, true, 3, 5), Is.False);
        });
    }

    [Test]
    public void ShopProjector_SelectsOnlyViewportAndUsesFullConservativeFormula()
    {
        Assert.That(UiProjectionPolicy.SelectShopViewport(7, 4, 20), Is.EqualTo(new[] { 7, 8, 9, 10 }));
        Assert.That(UiProjectionPolicy.SelectShopViewport(7, 4, 9), Is.EqualTo(new[] { 7, 8 }));
        Assert.That(UiProjectionPolicy.SelectShopViewport(0, 17, 20), Is.Null);

        var ready = new ShopActivationFacts(true, true, false, false, false, 1, 50, 50, true, false, true);
        Assert.That(UiProjectionPolicy.ShopEnabled(ready), Is.True);
        var blockers = new[]
        {
            ready with { Visible = false },
            ready with { SafetyReady = false },
            ready with { HasHeldItem = true },
            ready with { ReadOnly = true },
            ready with { Stock = 0 },
            ready with { CurrencyAmount = 49 },
            ready with { HasRequiredTradeItem = false },
            ready with { HasCanPurchaseCheck = true },
            ready with { VanillaSafeSalable = false },
        };
        Assert.That(blockers.All(value => !UiProjectionPolicy.ShopEnabled(value)), Is.True);
        Assert.That(UiProjectionPolicy.ShopEnabled(ready with { UnlimitedStock = true, Stock = 0 }), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(UiProjectionPolicy.IsExactActivationKnownType(typeof(BaseSalable), typeof(BaseSalable)), Is.True);
            Assert.That(UiProjectionPolicy.IsExactActivationKnownType(typeof(DerivedSalable), typeof(BaseSalable)), Is.False);
        });
    }

    [Test]
    public void QueryUiHandler_NoMenuAndUnsupportedMenusReturnCanonicalSnapshots()
    {
        var epochs = new Queue<string>(new[] { "unsupported-a", "unsupported-b" });
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => epochs.Dequeue());
        var noMenu = UiProjector.ProjectNoMenu(store);
        var noMenuRepeated = UiProjector.ProjectNoMenu(store);
        var firstMenu = new object();
        var first = Project(store, firstMenu, new FakeUiOwner(firstMenu), Array.Empty<UiElementDescriptor>(), new[]
        {
            new QueryWarning { Code = "UI_MENU_UNSUPPORTED", Message = "当前菜单类型仅提供公共外壳" },
        }, UiExtractorKind.Unsupported);
        var repeated = Project(store, firstMenu, new FakeUiOwner(firstMenu), Array.Empty<UiElementDescriptor>(), first.Warnings, UiExtractorKind.Unsupported);
        var secondMenu = new object();
        var replacement = Project(store, secondMenu, new FakeUiOwner(secondMenu), Array.Empty<UiElementDescriptor>(), first.Warnings, UiExtractorKind.Unsupported);

        Assert.Multiple(() =>
        {
            Assert.That(noMenu.Snapshot.MenuOpen, Is.False);
            Assert.That(noMenu.Snapshot.Menu, Is.Null);
            Assert.That(noMenu.Snapshot.UiRevision, Is.EqualTo(noMenuRepeated.Snapshot.UiRevision));
            Assert.That(first.Snapshot.MenuOpen, Is.True);
            Assert.That(first.Snapshot.Elements, Is.Empty);
            Assert.That(first.Warnings.Single().Code, Is.EqualTo("UI_MENU_UNSUPPORTED"));
            Assert.That(repeated.Snapshot.UiRevision, Is.EqualTo(first.Snapshot.UiRevision));
            Assert.That(replacement.Snapshot.UiRevision, Is.Not.EqualTo(first.Snapshot.UiRevision));
        });
    }

    [Test]
    public void QueryUiHandler_ValidatesOperationAndBuildsOnlyTerminalOutcomes()
    {
        var valid = new CommandRequest { QueryUi = new QueryUiRequest() };
        var invalid = new CommandRequest { QueryRuntime = new QueryRuntimeRequest() };
        var result = UiProjector.ProjectNoMenu(new OpaqueRefStore(InstanceId));
        var succeeded = QueryUiHandler.Succeeded("command", result);
        var failed = QueryUiHandler.Failed(
            "command",
            ErrorCode.ExecutionFailed,
            "UI 基本事实不可读",
            "ui_projection_failed"
        );

        Assert.Multiple(() =>
        {
            Assert.That(QueryUiRequestValidator.Validate(valid), Is.Null);
            Assert.That(QueryUiRequestValidator.Validate(invalid)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(succeeded.State, Is.EqualTo(CommandState.Succeeded));
            Assert.That(succeeded.Result.QueryUi, Is.SameAs(result));
            Assert.That(failed.State, Is.EqualTo(CommandState.Failed));
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(failed.Phase, Is.EqualTo("ui_projection_failed"));
        });
    }

    [Test]
    public void CapabilityRegistry_ExplicitlyAdvertisesQueryUiWithSharedObservationSet()
    {
        var registry = new CapabilityRegistry(InstanceId);
        Assert.That(
            registry.Snapshot.Capabilities.Select(item => item.Id),
            Is.EqualTo(new[] { "query_inventory", "query_runtime", "query_ui", "query_world" })
        );
    }

    [Test]
    public void UiElementFact_PreservesOptionalZeroPriceAndStockPresence()
    {
        var descriptor = Descriptor(0, new object(), new object()) with
        {
            Kind = UiElementKind.ItemSlot,
            Extractor = UiExtractorKind.ShopSaleRow,
            Price = 0,
            Stock = 0,
        };
        var fact = descriptor.ToFact(new Ref { Value = "ref" });

        Assert.Multiple(() =>
        {
            Assert.That(fact.HasPrice, Is.True);
            Assert.That(fact.Price, Is.Zero);
            Assert.That(fact.HasStock, Is.True);
            Assert.That(fact.Stock, Is.Zero);
            Assert.That(fact.Item, Is.Null);
        });
    }

    private static QueryUiResult Project(
        OpaqueRefStore store,
        object menu,
        FakeUiOwner owner,
        params UiElementDescriptor[] descriptors
    ) => Project(store, menu, owner, descriptors, Array.Empty<QueryWarning>());

    private static QueryUiResult Project(
        OpaqueRefStore store,
        object menu,
        FakeUiOwner owner,
        IReadOnlyList<UiElementDescriptor> descriptors,
        IEnumerable<QueryWarning> warnings,
        UiExtractorKind extractor = UiExtractorKind.GameMenuTab
    ) => UiProjector.ProjectDescriptors(
        menu,
        new UiMenuFact { MenuType = "GameMenu", Title = "背包" },
        extractor,
        "tab:0",
        descriptors,
        warnings,
        owner,
        store
    );

    private static UiElementDescriptor Descriptor(int index, object component, object target) =>
        new(
            UiExtractorKind.GameMenuTab,
            UiElementKind.Tab,
            index,
            component,
            target,
            $"tab:{index}",
            $"Tab {index}",
            true,
            index != 0,
            100 + index,
            200 + index
        );

    private static UiSnapshot Snapshot() => new()
    {
        MenuOpen = true,
        Menu = new UiMenuFact
        {
            MenuType = "GameMenu",
            MenuKind = MenuKind.Inventory,
            Title = "背包",
        },
        Elements =
        {
            new UiElementFact
            {
                Ref = new Ref { Value = "ref-a" },
                Kind = UiElementKind.Tab,
                Label = "背包",
                Visible = true,
                Enabled = true,
                Center = new PixelPoint { X = 100, Y = 200 },
            },
        },
    };

    private static string Hash(UiSnapshot snapshot, string epoch)
    {
        var clone = snapshot.Clone();
        return UiRevision.Finalize(clone, epoch, UiExtractorKind.GameMenuTab, "tab:0");
    }

    private sealed class FakeUiOwner : IUiElementRefOwner
    {
        private readonly object _menu;
        private readonly Dictionary<int, (object Component, object Target, string Guard)> _current = new();

        public FakeUiOwner(object menu) => _menu = menu;
        public bool Unavailable { get; set; }

        public void Set(int index, object component, object target, string guard) =>
            _current[index] = (component, target, guard);

        public void Remove(int index) => _current.Remove(index);

        public bool TryGetMenuIdentity(out object menu)
        {
            menu = _menu;
            return true;
        }

        public UiElementLookup ResolveCurrentElement(UiElementBindingIdentity identity)
        {
            if (Unavailable)
                return new UiElementLookup(UiElementLookupStatus.Unavailable);
            if (!_current.TryGetValue(identity.Index, out var current))
                return new UiElementLookup(UiElementLookupStatus.Stale);
            return new UiElementLookup(
                UiElementLookupStatus.Resolved,
                current.Component,
                current.Target,
                current.Guard
            );
        }
    }

    private class BaseGameMenu { }
    private sealed class DerivedGameMenu : BaseGameMenu { }
    private class BaseDialogue { }
    private sealed class DerivedDialogue : BaseDialogue { }
    private sealed class BaseShop { }
    private sealed class BaseLetter { }
    private class BaseSalable { }
    private sealed class DerivedSalable : BaseSalable { }
}
