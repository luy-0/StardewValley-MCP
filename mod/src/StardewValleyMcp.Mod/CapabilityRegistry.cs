using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class CapabilityRegistry
{
    private readonly IReadOnlyDictionary<CommandRequest.OperationOneofCase, RegisteredCapability> _byOperation;

    public CapabilityRegistry(string modInstanceId)
        : this(new OpaqueRefStore(modInstanceId))
    {
    }

    private CapabilityRegistry(OpaqueRefStore refs)
        : this(new ICapabilityHandler[]
        {
            new QueryRuntimeHandler(),
            new QueryWorldHandler(refs),
            new QueryInventoryHandler(refs),
            new QueryUiHandler(refs),
            new InspectHandler(refs),
        })
    {
    }

    internal CapabilityRegistry(IEnumerable<ICapabilityHandler> handlers)
    {
        var byId = new Dictionary<string, RegisteredCapability>(StringComparer.Ordinal);
        var byOperation = new Dictionary<CommandRequest.OperationOneofCase, RegisteredCapability>();
        foreach (var handler in handlers)
        {
            var descriptor = CapabilityCatalog.GetObservationDescriptor(handler.Id);
            CapabilityRegistrationContract.Validate(handler.Id, handler.Operation, descriptor);
            var registration = new RegisteredCapability(handler, descriptor);
            if (!byId.TryAdd(handler.Id, registration))
                throw new InvalidOperationException($"重复 capability id: {handler.Id}");
            if (!byOperation.TryAdd(handler.Operation, registration))
                throw new InvalidOperationException($"重复 capability operation: {handler.Operation}");
        }

        _byOperation = byOperation;
        Snapshot = CapabilityCatalog.CreateSnapshot(byId.Values.Select(item => item.Descriptor));
    }

    public CapabilitySnapshot Snapshot { get; }

    public bool TryResolve(
        CommandRequest request,
        out RegisteredCapability registration
    ) => _byOperation.TryGetValue(request.OperationCase, out registration!);
}

internal sealed record RegisteredCapability(ICapabilityHandler Handler, CapabilityDescriptor Descriptor);
