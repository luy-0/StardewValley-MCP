using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class QueryInventoryRefStoreTests
{
    private const string InstanceId = "11111111-1111-4111-8111-111111111111";

    [Test]
    public void OpaqueRefStoreObserveAndResolveReusesTokenAndExposesProvenance()
    {
        var store = new OpaqueRefStore(InstanceId);
        var owner = new FakeOwner(InventoryItemProvenance.Player, 2);
        var item = new object();
        owner.Set(0, item, "Test.Item:(O)24");

        var first = store.ObserveInventoryItem(owner, 0, item, "Test.Item:(O)24");
        var second = store.ObserveInventoryItem(owner, 0, item, "Test.Item:(O)24");
        var resolved = store.ResolveInventoryItem(first);

        Assert.Multiple(() =>
        {
            Assert.That(second.Value, Is.EqualTo(first.Value));
            Assert.That(first.Value, Does.Match($"^r1_{InstanceId}_[0-9a-f]{{48}}$"));
            Assert.That(resolved.Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
            Assert.That(resolved.Target?.Target, Is.SameAs(item));
            Assert.That(resolved.Target?.Slot, Is.EqualTo(0));
            Assert.That(resolved.Target?.Provenance, Is.EqualTo(InventoryItemProvenance.Player));
            Assert.That(resolved.Error, Is.Null);
        });
    }

    [Test]
    public void OpaqueRefStoreSameQidReplacementStalesOldRefAndCreatesNewToken()
    {
        var store = new OpaqueRefStore(InstanceId);
        var owner = new FakeOwner(InventoryItemProvenance.Container, 1);
        var original = new object();
        var replacement = new object();
        owner.Set(0, original, "Test.Item:(O)388");
        var oldRef = store.ObserveInventoryItem(owner, 0, original, "Test.Item:(O)388");

        owner.Set(0, replacement, "Test.Item:(O)388");
        var newRef = store.ObserveInventoryItem(owner, 0, replacement, "Test.Item:(O)388");

        Assert.Multiple(() =>
        {
            Assert.That(newRef.Value, Is.Not.EqualTo(oldRef.Value));
            Assert.That(store.ResolveInventoryItem(oldRef).Status, Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(store.ResolveInventoryItem(newRef).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
            Assert.That(store.ResolveInventoryItem(newRef).Target?.Target, Is.SameAs(replacement));
            Assert.That(store.ResolveInventoryItem(newRef).Target?.Provenance, Is.EqualTo(InventoryItemProvenance.Container));
        });
    }

    [Test]
    public void OpaqueRefStoreMovingAcrossSlotsNeverResurrectsOldToken()
    {
        var store = new OpaqueRefStore(InstanceId);
        var owner = new FakeOwner(InventoryItemProvenance.Player, 2);
        var item = new object();
        owner.Set(0, item, "Test.Item:(T)Axe");
        var slotZero = store.ObserveInventoryItem(owner, 0, item, "Test.Item:(T)Axe");

        owner.Set(0, null, "");
        owner.Set(1, item, "Test.Item:(T)Axe");
        store.ObserveEmptyInventorySlot(owner, 0);
        var slotOne = store.ObserveInventoryItem(owner, 1, item, "Test.Item:(T)Axe");

        owner.Set(1, null, "");
        owner.Set(0, item, "Test.Item:(T)Axe");
        store.ObserveEmptyInventorySlot(owner, 1);
        var returnedToZero = store.ObserveInventoryItem(owner, 0, item, "Test.Item:(T)Axe");

        Assert.Multiple(() =>
        {
            Assert.That(store.ResolveInventoryItem(slotZero).Status, Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(store.ResolveInventoryItem(slotOne).Status, Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(returnedToZero.Value, Is.Not.EqualTo(slotZero.Value));
            Assert.That(returnedToZero.Value, Is.Not.EqualTo(slotOne.Value));
            Assert.That(store.ResolveInventoryItem(returnedToZero).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
        });
    }

    [Test]
    public void OpaqueRefStoreSeparatesUnavailableFromMonotonicStale()
    {
        var store = new OpaqueRefStore(InstanceId);
        var owner = new FakeOwner(InventoryItemProvenance.Container, 1);
        var item = new object();
        owner.Set(0, item, "Test.Item:(O)390");
        var reference = store.ObserveInventoryItem(owner, 0, item, "Test.Item:(O)390");

        owner.Unavailable = true;
        var unavailable = store.ResolveInventoryItem(reference);
        owner.Unavailable = false;
        var recovered = store.ResolveInventoryItem(reference);
        owner.Current = false;
        var stale = store.ResolveInventoryItem(reference);
        owner.Current = true;
        var cannotResurrect = store.ResolveInventoryItem(reference);

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.Status, Is.EqualTo(InventoryItemResolveStatus.Unavailable));
            Assert.That(unavailable.Error?.Code, Is.EqualTo(ErrorCode.Internal));
            Assert.That(recovered.Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
            Assert.That(stale.Status, Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(stale.Error?.Code, Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(cannotResurrect.Status, Is.EqualTo(InventoryItemResolveStatus.Stale));
        });
    }

    [Test]
    public void OpaqueRefStoreSeparatesOwnersEvenWhenTheyShareTargetAndSlot()
    {
        var store = new OpaqueRefStore(InstanceId);
        var playerOwner = new FakeOwner(InventoryItemProvenance.Player, 1);
        var containerOwner = new FakeOwner(InventoryItemProvenance.Container, 1);
        var sharedTarget = new object();
        playerOwner.Set(0, sharedTarget, "shared");
        containerOwner.Set(0, sharedTarget, "shared");

        var playerRef = store.ObserveInventoryItem(playerOwner, 0, sharedTarget, "shared");
        var containerRef = store.ObserveInventoryItem(containerOwner, 0, sharedTarget, "shared");
        playerOwner.Current = false;

        Assert.Multiple(() =>
        {
            Assert.That(containerRef.Value, Is.Not.EqualTo(playerRef.Value));
            Assert.That(store.ResolveInventoryItem(playerRef).Status, Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(store.ResolveInventoryItem(containerRef).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
            Assert.That(store.ResolveInventoryItem(containerRef).Target?.Provenance, Is.EqualTo(InventoryItemProvenance.Container));
        });
    }

    [Test]
    public void AllowedKindLookupPreservesResolvedUnsupportedUnknownAndForeignStatuses()
    {
        var store = new OpaqueRefStore(InstanceId);
        var owner = new FakeOwner(InventoryItemProvenance.Player, 1);
        var item = new object();
        owner.Set(0, item, "Test.Item:(O)24");
        var issued = store.ObserveInventoryItem(owner, 0, item, "Test.Item:(O)24");
        var allowed = new HashSet<RefKind> { RefKind.InventoryItem };
        var disallowed = new HashSet<RefKind> { RefKind.WorldEntity, RefKind.Container };

        var resolved = store.ResolveAllowedKinds(issued, allowed, out var binding, out var target);
        var unsupported = store.ResolveAllowedKinds(issued, disallowed, out _, out _);
        var unknown = store.ResolveAllowedKinds(
            new Ref { Value = OpaqueRefTokenCodec.NewToken(InstanceId) },
            allowed,
            out _,
            out _
        );
        var unauthenticated = store.ResolveAllowedKinds(
            new Ref { Value = $"r1_{InstanceId}_0000000000000001{new string('0', 32)}" },
            allowed,
            out _,
            out _
        );
        var foreign = store.ResolveAllowedKinds(
            new Ref { Value = OpaqueRefTokenCodec.NewToken("22222222-2222-4222-8222-222222222222") },
            allowed,
            out _,
            out _
        );

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Status, Is.EqualTo(RefStatus.Resolved));
            Assert.That(binding?.Kind, Is.EqualTo(RefKind.InventoryItem));
            Assert.That(target, Is.SameAs(item));
            Assert.That(unsupported.Status, Is.EqualTo(RefStatus.Unsupported));
            Assert.That(unsupported.Kind, Is.EqualTo(RefKind.InventoryItem));
            Assert.That(unsupported.Error?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(unknown.Status, Is.EqualTo(RefStatus.NotFound));
            Assert.That(unauthenticated.Status, Is.EqualTo(RefStatus.NotFound));
            Assert.That(foreign.Status, Is.EqualTo(RefStatus.Stale));
            Assert.That(foreign.Kind, Is.EqualTo(RefKind.Unspecified));
        });
    }

    [Test]
    public void CentralTokenRegistryRetriesCollisionWithoutAliasingBindings()
    {
        var signingKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var collision = OpaqueRefTokenCodec.NewIssuedToken(InstanceId, 1, signingKey);
        var unique = OpaqueRefTokenCodec.NewIssuedToken(InstanceId, 2, signingKey);
        var tokens = new Queue<string>(new[] { collision, collision, unique });
        var store = new OpaqueRefStore(
            InstanceId,
            () => tokens.Dequeue(),
            tokenSigningKey: signingKey
        );
        var firstOwner = new FakeOwner(InventoryItemProvenance.Player, 1);
        var secondOwner = new FakeOwner(InventoryItemProvenance.Container, 1);
        var firstItem = new object();
        var secondItem = new object();
        firstOwner.Set(0, firstItem, "first");
        secondOwner.Set(0, secondItem, "second");

        var first = store.ObserveInventoryItem(firstOwner, 0, firstItem, "first");
        var second = store.ObserveInventoryItem(secondOwner, 0, secondItem, "second");

        Assert.Multiple(() =>
        {
            Assert.That(first.Value, Is.EqualTo(collision));
            Assert.That(second.Value, Is.EqualTo(unique));
            Assert.That(store.ResolveInventoryItem(first).Target?.Target, Is.SameAs(firstItem));
            Assert.That(store.ResolveInventoryItem(first).Target?.Provenance, Is.EqualTo(InventoryItemProvenance.Player));
            Assert.That(store.ResolveInventoryItem(second).Target?.Target, Is.SameAs(secondItem));
            Assert.That(store.ResolveInventoryItem(second).Target?.Provenance, Is.EqualTo(InventoryItemProvenance.Container));
        });
    }

    [Test]
    public void CentralTokenRegistryFailsClosedWhenCollisionRetriesAreExhausted()
    {
        var signingKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var collision = OpaqueRefTokenCodec.NewIssuedToken(InstanceId, 1, signingKey);
        var store = new OpaqueRefStore(
            InstanceId,
            () => collision,
            tokenSigningKey: signingKey
        );
        var firstOwner = new FakeOwner(InventoryItemProvenance.Player, 1);
        var secondOwner = new FakeOwner(InventoryItemProvenance.Container, 1);
        var firstItem = new object();
        var secondItem = new object();
        firstOwner.Set(0, firstItem, "first");
        secondOwner.Set(0, secondItem, "second");
        store.ObserveInventoryItem(firstOwner, 0, firstItem, "first");

        Assert.That(
            () => store.ObserveInventoryItem(secondOwner, 0, secondItem, "second"),
            Throws.TypeOf<InvalidOperationException>()
        );
        Assert.That(store.ResolveInventoryItem(new Ref { Value = collision }).Target?.Target, Is.SameAs(firstItem));
    }

    [Test]
    public void CentralTokenRegistryRejectsFactoryWithUntruthfulIssuanceSequence()
    {
        var signingKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var skipped = OpaqueRefTokenCodec.NewIssuedToken(InstanceId, 2, signingKey);
        var store = new OpaqueRefStore(
            InstanceId,
            () => skipped,
            tokenSigningKey: signingKey
        );
        var owner = new FakeOwner(InventoryItemProvenance.Player, 1);
        var item = new object();
        owner.Set(0, item, "item");

        Assert.That(
            () => store.ObserveInventoryItem(owner, 0, item, "item"),
            Throws.TypeOf<InvalidOperationException>()
        );
        Assert.That(store.RegisteredBindingCount, Is.Zero);
    }

    [Test]
    public void CentralTokenRegistryKeepsEvictedInjectedTokenPermanentlyStale()
    {
        var signingKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var tokens = new Queue<string>(new[]
        {
            OpaqueRefTokenCodec.NewIssuedToken(InstanceId, 1, signingKey),
            OpaqueRefTokenCodec.NewIssuedToken(InstanceId, 2, signingKey),
        });
        var store = new OpaqueRefStore(
            InstanceId,
            () => tokens.Dequeue(),
            capacity: 1,
            tokenSigningKey: signingKey
        );
        var owner = new FakeOwner(InventoryItemProvenance.Player, 2);
        var firstItem = new object();
        var secondItem = new object();
        owner.Set(0, firstItem, "first");
        owner.Set(1, secondItem, "second");
        var first = store.ObserveInventoryItem(owner, 0, firstItem, "first");
        store.ObserveInventoryItem(owner, 1, secondItem, "second");

        Assert.That(store.ResolveInventoryItem(first).Status, Is.EqualTo(InventoryItemResolveStatus.Stale));
    }

    [Test]
    public void CentralTokenRegistryBoundsCapacityAndTouchesLruOnAccess()
    {
        var store = new OpaqueRefStore(InstanceId, capacity: 2);
        var owner = new FakeOwner(InventoryItemProvenance.Player, 3);
        var firstItem = new object();
        var secondItem = new object();
        var thirdItem = new object();
        owner.Set(0, firstItem, "first");
        owner.Set(1, secondItem, "second");
        owner.Set(2, thirdItem, "third");
        var first = store.ObserveInventoryItem(owner, 0, firstItem, "first");
        var second = store.ObserveInventoryItem(owner, 1, secondItem, "second");

        Assert.That(store.ResolveInventoryItem(first).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
        var third = store.ObserveInventoryItem(owner, 2, thirdItem, "third");

        Assert.Multiple(() =>
        {
            Assert.That(store.RegisteredBindingCount, Is.EqualTo(2));
            Assert.That(store.ResolveInventoryItem(first).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
            Assert.That(store.ResolveInventoryItem(second).Status, Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(store.ResolveInventoryItem(second).Error?.Code, Is.EqualTo(ErrorCode.StaleRef));
            Assert.That(store.ResolveInventoryItem(third).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
        });

        var reissued = store.ObserveInventoryItem(owner, 1, secondItem, "second");
        Assert.Multiple(() =>
        {
            Assert.That(reissued.Value, Is.Not.EqualTo(second.Value));
            Assert.That(store.RegisteredBindingCount, Is.EqualTo(2));
            Assert.That(store.ResolveInventoryItem(second).Status, Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(store.ResolveInventoryItem(reissued).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
        });
    }

    [Test]
    public void CentralTokenRegistryReclaimsRecentStaleBeforeOldestLive()
    {
        var store = new OpaqueRefStore(InstanceId, capacity: 2);
        var owner = new FakeOwner(InventoryItemProvenance.Player, 3);
        var firstItem = new object();
        var secondItem = new object();
        var thirdItem = new object();
        owner.Set(0, firstItem, "first");
        owner.Set(1, secondItem, "second");
        owner.Set(2, thirdItem, "third");
        var first = store.ObserveInventoryItem(owner, 0, firstItem, "first");
        var second = store.ObserveInventoryItem(owner, 1, secondItem, "second");

        Assert.That(store.ResolveInventoryItem(first).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
        store.ObserveEmptyInventorySlot(owner, 0);
        var third = store.ObserveInventoryItem(owner, 2, thirdItem, "third");

        Assert.Multiple(() =>
        {
            Assert.That(store.RegisteredBindingCount, Is.EqualTo(2));
            Assert.That(store.ResolveInventoryItem(first).Status, Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(store.ResolveInventoryItem(second).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
            Assert.That(store.ResolveInventoryItem(third).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
        });
    }

    [Test]
    public void CentralTokenRegistryCapacityOneEvictsExactlyOneBindingPerIssuance()
    {
        var store = new OpaqueRefStore(InstanceId, capacity: 1);
        var owner = new FakeOwner(InventoryItemProvenance.Player, 2);
        var firstItem = new object();
        var secondItem = new object();
        owner.Set(0, firstItem, "first");
        owner.Set(1, secondItem, "second");
        var first = store.ObserveInventoryItem(owner, 0, firstItem, "first");
        var second = store.ObserveInventoryItem(owner, 1, secondItem, "second");

        Assert.Multiple(() =>
        {
            Assert.That(store.RegisteredBindingCount, Is.EqualTo(1));
            Assert.That(store.ResolveInventoryItem(first).Status, Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(store.ResolveInventoryItem(second).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
        });
    }

    [Test]
    public void CentralTokenRegistryTreatsUnavailableBindingAsOrdinaryLiveLruEntry()
    {
        var store = new OpaqueRefStore(InstanceId, capacity: 2);
        var owner = new FakeOwner(InventoryItemProvenance.Player, 3);
        var firstItem = new object();
        var unavailableItem = new object();
        var thirdItem = new object();
        owner.Set(0, firstItem, "first");
        owner.Set(1, unavailableItem, "unavailable");
        owner.Set(2, thirdItem, "third");
        var first = store.ObserveInventoryItem(owner, 0, firstItem, "first");
        var temporarilyUnavailable = store.ObserveInventoryItem(
            owner,
            1,
            unavailableItem,
            "unavailable"
        );

        owner.Unavailable = true;
        Assert.That(
            store.ResolveInventoryItem(temporarilyUnavailable).Status,
            Is.EqualTo(InventoryItemResolveStatus.Unavailable)
        );
        var third = store.ObserveInventoryItem(owner, 2, thirdItem, "third");
        owner.Unavailable = false;

        Assert.Multiple(() =>
        {
            Assert.That(store.RegisteredBindingCount, Is.EqualTo(2));
            Assert.That(store.ResolveInventoryItem(first).Status, Is.EqualTo(InventoryItemResolveStatus.Stale));
            Assert.That(store.ResolveInventoryItem(temporarilyUnavailable).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
            Assert.That(store.ResolveInventoryItem(third).Status, Is.EqualTo(InventoryItemResolveStatus.Resolved));
        });
    }

    private sealed class FakeOwner : IInventoryRefOwner
    {
        private readonly object _identity = new();
        private readonly object?[] _slots;
        private readonly string[] _guards;

        public FakeOwner(InventoryItemProvenance provenance, int capacity)
        {
            Provenance = provenance;
            _slots = new object?[capacity];
            _guards = new string[capacity];
        }

        public InventoryItemProvenance Provenance { get; }
        public bool Current { get; set; } = true;
        public bool Unavailable { get; set; }

        public void Set(int slot, object? target, string guard)
        {
            _slots[slot] = target;
            _guards[slot] = guard;
        }

        public bool TryGetIdentity(out object identity)
        {
            identity = _identity;
            return Current;
        }

        public InventorySlotLookup ResolveCurrentSlot(int slot)
        {
            if (Unavailable)
                return new InventorySlotLookup(InventorySlotLookupStatus.Unavailable);
            if (!Current || slot < 0 || slot >= _slots.Length)
                return new InventorySlotLookup(InventorySlotLookupStatus.Stale);
            return new InventorySlotLookup(
                InventorySlotLookupStatus.Resolved,
                _slots[slot],
                _guards[slot]
            );
        }
    }
}
