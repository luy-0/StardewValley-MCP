using System.Globalization;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class PlayerPresenceProjector
{
    public static PlayersSnapshot Project(IEnumerable<PlayerPresenceCapture> captures)
    {
        var materialized = captures.ToList();
        if (materialized.Count == 0 || materialized.Count(item => item.IsSelf) != 1)
            throw new InvalidOperationException("玩家集合必须包含唯一的当前玩家");
        if (materialized.Select(item => item.PlayerId).Distinct().Count() != materialized.Count)
            throw new InvalidOperationException("玩家集合包含重复 ID");

        var snapshot = new PlayersSnapshot();
        snapshot.Players.AddRange(
            materialized
                .OrderBy(item => item.IsSelf ? 0 : 1)
                .ThenBy(item => item.PlayerId)
                .Select(ProjectPlayer)
        );
        return snapshot;
    }

    internal static PlayerPresenceFact ProjectPlayer(PlayerPresenceCapture capture)
    {
        var fact = new PlayerPresenceFact
        {
            PlayerId = capture.PlayerId.ToString(CultureInfo.InvariantCulture),
            DisplayName = PublicStringPolicy.IsValid(capture.DisplayName)
                ? capture.DisplayName
                : "",
            Relation = capture.IsSelf ? PlayerRelation.Myself : PlayerRelation.Other,
            Online = capture.Online,
            IsHost = capture.IsHost,
        };

        var homeLocationId = RuntimeProjectionPolicy.HomeLocationId(
            capture.SavedHomeLocationId,
            capture.ResolvedHomeLocationId
        );
        if (PublicStringPolicy.IsNonEmptyValid(homeLocationId, 128))
            fact.HomeLocationId = homeLocationId;

        if (!capture.Online || capture.Live is not { } live)
            return fact;

        if (PublicStringPolicy.IsNonEmptyValid(live.LocationId, 128)
            && live.X is { } x
            && live.Y is { } y)
        {
            fact.Position = new WorldPosition
            {
                LocationId = live.LocationId,
                X = x,
                Y = y,
            };
        }

        if (DirectionFor(live.FacingDirection) is { } direction)
            fact.Facing = direction;

        if (live.Energy is { } energy
            && live.MaxEnergy is { } maxEnergy
            && double.IsFinite(energy)
            && double.IsFinite(maxEnergy))
        {
            fact.Energy = energy;
            fact.MaxEnergy = maxEnergy;
        }

        if (live.IsInBed is { } isInBed)
            fact.IsInBed = isInBed;

        return fact;
    }

    internal static Direction? DirectionFor(int? facingDirection) => facingDirection switch
    {
        0 => Direction.Up,
        1 => Direction.Right,
        2 => Direction.Down,
        3 => Direction.Left,
        _ => null,
    };
}
