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
internal sealed class OpaqueRefStore
{
    private readonly string _modInstanceId;
    private readonly ConditionalWeakTable<object, Dictionary<BindingKey, Binding>> _byIdentity = new();
    private readonly Dictionary<string, Binding> _byToken = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<GameLocation, Dictionary<LogicalKey, object>> _logicalIdentities = new();

    public OpaqueRefStore(string modInstanceId)
    {
        OpaqueRefTokenCodec.ValidateInstanceId(modInstanceId);
        _modInstanceId = modInstanceId;
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

        var token = OpaqueRefTokenCodec.NewToken(_modInstanceId);
        var binding = new Binding(token, target, location, kind, locatorKind, x, y, guard);
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

    public RefResolution Resolve(Ref reference, RefKind expectedKind, out object? target)
    {
        target = null;
        if (reference is null || string.IsNullOrEmpty(reference.Value))
            return Resolution(reference, RefStatus.NotFound, RefKind.Unspecified, ErrorCode.NotFound, "Ref 不存在");
        var tokenKnown = _byToken.TryGetValue(reference.Value, out var binding);
        var tokenDecision = OpaqueRefTokenCodec.Decide(reference.Value, _modInstanceId, tokenKnown);
        if (tokenDecision == OpaqueRefLookupDecision.Stale)
            return Resolution(reference, RefStatus.Stale, RefKind.Unspecified, ErrorCode.StaleRef, "Ref 来自已失效的 Mod 实例");
        if (tokenDecision != OpaqueRefLookupDecision.Lookup || binding is null)
            return Resolution(reference, RefStatus.NotFound, RefKind.Unspecified, ErrorCode.NotFound, "Ref 不存在");

        if (binding.Stale || !binding.TryGetCurrent(out target))
        {
            binding.Stale = true;
            target = null;
            return Resolution(reference, RefStatus.Stale, binding.Kind, ErrorCode.StaleRef, "Ref 已失效");
        }
        if (binding.Kind != expectedKind)
        {
            target = null;
            return Resolution(reference, RefStatus.Unsupported, binding.Kind, ErrorCode.InvalidArgument, "Ref 类型不匹配");
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

    private sealed class Binding
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
            string guard
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
        }

        public string Token { get; }
        public RefKind Kind { get; }
        public RefLocatorKind LocatorKind { get; }
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

        public bool TryGetCurrent(out object? target)
        {
            target = null;
            if (!_target.TryGetTarget(out var candidate))
                return false;
            if (!_location.TryGetTarget(out var boundLocation))
                return false;
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
                    return false;
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
                return false;
            if (!IsStillAttached(candidate, location))
                return false;
            target = candidate;
            return true;
        }

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
