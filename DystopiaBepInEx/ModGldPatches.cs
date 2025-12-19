using System.Collections.Generic;
using System.Net.Http;
using HarmonyLib;
using Polytopia.Data;
using Il2CppSystem.IO;
using BinaryReader = Il2CppSystem.IO.BinaryReader;
using EndOfStreamException = System.IO.EndOfStreamException;

namespace DystopiaBepInEx;

/// <summary>
/// Patches to support custom GameLogicData (GLD) loading.
///
/// The server embeds a GLD marker + ModGldVersion (int) in the serialized game state.
/// When deserializing, we read the trailing version ID, fetch the GLD, and set mockedGameLogicData.
/// ModGldVersion starts at 1000 for mods.
/// </summary>
public static class ModGldPatches
{
    private const string GldMarker = "##GLD:";

    // Cache parsed GLD by game Seed to handle rewinds/reloads
    private static readonly Dictionary<int, GameLogicData> _gldCache = new();
    private static readonly Dictionary<int, int> _versionCache = new(); // Seed → modGldVersion

    /// <summary>
    /// After GameState deserialization, check for trailing GLD version ID and set mockedGameLogicData.
    /// The server appends "##GLD:" + modGldVersion (int) after the normal serialized data.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameState), nameof(GameState.Deserialize))]
    private static void Deserialize_Postfix(GameState __instance, BinaryReader __0)
    {
        Plugin.Logger?.LogDebug("Deserialize_Postfix: Entered");

        try
        {
            var reader = __0;
            if (reader == null)
            {
                Plugin.Logger?.LogWarning("Deserialize_Postfix: reader is null");
                return;
            }

            var position = reader.BaseStream.Position;
            var length = reader.BaseStream.Length;
            var remaining = length - position;

            Plugin.Logger?.LogDebug($"Deserialize_Postfix: Stream position={position}, length={length}, remaining={remaining}");

            // Check if there's more data after normal deserialization
            if (position >= length)
            {
                Plugin.Logger?.LogDebug("Deserialize_Postfix: No trailing data (position >= length)");

                var sd = __instance.Seed;
                if (_gldCache.TryGetValue(sd, out var cachedGld))
                {
                    __instance.mockedGameLogicData = cachedGld;
                    var cachedVersion = _versionCache.GetValueOrDefault(sd, -1);
                    Plugin.Logger?.LogInfo($"Deserialize_Postfix: Applied cached GLD for Seed={sd}, ModGldVersion={cachedVersion}");
                }
                return;
            }

            Plugin.Logger?.LogDebug($"Deserialize_Postfix: Found {remaining} bytes of trailing data, attempting to read marker");

            var marker = reader.ReadString();
            Plugin.Logger?.LogDebug($"Deserialize_Postfix: Read marker string: '{marker}'");

            if (marker != GldMarker)
            {
                Plugin.Logger?.LogDebug($"Deserialize_Postfix: Marker mismatch - expected '{GldMarker}', got '{marker}'");
                return;
            }

            Plugin.Logger?.LogInfo($"Deserialize_Postfix: Found GLD marker '{GldMarker}'");

            var modGldVersion = reader.ReadInt32();
            Plugin.Logger?.LogInfo($"Deserialize_Postfix: Found embedded ModGldVersion: {modGldVersion}");

            Plugin.Logger?.LogDebug($"Deserialize_Postfix: Fetching GLD from server for version {modGldVersion}");
            var gldJson = FetchGldById(modGldVersion);
            if (string.IsNullOrEmpty(gldJson))
            {
                Plugin.Logger?.LogError($"Deserialize_Postfix: Failed to fetch GLD for ModGldVersion: {modGldVersion}");
                return;
            }

            Plugin.Logger?.LogDebug($"Deserialize_Postfix: Parsing GLD JSON ({gldJson.Length} chars)");

            var customGld = new GameLogicData();
            customGld.Parse(gldJson);
            __instance.mockedGameLogicData = customGld;

            // Cache for subsequent deserializations (rewinds, reloads)
            var seed = __instance.Seed;
            _gldCache[seed] = customGld;
            _versionCache[seed] = modGldVersion;

            Plugin.Logger?.LogInfo($"Deserialize_Postfix: Successfully set mockedGameLogicData from ModGldVersion: {modGldVersion}, cached for Seed={seed}");
        }
        catch (EndOfStreamException)
        {
            Plugin.Logger?.LogDebug("Deserialize_Postfix: EndOfStreamException - no trailing data");
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError($"Deserialize_Postfix: Exception: {ex.GetType().Name}: {ex.Message}");
            Plugin.Logger?.LogDebug($"Deserialize_Postfix: Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Fetch GLD from server using ModGldVersion ID
    /// </summary>
    private static string FetchGldById(int modGldVersion)
    {
        try
        {
            using var client = new HttpClient();
            var url = $"{Plugin.ServerUrl.TrimEnd('/')}/api/mods/gld/{modGldVersion}";
            Plugin.Logger?.LogDebug($"FetchGldById: Requesting URL: {url}");

            var response = client.GetAsync(url).Result;
            Plugin.Logger?.LogDebug($"FetchGldById: Response status: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var gld = response.Content.ReadAsStringAsync().Result;
                Plugin.Logger?.LogInfo($"FetchGldById: Successfully fetched mod GLD ({gld.Length} chars)");
                return gld;
            }

            var errorContent = response.Content.ReadAsStringAsync().Result;
            Plugin.Logger?.LogError($"FetchGldById: Failed with status {response.StatusCode}: {errorContent}");
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError($"FetchGldById: Exception: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
            {
                Plugin.Logger?.LogError($"FetchGldById: Inner exception: {ex.InnerException.Message}");
            }
        }
        return null;
    }
}
