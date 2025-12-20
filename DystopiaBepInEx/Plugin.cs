using System.Net.Http;
using System.Text.Json;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace DystopiaBepInEx;

public class ServerConfig
{
    public string ServerUrl { get; init; } = "https://dev.polydystopia.xyz";
    public bool VerboseLogging { get; init; } = false;
}

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    private const string CONFIG_FILE_NAME = "polydystopia_server_config.json";
    private const string DEFAULT_SERVER_URL = "https://dev.polydystopia.xyz";
    private Harmony _harmony = null!;

    internal static string ServerUrl { get; private set; } = DEFAULT_SERVER_URL;
    internal static ManualLogSource Logger { get; private set; } = null!;

    public override void Load()
    {
        Logger = Log;
        var config = LoadConfigFromFile();
        ServerUrl = config.ServerUrl;

        BuildConfigHelper.GetSelectedBuildConfig().buildServerURL = BuildServerURL.Custom;
        BuildConfigHelper.GetSelectedBuildConfig().customServerURL = config.ServerUrl;

        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

        _harmony.PatchAll(typeof(ModGldPatches));
        Log.LogInfo("Mod GLD patches applied");

        if (config.VerboseLogging)
        {
            Log.LogInfo("Verbose logging is ENABLED. Initializing debug patches...");

            UnityDebugPatches.Initialize(Log, _harmony);
            UnityDebugPatches.ApplyPatches();

            Log.LogInfo("Game message logging is now active. All game logs will be captured to BepInEx console.");
        }
        else
        {
            Log.LogInfo("Verbose logging is DISABLED. Set 'VerboseLogging: true' in config to enable.");
        }

        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private ServerConfig LoadConfigFromFile()
    {
        try
        {
            if (File.Exists(CONFIG_FILE_NAME))
            {
                var jsonContent = File.ReadAllText(CONFIG_FILE_NAME);
                var config = JsonSerializer.Deserialize<ServerConfig>(jsonContent);

                if (config != null && !string.IsNullOrEmpty(config.ServerUrl))
                {
                    Log.LogInfo($"Loaded config from {CONFIG_FILE_NAME}:");
                    Log.LogInfo($"- Server URL: {config.ServerUrl}");
                    Log.LogInfo($"- Verbose Logging: {config.VerboseLogging}");
                    return config;
                }
            }

            var defaultConfig = new ServerConfig { ServerUrl = DEFAULT_SERVER_URL, VerboseLogging = true };
            var defaultJson =
                JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CONFIG_FILE_NAME, defaultJson);
            Log.LogInfo($"Created default config file {CONFIG_FILE_NAME}");
            Log.LogInfo($"- Server URL: {DEFAULT_SERVER_URL}");
            Log.LogInfo($"- Verbose Logging: disabled");

            return defaultConfig;
        }
        catch (Exception ex)
        {
            Log.LogError($"Error reading config file: {ex.Message}. Using defaults.");
            return new ServerConfig { ServerUrl = DEFAULT_SERVER_URL, VerboseLogging = true };
        }
    }
}