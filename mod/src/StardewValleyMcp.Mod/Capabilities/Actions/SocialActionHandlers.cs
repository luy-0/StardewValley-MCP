using StardewModdingAPI;
using StardewValley;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal interface ISocialActionGameApi
{
    bool IsChatReady { get; }
    bool CanEmote { get; }
    bool IsEmoting { get; }
    int CurrentEmote { get; }
    bool TrySendChat(string content);
    void BroadcastEmote(string emoteName);
}

internal sealed class SayHandler : IImmediateCapabilityHandler
{
    private readonly ISocialActionGameApi _game;

    public SayHandler() : this(new StardewSocialActionGameApi()) { }

    internal SayHandler(ISocialActionGameApi game) => _game = game;

    public string Id => "say";
    public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.Say;

    public Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != Operation)
            return Invalid("say 请求类型无效");
        return TryCountScalars(request.Say.Content, out var count) && count is >= 1 and <= 500
            ? null
            : Invalid("content 必须为 1..500 个 Unicode Scalar，且不得包含 NUL");
    }

    public CommandEvent Execute(string commandId, CommandRequest request)
    {
        if (!_game.IsChatReady)
            return Failed(commandId, ErrorCode.NotReady, "游戏聊天系统尚未就绪", "not_ready");
        if (!TryCountScalars(request.Say.Content, out var count) || count is < 1 or > 500)
            return Failed(commandId, ErrorCode.InvalidArgument, "content 无效", "invalid_argument");
        try
        {
            if (!_game.TrySendChat(request.Say.Content))
                return Failed(commandId, ErrorCode.ExecutionFailed, "游戏拒绝发送聊天文本", "failed");
        }
        catch
        {
            return Failed(commandId, ErrorCode.ExecutionFailed, "游戏拒绝发送聊天文本", "failed");
        }
        return Succeeded(commandId, new CapabilityResult
        {
            Say = new SayResult { ContentLength = checked((uint)count) },
        });
    }

    internal static bool TryCountScalars(string content, out int count)
    {
        count = 0;
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character == '\0' || char.IsLowSurrogate(character))
                return false;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= content.Length || !char.IsLowSurrogate(content[index + 1]))
                    return false;
                index++;
            }
            count++;
        }
        return true;
    }

    private static Error Invalid(string message) => new() { Code = ErrorCode.InvalidArgument, Message = message };
    private static CommandEvent Succeeded(string commandId, CapabilityResult result) => new()
    {
        CommandId = commandId, State = CommandState.Succeeded, Phase = "completed", ProgressPercent = 100, Result = result,
    };
    private static CommandEvent Failed(string commandId, ErrorCode code, string message, string phase) => new()
    {
        CommandId = commandId, State = CommandState.Failed, Phase = phase, Error = new Error { Code = code, Message = message },
    };
}

internal sealed class EmoteHandler : IImmediateCapabilityHandler
{
    private readonly ISocialActionGameApi _game;

    public EmoteHandler() : this(new StardewSocialActionGameApi()) { }

    internal EmoteHandler(ISocialActionGameApi game) => _game = game;

    public string Id => "emote";
    public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.Emote;

    public Error? Validate(CommandRequest request) => request.OperationCase != Operation
        ? Invalid("emote 请求类型无效")
        : TryGetEmote(request.Emote.Emote, out _) ? null : Invalid("emote 必须为受支持的公开枚举");

    public CommandEvent Execute(string commandId, CommandRequest request)
    {
        if (!TryGetEmote(request.Emote.Emote, out var emote))
            return Failed(commandId, ErrorCode.InvalidArgument, "emote 必须为受支持的公开枚举", "invalid_argument");
        if (!_game.CanEmote)
            return Failed(commandId, ErrorCode.NotReady, "当前状态不能触发表情", "not_ready");
        try
        {
            _game.BroadcastEmote(emote.Name);
        }
        catch
        {
            return Failed(commandId, ErrorCode.ExecutionFailed, "游戏拒绝触发表情", "failed");
        }
        if (!_game.IsEmoting || _game.CurrentEmote != emote.IconIndex)
            return Failed(commandId, ErrorCode.ExecutionFailed, "未观察到请求的本地表情状态", "failed");
        return Succeeded(commandId, new CapabilityResult { Emote = new EmoteResult { Emote = request.Emote.Emote } });
    }

    internal static bool TryGetEmote(EmoteKind kind, out (string Name, int IconIndex) emote)
    {
        emote = kind switch
        {
            EmoteKind.Happy => ("happy", 32),
            EmoteKind.Sad => ("sad", 28),
            EmoteKind.Heart => ("heart", 20),
            EmoteKind.Exclamation => ("exclamation", 16),
            EmoteKind.Question => ("question", 8),
            EmoteKind.Angry => ("angry", 12),
            EmoteKind.Sleep => ("sleep", 24),
            EmoteKind.Music => ("music", 56),
            _ => default,
        };
        return emote.Name is not null;
    }

    private static Error Invalid(string message) => new() { Code = ErrorCode.InvalidArgument, Message = message };
    private static CommandEvent Succeeded(string commandId, CapabilityResult result) => new()
    {
        CommandId = commandId, State = CommandState.Succeeded, Phase = "completed", ProgressPercent = 100, Result = result,
    };
    private static CommandEvent Failed(string commandId, ErrorCode code, string message, string phase) => new()
    {
        CommandId = commandId, State = CommandState.Failed, Phase = phase, Error = new Error { Code = code, Message = message },
    };
}

internal sealed class StardewSocialActionGameApi : ISocialActionGameApi
{
    public bool IsChatReady => Context.IsWorldReady && Game1.player is not null && Game1.chatBox is not null;
    public bool CanEmote => Context.IsWorldReady && Game1.player is not null && Game1.player.CanEmote();
    public bool IsEmoting => Game1.player?.IsEmoting ?? false;
    public int CurrentEmote => Game1.player?.CurrentEmote ?? -1;

    public bool TrySendChat(string content)
    {
        if (!IsChatReady)
            return false;
        var language = LocalizedContentManager.CurrentLanguageCode;
        Game1.Multiplayer.sendChatMessage(language, content, Multiplayer.AllPlayers);
        Game1.chatBox.receiveChatMessage(Game1.player.UniqueMultiplayerID, 0, language, content);
        return true;
    }

    public void BroadcastEmote(string emoteName) => Game1.player.netDoEmote(emoteName);
}
