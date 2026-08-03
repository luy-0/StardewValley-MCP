using NUnit.Framework;

namespace StardewValleyMcp.Mod.Tests;

public sealed class ActionButtonInputTests
{
    [Test]
    public void SubmitPressesActionButtonAroundNativeInteraction()
    {
        var events = new List<string>();
        var input = new ActionButtonInput(new FakeButtonOverride(events));

        input.Submit(() => events.Add("action"));

        Assert.That(events, Is.EqualTo(new[] { "X:down", "action", "X:up" }));
    }

    [Test]
    public void SubmitReleasesActionButtonWhenNativeInteractionThrows()
    {
        var events = new List<string>();
        var input = new ActionButtonInput(new FakeButtonOverride(events));

        Assert.Throws<InvalidOperationException>(() =>
            input.Submit(() => throw new InvalidOperationException("interaction failed"))
        );

        Assert.That(events, Is.EqualTo(new[] { "X:down", "X:up" }));
    }

    private sealed class FakeButtonOverride : IButtonOverride
    {
        private readonly List<string> _events;

        public FakeButtonOverride(List<string> events)
        {
            _events = events;
        }

        public void SetActionButton(bool pressed) =>
            _events.Add($"X:{(pressed ? "down" : "up")}");
    }
}
