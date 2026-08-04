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
            Assert.That(resolved.Target?.Component, Is.SameAs(component));
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
    public void IncompleteCaptureMissingElementPreservesRefForLaterRecovery()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "menu-a");
        var menu = new object();
        var owner = new FakeUiOwner(menu);
        var component = new object();
        var target = new object();
        owner.Set(2, component, target, "tab:2");
        var first = Project(store, menu, owner, Descriptor(2, component, target));
        var reference = first.Snapshot.Elements.Single().Ref;

        owner.Remove(2);
        owner.Unavailable = true;
        var incomplete = Project(
            store,
            menu,
            owner,
            Array.Empty<UiElementDescriptor>(),
            new[]
            {
                new QueryWarning
                {
                    Code = "UI_ELEMENT_PROJECTION_FAILED",
                    Message = "1 个 UI 元素无法安全投影",
                },
            },
            completeness: UiElementSetCompleteness.Incomplete
        );
        var unavailable = store.ResolveForInspect(reference);

        owner.Unavailable = false;
        owner.Set(2, component, target, "tab:2");
        var recovered = Project(store, menu, owner, Descriptor(2, component, target));

        Assert.Multiple(() =>
        {
            Assert.That(incomplete.Snapshot.Elements, Is.Empty);
            Assert.That(unavailable.Resolution.Status, Is.EqualTo(RefStatus.FactUnavailable));
            Assert.That(unavailable.Resolution.Ref.Value, Is.EqualTo(reference.Value));
            Assert.That(recovered.Snapshot.Elements.Single().Ref.Value, Is.EqualTo(reference.Value));
            Assert.That(store.ResolveUiElement(reference).Status, Is.EqualTo(UiElementResolveStatus.Resolved));
        });
    }

    [Test]
    public void ProjectorSkippedDescriptorOverridesIncorrectCompleteClaim()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "menu-a");
        var menu = new object();
        var owner = new FakeUiOwner(menu);
        var component = new object();
        var target = new object();
        owner.Set(2, component, target, "tab:2");
        var first = Project(store, menu, owner, Descriptor(2, component, target));
        var reference = first.Snapshot.Elements.Single().Ref;

        owner.Remove(2);
        owner.Unavailable = true;
        var invalid = Descriptor(7, new object(), new object()) with { Guard = "" };
        var partial = Project(
            store,
            menu,
            owner,
            new[] { invalid },
            Array.Empty<QueryWarning>(),
            completeness: UiElementSetCompleteness.Complete
        );
        var unavailable = store.ResolveForInspect(reference);

        owner.Unavailable = false;
        owner.Set(2, component, target, "tab:2");
        var recovered = Project(store, menu, owner, Descriptor(2, component, target));

        Assert.Multiple(() =>
        {
            Assert.That(partial.Snapshot.Elements, Is.Empty);
            Assert.That(
                partial.Warnings.Select(warning => warning.Code),
                Is.EqualTo(new[] { "UI_ELEMENT_PROJECTION_FAILED" })
            );
            Assert.That(unavailable.Resolution.Status, Is.EqualTo(RefStatus.FactUnavailable));
            Assert.That(recovered.Snapshot.Elements.Single().Ref.Value, Is.EqualTo(reference.Value));
        });
    }

    [Test]
    public void DescriptorFactWarningDoesNotMakeElementSetIncomplete()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "menu-a");
        var menu = new object();
        var owner = new FakeUiOwner(menu);
        var keptComponent = new object();
        var keptTarget = new object();
        var removedComponent = new object();
        var removedTarget = new object();
        owner.Set(0, keptComponent, keptTarget, "tab:0");
        owner.Set(1, removedComponent, removedTarget, "tab:1");
        var first = Project(
            store,
            menu,
            owner,
            Descriptor(0, keptComponent, keptTarget),
            Descriptor(1, removedComponent, removedTarget)
        );
        var removedRef = first.Snapshot.Elements.Single(element => element.Index == 1).Ref;

        owner.Remove(1);
        var kept = Descriptor(0, keptComponent, keptTarget) with
        {
            DescriptorWarnings = new[]
            {
                new UiDescriptorWarning(
                    "UI_ITEM_FACT_UNAVAILABLE",
                    "当前商品的 Item 事实不可读"
                ),
            },
        };
        var complete = Project(store, menu, owner, kept);

        Assert.Multiple(() =>
        {
            Assert.That(complete.Warnings.Single().Code, Is.EqualTo("UI_ITEM_FACT_UNAVAILABLE"));
            Assert.That(
                store.ResolveUiElement(removedRef).Status,
                Is.EqualTo(UiElementResolveStatus.Stale)
            );
        });
    }

    [Test]
    public void InspectMissingUiElementUsesCaptureCompletenessAndIsolatesBatchItems()
    {
        var request = new InspectRequest();
        request.Refs.AddRange(new[]
        {
            new Ref { Value = "incomplete" },
            new Ref { Value = "complete" },
            new Ref { Value = "ok" },
        });
        var incomplete = Capture(UiElementSetCompleteness.Incomplete);
        var complete = Capture(UiElementSetCompleteness.Complete);
        var successful = Capture(
            UiElementSetCompleteness.Complete,
            new UiElementFact
            {
                Ref = new Ref { Value = "ok" },
                Kind = UiElementKind.Tab,
                Label = "背包",
            }
        );

        var result = InspectHandler.Assemble(
            request,
            reference => new InspectRefLookup(
                new RefResolution
                {
                    Ref = reference.Clone(),
                    Status = RefStatus.Resolved,
                    Kind = RefKind.UiElement,
                },
                new TestUiInspectTarget(RefKind.UiElement)
            ),
            (reference, _) =>
            {
                var capture = reference.Value switch
                {
                    "incomplete" => incomplete,
                    "complete" => complete,
                    _ => successful,
                };
                var warnings = new List<QueryWarning>();
                return new InspectProjectionResult(
                    new InspectedRef
                    {
                        UiElement = InspectFactProjector.ProjectUiElement(
                            reference,
                            capture,
                            warnings
                        ),
                    },
                    warnings
                );
            }
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items.Select(item => item.Resolution.Status),
                Is.EqualTo(new[]
                {
                    RefStatus.FactUnavailable,
                    RefStatus.Stale,
                    RefStatus.Resolved,
                })
            );
            Assert.That(
                result.Items.Select(item => item.Resolution.Error?.Code),
                Is.EqualTo(new ErrorCode?[]
                {
                    ErrorCode.Internal,
                    ErrorCode.StaleRef,
                    null,
                })
            );
            Assert.That(result.Items[2].UiElement.Ref.Value, Is.EqualTo("ok"));
            Assert.That(result.Warnings, Is.Empty);
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
            value => value.Elements[0].InventorySide = UiInventorySide.Player,
            value => value.Elements[0].ItemRef = new Ref { Value = "item-ref" },
            value => value.Inventories.Add(new UiInventoryLink
            {
                Side = UiInventorySide.Player,
                InventoryRevision = "inventory-revision",
                SlotCount = 36,
            }),
        };

        Assert.That(Hash(baseSnapshot, "epoch-b"), Is.Not.EqualTo(baseline));
        foreach (var mutate in mutations)
        {
            var changed = baseSnapshot.Clone();
            mutate(changed);
            Assert.That(Hash(changed, "epoch-a"), Is.Not.EqualTo(baseline));
        }
        Assert.That(UiRevision.CanonicalMaterial(baseSnapshot, "epoch-a", UiExtractorKind.GameMenu, "tab:0"), Is.EqualTo(UiRevision.CanonicalMaterial(baseSnapshot.Clone(), "epoch-a", UiExtractorKind.GameMenu, "tab:0")));
    }

    [Test]
    public void QueryUiClassifier_DerivedVanillaMenusAreUnsupportedShellOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UiProjectionPolicy.ClassifyExact(typeof(BaseGameMenu), typeof(BaseGameMenu), typeof(BaseDialogue), typeof(BaseShop), typeof(BaseItemGrab)), Is.EqualTo(UiMenuClassification.GameMenu));
            Assert.That(UiProjectionPolicy.ClassifyExact(typeof(BaseItemGrab), typeof(BaseGameMenu), typeof(BaseDialogue), typeof(BaseShop), typeof(BaseItemGrab)), Is.EqualTo(UiMenuClassification.ItemGrabMenu));
            Assert.That(UiProjectionPolicy.ClassifyExact(typeof(DerivedGameMenu), typeof(BaseGameMenu), typeof(BaseDialogue), typeof(BaseShop), typeof(BaseItemGrab)), Is.EqualTo(UiMenuClassification.Unsupported));
            Assert.That(UiProjectionPolicy.ClassifyExact(typeof(DerivedItemGrab), typeof(BaseGameMenu), typeof(BaseDialogue), typeof(BaseShop), typeof(BaseItemGrab)), Is.EqualTo(UiMenuClassification.Unsupported));
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
            Assert.That(UiProjectionPolicy.DialogueExtractor(true), Is.EqualTo(UiExtractorKind.DialogueResponse));
            Assert.That(UiProjectionPolicy.DialogueExtractor(false), Is.EqualTo(UiExtractorKind.DialogueAdvance));
            Assert.That(UiProjectionPolicy.DialogueHasNextPage(false, false, 0, 2), Is.True);
            Assert.That(UiProjectionPolicy.DialogueHasNextPage(false, false, 0, 1), Is.False);
            Assert.That(UiProjectionPolicy.DialogueHasNextPage(true, true, 1, 0), Is.True);
            Assert.That(UiProjectionPolicy.DialogueHasNextPage(true, false, 2, 0), Is.True);
            Assert.That(UiProjectionPolicy.DialogueHasNextPage(true, false, 1, 0), Is.False);
            Assert.That(UiProjectionPolicy.DialogueAdvanceLabel(true), Is.EqualTo("继续"));
            Assert.That(UiProjectionPolicy.DialogueAdvanceLabel(false), Is.EqualTo("结束"));
        });
    }

    [Test]
    public void DialogueAdvance_UsesSemanticBindingAndPageChangeStalesOldRef()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "dialogue-a");
        var menu = new object();
        var owner = new FakeUiOwner(menu);
        owner.Set(0, null, menu, "dialogue-page:1");
        var first = ProjectDialogueAdvance(store, menu, owner, "dialogue-page:1", "继续", enabled: true);
        var repeated = ProjectDialogueAdvance(store, menu, owner, "dialogue-page:1", "继续", enabled: true);
        var reference = first.Snapshot.Elements.Single().Ref;

        var inspected = store.ResolveForInspect(reference);
        var inspectFact = InspectFactProjector.ProjectUiElement(
            reference,
            new UiRuntimeProjectionCapture(repeated, UiElementSetCompleteness.Complete),
            new List<QueryWarning>()
        );

        owner.Set(0, null, menu, "dialogue-page:2");
        var next = ProjectDialogueAdvance(store, menu, owner, "dialogue-page:2", "结束", enabled: true);

        Assert.Multiple(() =>
        {
            Assert.That(repeated.Snapshot.Elements.Single().Ref.Value, Is.EqualTo(reference.Value));
            Assert.That(first.Snapshot.Elements.Single().Center, Is.EqualTo(new PixelPoint { X = 0, Y = 0 }));
            Assert.That(inspected.Resolution.Status, Is.EqualTo(RefStatus.Resolved));
            Assert.That(inspected.Target, Is.TypeOf<UiElementInspectTarget>());
            Assert.That(inspectFact.Ref.Value, Is.EqualTo(reference.Value));
            Assert.That(next.Snapshot.Elements.Single().Ref.Value, Is.Not.EqualTo(reference.Value));
            Assert.That(store.ResolveUiElement(reference).Status, Is.EqualTo(UiElementResolveStatus.Stale));
        });
    }

    [Test]
    public void DialogueAdvance_IncompleteCapturePreservesOldRefAsUnavailable()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "dialogue-a");
        var menu = new object();
        var owner = new FakeUiOwner(menu);
        owner.Set(0, null, menu, "dialogue-page:1");
        var first = ProjectDialogueAdvance(store, menu, owner, "dialogue-page:1", "继续", enabled: true);
        var reference = first.Snapshot.Elements.Single().Ref;

        owner.Unavailable = true;
        var incomplete = Project(
            store,
            menu,
            owner,
            Array.Empty<UiElementDescriptor>(),
            new[] { new QueryWarning { Code = "UI_MENU_FACT_UNAVAILABLE", Message = "当前菜单事实不可读" } },
            UiExtractorKind.DialogueAdvance,
            UiElementSetCompleteness.Incomplete
        );
        var unavailable = store.ResolveForInspect(reference);

        owner.Unavailable = false;
        owner.Set(0, null, menu, "dialogue-page:1");
        var recovered = ProjectDialogueAdvance(store, menu, owner, "dialogue-page:1", "继续", enabled: true);

        Assert.Multiple(() =>
        {
            Assert.That(incomplete.Snapshot.Elements, Is.Empty);
            Assert.That(unavailable.Resolution.Status, Is.EqualTo(RefStatus.FactUnavailable));
            Assert.That(recovered.Snapshot.Elements.Single().Ref.Value, Is.EqualTo(reference.Value));
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
    public void CapabilityRegistry_AdvertisesAllPublicV1Capabilities()
    {
        var registry = DefaultCapabilitySet.Create(InstanceId);
        Assert.That(
            registry.Snapshot.Capabilities.Select(item => item.Id),
            Is.EqualTo(new[]
            {
                "activate_ui", "close_menu", "emote", "equip", "face", "inspect", "interact",
                "navigate", "open_menu", "query_inventory", "query_runtime", "query_ui", "query_world", "say",
                "set_equipment_slot", "transfer_inventory_item", "use_tool",
            })
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

    [Test]
    public void ItemGrabProjection_ReusesInventorySnapshotsAndSeparatesBothSlotSides()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "item-grab-a");
        var playerOwner = new FakeInventoryOwner(InventoryItemProvenance.Player, 2);
        var containerOwner = new FakeInventoryOwner(InventoryItemProvenance.Container, 2);
        var playerItem = new object();
        var containerItem = new object();
        playerOwner.Set(0, playerItem, "player-item");
        containerOwner.Set(1, containerItem, "container-item");
        var playerSnapshot = InventoryProjector.ProjectCapturedSlots(
            playerOwner,
            "player",
            null,
            new[]
            {
                new CapturedInventorySlot(playerItem, "player-item"),
                new CapturedInventorySlot(null, ""),
            },
            0,
            true,
            store,
            (_, reference) => new ItemFact
            {
                Ref = reference.Clone(),
                QualifiedItemId = "(O)24",
                DisplayName = "防风草",
                Stack = 1,
            }
        );
        var containerRef = new Ref { Value = "container-ref" };
        var containerSnapshot = InventoryProjector.ProjectCapturedSlots(
            containerOwner,
            "chest",
            containerRef,
            new[]
            {
                new CapturedInventorySlot(null, ""),
                new CapturedInventorySlot(containerItem, "container-item"),
            },
            int.MinValue,
            true,
            store,
            (_, reference) => new ItemFact
            {
                Ref = reference.Clone(),
                QualifiedItemId = "(O)388",
                DisplayName = "木材",
                Stack = 12,
            }
        );
        var playerComponents = new object[]
        {
            new FakeSlotComponent(new UiBounds(0, 0, 64, 64)),
            new FakeSlotComponent(new UiBounds(64, 0, 64, 64)),
        };
        var containerComponents = new object[]
        {
            new FakeSlotComponent(new UiBounds(0, 80, 64, 64)),
            new FakeSlotComponent(new UiBounds(64, 80, 64, 64)),
        };
        var descriptors = ItemGrabMenuProjector.CreateSlotDescriptors(
                playerComponents,
                playerSnapshot,
                UiInventorySide.Player,
                component => ((FakeSlotComponent)component).Bounds,
                _ => true,
                new UiBounds(0, 0, 400, 300)
            )
            .Concat(ItemGrabMenuProjector.CreateSlotDescriptors(
                containerComponents,
                containerSnapshot,
                UiInventorySide.Container,
                component => ((FakeSlotComponent)component).Bounds,
                _ => true,
                new UiBounds(0, 0, 400, 300)
            ))
            .ToArray();
        var menu = new object();
        var uiOwner = new FakeUiOwner(menu);
        foreach (var descriptor in descriptors)
        {
            uiOwner.Set(
                descriptor.InventorySide!.Value,
                descriptor.Index,
                descriptor.Component,
                descriptor.SemanticTarget,
                descriptor.Guard
            );
        }
        var inventories = new[]
        {
            UiProjector.ToInventoryLink(UiInventorySide.Container, containerSnapshot),
            UiProjector.ToInventoryLink(UiInventorySide.Player, playerSnapshot),
        };
        var result = UiProjector.ProjectDescriptors(
            menu,
            new UiMenuFact { MenuType = "ItemGrabMenu" },
            UiExtractorKind.ItemGrabSlot,
            "item-grab",
            descriptors,
            Array.Empty<QueryWarning>(),
            uiOwner,
            store,
            UiElementSetCompleteness.Complete,
            inventories
        );

        var playerZero = result.Snapshot.Elements.Single(item =>
            item.InventorySide == UiInventorySide.Player && item.Index == 0);
        var containerZero = result.Snapshot.Elements.Single(item =>
            item.InventorySide == UiInventorySide.Container && item.Index == 0);
        var containerOne = result.Snapshot.Elements.Single(item =>
            item.InventorySide == UiInventorySide.Container && item.Index == 1);
        Assert.Multiple(() =>
        {
            Assert.That(result.Snapshot.Inventories.Select(item => item.Side), Is.EqualTo(new[]
            {
                UiInventorySide.Player,
                UiInventorySide.Container,
            }));
            Assert.That(result.Snapshot.Inventories[0].InventoryRevision, Is.EqualTo(playerSnapshot.InventoryRevision));
            Assert.That(result.Snapshot.Inventories[1].InventoryRevision, Is.EqualTo(containerSnapshot.InventoryRevision));
            Assert.That(result.Snapshot.Inventories[1].ContainerRef, Is.EqualTo(containerRef));
            Assert.That(playerZero.Ref.Value, Is.Not.EqualTo(containerZero.Ref.Value));
            Assert.That(playerZero.ItemRef, Is.EqualTo(playerSnapshot.Slots[0].Item.Ref));
            Assert.That(containerOne.ItemRef, Is.EqualTo(containerSnapshot.Slots[1].Item.Ref));
            Assert.That(containerZero.ItemRef, Is.Null);
            Assert.That(result.Snapshot.Elements.All(item => item.Item is null), Is.True);
            Assert.That(result.Snapshot.Elements.All(item => !item.Enabled), Is.True);
            Assert.That(store.ResolveUiElement(playerZero.Ref).Status, Is.EqualTo(UiElementResolveStatus.Resolved));
            Assert.That(store.ResolveUiElement(containerZero.Ref).Status, Is.EqualTo(UiElementResolveStatus.Resolved));
        });
    }

    [Test]
    public void ItemGrabProjection_IncompleteCapturePreservesBothSidedSlotRefs()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "item-grab-a");
        var menu = new object();
        var owner = new FakeUiOwner(menu);
        var playerComponent = new object();
        var containerComponent = new object();
        var player = ItemGrabDescriptor(UiInventorySide.Player, playerComponent);
        var container = ItemGrabDescriptor(UiInventorySide.Container, containerComponent);
        owner.Set(UiInventorySide.Player, 0, playerComponent, playerComponent, player.Guard);
        owner.Set(UiInventorySide.Container, 0, containerComponent, containerComponent, container.Guard);
        var links = new[]
        {
            new UiInventoryLink
            {
                Side = UiInventorySide.Player,
                InventoryRevision = "player-revision",
                SlotCount = 1,
            },
            new UiInventoryLink
            {
                Side = UiInventorySide.Container,
                InventoryRevision = "container-revision",
                SlotCount = 1,
                ContainerRef = new Ref { Value = "container-ref" },
            },
        };
        var first = ProjectItemGrab(store, menu, owner, new[] { player, container }, links);
        var firstRefs = first.Snapshot.Elements.Select(item => item.Ref.Clone()).ToArray();

        owner.Unavailable = true;
        var incomplete = ProjectItemGrab(
            store,
            menu,
            owner,
            Array.Empty<UiElementDescriptor>(),
            Array.Empty<UiInventoryLink>(),
            UiElementSetCompleteness.Incomplete
        );
        var unavailable = firstRefs.Select(store.ResolveForInspect).ToArray();
        owner.Unavailable = false;
        var recovered = ProjectItemGrab(store, menu, owner, new[] { player, container }, links);

        Assert.Multiple(() =>
        {
            Assert.That(incomplete.Snapshot.Elements, Is.Empty);
            Assert.That(unavailable.All(item => item.Resolution.Status == RefStatus.FactUnavailable), Is.True);
            Assert.That(
                recovered.Snapshot.Elements.Select(item => item.Ref.Value),
                Is.EqualTo(firstRefs.Select(item => item.Value))
            );
        });
    }

    [Test]
    public void ItemGrabProjection_PlayerMayExposeOnlyUnlockedSlotsWithinThirtySixVisualSlots()
    {
        var names = Enumerable.Range(0, 36).Select(index => index.ToString()).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                ItemGrabMenuProjector.HasCompleteSlotCoverage(12, 36, names, true),
                Is.True
            );
            Assert.That(
                ItemGrabMenuProjector.HasCompleteSlotCoverage(12, 36, names, false),
                Is.False
            );
            Assert.That(
                ItemGrabMenuProjector.HasCompleteSlotCoverage(
                    12,
                    36,
                    names.Select((name, index) => index == 11 ? "12" : name).ToArray(),
                    true
                ),
                Is.False
            );
        });
    }

    [Test]
    public void InventoryPageProjection_ReusesPlayerInventoryAndKeepsEquipmentSlotRefsStable()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "inventory-page-a");
        var inventoryOwner = new FakeInventoryOwner(InventoryItemProvenance.Player, 2);
        var inventoryItem = new object();
        inventoryOwner.Set(0, inventoryItem, "player-item");
        var inventory = InventoryProjector.ProjectCapturedSlots(
            inventoryOwner,
            "player",
            null,
            new[]
            {
                new CapturedInventorySlot(inventoryItem, "player-item"),
                new CapturedInventorySlot(null, ""),
            },
            0,
            true,
            store,
            (_, reference) => new ItemFact
            {
                Ref = reference.Clone(),
                QualifiedItemId = "(T)Axe",
                DisplayName = "斧头",
                Stack = 1,
                Tool = true,
            }
        );
        var backpackComponents = new object[]
        {
            new FakeSlotComponent(new UiBounds(0, 0, 64, 64)),
            new FakeSlotComponent(new UiBounds(64, 0, 64, 64)),
        };
        var backpack = InventoryPageProjector.CreateBackpackDescriptors(
            backpackComponents,
            inventory,
            component => ((FakeSlotComponent)component).Bounds,
            _ => true,
            new UiBounds(0, 0, 800, 600)
        );
        var equipmentKinds = new[]
        {
            UiEquipmentSlotKind.Pants,
            UiEquipmentSlotKind.Hat,
            UiEquipmentSlotKind.RightRing,
            UiEquipmentSlotKind.Boots,
            UiEquipmentSlotKind.LeftRing,
            UiEquipmentSlotKind.Shirt,
            UiEquipmentSlotKind.Trinket,
        };
        var equipmentComponents = equipmentKinds.ToDictionary(kind => kind, _ => new object());
        CapturedUiEquipmentSlot Equipment(UiEquipmentSlotKind kind, string suffix) => new(
            kind,
            0,
            equipmentComponents[kind],
            new UiBounds(100 + (int)kind * 64, 160, 64, 64),
            true,
            new ItemFact
            {
                QualifiedItemId = $"(O){suffix}",
                DisplayName = $"装备 {suffix}",
                Stack = 1,
            }
        );
        var equipment = equipmentKinds
            .Select(kind => kind == UiEquipmentSlotKind.Pants
                ? new CapturedUiEquipmentSlot(
                    kind,
                    0,
                    equipmentComponents[kind],
                    new UiBounds(100 + (int)kind * 64, 160, 64, 64),
                    true,
                    null
                )
                : Equipment(kind, ((int)kind).ToString()))
            .ToArray();
        var equipmentDescriptors = InventoryPageProjector.CreateEquipmentDescriptors(
            equipment,
            new UiBounds(0, 0, 800, 600)
        );
        var menu = new object();
        var owner = new FakeUiOwner(menu);
        foreach (var descriptor in backpack)
        {
            owner.Set(
                UiInventorySide.Player,
                descriptor.Index,
                descriptor.Component,
                descriptor.SemanticTarget,
                descriptor.Guard
            );
        }
        foreach (var descriptor in equipmentDescriptors)
        {
            owner.SetEquipment(
                descriptor.EquipmentSlotKind!.Value,
                descriptor.Index,
                descriptor.Component,
                descriptor.SemanticTarget,
                descriptor.Guard
            );
        }
        var links = new[]
        {
            UiProjector.ToInventoryLink(UiInventorySide.Player, inventory),
        };
        var first = ProjectGameMenu(
            store,
            menu,
            owner,
            backpack.Concat(equipmentDescriptors).ToArray(),
            links
        );
        var reversed = ProjectGameMenu(
            store,
            menu,
            owner,
            backpack.Concat(equipmentDescriptors.Reverse()).ToArray(),
            links
        );
        var equipmentFacts = first.Snapshot.Elements
            .Where(item => item.Kind == UiElementKind.EquipmentSlot)
            .ToArray();
        var firstRefs = equipmentFacts.ToDictionary(
            item => item.EquipmentSlotKind,
            item => item.Ref.Value
        );

        var changedEquipment = equipment
            .Select(item => item.Kind == UiEquipmentSlotKind.Hat
                ? item with
                {
                    Item = new ItemFact
                    {
                        QualifiedItemId = "(H)Changed",
                        DisplayName = "新帽子",
                        Stack = 1,
                    },
                }
                : item)
            .ToArray();
        var changedDescriptors = InventoryPageProjector.CreateEquipmentDescriptors(
            changedEquipment,
            new UiBounds(0, 0, 800, 600)
        );
        var changed = ProjectGameMenu(
            store,
            menu,
            owner,
            backpack.Concat(changedDescriptors).ToArray(),
            links
        );

        Assert.Multiple(() =>
        {
            Assert.That(first.Snapshot.Inventories.Single().InventoryRevision, Is.EqualTo(inventory.InventoryRevision));
            Assert.That(first.Snapshot.Elements.Single(item =>
                item.Kind == UiElementKind.ItemSlot && item.Index == 0).ItemRef,
                Is.EqualTo(inventory.Slots[0].Item.Ref));
            Assert.That(first.Snapshot.Elements.Single(item =>
                item.Kind == UiElementKind.ItemSlot && item.Index == 1).ItemRef,
                Is.Null);
            Assert.That(equipmentFacts, Has.Length.EqualTo(7));
            Assert.That(equipmentFacts.Select(item => item.EquipmentSlotKind), Is.Ordered);
            Assert.That(equipmentFacts.All(item => item.Index == 0), Is.True);
            Assert.That(equipmentFacts.All(item => (item.Item is null || item.Item.Ref is null) && !item.Enabled), Is.True);
            var emptyPants = equipmentFacts.Single(item =>
                item.EquipmentSlotKind == UiEquipmentSlotKind.Pants);
            Assert.That(emptyPants.Item, Is.Null);
            Assert.That(emptyPants.Label, Is.Empty);
            Assert.That(equipmentFacts.Select(item => item.Ref.Value), Is.Unique);
            Assert.That(equipmentFacts.All(item =>
                store.ResolveUiElement(item.Ref).Status == UiElementResolveStatus.Resolved), Is.True);
            Assert.That(equipmentFacts.All(item =>
                store.ResolveForInspect(item.Ref).Resolution.Status == RefStatus.Resolved), Is.True);
            Assert.That(reversed.Snapshot.UiRevision, Is.EqualTo(first.Snapshot.UiRevision));
            Assert.That(changed.Snapshot.UiRevision, Is.Not.EqualTo(first.Snapshot.UiRevision));
            Assert.That(
                changed.Snapshot.Elements
                    .Where(item => item.Kind == UiElementKind.EquipmentSlot)
                    .ToDictionary(item => item.EquipmentSlotKind, item => item.Ref.Value),
                Is.EqualTo(firstRefs)
            );
        });
    }

    [Test]
    public void InventoryPageProjection_RejectsOrphanOrContainerInventoryLinks()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "inventory-page-a");
        var menu = new object();
        var owner = new FakeUiOwner(menu);
        var component = new object();
        var descriptor = new UiElementDescriptor(
            UiExtractorKind.GameMenu,
            UiElementKind.ItemSlot,
            0,
            component,
            component,
            "inventory-page-backpack:0",
            "",
            true,
            false,
            32,
            32,
            InventorySide: UiInventorySide.Player
        );
        owner.Set(UiInventorySide.Player, 0, component, component, descriptor.Guard);
        var playerLink = new UiInventoryLink
        {
            Side = UiInventorySide.Player,
            InventoryRevision = new string('1', 64),
            SlotCount = 1,
        };
        var containerLink = new UiInventoryLink
        {
            Side = UiInventorySide.Container,
            InventoryRevision = new string('2', 64),
            SlotCount = 1,
            ContainerRef = new Ref { Value = "container" },
        };

        Assert.Multiple(() =>
        {
            Assert.Throws<UiProjectionException>(() => ProjectGameMenu(
                store,
                menu,
                owner,
                Array.Empty<UiElementDescriptor>(),
                new[] { playerLink }
            ));
            Assert.Throws<UiProjectionException>(() => ProjectGameMenu(
                store,
                menu,
                owner,
                new[] { descriptor },
                Array.Empty<UiInventoryLink>()
            ));
            Assert.Throws<UiProjectionException>(() => ProjectGameMenu(
                store,
                menu,
                owner,
                new[] { descriptor },
                new[] { containerLink }
            ));
        });
    }

    [Test]
    public void GameMenuActivationPolicy_AllowsOnlyFreshExactTabs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UiProjectionPolicy.CanActivateGameMenuElement(
                UiExtractorKind.GameMenu,
                UiElementKind.Tab,
                UiElementKind.Tab,
                typeof(BaseGameMenu),
                typeof(BaseGameMenu)
            ), Is.True);
            Assert.That(UiProjectionPolicy.CanActivateGameMenuElement(
                UiExtractorKind.GameMenu,
                UiElementKind.EquipmentSlot,
                UiElementKind.EquipmentSlot,
                typeof(BaseGameMenu),
                typeof(BaseGameMenu)
            ), Is.False);
            Assert.That(UiProjectionPolicy.CanActivateGameMenuElement(
                UiExtractorKind.GameMenu,
                UiElementKind.ItemSlot,
                UiElementKind.ItemSlot,
                typeof(BaseGameMenu),
                typeof(BaseGameMenu)
            ), Is.False);
            Assert.That(UiProjectionPolicy.CanActivateGameMenuElement(
                UiExtractorKind.GameMenu,
                UiElementKind.Tab,
                UiElementKind.EquipmentSlot,
                typeof(BaseGameMenu),
                typeof(BaseGameMenu)
            ), Is.False);
            Assert.That(UiProjectionPolicy.CanActivateGameMenuElement(
                UiExtractorKind.GameMenu,
                UiElementKind.Tab,
                UiElementKind.Tab,
                typeof(DerivedGameMenu),
                typeof(BaseGameMenu)
            ), Is.False);
        });
    }

    [Test]
    public void GameMenuIncompleteCapture_DisablesEveryTab()
    {
        var tab = Descriptor(1, new object(), new object()) with { Enabled = true };
        var equipment = new UiElementDescriptor(
            UiExtractorKind.GameMenu,
            UiElementKind.EquipmentSlot,
            0,
            new object(),
            new object(),
            "inventory-page-equipment:Hat:0",
            "帽子",
            true,
            false,
            10,
            10,
            EquipmentSlotKind: UiEquipmentSlotKind.Hat
        );
        var descriptors = new List<UiElementDescriptor> { tab, equipment };

        UiRuntimeProjector.DisableGameMenuTabs(descriptors);

        Assert.Multiple(() =>
        {
            Assert.That(descriptors[0].Enabled, Is.False);
            Assert.That(descriptors[1], Is.EqualTo(equipment));
        });
    }

    [TestCase(12)]
    [TestCase(24)]
    [TestCase(36)]
    public void InventoryPageProjection_UsesOnlyUnlockedBackpackSlots(int unlockedSlots)
    {
        var names = Enumerable.Range(0, 36).Select(index => index.ToString()).ToArray();
        var ids = Enumerable.Range(0, 36).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(InventoryPageProjector.HasCompleteBackpackCoverage(
                unlockedSlots,
                36,
                names,
                ids
            ), Is.True);
            Assert.That(InventoryPageProjector.HasCompleteBackpackCoverage(
                unlockedSlots,
                36,
                names,
                ids.Select((id, index) => index == unlockedSlots - 1 ? id + 1 : id).ToArray()
            ), Is.False);
        });
    }

    [Test]
    public void InventoryPageProjection_MapsExactEquipmentComponentsOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(InventoryPageProjector.TryClassifyEquipmentComponent(
                "Hat", 101, out var hat, out var hatIndex), Is.True);
            Assert.That(hat, Is.EqualTo(UiEquipmentSlotKind.Hat));
            Assert.That(hatIndex, Is.Zero);
            Assert.That(InventoryPageProjector.TryClassifyEquipmentComponent(
                "Left Ring", 102, out var left, out _), Is.True);
            Assert.That(left, Is.EqualTo(UiEquipmentSlotKind.LeftRing));
            Assert.That(InventoryPageProjector.TryClassifyEquipmentComponent(
                "Right Ring", 103, out var right, out _), Is.True);
            Assert.That(right, Is.EqualTo(UiEquipmentSlotKind.RightRing));
            Assert.That(InventoryPageProjector.TryClassifyEquipmentComponent(
                "Trinket", 121, out var trinket, out var trinketIndex), Is.True);
            Assert.That(trinket, Is.EqualTo(UiEquipmentSlotKind.Trinket));
            Assert.That(trinketIndex, Is.EqualTo(1));
            Assert.That(InventoryPageProjector.TryClassifyEquipmentComponent(
                "Hat", 102, out _, out _), Is.False);
            Assert.That(InventoryPageProjector.TryClassifyEquipmentComponent(
                "Unknown", 101, out _, out _), Is.False);
        });
    }

    [Test]
    public void InventoryPageProjection_CurrentDescriptorMatchingIncludesEquipmentKindAndOrdinal()
    {
        var kinds = new[]
        {
            UiEquipmentSlotKind.Hat,
            UiEquipmentSlotKind.LeftRing,
            UiEquipmentSlotKind.RightRing,
            UiEquipmentSlotKind.Boots,
            UiEquipmentSlotKind.Shirt,
            UiEquipmentSlotKind.Pants,
        };
        foreach (var kind in kinds)
        {
            var component = new object();
            var descriptor = new UiElementDescriptor(
                UiExtractorKind.GameMenu,
                UiElementKind.EquipmentSlot,
                0,
                component,
                component,
                $"equipment:{kind}:0",
                "",
                true,
                false,
                0,
                0,
                EquipmentSlotKind: kind
            );
            var identity = new UiElementBindingIdentity(
                UiExtractorKind.GameMenu,
                UiElementKind.EquipmentSlot,
                UiInventorySide.Unspecified,
                kind,
                0,
                component,
                component,
                descriptor.Guard
            );
            Assert.That(
                UiRuntimeProjector.DescriptorMatchesIdentity(descriptor, identity),
                Is.True,
                kind.ToString()
            );
            var wrongKind = identity with
            {
                EquipmentSlotKind = kind == UiEquipmentSlotKind.Hat
                    ? UiEquipmentSlotKind.LeftRing
                    : UiEquipmentSlotKind.Hat,
            };
            Assert.That(
                UiRuntimeProjector.DescriptorMatchesIdentity(descriptor, wrongKind),
                Is.False,
                $"错误装备类型不应匹配 {kind}"
            );
        }

        var trinketComponent = new object();
        var trinket = new UiElementDescriptor(
            UiExtractorKind.GameMenu,
            UiElementKind.EquipmentSlot,
            1,
            trinketComponent,
            trinketComponent,
            "equipment:trinket:1",
            "",
            true,
            false,
            0,
            0,
            EquipmentSlotKind: UiEquipmentSlotKind.Trinket
        );
        var trinketIdentity = new UiElementBindingIdentity(
            UiExtractorKind.GameMenu,
            UiElementKind.EquipmentSlot,
            UiInventorySide.Unspecified,
            UiEquipmentSlotKind.Trinket,
            1,
            trinketComponent,
            trinketComponent,
            trinket.Guard
        );
        Assert.That(UiRuntimeProjector.DescriptorMatchesIdentity(trinket, trinketIdentity), Is.True);
        Assert.That(UiRuntimeProjector.DescriptorMatchesIdentity(
            trinket,
            trinketIdentity with { Index = 0 }
        ), Is.False);
    }

    [Test]
    public void InventoryPageProjection_IncompletePreservesRefAndCompletePageSwitchStalesIt()
    {
        var store = new OpaqueRefStore(InstanceId, menuEpochFactory: () => "inventory-page-a");
        var menu = new object();
        var owner = new FakeUiOwner(menu);
        var component = new object();
        var equipment = new UiElementDescriptor(
            UiExtractorKind.GameMenu,
            UiElementKind.EquipmentSlot,
            0,
            component,
            component,
            "inventory-page-equipment:Hat:0",
            "帽子",
            true,
            false,
            32,
            32,
            Item: new ItemFact
            {
                QualifiedItemId = "(H)1",
                DisplayName = "帽子",
                Stack = 1,
            },
            EquipmentSlotKind: UiEquipmentSlotKind.Hat
        );
        owner.SetEquipment(
            UiEquipmentSlotKind.Hat,
            0,
            component,
            component,
            equipment.Guard
        );
        var first = ProjectGameMenu(
            store,
            menu,
            owner,
            new[] { equipment },
            Array.Empty<UiInventoryLink>()
        );
        var reference = first.Snapshot.Elements.Single().Ref.Clone();

        owner.Unavailable = true;
        _ = ProjectGameMenu(
            store,
            menu,
            owner,
            Array.Empty<UiElementDescriptor>(),
            Array.Empty<UiInventoryLink>(),
            UiElementSetCompleteness.Incomplete
        );
        var unavailable = store.ResolveForInspect(reference);
        owner.Unavailable = false;
        var recovered = ProjectGameMenu(
            store,
            menu,
            owner,
            new[] { equipment },
            Array.Empty<UiInventoryLink>()
        );

        _ = ProjectGameMenu(
            store,
            menu,
            owner,
            Array.Empty<UiElementDescriptor>(),
            Array.Empty<UiInventoryLink>()
        );
        var stale = store.ResolveForInspect(reference);

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.Resolution.Status, Is.EqualTo(RefStatus.FactUnavailable));
            Assert.That(recovered.Snapshot.Elements.Single().Ref.Value, Is.EqualTo(reference.Value));
            Assert.That(stale.Resolution.Status, Is.EqualTo(RefStatus.Stale));
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
        UiExtractorKind extractor = UiExtractorKind.GameMenu,
        UiElementSetCompleteness completeness = UiElementSetCompleteness.Complete
    ) => UiProjector.ProjectDescriptors(
        menu,
        new UiMenuFact { MenuType = "GameMenu", Title = "背包" },
        extractor,
        "tab:0",
        descriptors,
        warnings,
        owner,
        store,
        completeness
    );

    private static QueryUiResult ProjectItemGrab(
        OpaqueRefStore store,
        object menu,
        FakeUiOwner owner,
        IReadOnlyList<UiElementDescriptor> descriptors,
        IReadOnlyList<UiInventoryLink> inventories,
        UiElementSetCompleteness completeness = UiElementSetCompleteness.Complete
    ) => UiProjector.ProjectDescriptors(
        menu,
        new UiMenuFact { MenuType = "ItemGrabMenu" },
        UiExtractorKind.ItemGrabSlot,
        "item-grab",
        descriptors,
        Array.Empty<QueryWarning>(),
        owner,
        store,
        completeness,
        inventories
    );

    private static QueryUiResult ProjectGameMenu(
        OpaqueRefStore store,
        object menu,
        FakeUiOwner owner,
        IReadOnlyList<UiElementDescriptor> descriptors,
        IReadOnlyList<UiInventoryLink> inventories,
        UiElementSetCompleteness completeness = UiElementSetCompleteness.Complete
    ) => UiProjector.ProjectDescriptors(
        menu,
        new UiMenuFact
        {
            MenuType = "GameMenu",
            MenuKind = MenuKind.Inventory,
            Title = "背包",
        },
        UiExtractorKind.GameMenu,
        "tab:0",
        descriptors,
        Array.Empty<QueryWarning>(),
        owner,
        store,
        completeness,
        inventories
    );

    private static UiElementDescriptor ItemGrabDescriptor(
        UiInventorySide side,
        object component
    ) => new(
        UiExtractorKind.ItemGrabSlot,
        UiElementKind.ItemSlot,
        0,
        component,
        component,
        $"item-grab-slot:{side}:0",
        "",
        true,
        false,
        32,
        32,
        InventorySide: side
    );

    private static UiRuntimeProjectionCapture Capture(
        UiElementSetCompleteness completeness,
        params UiElementFact[] elements
    )
    {
        var result = new QueryUiResult
        {
            Snapshot = new UiSnapshot
            {
                MenuOpen = true,
                Menu = new UiMenuFact { MenuType = "GameMenu" },
            },
        };
        result.Snapshot.Elements.AddRange(elements);
        return new UiRuntimeProjectionCapture(result, completeness);
    }

    private static UiElementDescriptor Descriptor(int index, object component, object target) =>
        new(
            UiExtractorKind.GameMenu,
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

    private static QueryUiResult ProjectDialogueAdvance(
        OpaqueRefStore store,
        object menu,
        FakeUiOwner owner,
        string guard,
        string label,
        bool enabled
    ) => Project(
        store,
        menu,
        owner,
        new[]
        {
            new UiElementDescriptor(
                UiExtractorKind.DialogueAdvance,
                UiElementKind.DialogueAdvance,
                0,
                null,
                menu,
                guard,
                label,
                true,
                enabled,
                0,
                0
            ),
        },
        Array.Empty<QueryWarning>(),
        UiExtractorKind.DialogueAdvance
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
        return UiRevision.Finalize(clone, epoch, UiExtractorKind.GameMenu, "tab:0");
    }

    private sealed class FakeUiOwner : IUiElementRefOwner
    {
        private readonly object _menu;
        private readonly Dictionary<
            (UiInventorySide Side, UiEquipmentSlotKind EquipmentKind, int Index),
            (object? Component, object Target, string Guard)
        > _current = new();

        public FakeUiOwner(object menu) => _menu = menu;
        public bool Unavailable { get; set; }

        public void Set(int index, object? component, object target, string guard) =>
            Set(UiInventorySide.Unspecified, index, component, target, guard);

        public void Set(
            UiInventorySide side,
            int index,
            object? component,
            object target,
            string guard
        ) => Set(side, UiEquipmentSlotKind.Unspecified, index, component, target, guard);

        public void SetEquipment(
            UiEquipmentSlotKind equipmentKind,
            int index,
            object? component,
            object target,
            string guard
        ) => Set(
            UiInventorySide.Unspecified,
            equipmentKind,
            index,
            component,
            target,
            guard
        );

        private void Set(
            UiInventorySide side,
            UiEquipmentSlotKind equipmentKind,
            int index,
            object? component,
            object target,
            string guard
        ) => _current[(side, equipmentKind, index)] = (component, target, guard);

        public void Remove(int index) => _current.Remove((
            UiInventorySide.Unspecified,
            UiEquipmentSlotKind.Unspecified,
            index
        ));

        public bool TryGetMenuIdentity(out object menu)
        {
            menu = _menu;
            return true;
        }

        public UiElementLookup ResolveCurrentElement(UiElementBindingIdentity identity)
        {
            if (Unavailable)
                return new UiElementLookup(UiElementLookupStatus.Unavailable);
            if (!_current.TryGetValue((
                identity.InventorySide,
                identity.EquipmentSlotKind,
                identity.Index
            ), out var current))
                return new UiElementLookup(UiElementLookupStatus.Stale);
            return new UiElementLookup(
                UiElementLookupStatus.Resolved,
                current.Component,
                current.Target,
                current.Guard
            );
        }
    }

    private sealed record TestUiInspectTarget(RefKind Value)
        : InspectableRefTarget(Value);

    private sealed record FakeSlotComponent(UiBounds Bounds);

    private sealed class FakeInventoryOwner : IInventoryRefOwner
    {
        private readonly object _identity = new();
        private readonly Dictionary<int, InventorySlotLookup> _slots = new();

        public FakeInventoryOwner(InventoryItemProvenance provenance, int capacity)
        {
            Provenance = provenance;
            Capacity = capacity;
        }

        public InventoryItemProvenance Provenance { get; }
        public int Capacity { get; }

        public void Set(int index, object? target, string guard) =>
            _slots[index] = new InventorySlotLookup(
                InventorySlotLookupStatus.Resolved,
                target,
                guard
            );

        public bool TryGetIdentity(out object identity)
        {
            identity = _identity;
            return true;
        }

        public InventorySlotLookup ResolveCurrentSlot(int slot) =>
            slot >= 0 && slot < Capacity && _slots.TryGetValue(slot, out var current)
                ? current
                : slot >= 0 && slot < Capacity
                    ? new InventorySlotLookup(InventorySlotLookupStatus.Resolved)
                    : new InventorySlotLookup(InventorySlotLookupStatus.Stale);
    }

    private class BaseGameMenu { }
    private sealed class DerivedGameMenu : BaseGameMenu { }
    private class BaseDialogue { }
    private sealed class DerivedDialogue : BaseDialogue { }
    private sealed class BaseShop { }
    private class BaseItemGrab { }
    private sealed class DerivedItemGrab : BaseItemGrab { }
    private sealed class BaseLetter { }
    private class BaseSalable { }
    private sealed class DerivedSalable : BaseSalable { }
}
