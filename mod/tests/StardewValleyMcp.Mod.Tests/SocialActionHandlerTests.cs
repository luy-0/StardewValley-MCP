using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class SocialActionHandlerTests
{
    [Test]
    public void SayValidatesUnicodeScalarsAndSendsOriginalText()
    {
        var game = new FakeSocialGame();
        var handler = new SayHandler(game);
        var content = "A😀中";
        var request = new CommandRequest { Say = new SayRequest { Content = content } };

        var result = handler.Execute(CommandId, request);

        Assert.Multiple(() =>
        {
            Assert.That(handler.Validate(request), Is.Null);
            Assert.That(game.SentChat, Is.EqualTo(content));
            Assert.That(result.State, Is.EqualTo(CommandState.Succeeded));
            Assert.That(result.Result.Say.ContentLength, Is.EqualTo(3));
        });
    }

    [TestCase("")]
    [TestCase("a\0b")]
    public void SayRejectsInvalidScalarContent(string content)
    {
        var handler = new SayHandler(new FakeSocialGame());

        var error = handler.Validate(new CommandRequest { Say = new SayRequest { Content = content } });

        Assert.That(error!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
    }

    [Test]
    public void SayScalarCounterRejectsUnpairedSurrogates()
    {
        var valid = SayHandler.TryCountScalars("A😀中", out var count);
        var invalid = SayHandler.TryCountScalars("\ud800", out _);

        Assert.Multiple(() =>
        {
            Assert.That(valid, Is.True);
            Assert.That(count, Is.EqualTo(3));
            Assert.That(invalid, Is.False);
        });
    }

    [Test]
    public void SayRejectsMoreThanFiveHundredScalars()
    {
        var handler = new SayHandler(new FakeSocialGame());

        var error = handler.Validate(new CommandRequest { Say = new SayRequest { Content = string.Concat(Enumerable.Repeat("😀", 501)) } });

        Assert.That(error!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
    }

    [Test]
    public void SayReturnsNotReadyOrExecutionFailedFromGameAdapter()
    {
        var notReady = new FakeSocialGame { IsChatReady = false };
        var rejected = new FakeSocialGame { SendSucceeds = false };

        var unavailable = new SayHandler(notReady).Execute(CommandId, SayRequest("hello"));
        var failed = new SayHandler(rejected).Execute(CommandId, SayRequest("hello"));

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.Error.Code, Is.EqualTo(ErrorCode.NotReady));
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
        });
    }

    [TestCase(EmoteKind.Happy, "happy", 32, false)]
    [TestCase(EmoteKind.Sad, "sad", 28, false)]
    [TestCase(EmoteKind.Heart, "heart", 20, false)]
    [TestCase(EmoteKind.Exclamation, "exclamation", 16, false)]
    [TestCase(EmoteKind.Question, "question", 8, false)]
    [TestCase(EmoteKind.Angry, "angry", 12, false)]
    [TestCase(EmoteKind.Sleep, "sleep", 24, false)]
    [TestCase(EmoteKind.Music, "music", 56, true)]
    [TestCase(EmoteKind.Note, "note", 56, false)]
    [TestCase(EmoteKind.Game, "game", 52, false)]
    [TestCase(EmoteKind.X, "x", 36, false)]
    [TestCase(EmoteKind.Pause, "pause", 40, false)]
    [TestCase(EmoteKind.Blush, "blush", 60, false)]
    [TestCase(EmoteKind.Yes, "yes", 56, true)]
    [TestCase(EmoteKind.No, "no", 36, true)]
    [TestCase(EmoteKind.Sick, "sick", 12, true)]
    [TestCase(EmoteKind.Laugh, "laugh", 56, true)]
    [TestCase(EmoteKind.Surprised, "surprised", 16, true)]
    [TestCase(EmoteKind.Hi, "hi", 56, true)]
    [TestCase(EmoteKind.Taunt, "taunt", 12, true)]
    [TestCase(EmoteKind.Uh, "uh", 40, true)]
    [TestCase(EmoteKind.Jar, "jar", -1, true)]
    public void EmoteMapsEveryPublicEnumAndConfirmsLocalState(EmoteKind kind, string expectedName, int expectedIndex, bool expectedAnimation)
    {
        var game = new FakeSocialGame
        {
            EmoteByName = { [expectedName] = (expectedIndex < 0 ? null : expectedIndex, expectedAnimation) },
        };
        var handler = new EmoteHandler(game);

        var result = handler.Execute(CommandId, new CommandRequest { Emote = new EmoteRequest { Emote = kind } });

        Assert.Multiple(() =>
        {
            Assert.That(game.BroadcastName, Is.EqualTo(expectedName));
            Assert.That(result.State, Is.EqualTo(CommandState.Succeeded));
            Assert.That(result.Result.Emote.Emote, Is.EqualTo(kind));
        });
    }

    [Test]
    public void EmoteRejectsUnspecifiedAndReportsBlockedOrUnconfirmedGameState()
    {
        var handler = new EmoteHandler(new FakeSocialGame());
        var invalid = handler.Validate(new CommandRequest { Emote = new EmoteRequest { Emote = EmoteKind.Unspecified } });
        var unknown = handler.Validate(new CommandRequest { Emote = new EmoteRequest { Emote = (EmoteKind)999 } });
        var blocked = new EmoteHandler(new FakeSocialGame { CanEmote = false }).Execute(CommandId, new CommandRequest { Emote = new EmoteRequest { Emote = EmoteKind.Happy } });
        var unconfirmed = new EmoteHandler(new FakeSocialGame()).Execute(CommandId, new CommandRequest { Emote = new EmoteRequest { Emote = EmoteKind.Happy } });

        Assert.Multiple(() =>
        {
            Assert.That(invalid!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(unknown!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(blocked.Error.Code, Is.EqualTo(ErrorCode.NotReady));
            Assert.That(unconfirmed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
        });
    }

    [Test]
    public void AnimationOnlyEmoteDoesNotDependOnAnOlderBubbleState()
    {
        var game = new FakeSocialGame
        {
            IsEmoting = true,
            CurrentEmote = 32,
            EmoteByName = { ["jar"] = (null, true) },
        };

        var result = new EmoteHandler(game).Execute(
            CommandId,
            new CommandRequest { Emote = new EmoteRequest { Emote = EmoteKind.Jar } }
        );

        Assert.That(result.State, Is.EqualTo(CommandState.Succeeded));
    }

    private static CommandRequest SayRequest(string content) => new() { Say = new SayRequest { Content = content } };
    private const string CommandId = "11111111-1111-4111-8111-111111111111";

    private sealed class FakeSocialGame : ISocialActionGameApi
    {
        public bool IsChatReady { get; set; } = true;
        public bool CanEmote { get; set; } = true;
        public bool IsEmoting { get; set; }
        public bool IsEmoteAnimating { get; private set; }
        public int CurrentEmote { get; set; } = -1;
        public bool SendSucceeds { get; set; } = true;
        public string? SentChat { get; private set; }
        public string? BroadcastName { get; private set; }
        public Dictionary<string, (int? IconIndex, bool Animating)> EmoteByName { get; } = new(StringComparer.Ordinal);

        public bool TrySendChat(string content)
        {
            SentChat = content;
            return SendSucceeds;
        }

        public void BroadcastEmote(string emoteName)
        {
            BroadcastName = emoteName;
            if (!EmoteByName.TryGetValue(emoteName, out var state))
                return;
            if (state.IconIndex is not null)
            {
                IsEmoting = true;
                CurrentEmote = state.IconIndex.Value;
            }
            IsEmoteAnimating = state.Animating;
        }
    }
}
