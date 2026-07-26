using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Protocol.Tests;

public sealed class ObservationContractTests
{
    private const string Digest = "14664e95bbeb39b4c0ab235a5a7b3bf9df8fa2f702e66e1157b23dd4082f2994";

    [Test]
    public void ObservationDescriptorsMatchStageThreeCatalog()
    {
        var snapshot = CapabilityCatalog.CreateObservationSnapshot();
        Assert.That(snapshot.Digest, Is.EqualTo(Digest));
        Assert.That(snapshot.Capabilities.Select(item => item.Id), Is.EqualTo(new[]
        {
            "inspect", "query_inventory", "query_runtime", "query_ui", "query_world",
        }));
        Assert.That(snapshot.Capabilities.All(item =>
            item.ContractVersion == "1.0.0"
            && item.SideEffect == SideEffect.ReadOnly
            && item.Execution == ExecutionMode.Immediate
            && !item.Cancellable
            && item.RequiredScope == "game:read"
            && !item.Destructive
            && item.Risks.Count == 0
        ), Is.True);
    }

    [Test]
    public void RegisteredSubsetDoesNotPreannounceUnimplementedCapabilities()
    {
        var snapshot = CapabilityCatalog.CreateSnapshotFor(new[] { "query_runtime" });
        Assert.That(snapshot.Capabilities.Select(item => item.Id), Is.EqualTo(new[] { "query_runtime" }));
        Assert.That(snapshot.Digest, Is.EqualTo("6c9c9fc8002032a8b4191e3d4809f74ae9c20abcfb26fbf579d7a329d7daf199"));
    }

    [Test]
    public void CapabilityRegistrationMatchesIdOperationAndRequestType()
    {
        var descriptor = CapabilityCatalog.GetObservationDescriptor("query_runtime");

        Assert.DoesNotThrow(() => CapabilityRegistrationContract.Validate(
            "query_runtime",
            CommandRequest.OperationOneofCase.QueryRuntime,
            descriptor
        ));
    }

    [Test]
    public void CapabilityRegistrationRejectsMismatchedIdOperationOrRequestType()
    {
        var descriptor = CapabilityCatalog.GetObservationDescriptor("query_runtime");

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => CapabilityRegistrationContract.Validate(
                "query_world",
                CommandRequest.OperationOneofCase.QueryRuntime,
                descriptor
            ));
            Assert.Throws<InvalidOperationException>(() => CapabilityRegistrationContract.Validate(
                "query_runtime",
                CommandRequest.OperationOneofCase.QueryWorld,
                descriptor
            ));

            var wrongRequestType = descriptor.Clone();
            wrongRequestType.RequestType = nameof(QueryWorldRequest);
            Assert.Throws<InvalidOperationException>(() => CapabilityRegistrationContract.Validate(
                "query_runtime",
                CommandRequest.OperationOneofCase.QueryRuntime,
                wrongRequestType
            ));
        });
    }
}
