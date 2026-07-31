using System.Runtime.CompilerServices;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal interface IOpaqueBinding
{
    string Token { get; }
    RefKind Kind { get; }
    bool Stale { get; set; }
    OpaqueBindingCurrentStatus ResolveCurrent(out object? target);
}

internal enum OpaqueBindingCurrentStatus
{
    Resolved,
    Stale,
    Unavailable,
}

/// <summary>
/// Tracks inventory item identity by owner and zero-based slot. The runtime owner is
/// responsible for re-reading the authoritative inventory when a Ref is resolved.
/// </summary>
internal sealed class InventoryItemBindingStore
{
    private readonly ConditionalWeakTable<object, Dictionary<int, InventoryItemBinding>> _byOwner = new();

    public InventoryItemBinding Observe(
        IInventoryRefOwner owner,
        int slot,
        object target,
        string guard,
        Func<string> createToken
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(target);
        if (slot < 0)
            throw new ArgumentOutOfRangeException(nameof(slot));
        if (!owner.TryGetIdentity(out var ownerIdentity))
            throw new InvalidOperationException("库存 owner 已失效");

        var slots = _byOwner.GetOrCreateValue(ownerIdentity);
        if (slots.TryGetValue(slot, out var current))
        {
            if (current.Matches(owner, target, guard))
                return current;
            current.Stale = true;
        }

        var binding = new InventoryItemBinding(createToken(), owner, slot, target, guard);
        slots[slot] = binding;
        return binding;
    }

    public void ObserveEmpty(IInventoryRefOwner owner, int slot)
    {
        if (!owner.TryGetIdentity(out var ownerIdentity))
            return;
        if (!_byOwner.TryGetValue(ownerIdentity, out var slots)
            || !slots.Remove(slot, out var current))
            return;
        current.Stale = true;
    }

    public void Complete(IInventoryRefOwner owner, int capacity)
    {
        if (!owner.TryGetIdentity(out var ownerIdentity)
            || !_byOwner.TryGetValue(ownerIdentity, out var slots))
            return;
        foreach (var slot in slots.Keys.Where(slot => slot >= capacity).ToArray())
        {
            slots[slot].Stale = true;
            slots.Remove(slot);
        }
    }
}

internal sealed class InventoryItemBinding : IOpaqueBinding
{
    private readonly IInventoryRefOwner _owner;
    private readonly WeakReference<object> _target;
    private readonly string _guard;

    public InventoryItemBinding(
        string token,
        IInventoryRefOwner owner,
        int slot,
        object target,
        string guard
    )
    {
        Token = token;
        _owner = owner;
        Slot = slot;
        _target = new WeakReference<object>(target);
        _guard = guard;
        Provenance = owner.Provenance;
    }

    public string Token { get; }
    public RefKind Kind => RefKind.InventoryItem;
    public int Slot { get; }
    public InventoryItemProvenance Provenance { get; }
    public bool Stale { get; set; }

    public bool Matches(IInventoryRefOwner owner, object target, string guard)
    {
        if (Stale
            || owner.Provenance != Provenance
            || !_owner.TryGetIdentity(out var previousOwner)
            || !owner.TryGetIdentity(out var currentOwner)
            || !ReferenceEquals(previousOwner, currentOwner)
            || !_target.TryGetTarget(out var previousTarget)
            || !ReferenceEquals(previousTarget, target)
            || !string.Equals(_guard, guard, StringComparison.Ordinal))
            return false;
        // 调用方已在本次 Handler 中从权威库存捕获该 Slot；这里重复读取会让
        // 每个 Item 都重新选择 Chest backing。真正解析 Ref 时仍执行完整实时校验。
        return true;
    }

    public OpaqueBindingCurrentStatus ResolveCurrent(out object? target)
    {
        target = null;
        if (Stale || !_target.TryGetTarget(out var expected))
            return OpaqueBindingCurrentStatus.Stale;
        var current = _owner.ResolveCurrentSlot(Slot);
        if (current.Status == InventorySlotLookupStatus.Unavailable)
            return OpaqueBindingCurrentStatus.Unavailable;
        if (current.Status == InventorySlotLookupStatus.Stale
            || current.Target is null
            || !ReferenceEquals(expected, current.Target)
            || !string.Equals(_guard, current.Guard, StringComparison.Ordinal))
            return OpaqueBindingCurrentStatus.Stale;
        target = current.Target;
        return OpaqueBindingCurrentStatus.Resolved;
    }
}
