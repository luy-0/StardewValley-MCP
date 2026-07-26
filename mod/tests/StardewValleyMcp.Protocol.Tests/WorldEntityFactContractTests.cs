using Google.Protobuf;
using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Protocol.Tests;

public sealed class WorldEntityFactContractTests
{
    [Test]
    public void ActionableKeepsFieldFiveAndTracksPresence()
    {
        Assert.That(WorldEntityFact.ActionableFieldNumber, Is.EqualTo(5));

        var unknown = new WorldEntityFact { DisplayName = "Unknown" };
        var notActionable = new WorldEntityFact { DisplayName = "Known false", Actionable = false };
        var actionable = new WorldEntityFact { DisplayName = "Known true", Actionable = true };

        Assert.Multiple(() =>
        {
            Assert.That(unknown.HasActionable, Is.False);
            Assert.That(notActionable.HasActionable, Is.True);
            Assert.That(notActionable.Actionable, Is.False);
            Assert.That(actionable.HasActionable, Is.True);
            Assert.That(actionable.Actionable, Is.True);
        });

        var notActionableRoundTrip = WorldEntityFact.Parser.ParseFrom(notActionable.ToByteArray());
        Assert.That(notActionableRoundTrip.HasActionable, Is.True);
        Assert.That(notActionableRoundTrip.Actionable, Is.False);
    }
}
