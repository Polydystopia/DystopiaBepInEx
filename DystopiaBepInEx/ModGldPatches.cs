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
/// New approach: The server embeds a GLD hash in the serialized game state (after the normal data).
/// When deserializing, we read the trailing hash, fetch the GLD, and set mockedGameLogicData.
///
/// Legacy support: Versions >= 1000 still work for backwards compatibility.
/// </summary>
public static class ModGldPatches
{
    private const string GldMarker = "##GLD:";

    /// <summary>
    /// After GameState deserialization, check for trailing GLD hash and set mockedGameLogicData.
    /// The server appends "##GLD:" + hash after the normal serialized data.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameState), nameof(GameState.Deserialize))]
    private static void Deserialize_Postfix(GameState __instance, BinaryReader __0)
    {
        try
        {
            var reader = __0;
            if (reader == null)
            {
                Plugin.Logger?.LogDebug("Deserialize_Postfix: reader is null");
                return;
            }

            // Check if there's more data after normal deserialization
            if (reader.BaseStream.Position >= reader.BaseStream.Length)
            {
                return; // No trailing data
            }

            // Try to read the marker
            var marker = reader.ReadString();
            if (marker != GldMarker)
            {
                Plugin.Logger?.LogDebug($"No GLD marker found (got: {marker})");
                return;
            }

            // Read the hash
            var hash = reader.ReadString();
            Plugin.Logger?.LogInfo($"Found embedded GLD hash: {hash}");

            // Fetch GLD from server
            var gldJson = FetchGldByHash(hash);
            if (string.IsNullOrEmpty(gldJson))
            {
                Plugin.Logger?.LogError($"Failed to fetch GLD for hash: {hash}");
                return;
            }

            // Parse and set as mockedGameLogicData
            var customGld = new GameLogicData();
            customGld.Parse(gldJson);
            __instance.mockedGameLogicData = customGld;

            Plugin.Logger?.LogInfo($"Successfully set mockedGameLogicData from hash: {hash}");
        }
        catch (EndOfStreamException)
        {
            // Normal case - no trailing data
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug($"Error reading trailing GLD hash: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetch GLD from server using hash
    /// </summary>
    private static string FetchGldByHash(string gldHash)
    {
        try
        {
            using var client = new HttpClient();
            var url = $"{Plugin.ServerUrl.TrimEnd('/')}/api/mods/gld/{gldHash}";
            var response = client.GetAsync(url).Result;
            if (response.IsSuccessStatusCode)
            {
                var gld = response.Content.ReadAsStringAsync().Result;
                Plugin.Logger?.LogInfo($"Successfully fetched mod GLD by hash ({gld.Length} chars)");
                return gld;
            }
            Plugin.Logger?.LogError($"Failed to fetch mod GLD by hash: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError($"Error fetching mod GLD by hash: {ex.Message}");
        }
        return null;
    }
}
