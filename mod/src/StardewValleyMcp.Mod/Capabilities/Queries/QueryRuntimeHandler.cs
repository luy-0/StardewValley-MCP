using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class QueryRuntimeHandler : IImmediateCapabilityHandler
{
    public string Id => "query_runtime";

    public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.QueryRuntime;

    public Error? Validate(CommandRequest request)
    {
        return request.OperationCase == Operation
            ? null
            : new Error { Code = ErrorCode.InvalidArgument, Message = "query_runtime 请求类型无效" };
    }

    public CommandEvent Execute(string commandId, CommandRequest request)
    {
        if (!Context.IsWorldReady || Game1.player?.currentLocation is null)
            return Failed(commandId, ErrorCode.NotReady, "运行时尚未就绪", "not_ready");

        try
        {
            var player = Game1.player;
            var location = player.currentLocation;
            var weather = location.GetWeather();
            var savedHomeLocationId = player.homeLocation.Value ?? "";
            var resolvedHomeLocationId = ResolveHomeLocationId(player);
            var warnings = new List<QueryWarning>();
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
                        HomeLocationId = RuntimeProjectionPolicy.HomeLocationId(
                            savedHomeLocationId,
                            resolvedHomeLocationId
                        ),
                    },
                    Weather = new WeatherFact
                    {
                        Raining = weather?.IsRaining ?? Game1.isRaining,
                        Lightning = weather?.IsLightning ?? Game1.isLightning,
                        Snowing = weather?.IsSnowing ?? false,
                        GreenRain = weather?.IsGreenRain ?? false,
                        FestivalDay = Utility.isFestivalDay(Game1.dayOfMonth, Game1.season),
                        Kind = CurrentWeatherKind(location, weather),
                        Tomorrow = TomorrowWeatherKind(location, weather),
                    },
                    Ui = new UiSummary
                    {
                        MenuOpen = Game1.activeClickableMenu is not null,
                        MenuType = Game1.activeClickableMenu?.GetType().Name ?? "",
                    },
                    DailyLuck = new DailyLuckFact
                    {
                        Value = player.DailyLuck,
                        Tier = RuntimeProjectionPolicy.ClassifyDailyLuck(
                            player.DailyLuck,
                            player.team.sharedDailyLuck.Value
                        ),
                    },
                    QueenOfSauce = QueryQueenOfSauce(warnings),
                },
            };
            result.Warnings.AddRange(warnings);
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

    private static WeatherKind CurrentWeatherKind(GameLocation location, StardewValley.Network.LocationWeather? weather)
    {
        if (Game1.weddingToday)
            return WeatherKind.Wedding;
        if (Utility.isFestivalDay(Game1.dayOfMonth, Game1.season))
            return WeatherKind.Festival;
        if (weather?.IsGreenRain ?? Game1.isGreenRain)
            return WeatherKind.GreenRain;
        if (weather?.IsLightning ?? Game1.isLightning)
            return WeatherKind.Storm;
        if (weather?.IsSnowing ?? Game1.isSnowing)
            return WeatherKind.Snow;
        if (weather?.IsRaining ?? Game1.isRaining)
            return WeatherKind.Rain;
        if (weather?.IsDebrisWeather ?? Game1.IsDebrisWeatherHere(location))
            return WeatherKind.Wind;
        return WeatherKind.Sun;
    }

    private static WeatherKind TomorrowWeatherKind(GameLocation location, StardewValley.Network.LocationWeather? weather)
    {
        if (location.InIslandContext())
            return WeatherKindFromId(weather?.WeatherForTomorrow ?? Game1.weather_sunny);

        var tomorrow = new WorldDate(Game1.Date)
        {
            TotalDays = Game1.Date.TotalDays + 1,
        };
        var raw = Game1.IsMasterGame
            ? Game1.weatherForTomorrow
            : Game1.netWorldState.Value.WeatherForTomorrow;
        return WeatherKindFromId(Game1.getWeatherModificationsForDate(tomorrow, raw ?? Game1.weather_sunny));
    }

    private static WeatherKind WeatherKindFromId(string weatherId)
    {
        return weatherId switch
        {
            Game1.weather_rain => WeatherKind.Rain,
            Game1.weather_lightning => WeatherKind.Storm,
            Game1.weather_snow => WeatherKind.Snow,
            Game1.weather_debris => WeatherKind.Wind,
            Game1.weather_green_rain => WeatherKind.GreenRain,
            Game1.weather_festival => WeatherKind.Festival,
            Game1.weather_wedding => WeatherKind.Wedding,
            Game1.weather_sunny => WeatherKind.Sun,
            _ => WeatherKind.Unspecified,
        };
    }

    private static TvCookingRecipeFact QueryQueenOfSauce(List<QueryWarning> warnings)
    {
        var dayName = Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth);
        var available = dayName == "Sun" || (dayName == "Wed" && Game1.stats.DaysPlayed > 7);
        var result = new TvCookingRecipeFact
        {
            Available = available,
            Rerun = dayName == "Wed" && available,
        };
        if (!available)
            return result;

        try
        {
            var week = QueenOfSauceWeek(result.Rerun);
            var channelData = DataLoader.Tv_CookingChannel(Game1.temporaryContent);
            if (!channelData.TryGetValue(week.ToString(), out var raw))
                channelData.TryGetValue("1", out raw);
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            var parts = raw.Split('/');
            var recipeKey = parts[0];
            var alreadyKnown = Game1.player.cookingRecipes.ContainsKey(recipeKey);
            result.RecipeKey = recipeKey;
            result.DisplayName = new CraftingRecipe(recipeKey, isCookingRecipe: true).DisplayName;
            result.AlreadyKnown = alreadyKnown;
            result.Learnable = !alreadyKnown;
        }
        catch
        {
            warnings.Add(
                new QueryWarning
                {
                    Code = "QUEEN_OF_SAUCE_UNAVAILABLE",
                    Message = "读取今日美食节目菜谱失败",
                }
            );
        }
        return result;
    }

    private static int QueenOfSauceWeek(bool rerun)
    {
        if (rerun)
        {
            var team = Game1.player.team;
            return team.lastDayQueenOfSauceRerunUpdated.Value == Game1.Date.TotalDays
                ? team.queenOfSauceRerunWeek.Value
                : ComputeQueenOfSauceRerunWeek();
        }

        var whichWeek = (int)(Game1.stats.DaysPlayed % 224 / 7);
        return Game1.stats.DaysPlayed % 224 == 0 ? 32 : whichWeek;
    }

    private static int ComputeQueenOfSauceRerunWeek()
    {
        var totalRerunWeeksAvailable = Math.Min((int)(Game1.stats.DaysPlayed - 3) / 7, 32);
        var channelData = DataLoader.Tv_CookingChannel(Game1.temporaryContent);
        var weekToRecipeMap = new Dictionary<int, string>();
        foreach (var key in channelData.Keys)
            weekToRecipeMap[Convert.ToInt32(key)] = channelData[key].Split('/')[0];

        var recipeWeeksNotKnownByAllFarmers = new List<int>();
        for (var week = 1; week <= totalRerunWeeksAvailable; week++)
        {
            foreach (var farmer in Game1.getAllFarmers())
            {
                if (!farmer.cookingRecipes.ContainsKey(weekToRecipeMap[week]))
                {
                    recipeWeeksNotKnownByAllFarmers.Add(week);
                    break;
                }
            }
        }

        var random = Utility.CreateDaySaveRandom();
        return recipeWeeksNotKnownByAllFarmers.Count == 0
            ? Math.Max(1, 1 + random.Next(totalRerunWeeksAvailable))
            : recipeWeeksNotKnownByAllFarmers[random.Next(recipeWeeksNotKnownByAllFarmers.Count)];
    }

    private static string ResolveHomeLocationId(Farmer player)
    {
        try
        {
            return Utility.getHomeOfFarmer(player)?.NameOrUniqueName ?? "";
        }
        catch
        {
            return "";
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
