using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal interface IUiElementRefOwner
{
    bool TryGetMenuIdentity(out object menu);
    UiElementLookup ResolveCurrentElement(UiElementBindingIdentity identity);
}

internal enum UiElementLookupStatus
{
    Resolved,
    Stale,
    Unavailable,
}

internal readonly record struct UiElementLookup(
    UiElementLookupStatus Status,
    object? Component = null,
    object? SemanticTarget = null,
    string Guard = ""
);

internal enum UiExtractorKind
{
    Unsupported,
    GameMenu,
    DialogueResponse,
    DialogueAdvance,
    ShopSaleRow,
    ItemGrabSlot,
}

internal readonly record struct UiElementBindingIdentity(
    UiExtractorKind Extractor,
    UiElementKind PublicKind,
    UiInventorySide InventorySide,
    UiEquipmentSlotKind EquipmentSlotKind,
    int Index,
    object? Component,
    object SemanticTarget,
    string Guard
);

internal sealed class UiElementBindingStore
{
    private readonly ConditionalWeakTable<object, MenuState> _menus = new();
    private readonly Func<string> _epochFactory;
    private WeakReference<object>? _activeMenu;

    public UiElementBindingStore(Func<string>? epochFactory = null)
    {
        _epochFactory = epochFactory ?? CreateEpoch;
    }

    public UiProjectionSession Begin(object menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        if (_activeMenu?.TryGetTarget(out var previous) == true
            && !ReferenceEquals(previous, menu)
            && _menus.TryGetValue(previous, out var previousState))
        {
            previousState.StaleAll();
        }

        _activeMenu = new WeakReference<object>(menu);
        var state = _menus.GetValue(menu, _ => new MenuState(_epochFactory()));
        state.Generation = checked(state.Generation + 1);
        return new UiProjectionSession(menu, state.Epoch, state.Generation);
    }

    public UiElementBinding Observe(
        UiProjectionSession session,
        IUiElementRefOwner owner,
        UiElementBindingIdentity identity,
        Func<string> createToken
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!owner.TryGetMenuIdentity(out var ownerMenu)
            || !ReferenceEquals(ownerMenu, session.Menu)
            || !_menus.TryGetValue(session.Menu, out var state)
            || state.Generation != session.Generation
            || !string.Equals(state.Epoch, session.MenuEpoch, StringComparison.Ordinal))
            throw new InvalidOperationException("UI 投影 Session 已失效");

        var key = new UiBindingKey(
            identity.Extractor,
            identity.PublicKind,
            identity.InventorySide,
            identity.EquipmentSlotKind,
            identity.Index
        );
        if (state.Bindings.TryGetValue(key, out var current))
        {
            if (current.Matches(owner, identity, session.MenuEpoch))
            {
                current.ObservedGeneration = session.Generation;
                return current;
            }
            current.Stale = true;
        }

        var binding = new UiElementBinding(
            createToken(),
            owner,
            session.Menu,
            session.MenuEpoch,
            identity,
            session.Generation
        );
        state.Bindings[key] = binding;
        return binding;
    }

    public void Complete(UiProjectionSession session)
    {
        if (!_menus.TryGetValue(session.Menu, out var state)
            || state.Generation != session.Generation
            || !string.Equals(state.Epoch, session.MenuEpoch, StringComparison.Ordinal))
            throw new InvalidOperationException("UI 投影 Session 已失效");

        foreach (var key in state.Bindings.Keys.ToArray())
        {
            var binding = state.Bindings[key];
            if (binding.ObservedGeneration == session.Generation)
                continue;
            binding.Stale = true;
            state.Bindings.Remove(key);
        }
    }

    public void CloseActive()
    {
        if (_activeMenu?.TryGetTarget(out var active) == true
            && _menus.TryGetValue(active, out var state))
            state.StaleAll();
        _activeMenu = null;
    }

    private static string CreateEpoch() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private readonly record struct UiBindingKey(
        UiExtractorKind Extractor,
        UiElementKind PublicKind,
        UiInventorySide InventorySide,
        UiEquipmentSlotKind EquipmentSlotKind,
        int Index
    );

    private sealed class MenuState
    {
        public MenuState(string epoch)
        {
            if (string.IsNullOrEmpty(epoch))
                throw new InvalidOperationException("UI menu epoch 不能为空");
            Epoch = epoch;
        }

        public string Epoch { get; }
        public long Generation { get; set; }
        public Dictionary<UiBindingKey, UiElementBinding> Bindings { get; } = new();

        public void StaleAll()
        {
            foreach (var binding in Bindings.Values)
                binding.Stale = true;
            Bindings.Clear();
        }
    }
}

internal sealed class UiElementBinding : IOpaqueBinding
{
    private readonly IUiElementRefOwner _owner;
    private readonly WeakReference<object> _menu;
    private readonly WeakReference<object>? _component;
    private readonly WeakReference<object> _semanticTarget;
    private readonly string _guard;

    public UiElementBinding(
        string token,
        IUiElementRefOwner owner,
        object menu,
        string menuEpoch,
        UiElementBindingIdentity identity,
        long observedGeneration
    )
    {
        Token = token;
        _owner = owner;
        _menu = new WeakReference<object>(menu);
        _component = identity.Component is null
            ? null
            : new WeakReference<object>(identity.Component);
        _semanticTarget = new WeakReference<object>(identity.SemanticTarget);
        MenuEpoch = menuEpoch;
        Extractor = identity.Extractor;
        PublicKind = identity.PublicKind;
        InventorySide = identity.InventorySide;
        EquipmentSlotKind = identity.EquipmentSlotKind;
        Index = identity.Index;
        _guard = identity.Guard;
        ObservedGeneration = observedGeneration;
    }

    public string Token { get; }
    public RefKind Kind => RefKind.UiElement;
    public string MenuEpoch { get; }
    public UiExtractorKind Extractor { get; }
    public UiElementKind PublicKind { get; }
    public UiInventorySide InventorySide { get; }
    public UiEquipmentSlotKind EquipmentSlotKind { get; }
    public int Index { get; }
    public long ObservedGeneration { get; set; }
    public bool Stale { get; set; }
    public object? ResolvedComponent { get; private set; }

    public bool Matches(
        IUiElementRefOwner owner,
        UiElementBindingIdentity identity,
        string menuEpoch
    ) => !Stale
        && string.Equals(MenuEpoch, menuEpoch, StringComparison.Ordinal)
        && Extractor == identity.Extractor
        && PublicKind == identity.PublicKind
        && InventorySide == identity.InventorySide
        && EquipmentSlotKind == identity.EquipmentSlotKind
        && Index == identity.Index
        && string.Equals(_guard, identity.Guard, StringComparison.Ordinal)
        && _owner.TryGetMenuIdentity(out var previousMenu)
        && owner.TryGetMenuIdentity(out var currentMenu)
        && ReferenceEquals(previousMenu, currentMenu)
        && ComponentMatches(identity.Component)
        && _semanticTarget.TryGetTarget(out var previousTarget)
        && ReferenceEquals(previousTarget, identity.SemanticTarget);

    private bool ComponentMatches(object? component) =>
        component is null
            ? _component is null
            : _component is not null
                && _component.TryGetTarget(out var previous)
                && ReferenceEquals(previous, component);

    public OpaqueBindingCurrentStatus ResolveCurrent(out object? target)
    {
        target = null;
        ResolvedComponent = null;
        if (Stale
            || !_menu.TryGetTarget(out var menu)
            || !TryGetComponent(out var component)
            || !_semanticTarget.TryGetTarget(out var semanticTarget)
            || !_owner.TryGetMenuIdentity(out var ownerMenu)
            || !ReferenceEquals(menu, ownerMenu))
            return OpaqueBindingCurrentStatus.Stale;

        var current = _owner.ResolveCurrentElement(new UiElementBindingIdentity(
            Extractor,
            PublicKind,
            InventorySide,
            EquipmentSlotKind,
            Index,
            component,
            semanticTarget,
            _guard
        ));
        if (current.Status == UiElementLookupStatus.Unavailable)
            return OpaqueBindingCurrentStatus.Unavailable;
        if (current.Status == UiElementLookupStatus.Stale
            || current.SemanticTarget is null
            || !ComponentsMatch(component, current.Component)
            || !ReferenceEquals(semanticTarget, current.SemanticTarget)
            || !string.Equals(_guard, current.Guard, StringComparison.Ordinal))
            return OpaqueBindingCurrentStatus.Stale;
        target = current.SemanticTarget;
        ResolvedComponent = current.Component;
        return OpaqueBindingCurrentStatus.Resolved;
    }

    private bool TryGetComponent(out object? component)
    {
        component = null;
        return _component is null || _component.TryGetTarget(out component);
    }

    private static bool ComponentsMatch(object? left, object? right) =>
        left is null ? right is null : ReferenceEquals(left, right);
}

internal readonly record struct UiProjectionSession(
    object Menu,
    string MenuEpoch,
    long Generation
);

internal enum UiElementResolveStatus
{
    Resolved,
    Stale,
    NotFound,
    Unsupported,
    Unavailable,
}

internal sealed record ResolvedUiElementRef(
    object Target,
    object? Component,
    string MenuEpoch,
    UiExtractorKind Extractor,
    UiElementKind PublicKind,
    UiInventorySide InventorySide,
    UiEquipmentSlotKind EquipmentSlotKind,
    int Index
);

internal sealed record UiElementResolveResult(
    UiElementResolveStatus Status,
    RefKind Kind,
    Error? Error,
    ResolvedUiElementRef? Target
);
