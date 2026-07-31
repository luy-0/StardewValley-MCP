using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class FaceHandlerTests
{
    [Test]
    public void FaceNoOpSucceedsWithoutSubmittingAndMarksChangedFalse()
    {
        var game = new FakeFaceGame { FacingDirection = 0 };
        var continuation = new FaceHandler(game).Start(CommandId, Request(Direction.Up));

        var step = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(game.FaceCalls, Is.Zero);
            Assert.That(continuation.CanCancel, Is.True);
            Assert.That(step.Result.Face.FinalDirection, Is.EqualTo(Direction.Up));
            Assert.That(step.Result.Face.Changed, Is.False);
        });
    }

    [Test]
    public void FaceDirectlySubmitsDirectionThenObservesChangedSuccess()
    {
        var game = new FakeFaceGame { FacingDirection = 0, ApplyImmediately = true };
        var continuation = new FaceHandler(game).Start(CommandId, Request(Direction.Left));

        var step = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(game.FaceCalls, Is.EqualTo(1));
            Assert.That(game.FacingDirection, Is.EqualTo(3));
            Assert.That(continuation.CanCancel, Is.False);
            Assert.That(step.Result.Face.FinalDirection, Is.EqualTo(Direction.Left));
            Assert.That(step.Result.Face.Changed, Is.True);
        });
    }

    [Test]
    public void FaceRemainsRunningUntilThePostconditionIsObserved()
    {
        var game = new FakeFaceGame { FacingDirection = 0 };
        var continuation = new FaceHandler(game).Start(CommandId, Request(Direction.Right));

        var pending = continuation.Tick(ContinuationStopSignal.None);
        game.FacingDirection = 1;
        var completed = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(pending, Is.TypeOf<ContinuationStep.Pending>());
            Assert.That(completed.Result.Face.Changed, Is.True);
        });
    }

    [Test]
    public void FaceStopsBeforeCommitAndReportsNotReadyWhenBlocked()
    {
        var stoppedGame = new FakeFaceGame();
        var stopped = new FaceHandler(stoppedGame).Start(CommandId, Request(Direction.Down)).Tick(ContinuationStopSignal.CancelRequested);
        var blockedGame = new FakeFaceGame { CanFace = false };
        var blocked = (ContinuationStep.Failed)new FaceHandler(blockedGame).Start(CommandId, Request(Direction.Down)).Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.TypeOf<ContinuationStep.Stopped>());
            Assert.That(stoppedGame.FaceCalls, Is.Zero);
            Assert.That(blocked.Error.Code, Is.EqualTo(ErrorCode.NotReady));
        });
    }

    [Test]
    public void FaceValidateRejectsWrongOperationAndUnspecifiedDirectionWithoutReadingGame()
    {
        var handler = new FaceHandler(new FakeFaceGame());

        var invalidDirection = handler.Validate(Request(Direction.Unspecified));
        var wrongOperation = handler.Validate(new CommandRequest { QueryRuntime = new QueryRuntimeRequest() });

        Assert.Multiple(() =>
        {
            Assert.That(invalidDirection!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(wrongOperation!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
        });
    }

    private static CommandRequest Request(Direction direction) => new() { Face = new FaceRequest { Direction = direction } };
    private const string CommandId = "22222222-2222-4222-8222-222222222222";

    private sealed class FakeFaceGame : IFaceGameApi
    {
        public bool IsReady { get; set; } = true;
        public bool CanFace { get; set; } = true;
        public int FacingDirection { get; set; }
        public bool ApplyImmediately { get; set; }
        public int FaceCalls { get; private set; }

        public void FaceDirection(int direction)
        {
            FaceCalls++;
            if (ApplyImmediately)
                FacingDirection = direction;
        }
    }
}
