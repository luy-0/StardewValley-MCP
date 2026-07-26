using Google.Protobuf;
using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Protocol.Tests;

public sealed class DoorFactContractTests
{
    [Test]
    public void LockedKeepsFieldOneAndTracksPresence()
    {
        Assert.That(DoorFact.LockedFieldNumber, Is.EqualTo(1));

        var unknown = new DoorFact { TargetLocationId = "CustomLocation" };
        var accessible = new DoorFact { Locked = false, TargetLocationId = "FarmHouse" };
        var locked = new DoorFact { Locked = true, TargetLocationId = "SeedShop" };

        Assert.Multiple(() =>
        {
            Assert.That(unknown.HasLocked, Is.False);
            Assert.That(accessible.HasLocked, Is.True);
            Assert.That(accessible.Locked, Is.False);
            Assert.That(locked.HasLocked, Is.True);
            Assert.That(locked.Locked, Is.True);
        });

        var accessibleRoundTrip = DoorFact.Parser.ParseFrom(accessible.ToByteArray());
        Assert.That(accessibleRoundTrip.HasLocked, Is.True);
        Assert.That(accessibleRoundTrip.Locked, Is.False);
    }
}
