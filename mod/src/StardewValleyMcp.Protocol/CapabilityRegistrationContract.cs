namespace StardewValleyMcp.Protocol.V1;

public static class CapabilityRegistrationContract
{
    public static void Validate(
        string handlerId,
        CommandRequest.OperationOneofCase operation,
        CapabilityDescriptor descriptor
    )
    {
        if (!string.Equals(handlerId, descriptor.Id, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Handler id '{handlerId}' 与 descriptor id '{descriptor.Id}' 不一致"
            );

        var field = CommandRequest.Descriptor.FindFieldByNumber((int)operation);
        if (field?.ContainingOneof?.Name != "operation" || field.MessageType is null)
            throw new InvalidOperationException($"Operation '{operation}' 不是 CommandRequest operation message");

        if (!string.Equals(field.Name, handlerId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Handler id '{handlerId}' 与 operation field '{field.Name}' 不一致"
            );

        if (!string.Equals(field.MessageType.Name, descriptor.RequestType, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Operation request type '{field.MessageType.Name}' 与 descriptor request type '{descriptor.RequestType}' 不一致"
            );
    }
}
