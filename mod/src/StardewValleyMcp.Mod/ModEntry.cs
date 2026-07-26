using System.Net;
using System.Security.Cryptography;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace StardewValleyMcp.Mod;

public sealed class ModEntry : StardewModdingAPI.Mod
{
    private LocalServer? _server;
    private SaveAutoLoader? _saveAutoLoader;

    public override void Entry(IModHelper helper)
    {
        var config = helper.ReadConfig<ModConfig>();
        _saveAutoLoader = new SaveAutoLoader(helper, Monitor, config);
        if (string.IsNullOrWhiteSpace(config.SharedSecretBase64))
        {
            config.SharedSecretBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            helper.WriteConfig(config);
            Monitor.Log("已生成本地共享秘密，请将 Mod config.json 中的值配置给 MCP。", LogLevel.Info);
        }

        if (!IPAddress.TryParse(config.Host, out var address) || !IPAddress.IsLoopback(address))
        {
            Monitor.Log("Host 必须是 loopback IP 地址。", LogLevel.Error);
            return;
        }
        if (config.Port is < 1024 or > 65535)
        {
            Monitor.Log("Port 必须位于 1024..65535。", LogLevel.Error);
            return;
        }

        byte[] secret;
        try
        {
            secret = Convert.FromBase64String(config.SharedSecretBase64);
        }
        catch (FormatException)
        {
            Monitor.Log("SharedSecretBase64 不是有效的 Base64。", LogLevel.Error);
            return;
        }
        if (secret.Length < 32)
        {
            Monitor.Log("共享秘密解码后至少需要 32 字节。", LogLevel.Error);
            return;
        }

        var modInstanceId = Guid.NewGuid().ToString("D");
        var registry = new CapabilityRegistry(modInstanceId);
        _server = new LocalServer(address, config.Port, secret, Monitor, registry, modInstanceId);
        CryptographicOperations.ZeroMemory(secret);
        _server.Start();
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        Monitor.Log(
            $"Stardew Valley MCP 已注册能力：{string.Join(", ", registry.Snapshot.Capabilities.Select(item => item.Id))}",
            LogLevel.Info
        );
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs eventArgs)
    {
        _server?.ProcessOne();
    }
}
