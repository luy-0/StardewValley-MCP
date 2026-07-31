namespace StardewValleyMcp.Mod;

internal sealed class ModConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 24642;
    public string SharedSecretBase64 { get; set; } = "";
    public bool AutoLoadSave { get; set; }
    public string AutoLoadSaveName { get; set; } = "";
    public int AutoLoadTimeoutSeconds { get; set; } = 180;
}
