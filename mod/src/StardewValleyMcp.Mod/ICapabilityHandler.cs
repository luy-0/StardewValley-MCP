using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal interface ICapabilityHandler
{
    string Id { get; }
    CommandRequest.OperationOneofCase Operation { get; }
    Error? Validate(CommandRequest request);
    CommandEvent Execute(string commandId, CommandRequest request);
}
