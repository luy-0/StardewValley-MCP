using StardewModdingAPI;
using StardewValley;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class QueryRuntimeHandler
{
    public static CommandEvent Execute(string commandId)
    {
        if (!Context.IsWorldReady || Game1.player?.currentLocation is null)
            return Failed(commandId, ErrorCode.NotReady, "运行时尚未就绪", "not_ready");

        try
        {
            var player = Game1.player;
            var location = player.currentLocation;
            var weather = location.GetWeather();
            var result = new QueryRuntimeResult
            {
                Snapshot = new RuntimeSnapshot
                {
                    Date = new GameDate
                    {
                        Season = Game1.currentSeason,
                        DayOfMonth = checked((uint)Game1.dayOfMonth),
                        Year = checked((uint)Game1.year),
                    },
                    TimeOfDay = checked((uint)Game1.timeOfDay),
                    Player = new PlayerFact
                    {
                        Position = new WorldPosition
                        {
                            LocationId = location.NameOrUniqueName,
                            X = player.TilePoint.X,
                            Y = player.TilePoint.Y,
                        },
                        Facing = player.FacingDirection switch
                        {
                            0 => Direction.Up,
                            1 => Direction.Right,
                            2 => Direction.Down,
                            3 => Direction.Left,
                            _ => Direction.Unspecified,
                        },
                        Money = player.Money,
                        Energy = player.Stamina,
                        MaxEnergy = player.MaxStamina,
                        Health = player.health,
                        MaxHealth = player.maxHealth,
                        CanMove = player.CanMove,
                    },
                    Weather = new WeatherFact
                    {
                        Raining = weather?.IsRaining ?? Game1.isRaining,
                        Lightning = weather?.IsLightning ?? Game1.isLightning,
                        Snowing = weather?.IsSnowing ?? false,
                        GreenRain = weather?.IsGreenRain ?? false,
                        FestivalDay = Utility.isFestivalDay(Game1.dayOfMonth, Game1.season),
                    },
                    Ui = new UiSummary
                    {
                        MenuOpen = Game1.activeClickableMenu is not null,
                        MenuType = Game1.activeClickableMenu?.GetType().Name ?? "",
                    },
                },
            };
            return new CommandEvent
            {
                CommandId = commandId,
                State = CommandState.Succeeded,
                Phase = "completed",
                ProgressPercent = 100,
                Result = new CapabilityResult { QueryRuntime = result },
            };
        }
        catch
        {
            return Failed(commandId, ErrorCode.Internal, "读取运行时状态失败", "failed");
        }
    }

    private static CommandEvent Failed(
        string commandId,
        ErrorCode code,
        string message,
        string phase
    )
    {
        return new CommandEvent
        {
            CommandId = commandId,
            State = CommandState.Failed,
            Phase = phase,
            Error = new Error { Code = code, Message = message },
        };
    }
}
