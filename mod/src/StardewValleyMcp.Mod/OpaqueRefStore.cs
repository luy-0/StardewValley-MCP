using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

/// <summary>
/// Owns process-local opaque references. Tokens are never decoded; resolution only uses
/// the in-memory binding and verifies that the original runtime object is still attached
/// to the same location collection.
/// </summary>
/// <remarks>
/// 当前 V1 观察切片的已知限制：
/// <list type="bullet">
/// <item>Ref 只在单个 Mod 进程内有效。游戏重启或 Mod 重载后，客户端必须把旧 Ref 视为 stale 并重新查询 Snapshot。</item>
/// <item>Token 索引目前没有 TTL 或容量策略。长时间会话观察大量不同对象时，stale binding 会保留到进程退出。</item>
/// <item>部分世界实体依赖运行时对象身份、Location、Tile 与 Guard 校验。这比解析公开字符串更严格，但无法在游戏重建等价对象后恢复身份。</item>
/// <item>Door 等逻辑身份 Ref 绑定在 Location 与 Tile 级身份上，适合同一已加载世界内的 inspect 和后续动作，不是持久存档级 ID。</item>
/// </list>
/// </remarks>
internal sealed class OpaqueRefStore
{
    private const int TokenGenerationAttempts = 8;
    private readonly string _modInstanceId;
    private readonly Func<string> _tokenFactory;
    private readonly ConditionalWeakTable<object, Dictionary<BindingKey, Binding>> _byIdentity = new();
    private readonly Dictionary<string, IOpaqueBinding> _byToken = new(StringComparer.Ordinal);
    private readonly InventoryItemBindingStore _inventoryItems = new();
    private readonly UiElementBindingStore _uiElements;
    private readonly ConditionalWeakTable<object, Dictionary<LogicalKey, object>> _logicalIdentities = new();

    public OpaqueRefStore(
        string modInstanceId,
        Func<string>? tokenFactory = null,
        Func<string>? menuEpochFactory = null
    )
    {
        OpaqueRefTokenCodec.ValidateInstanceId(modInstanceId);
        _modInstanceId = modInstanceId;
        _tokenFactory = tokenFactory ?? (() => OpaqueRefTokenCodec.NewToken(_modInstanceId));
        _uiElements = new UiElementBindingStore(menuEpochFactory);
    }

    public Ref GetOrCreate(
        object target,
        GameLocation location,
        RefKind kind,
        RefLocatorKind locatorKind,
        int x,
        int y,
        string guard,
        string role = "world"
    )
    {
        var bindings = _byIdentity.GetOrCreateValue(target);
        var bindingKey = new BindingKey(kind, role);
        if (bindings.TryGetValue(bindingKey, out var current))
        {
            if (current.Matches(location, kind, locatorKind, guard))
            {
                current.X = x;
                current.Y = y;
                return new Ref { Value = current.Token };
            }

            current.Stale = true;
        }

        var token = CreateUniqueToken();
        var binding = new Binding(token, target, location, kind, locatorKind, x, y, guard, role);
        bindings[bindingKey] = binding;
        _byToken.Add(token, binding);
        return new Ref { Value = token };
    }

    public object GetLogicalIdentity(GameLocation location, RefLocatorKind kind, int x, int y)
    {
        var identities = _logicalIdentities.GetOrCreateValue(location);
        var key = new LogicalKey(kind, x, y);
        if (!identities.TryGetValue(key, out var identity))
        {
            identity = new object();
            identities.Add(key, identity);
        }
        return identity;
    }

    public Ref ObserveInventoryItem(
        IInventoryRefOwner owner,
        int slot,
        object target,
        string guard
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(target);
        if (slot < 0)
            throw new ArgumentOutOfRangeException(nameof(slot));
        var binding = _inventoryItems.Observe(
            owner,
            slot,
            target,
            guard,
            CreateUniqueToken
        );
        if (_byToken.TryGetValue(binding.Token, out var registered))
        {
            if (!ReferenceEquals(registered, binding))
                throw new InvalidOperationException("Ref token 冲突");
        }
        else
        {
            _byToken.Add(binding.Token, binding);
        }
        return new Ref { Value = binding.Token };
    }

    public void ObserveEmptyInventorySlot(IInventoryRefOwner owner, int slot)
    {
        _inventoryItems.ObserveEmpty(owner, slot);
    }

    public void CompleteInventoryObservation(IInventoryRefOwner owner, int capacity)
    {
        _inventoryItems.Complete(owner, capacity);
    }

    public UiProjectionSession BeginUiProjection(object menu) => _uiElements.Begin(menu);

    public Ref ObserveUiElement(
        UiProjectionSession session,
        IUiElementRefOwner owner,
        UiElementBindingIdentity identity
    )
    {
        var binding = _uiElements.Observe(session, owner, identity, CreateUniqueToken);
        if (_byToken.TryGetValue(binding.Token, out var registered))
        {
            if (!ReferenceEquals(registered, binding))
                throw new InvalidOperationException("Ref token 冲突");
        }
        else
        {
            _byToken.Add(binding.Token, binding);
        }
        return new Ref { Value = binding.Token };
    }

    public void CompleteUiProjection(UiProjectionSession session) =>
        _uiElements.Complete(session);

    public void CloseUiProjection() => _uiElements.CloseActive();

    public UiElementResolveResult ResolveUiElement(Ref reference)
    {
        try
        {
            var resolution = ResolveCore(
                reference,
                kind => kind == RefKind.UiElement,
                out var binding,
                out var target
            );
            var status = resolution.Status switch
            {
                RefStatus.Resolved => UiElementResolveStatus.Resolved,
                RefStatus.Stale => UiElementResolveStatus.Stale,
                RefStatus.NotFound => UiElementResolveStatus.NotFound,
                RefStatus.Unsupported => UiElementResolveStatus.Unsupported,
                _ => UiElementResolveStatus.Unavailable,
            };
            if (status != UiElementResolveStatus.Resolved)
                return new UiElementResolveResult(status, resolution.Kind, resolution.Error, null);
            if (binding is not UiElementBinding uiBinding || target is null)
            {
                return new UiElementResolveResult(
                    UiElementResolveStatus.Unavailable,
                    RefKind.UiElement,
                    new Error { Code = ErrorCode.Internal, Message = "当前 UI Ref 绑定不可用" },
                    null
                );
            }
            return new UiElementResolveResult(
                status,
                resolution.Kind,
                null,
                new ResolvedUiElementRef(
                    target,
                    uiBinding.MenuEpoch,
                    uiBinding.Extractor,
                    uiBinding.PublicKind,
                    uiBinding.Index
                )
            );
        }
        catch (OpaqueRefUnavailableException)
        {
            return new UiElementResolveResult(
                UiElementResolveStatus.Unavailable,
                RefKind.UiElement,
                new Error { Code = ErrorCode.Internal, Message = "当前 UI Ref 事实不可用" },
                null
            );
        }
    }

    /// <summary>
    /// Resolves an inspect Ref exactly once and preserves the concrete binding family for
    /// the projection layer. A temporarily unreadable binding remains live and is reported
    /// as FACT_UNAVAILABLE rather than being marked stale.
    /// </summary>
    public InspectRefLookup ResolveForInspect(Ref reference)
    {
        IOpaqueBinding? binding = null;
        object? target = null;
        try
        {
            var resolution = ResolveCore(
                reference,
                IsInspectableKind,
                out binding,
                out target
            );
            if (resolution.Status != RefStatus.Resolved)
                return new InspectRefLookup(resolution, null);
            if (target is null)
                return FactUnavailable(reference, binding?.Kind ?? RefKind.Unspecified);

            InspectableRefTarget? inspected = binding switch
            {
                Binding contextual when contextual.Kind == RefKind.WorldEntity =>
                    CreateContextTarget(contextual, target, RefKind.WorldEntity),
                Binding contextual when contextual.Kind == RefKind.Character =>
                    CreateContextTarget(contextual, target, RefKind.Character),
                Binding contextual when contextual.Kind == RefKind.Container =>
                    CreateContextTarget(contextual, target, RefKind.Container),
                InventoryItemBinding item when item.Kind == RefKind.InventoryItem =>
                    new InventoryItemInspectTarget(
                        new InventoryItemRefTarget(target, item.Slot, item.Provenance)
                    ),
                UiElementBinding ui when ui.Kind == RefKind.UiElement =>
                    new UiElementInspectTarget(
                        new ResolvedUiElementRef(
                            target,
                            ui.MenuEpoch,
                            ui.Extractor,
                            ui.PublicKind,
                            ui.Index
                        )
                    ),
                _ => null,
            };
            if (inspected is not null)
                return new InspectRefLookup(resolution, inspected);
            return new InspectRefLookup(
                Resolution(
                    reference,
                    RefStatus.Unsupported,
                    binding?.Kind ?? RefKind.Unspecified,
                    ErrorCode.InvalidArgument,
                    "当前 Ref 类型不支持检查"
                ),
                null
            );
        }
        catch (OpaqueRefUnavailableException)
        {
            return FactUnavailable(reference, binding?.Kind ?? RefKind.Unspecified);
        }
    }

    private static bool IsInspectableKind(RefKind kind) => kind is
        RefKind.WorldEntity
        or RefKind.Character
        or RefKind.InventoryItem
        or RefKind.Container
        or RefKind.UiElement;

    private static InspectableRefTarget? CreateContextTarget(
        Binding binding,
        object target,
        RefKind kind
    )
    {
        if (!binding.TryGetLocation(out var location))
            return null;
        var resolved = new ResolvedOpaqueRef(
            target,
            binding.Kind,
            location,
            binding.LocatorKind,
            binding.X,
            binding.Y,
            binding.Guard,
            binding.Role
        );
        return kind switch
        {
            RefKind.WorldEntity => new WorldEntityInspectTarget(resolved),
            RefKind.Character => new CharacterInspectTarget(resolved),
            RefKind.Container => new ContainerInspectTarget(resolved),
            _ => null,
        };
    }

    private static InspectRefLookup FactUnavailable(Ref reference, RefKind kind) => new(
        Resolution(
            reference,
            RefStatus.FactUnavailable,
            kind,
            ErrorCode.Internal,
            "当前 Ref 事实不可用"
        ),
        null
    );

    public RefResolution Resolve(
        Ref reference,
        IReadOnlySet<RefKind> allowedKinds,
        out ResolvedOpaqueRef? resolved
    )
    {
        var resolution = ResolveAllowedKinds(
            reference,
            allowedKinds,
            out var binding,
            out var target
        );
        resolved = null;
        if (resolution.Status != RefStatus.Resolved
            || binding is not Binding contextual
            || target is null
            || !contextual.TryGetLocation(out var location))
            return resolution;
        resolved = new ResolvedOpaqueRef(
            target,
            contextual.Kind,
            location,
            contextual.LocatorKind,
            contextual.X,
            contextual.Y,
            contextual.Guard,
            contextual.Role
        );
        return resolution;
    }

    public InventoryItemResolveResult ResolveInventoryItem(Ref reference)
    {
        try
        {
            var resolution = ResolveCore(
                reference,
                kind => kind == RefKind.InventoryItem,
                out var binding,
                out var target
            );
            var status = resolution.Status switch
            {
                RefStatus.Resolved => InventoryItemResolveStatus.Resolved,
                RefStatus.Stale => InventoryItemResolveStatus.Stale,
                RefStatus.NotFound => InventoryItemResolveStatus.NotFound,
                RefStatus.Unsupported => InventoryItemResolveStatus.Unsupported,
                _ => InventoryItemResolveStatus.Unavailable,
            };
            InventoryItemRefTarget? resolved = null;
            if (status == InventoryItemResolveStatus.Resolved)
            {
                if (binding is not InventoryItemBinding itemBinding || target is null)
                {
                    return new InventoryItemResolveResult(
                        InventoryItemResolveStatus.Unavailable,
                        RefKind.InventoryItem,
                        new Error { Code = ErrorCode.Internal, Message = "当前 Item Ref 绑定不可用" },
                        null
                    );
                }
                resolved = new InventoryItemRefTarget(
                    target,
                    itemBinding.Slot,
                    itemBinding.Provenance
                );
            }
            return new InventoryItemResolveResult(
                status,
                resolution.Kind,
                resolution.Error,
                resolved
            );
        }
        catch (OpaqueRefUnavailableException)
        {
            return new InventoryItemResolveResult(
                InventoryItemResolveStatus.Unavailable,
                RefKind.InventoryItem,
                new Error { Code = ErrorCode.Internal, Message = "当前 Item Ref 事实不可用" },
                null
            );
        }
    }

    internal RefResolution ResolveAllowedKinds(
        Ref reference,
        IReadOnlySet<RefKind> allowedKinds,
        out IOpaqueBinding? binding,
        out object? target
    )
    {
        return ResolveCore(reference, allowedKinds.Contains, out binding, out target);
    }

    private RefResolution ResolveCore(
        Ref reference,
        Func<RefKind, bool> kindAllowed,
        out IOpaqueBinding? binding,
        out object? target
    )
    {
        binding = null;
        target = null;
        if (reference is null || string.IsNullOrEmpty(reference.Value))
            return Resolution(reference, RefStatus.NotFound, RefKind.Unspecified, ErrorCode.NotFound, "Ref 不存在");
        var tokenKnown = _byToken.TryGetValue(reference.Value, out binding);
        var tokenDecision = OpaqueRefTokenCodec.Decide(reference.Value, _modInstanceId, tokenKnown);
        if (tokenDecision == OpaqueRefLookupDecision.Stale)
            return Resolution(reference, RefStatus.Stale, RefKind.Unspecified, ErrorCode.StaleRef, "Ref 来自已失效的 Mod 实例");
        if (tokenDecision != OpaqueRefLookupDecision.Lookup || binding is null)
            return Resolution(reference, RefStatus.NotFound, RefKind.Unspecified, ErrorCode.NotFound, "Ref 不存在");

        if (!kindAllowed(binding.Kind))
            return Resolution(reference, RefStatus.Unsupported, binding.Kind, ErrorCode.InvalidArgument, "Ref 类型不匹配");
        if (binding.Stale)
        {
            target = null;
            return Resolution(reference, RefStatus.Stale, binding.Kind, ErrorCode.StaleRef, "Ref 已失效");
        }
        var current = binding.ResolveCurrent(out target);
        if (current == OpaqueBindingCurrentStatus.Unavailable)
            throw new OpaqueRefUnavailableException();
        if (current == OpaqueBindingCurrentStatus.Stale)
        {
            binding.Stale = true;
            target = null;
            return Resolution(reference, RefStatus.Stale, binding.Kind, ErrorCode.StaleRef, "Ref 已失效");
        }
        return new RefResolution
        {
            Ref = reference.Clone(),
            Status = RefStatus.Resolved,
            Kind = binding.Kind,
        };
    }

    private static RefResolution Resolution(
        Ref? reference,
        RefStatus status,
        RefKind kind,
        ErrorCode code,
        string message
    ) => new()
    {
        Ref = reference?.Clone() ?? new Ref(),
        Status = status,
        Kind = kind,
        Error = new Error { Code = code, Message = message },
    };

    private string CreateUniqueToken()
    {
        for (var attempt = 0; attempt < TokenGenerationAttempts; attempt++)
        {
            var token = _tokenFactory();
            if (OpaqueRefTokenCodec.Classify(token, _modInstanceId)
                    != OpaqueRefTokenScope.CurrentInstance)
                throw new InvalidOperationException("Ref token 生成器返回了无效 token");
            if (!_byToken.ContainsKey(token))
                return token;
        }
        throw new InvalidOperationException("无法生成唯一 Ref token");
    }

    private sealed class Binding : IOpaqueBinding
    {
        private readonly WeakReference<object> _target;
        private readonly WeakReference<GameLocation> _location;
        private readonly string _guard;
        private string _locationId;

        public Binding(
            string token,
            object target,
            GameLocation location,
            RefKind kind,
            RefLocatorKind locatorKind,
            int x,
            int y,
            string guard,
            string role
        )
        {
            Token = token;
            _target = new WeakReference<object>(target);
            _location = new WeakReference<GameLocation>(location);
            _locationId = location.NameOrUniqueName;
            Kind = kind;
            LocatorKind = locatorKind;
            X = x;
            Y = y;
            _guard = guard;
            Role = role;
        }

        public string Token { get; }
        public RefKind Kind { get; }
        public RefLocatorKind LocatorKind { get; }
        public string Guard => _guard;
        public string Role { get; }
        public int X { get; set; }
        public int Y { get; set; }
        public bool Stale { get; set; }

        public bool Matches(
            GameLocation location,
            RefKind kind,
            RefLocatorKind locatorKind,
            string guard
        )
        {
            if (Stale
                || Kind != kind
                || LocatorKind != locatorKind
                || !string.Equals(_guard, guard, StringComparison.Ordinal))
                return false;
            if (_location.TryGetTarget(out var boundLocation) && ReferenceEquals(boundLocation, location))
                return true;
            if (boundLocation is null)
                return false;
            if (LocatorKind != RefLocatorKind.Character
                || !_target.TryGetTarget(out var target)
                || !LoadedLocationInstancePolicy.AllowsCharacterMove(
                    _locationId,
                    boundLocation,
                    location.NameOrUniqueName,
                    location,
                    GameLocationIdentity.EnumerateLoadedInstances()
                )
                || !IsStillAttached(target, location))
                return false;
            _location.SetTarget(location);
            _locationId = location.NameOrUniqueName;
            return true;
        }

        public OpaqueBindingCurrentStatus ResolveCurrent(out object? target)
        {
            target = null;
            try
            {
                if (!_target.TryGetTarget(out var candidate))
                    return OpaqueBindingCurrentStatus.Stale;
                if (!_location.TryGetTarget(out var boundLocation))
                    return OpaqueBindingCurrentStatus.Stale;
                GameLocation? location = boundLocation;
                if (LocatorKind == RefLocatorKind.Character)
                {
                    var currentLocation = candidate switch
                    {
                        NPC npc => npc.currentLocation,
                        FarmAnimal animal => animal.currentLocation,
                        _ => null,
                    };
                    if (currentLocation is null
                        || !LoadedLocationInstancePolicy.AllowsCharacterMove(
                            _locationId,
                            boundLocation,
                            currentLocation.NameOrUniqueName,
                            currentLocation,
                            GameLocationIdentity.EnumerateLoadedInstances()
                        ))
                        return OpaqueBindingCurrentStatus.Stale;
                    location = currentLocation;
                    if (!ReferenceEquals(boundLocation, currentLocation))
                    {
                        _location.SetTarget(currentLocation);
                        _locationId = currentLocation.NameOrUniqueName;
                    }
                }
                else if (!LoadedLocationInstancePolicy.IsCurrent(
                    _locationId,
                    boundLocation,
                    GameLocationIdentity.EnumerateLoadedInstances()
                ))
                    return OpaqueBindingCurrentStatus.Stale;
                if (!IsStillAttached(candidate, location))
                    return OpaqueBindingCurrentStatus.Stale;
                target = candidate;
                return OpaqueBindingCurrentStatus.Resolved;
            }
            catch
            {
                target = null;
                return OpaqueBindingCurrentStatus.Unavailable;
            }
        }

        public bool TryGetLocation(out GameLocation location) =>
            _location.TryGetTarget(out location!);

        private bool IsStillAttached(object target, GameLocation location)
        {
            var tile = new Vector2(X, Y);
            return LocatorKind switch
            {
                RefLocatorKind.TerrainFeature =>
                    location.terrainFeatures.TryGetValue(tile, out var feature)
                    && ReferenceEquals(feature, target),
                RefLocatorKind.Object =>
                    location.Objects.Values.Any(obj => ReferenceEquals(obj, target)),
                RefLocatorKind.Fridge => ReferenceEquals(location.GetFridge(onlyUnlocked: false), target),
                RefLocatorKind.Furniture => location.furniture.Any(item => ReferenceEquals(item, target)),
                RefLocatorKind.ResourceClump => location.resourceClumps.Any(item => ReferenceEquals(item, target)),
                RefLocatorKind.Warp => location.warps.Any(item => ReferenceEquals(item, target)),
                RefLocatorKind.Character =>
                    location.characters.Any(item => ReferenceEquals(item, target))
                    || location.Animals.Values.Any(item => ReferenceEquals(item, target)),
                RefLocatorKind.Door =>
                    location.doors.TryGetValue(new Point(X, Y), out var doorTarget)
                    && string.Equals(doorTarget, _guard, StringComparison.Ordinal),
                _ => false,
            };
        }
    }

    private readonly record struct LogicalKey(RefLocatorKind Kind, int X, int Y);
    private readonly record struct BindingKey(RefKind Kind, string Role);
}

internal interface IInventoryRefOwner
{
    InventoryItemProvenance Provenance { get; }
    bool TryGetIdentity(out object identity);
    InventorySlotLookup ResolveCurrentSlot(int slot);
}

internal enum InventorySlotLookupStatus
{
    Resolved,
    Stale,
    Unavailable,
}

internal readonly record struct InventorySlotLookup(
    InventorySlotLookupStatus Status,
    object? Target = null,
    string Guard = ""
);

internal enum InventoryItemProvenance
{
    Player,
    Container,
}

internal sealed record InventoryItemRefTarget(
    object Target,
    int Slot,
    InventoryItemProvenance Provenance
);

internal enum InventoryItemResolveStatus
{
    Resolved,
    Stale,
    NotFound,
    Unsupported,
    Unavailable,
}

internal sealed record InventoryItemResolveResult(
    InventoryItemResolveStatus Status,
    RefKind Kind,
    Error? Error,
    InventoryItemRefTarget? Target
);

internal sealed class OpaqueRefUnavailableException : Exception
{
}

internal sealed record ResolvedOpaqueRef(
    object Target,
    RefKind Kind,
    GameLocation Location,
    RefLocatorKind LocatorKind,
    int X,
    int Y,
    string Guard,
    string Role
);

internal sealed record InspectRefLookup(
    RefResolution Resolution,
    InspectableRefTarget? Target
);

internal abstract record InspectableRefTarget(RefKind Kind);
internal sealed record WorldEntityInspectTarget(ResolvedOpaqueRef Value)
    : InspectableRefTarget(RefKind.WorldEntity);
internal sealed record CharacterInspectTarget(ResolvedOpaqueRef Value)
    : InspectableRefTarget(RefKind.Character);
internal sealed record InventoryItemInspectTarget(InventoryItemRefTarget Value)
    : InspectableRefTarget(RefKind.InventoryItem);
internal sealed record ContainerInspectTarget(ResolvedOpaqueRef Value)
    : InspectableRefTarget(RefKind.Container);
internal sealed record UiElementInspectTarget(ResolvedUiElementRef Value)
    : InspectableRefTarget(RefKind.UiElement);

internal static class OpaqueRefTokenCodec
{
    private const string Prefix = "r1_";
    private const int InstanceIdLength = 36;
    private const int RandomLength = 48;
    private const int TokenLength = 3 + InstanceIdLength + 1 + RandomLength;

    public static void ValidateInstanceId(string modInstanceId)
    {
        if (!IsInstanceId(modInstanceId))
            throw new ArgumentException("mod_instance_id 必须是小写 UUID", nameof(modInstanceId));
    }

    public static string NewToken(string modInstanceId)
    {
        ValidateInstanceId(modInstanceId);
        return $"{Prefix}{modInstanceId}_{LowerHex(24)}";
    }

    public static OpaqueRefTokenScope Classify(string token, string currentModInstanceId)
    {
        if (!IsInstanceId(currentModInstanceId)
            || token.Length != TokenLength
            || !token.StartsWith(Prefix, StringComparison.Ordinal)
            || token[Prefix.Length + InstanceIdLength] != '_'
            || !IsInstanceId(token.Substring(Prefix.Length, InstanceIdLength))
            || !IsLowerHex(token.AsSpan(Prefix.Length + InstanceIdLength + 1, RandomLength)))
            return OpaqueRefTokenScope.Invalid;

        return token.AsSpan(Prefix.Length, InstanceIdLength).SequenceEqual(currentModInstanceId)
            ? OpaqueRefTokenScope.CurrentInstance
            : OpaqueRefTokenScope.ForeignInstance;
    }

    public static OpaqueRefLookupDecision Decide(
        string token,
        string currentModInstanceId,
        bool issuedByCurrentInstance
    ) => Classify(token, currentModInstanceId) switch
    {
        OpaqueRefTokenScope.ForeignInstance => OpaqueRefLookupDecision.Stale,
        OpaqueRefTokenScope.CurrentInstance when issuedByCurrentInstance => OpaqueRefLookupDecision.Lookup,
        _ => OpaqueRefLookupDecision.NotFound,
    };

    private static string LowerHex(int byteCount) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(byteCount)).ToLowerInvariant();

    private static bool IsInstanceId(string value) =>
        value.Length == InstanceIdLength
        && value == value.ToLowerInvariant()
        && Guid.TryParseExact(value, "D", out _);

    private static bool IsLowerHex(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        }
        return true;
    }
}

internal enum OpaqueRefTokenScope
{
    Invalid,
    CurrentInstance,
    ForeignInstance,
}

internal enum OpaqueRefLookupDecision
{
    NotFound,
    Stale,
    Lookup,
}

internal static class LoadedLocationInstancePolicy
{
    public static bool IsCurrent(
        string locationId,
        object instance,
        IEnumerable<(string LocationId, object Instance)> loaded
    ) => loaded.Any(candidate =>
        string.Equals(candidate.LocationId, locationId, StringComparison.OrdinalIgnoreCase)
        && ReferenceEquals(candidate.Instance, instance));

    public static bool AllowsCharacterMove(
        string previousLocationId,
        object previousInstance,
        string currentLocationId,
        object currentInstance,
        IEnumerable<(string LocationId, object Instance)> loaded
    ) => IsCurrent(currentLocationId, currentInstance, loaded)
        && (ReferenceEquals(previousInstance, currentInstance)
            || !string.Equals(previousLocationId, currentLocationId, StringComparison.OrdinalIgnoreCase));
}

internal static class GameLocationIdentity
{
    public static GameLocation? FindExact(string locationId)
    {
        GameLocation? match = null;
        Utility.ForEachLocation(
            location =>
            {
                try
                {
                    if (!string.Equals(
                        location.NameOrUniqueName,
                        locationId,
                        StringComparison.OrdinalIgnoreCase
                    ))
                        return true;
                }
                catch
                {
                    return true;
                }
                match = location;
                return false;
            },
            includeInteriors: true,
            includeGenerated: true
        );
        return match;
    }

    public static bool IsCurrent(string locationId, GameLocation instance) =>
        LoadedLocationInstancePolicy.IsCurrent(locationId, instance, EnumerateLoadedInstances());

    public static IEnumerable<(string LocationId, object Instance)> EnumerateLoadedInstances()
    {
        var loaded = new List<(string LocationId, object Instance)>();
        Utility.ForEachLocation(
            location =>
            {
                try
                {
                    loaded.Add((location.NameOrUniqueName, location));
                }
                catch
                {
                    // An invalid third-party Location can't validate an existing Ref.
                }
                return true;
            },
            includeInteriors: true,
            includeGenerated: true
        );
        return loaded;
    }
}

internal enum RefLocatorKind
{
    TerrainFeature,
    Object,
    Fridge,
    Furniture,
    ResourceClump,
    Warp,
    Door,
    Character,
}
