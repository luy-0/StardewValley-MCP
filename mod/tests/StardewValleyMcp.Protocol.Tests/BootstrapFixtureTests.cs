using System.Text.Json;
using System.Security.Cryptography;
using Google.Protobuf;
using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Protocol.Tests;

public sealed class BootstrapFixtureTests
{
    private const string Digest = "6c9c9fc8002032a8b4191e3d4809f74ae9c20abcfb26fbf579d7a329d7daf199";

    [Test]
    public void BootstrapFixturesMatchQueryRuntimeLifecycle()
    {
        var ready = Parse("server-ready.json");
        Assert.That(ready.ServerReady.CapabilitySnapshot.Digest, Is.EqualTo(Digest));
        Assert.That(ready.ServerReady.CapabilitySnapshot.Capabilities, Has.Count.EqualTo(1));
        Assert.That(ready.ServerReady.CapabilitySnapshot.Capabilities[0].Id, Is.EqualTo("query_runtime"));

        var request = Parse("query-runtime.request.json");
        Assert.That(request.CommandRequest.OperationCase, Is.EqualTo(CommandRequest.OperationOneofCase.QueryRuntime));
        Assert.That(request.Fence.CapabilityDigest, Is.EqualTo(Digest));

        var accepted = Parse("query-runtime.accepted.json");
        Assert.That(accepted.CommandEvent.State, Is.EqualTo(CommandState.Accepted));
        Assert.That(accepted.CommandEvent.OutcomeCase, Is.EqualTo(CommandEvent.OutcomeOneofCase.None));

        var succeeded = Parse("query-runtime.succeeded.json");
        Assert.That(succeeded.CommandEvent.State, Is.EqualTo(CommandState.Succeeded));
        Assert.That(succeeded.CommandEvent.Result.ResultCase, Is.EqualTo(CapabilityResult.ResultOneofCase.QueryRuntime));

        var notReady = Parse("query-runtime.not-ready.json");
        Assert.That(notReady.CommandEvent.State, Is.EqualTo(CommandState.Failed));
        Assert.That(notReady.CommandEvent.Error.Code, Is.EqualTo(ErrorCode.NotReady));
        Assert.That(notReady.CommandEvent.OutcomeCase, Is.EqualTo(CommandEvent.OutcomeOneofCase.Error));
    }

    [Test]
    public void BootstrapHmacVectorUsesSingletonDigest()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath("hmac-sha256.json")));
        var root = document.RootElement;
        Assert.That(root.GetProperty("capabilityDigest").GetString(), Is.EqualTo(Digest));

        var secret = Convert.FromBase64String(root.GetProperty("secretBase64").GetString()!);
        var version = new ProtocolVersion { Major = 1, Minor = 0 };
        var clientTag = Authentication.ComputeClientTag(
            secret,
            root.GetProperty("modInstanceId").GetString()!,
            root.GetProperty("clientInstanceId").GetString()!,
            Convert.FromBase64String(root.GetProperty("serverNonceBase64").GetString()!),
            Convert.FromBase64String(root.GetProperty("clientNonceBase64").GetString()!),
            version,
            root.GetProperty("resumeSessionId").GetString()!
        );
        Assert.That(
            Convert.ToBase64String(clientTag),
            Is.EqualTo(root.GetProperty("clientAuthTagBase64").GetString())
        );

        var serverTag = Authentication.ComputeServerTag(
            secret,
            root.GetProperty("modInstanceId").GetString()!,
            root.GetProperty("clientInstanceId").GetString()!,
            Convert.FromBase64String(root.GetProperty("serverNonceBase64").GetString()!),
            Convert.FromBase64String(root.GetProperty("clientNonceBase64").GetString()!),
            version,
            root.GetProperty("sessionId").GetString()!,
            root.GetProperty("leaseEpoch").GetUInt64(),
            Digest,
            root.GetProperty("resultRetentionMs").GetUInt32(),
            root.GetProperty("reconnectGraceMs").GetUInt32()
        );
        Assert.That(
            Convert.ToBase64String(serverTag),
            Is.EqualTo(root.GetProperty("serverAuthTagBase64").GetString())
        );
    }

    [Test]
    public void QueryRuntimeDescriptorMatchesBootstrapDigest()
    {
        var snapshot = QueryRuntimeContract.CreateSnapshot();
        Assert.That(snapshot.Digest, Is.EqualTo(Digest));
        Assert.That(snapshot.Capabilities, Has.Count.EqualTo(1));
        Assert.That(snapshot.Capabilities[0].Id, Is.EqualTo("query_runtime"));
    }

    [Test]
    public async Task FrameCodecRoundTripsAndRejectsZeroLength()
    {
        var expected = Parse("query-runtime.request.json");
        await using var stream = new MemoryStream();
        await FrameCodec.WriteAsync(stream, expected, CancellationToken.None);
        stream.Position = 0;
        var actual = await FrameCodec.ReadAsync(stream, CancellationToken.None);
        Assert.That(actual, Is.EqualTo(expected));

        await using var invalid = new MemoryStream(new byte[4]);
        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await FrameCodec.ReadAsync(invalid, CancellationToken.None)
        );
    }

    private static TransportFrame Parse(string name)
    {
        return JsonParser.Default.Parse<TransportFrame>(File.ReadAllText(FixturePath(name)));
    }

    private static string FixturePath(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }
}
