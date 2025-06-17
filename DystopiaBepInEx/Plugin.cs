using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace DystopiaBepInEx;

public class ServerConfig
{
    public string ServerUrl { get; init; } = "http://localhost:5051";
}

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    private const string CONFIG_FILE_NAME = "polydystopia_server_config.json";
    private const string DEFAULT_SERVER_URL = "http://localhost:5051";

    public override void Load()
    {
        var serverUrl = LoadServerUrlFromFile();

        BuildConfigHelper.GetSelectedBuildConfig().buildServerURL = BuildServerURL.Custom;
        BuildConfigHelper.GetSelectedBuildConfig().customServerURL = serverUrl;

        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private string LoadServerUrlFromFile()
    {
        try
        {
            if (File.Exists(CONFIG_FILE_NAME))
            {
                var jsonContent = File.ReadAllText(CONFIG_FILE_NAME);
                var config = JsonSerializer.Deserialize<ServerConfig>(jsonContent);

                if (config != null && !string.IsNullOrEmpty(config.ServerUrl))
                {
                    Log.LogInfo($"Loaded server URL from {CONFIG_FILE_NAME}: {config.ServerUrl}");
                    return config.ServerUrl;
                }
            }

            var defaultConfig = new ServerConfig { ServerUrl = DEFAULT_SERVER_URL };
            var defaultJson =
                JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CONFIG_FILE_NAME, defaultJson);
            Log.LogInfo($"Created default config file {CONFIG_FILE_NAME} with URL: {DEFAULT_SERVER_URL}");

            return DEFAULT_SERVER_URL;
        }
        catch (Exception ex)
        {
            Log.LogError($"Error reading config file: {ex.Message}. Using default URL: {DEFAULT_SERVER_URL}");

            return DEFAULT_SERVER_URL;
        }
    }
}