using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class CraftingPageProjectionTests
{
    [Test]
    public void FactProjectionSortsMaterialsAndPossibleOutputs()
    {
        var fact = CraftingRecipeFactProjector.Project(new CraftingRecipeProjectionSource(
            "Wood Fence",
            "木围栏",
            Known: true,
            Craftable: true,
            new[]
            {
                new CraftingMaterialProjectionSource("388", "木材", 2, 8),
                new CraftingMaterialProjectionSource("-4", "鱼", 1, 3),
            },
            new[]
            {
                new CraftingOutputProjectionSource("(O)322", "木围栏", 1),
                new CraftingOutputProjectionSource("(O)10", "备选产出", 1),
            }
        ));

        Assert.Multiple(() =>
        {
            Assert.That(fact.RecipeKey, Is.EqualTo("Wood Fence"));
            Assert.That(fact.Known, Is.True);
            Assert.That(fact.Craftable, Is.True);
            Assert.That(fact.Materials.Select(item => item.IngredientKey),
                Is.EqualTo(new[] { "-4", "388" }));
            Assert.That(fact.Materials[1].AvailableQuantity, Is.EqualTo(8));
            Assert.That(fact.PossibleOutputs.Select(item => item.QualifiedItemId),
                Is.EqualTo(new[] { "(O)10", "(O)322" }));
        });
    }

    [Test]
    public void UnknownRecipeCannotBeReportedCraftable()
    {
        var fact = CraftingRecipeFactProjector.Project(Source(known: false));
        Assert.Multiple(() =>
        {
            Assert.That(fact.Craftable, Is.False);
            Assert.That(UiProjectionPolicy.CanActivateGameMenuElement(
                UiExtractorKind.GameMenu,
                UiElementKind.CraftingRecipe,
                UiElementKind.CraftingRecipe,
                typeof(BaseMenu),
                typeof(BaseMenu)
            ), Is.False);
        });
    }

    [Test]
    public void InvalidOrDuplicateMaterialFactsFailClosed()
    {
        var duplicate = Source() with
        {
            Materials = new[]
            {
                new CraftingMaterialProjectionSource("388", "木材", 1, 1),
                new CraftingMaterialProjectionSource("388", "木材", 1, 1),
            },
        };
        var invalidQuantity = Source() with
        {
            Materials = new[]
            {
                new CraftingMaterialProjectionSource("388", "木材", 0, 1),
            },
        };

        Assert.Multiple(() =>
        {
            Assert.Throws<UiProjectionException>(() =>
                CraftingRecipeFactProjector.Project(duplicate));
            Assert.Throws<UiProjectionException>(() =>
                CraftingRecipeFactProjector.Project(invalidQuantity));
        });
    }

    [Test]
    public void AllPagesAreProjectedButOnlyCurrentPageIsVisibleAndNoneIsEnabled()
    {
        var first = Element(page: 0, index: 0, "first");
        var second = Element(page: 1, index: 1, "second");
        var descriptors = CraftingPageProjector.CreateDescriptors(
            new[] { second, first },
            currentPage: 1,
            new UiBounds(0, 0, 800, 600)
        );

        Assert.Multiple(() =>
        {
            Assert.That(descriptors.Select(item => item.Index), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(descriptors.Select(item => item.Visible), Is.EqualTo(new[] { false, true }));
            Assert.That(descriptors.All(item => !item.Enabled), Is.True);
            Assert.That(descriptors.All(item => item.Kind == UiElementKind.CraftingRecipe), Is.True);
            Assert.That(descriptors[1].CraftingRecipe!.RecipeKey, Is.EqualTo("second"));
            Assert.That(descriptors.All(item => item.IsValid()), Is.True);
        });
    }

    [Test]
    public void GlobalOrdinalsMustBeContiguousAndUnique()
    {
        var gap = new[] { Element(0, 0, "first"), Element(0, 2, "third") };
        var duplicate = new[] { Element(0, 0, "first"), Element(1, 0, "other") };

        Assert.Multiple(() =>
        {
            Assert.Throws<UiProjectionException>(() =>
                CraftingPageProjector.CreateDescriptors(gap, 0, new UiBounds(0, 0, 800, 600)));
            Assert.Throws<UiProjectionException>(() =>
                CraftingPageProjector.CreateDescriptors(duplicate, 0, new UiBounds(0, 0, 800, 600)));
        });
    }

    [Test]
    public void RecipeCountAbovePublicLimitFailsInsteadOfTruncating()
    {
        var captured = Enumerable.Range(0, CraftingPageProjector.RecipeLimit + 1)
            .Select(index => Element(0, index, $"recipe-{index}"))
            .ToArray();

        Assert.Throws<UiProjectionException>(() =>
            CraftingPageProjector.CreateDescriptors(
                captured,
                0,
                new UiBounds(0, 0, 800, 600)
            ));
    }

    [Test]
    public void RecipeFactAndPageVisibilityParticipateInUiRevision()
    {
        var first = Snapshot(available: 1, visible: true);
        var same = Snapshot(available: 1, visible: true);
        var materialChanged = Snapshot(available: 2, visible: true);
        var pageChanged = Snapshot(available: 1, visible: false);

        Assert.Multiple(() =>
        {
            Assert.That(first.UiRevision, Is.EqualTo(same.UiRevision));
            Assert.That(first.UiRevision, Is.Not.EqualTo(materialChanged.UiRevision));
            Assert.That(first.UiRevision, Is.Not.EqualTo(pageChanged.UiRevision));
        });
    }

    [Test]
    public void PageSwitchKeepsRecipeRefsButComponentRebuildMakesOldRefStale()
    {
        var refs = new OpaqueRefStore("94444444-4444-4444-8444-444444444444");
        var menu = new object();
        var captured = new[] { Element(0, 0, "first"), Element(1, 1, "second") };
        var firstDescriptors = CraftingPageProjector.CreateDescriptors(
            captured, 0, new UiBounds(0, 0, 800, 600)
        );
        var owner = new FakeUiOwner(menu, firstDescriptors);
        var first = Project(menu, owner, refs, firstDescriptors, "crafting:0");
        var firstRefs = first.Snapshot.Elements.Select(item => item.Ref.Clone()).ToArray();

        var secondDescriptors = CraftingPageProjector.CreateDescriptors(
            captured, 1, new UiBounds(0, 0, 800, 600)
        );
        owner.Descriptors = secondDescriptors;
        var second = Project(menu, owner, refs, secondDescriptors, "crafting:1");

        var rebuilt = new[] { Element(0, 0, "first"), Element(1, 1, "second") };
        var rebuiltDescriptors = CraftingPageProjector.CreateDescriptors(
            rebuilt, 1, new UiBounds(0, 0, 800, 600)
        );
        owner.Descriptors = rebuiltDescriptors;
        var third = Project(menu, owner, refs, rebuiltDescriptors, "crafting:1");

        Assert.Multiple(() =>
        {
            Assert.That(second.Snapshot.Elements.Select(item => item.Ref.Value),
                Is.EqualTo(firstRefs.Select(item => item.Value)));
            Assert.That(second.Snapshot.UiRevision, Is.Not.EqualTo(first.Snapshot.UiRevision));
            Assert.That(third.Snapshot.Elements.Select(item => item.Ref.Value),
                Is.Not.EqualTo(firstRefs.Select(item => item.Value)));
            Assert.That(refs.ResolveUiElement(firstRefs[0]).Status,
                Is.EqualTo(UiElementResolveStatus.Stale));
        });
    }

    private static CraftingRecipeProjectionSource Source(bool known = true) => new(
        "Wood Fence",
        "木围栏",
        known,
        Craftable: true,
        new[] { new CraftingMaterialProjectionSource("388", "木材", 2, 8) },
        new[] { new CraftingOutputProjectionSource("(O)322", "木围栏", 1) }
    );

    private static CapturedCraftingRecipeElement Element(
        int page,
        int index,
        string key
    ) => new(
        page,
        index,
        new object(),
        new object(),
        $"crafting-recipe:{page}:{index}:{key}",
        new UiBounds(32 + index * 64, 32, 64, 64),
        true,
        CraftingRecipeFactProjector.Project(Source() with
        {
            RecipeKey = key,
            DisplayName = key,
        })
    );

    private static UiSnapshot Snapshot(int available, bool visible)
    {
        var fact = CraftingRecipeFactProjector.Project(Source() with
        {
            Materials = new[]
            {
                new CraftingMaterialProjectionSource("388", "木材", 2, available),
            },
        });
        var snapshot = new UiSnapshot
        {
            MenuOpen = true,
            Menu = new UiMenuFact
            {
                MenuType = "GameMenu",
                MenuKind = MenuKind.Crafting,
            },
        };
        snapshot.Elements.Add(new UiElementFact
        {
            Ref = new Ref { Value = "recipe-ref" },
            Kind = UiElementKind.CraftingRecipe,
            Label = fact.DisplayName,
            Visible = visible,
            Enabled = false,
            Center = new PixelPoint { X = 64, Y = 64 },
            Index = 0,
            CraftingRecipe = fact,
        });
        UiRevision.Finalize(snapshot, "menu", UiExtractorKind.GameMenu, "crafting:0");
        return snapshot;
    }

    private static QueryUiResult Project(
        object menu,
        FakeUiOwner owner,
        OpaqueRefStore refs,
        IReadOnlyList<UiElementDescriptor> descriptors,
        string actionState
    ) => UiProjector.ProjectDescriptors(
        menu,
        new UiMenuFact
        {
            MenuType = "GameMenu",
            MenuKind = MenuKind.Crafting,
        },
        UiExtractorKind.GameMenu,
        actionState,
        descriptors,
        Array.Empty<QueryWarning>(),
        owner,
        refs
    );

    private sealed class FakeUiOwner : IUiElementRefOwner
    {
        private readonly object _menu;
        public FakeUiOwner(object menu, IReadOnlyList<UiElementDescriptor> descriptors)
        {
            _menu = menu;
            Descriptors = descriptors;
        }
        public IReadOnlyList<UiElementDescriptor> Descriptors { get; set; }
        public bool TryGetMenuIdentity(out object menu)
        {
            menu = _menu;
            return true;
        }
        public UiElementLookup ResolveCurrentElement(UiElementBindingIdentity identity)
        {
            var descriptor = Descriptors.SingleOrDefault(item =>
                UiRuntimeProjector.DescriptorMatchesIdentity(item, identity));
            if (descriptor is null)
                return new UiElementLookup(UiElementLookupStatus.Stale);
            return ReferenceEquals(descriptor.Component, identity.Component)
                && ReferenceEquals(descriptor.SemanticTarget, identity.SemanticTarget)
                && descriptor.Guard == identity.Guard
                ? new UiElementLookup(
                    UiElementLookupStatus.Resolved,
                    descriptor.Component,
                    descriptor.SemanticTarget,
                    descriptor.Guard
                )
                : new UiElementLookup(UiElementLookupStatus.Stale);
        }
    }

    private sealed class BaseMenu { }
}
