using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class QueryPlayersModContractTests
{
    private const string CommandId = "61000000-0000-4000-8000-000000000001";

    [Test]
    public void HandlerReturnsNotReadyBeforeSaveIsLoaded()
    {
        var handler = new QueryPlayersHandler(new StubReader(false, Array.Empty<PlayerPresenceCapture>()));

        var result = handler.Execute(CommandId, Request());

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(CommandState.Failed));
            Assert.That(result.Error.Code, Is.EqualTo(ErrorCode.NotReady));
            Assert.That(result.Phase, Is.EqualTo("not_ready"));
        });
    }

    [Test]
    public void HandlerProjectsStableRosterAndOmitsOfflineLiveFacts()
    {
        var captures = new[]
        {
            Capture(
                42,
                "Sea",
                online: true,
                isHost: true,
                live: new PlayerLivePresenceCapture("Cabin-42", 5, 9, 3, 180.5, 270, true),
                savedHome: "Cabin",
                resolvedHome: "Cabin-42"
            ),
            Capture(
                7,
                "Nicole",
                online: true,
                isSelf: true,
                live: new PlayerLivePresenceCapture("Farm", 53, 14, 2, 243, 338, false),
                savedHome: "FarmHouse",
                resolvedHome: "FarmHouse"
            ),
            Capture(
                -17,
                "Robin",
                online: false,
                live: new PlayerLivePresenceCapture("Town", 1, 2, 0, 1, 2, true),
                savedHome: "Cabin",
                resolvedHome: "Cabin--17"
            ),
        };
        var handler = new QueryPlayersHandler(new StubReader(true, captures));

        var result = handler.Execute(CommandId, Request());
        var players = result.Result.QueryPlayers.Snapshot.Players;

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(CommandState.Succeeded));
            Assert.That(players.Select(player => player.PlayerId), Is.EqualTo(new[] { "7", "-17", "42" }));
            Assert.That(players[0].DisplayName, Is.EqualTo("Nicole"));
            Assert.That(players[0].Relation, Is.EqualTo(PlayerRelation.Myself));
            Assert.That(players[0].Position.LocationId, Is.EqualTo("Farm"));
            Assert.That(players[0].Facing, Is.EqualTo(Direction.Down));
            Assert.That(players[0].Energy, Is.EqualTo(243));
            Assert.That(players[0].MaxEnergy, Is.EqualTo(338));
            Assert.That(players[0].IsInBed, Is.False);
            Assert.That(players[1].Relation, Is.EqualTo(PlayerRelation.Other));
            Assert.That(players[1].Online, Is.False);
            Assert.That(players[1].Position, Is.Null);
            Assert.That(players[1].HasFacing, Is.False);
            Assert.That(players[1].HasEnergy, Is.False);
            Assert.That(players[1].HasMaxEnergy, Is.False);
            Assert.That(players[1].HasIsInBed, Is.False);
            Assert.That(players[1].HomeLocationId, Is.EqualTo("Cabin--17"));
            Assert.That(players[2].Online, Is.True);
            Assert.That(players[2].IsHost, Is.True);
            Assert.That(players[2].Facing, Is.EqualTo(Direction.Left));
        });
    }

    [Test]
    public void ProjectorRejectsMissingSelfAndDuplicateIds()
    {
        var withoutSelf = new[] { Capture(1, "Other", online: false) };
        var duplicateIds = new[]
        {
            Capture(1, "Self", online: true, isSelf: true),
            Capture(1, "Other", online: false),
        };

        Assert.Multiple(() =>
        {
            Assert.That(() => PlayerPresenceProjector.Project(withoutSelf), Throws.InvalidOperationException);
            Assert.That(() => PlayerPresenceProjector.Project(duplicateIds), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void HandlerRejectsWrongOperationAndMapsReaderFailure()
    {
        var handler = new QueryPlayersHandler(new ThrowingReader());

        var invalid = handler.Validate(new CommandRequest { QueryRuntime = new QueryRuntimeRequest() });
        var failed = handler.Execute(CommandId, Request());

        Assert.Multiple(() =>
        {
            Assert.That(invalid?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(failed.State, Is.EqualTo(CommandState.Failed));
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(failed.Phase, Is.EqualTo("player_projection_failed"));
        });
    }

    private static CommandRequest Request() => new()
    {
        QueryPlayers = new QueryPlayersRequest(),
    };

    private static PlayerPresenceCapture Capture(
        long id,
        string name,
        bool online,
        bool isHost = false,
        bool isSelf = false,
        PlayerLivePresenceCapture? live = null,
        string savedHome = "",
        string resolvedHome = ""
    ) => new(id, name, online, isHost, isSelf, live, savedHome, resolvedHome);

    private sealed class StubReader : IPlayerRosterReader
    {
        private readonly IReadOnlyList<PlayerPresenceCapture> _captures;

        public StubReader(bool isWorldReady, IReadOnlyList<PlayerPresenceCapture> captures)
        {
            IsWorldReady = isWorldReady;
            _captures = captures;
        }

        public bool IsWorldReady { get; }

        public IReadOnlyList<PlayerPresenceCapture> Capture() => _captures;
    }

    private sealed class ThrowingReader : IPlayerRosterReader
    {
        public bool IsWorldReady => true;

        public IReadOnlyList<PlayerPresenceCapture> Capture() =>
            throw new InvalidOperationException("boom");
    }
}
