using HarmonyLib;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Il2CppMicrosoft.Extensions.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Microsoft.Extensions.Logging;

namespace DystopiaBepInEx;

public static class UnityDebugPatches
{
    private static ManualLogSource _logger;
    private static Harmony _harmony;
    private static bool _unityLogHooked = false;

    public static void Initialize(ManualLogSource logger, Harmony harmony)
    {
        _logger = logger;
        _harmony = harmony;
    }

    public static void ApplyPatches()
    {
        _logger.LogInfo("=== Applying Verbose Logging Patches ===");

        // 1. Hook Unity Application.logMessageReceived (MOST RELIABLE for IL2CPP)
        HookUnityLogCallback();

        // 2. Patch IL2CPP Console methods
        PatchIL2CppConsole();

        // 3. Patch custom DebugLogger (THIS IS THE KEY ONE FOR VERBOSE LOGS!)
        PatchDebugLogger();

        // 4. Scan game assemblies for custom logging
        ScanGameAssemblies();

        _logger.LogInfo("=== Verbose Logging Initialization Complete ===");
    }

    private static void HookUnityLogCallback()
    {
        try
        {
            _logger.LogInfo("[Unity] Hooking Application log handler...");

            // IL2CPP approach: Direct assignment to internal callback handler
            // This is how BepInEx's IL2CPPUnityLogSource does it
            Application.s_LogCallbackHandler = new Action<string, string, LogType>(OnUnityLogMessageReceived);
            _unityLogHooked = true;

            _logger.LogInfo("[Unity] ✓ Successfully hooked Unity log callback");
            _logger.LogInfo("[Unity]   This will capture ALL Debug.Log, Debug.LogWarning, Debug.LogError calls");
        }
        catch (Exception ex)
        {
            _logger.LogError($"[Unity] ✗ Failed to hook log callback: {ex.Message}");
            _logger.LogError($"[Unity] Stack trace: {ex.StackTrace}");
        }
    }

    private static void OnUnityLogMessageReceived(string logString, string stackTrace, LogType logType)
    {
        if (!_unityLogHooked || _logger == null)
            return;

        try
        {
            // Prefix based on log type
            string prefix = logType switch
            {
                LogType.Error => "[GAME ERROR]",
                LogType.Assert => "[GAME ASSERT]",
                LogType.Warning => "[GAME WARNING]",
                LogType.Log => "[GAME LOG]",
                LogType.Exception => "[GAME EXCEPTION]",
                _ => "[GAME]"
            };

            // Log the message
            switch (logType)
            {
                case LogType.Error:
                case LogType.Exception:
                    _logger.LogError($"{prefix} {logString}");
                    if (!string.IsNullOrEmpty(stackTrace))
                    {
                        _logger.LogError($"Stack Trace:\n{stackTrace}");
                    }
                    break;
                case LogType.Warning:
                    _logger.LogWarning($"{prefix} {logString}");
                    break;
                default:
                    _logger.LogInfo($"{prefix} {logString}");
                    break;
            }
        }
        catch
        {
            // Silently fail to avoid breaking the game
        }
    }

    private static void PatchIL2CppConsole()
    {
        try
        {
            _logger.LogInfo("[Console] Searching for IL2CPP Console methods...");

            // Try to find Il2CppSystem.Console
            Type consoleType = FindType("Il2CppSystem.Console");

            if (consoleType != null)
            {
                _logger.LogInfo($"[Console] Found Il2CppSystem.Console in: {consoleType.Assembly.GetName().Name}");

                // Patch IL2CPP Console methods
                PatchMethod(consoleType, "WriteLine", nameof(ConsoleWriteLinePrefix), new[] { typeof(string) });
                PatchMethod(consoleType, "Write", nameof(ConsoleWritePrefix), new[] { typeof(string) });

                _logger.LogInfo("[Console] ✓ IL2CPP Console patches applied");
            }
            else
            {
                _logger.LogInfo("[Console] Il2CppSystem.Console not found (game may not use console output)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[Console] Patch error: {ex.Message}");
        }
    }

    private static void ScanGameAssemblies()
    {
        try
        {
            _logger.LogInfo("[GameAssemblies] Scanning for custom logging methods...");

            string[] gameAssemblyNames = { "PolytopiaAssembly", "GameLogicAssembly", "PolytopiaBackendBase" };
            int patchedCount = 0;

            foreach (var asmName in gameAssemblyNames)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == asmName);

                if (assembly != null)
                {
                    _logger.LogInfo($"[GameAssemblies] Scanning {asmName}...");

                    // Look for types with "Log", "Logger", "Debug" in their name
                    var loggerTypes = assembly.GetTypes()
                        .Where(t => t.Name.Contains("Log") || t.Name.Contains("Debug") || t.Name.Contains("Trace"))
                        .ToArray();

                    foreach (var type in loggerTypes)
                    {
                        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                            .Where(m => m.Name.Contains("Log") || m.Name.Contains("Write") || m.Name.Contains("Print"))
                            .ToArray();

                        foreach (var method in methods)
                        {
                            try
                            {
                                _harmony.Patch(method, prefix: new HarmonyMethod(typeof(UnityDebugPatches), nameof(GameMethodPrefix)));
                                _logger.LogInfo($"[GameAssemblies]   ✓ Patched {type.Name}.{method.Name}");
                                patchedCount++;
                            }
                            catch
                            {
                                // Skip methods that can't be patched
                            }
                        }
                    }
                }
            }

            if (patchedCount > 0)
            {
                _logger.LogInfo($"[GameAssemblies] ✓ Patched {patchedCount} game-specific logging methods");
            }
            else
            {
                _logger.LogInfo("[GameAssemblies] No game-specific logging methods found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[GameAssemblies] Scan error: {ex.Message}");
        }
    }

    private static void PatchDebugLogger()
    {
        try
        {
            _logger.LogInfo("[DebugLogger] Patching custom DebugLogger.Log static methods...");

            // Find the Log type from DebugLogger assembly using reflection
            Type logType = FindType("Log");

            if (logType == null)
            {
                _logger.LogWarning("[DebugLogger] ✗ Could not find Log type in any loaded assembly");
                return;
            }

            _logger.LogInfo($"[DebugLogger] Found Log type: {logType.FullName ?? logType.Name} in {logType.Assembly.GetName().Name}");

            int patchedCount = 0;

            // Get all static public methods
            var allMethods = logType.GetMethods(BindingFlags.Public | BindingFlags.Static);

            // Patch ALL Log.Spam overloads
            foreach (var method in allMethods.Where(m => m.Name == "Spam"))
            {
                try
                {
                    _harmony.Patch(method, prefix: new HarmonyMethod(typeof(UnityDebugPatches), nameof(LogSpamPrefix)));
                    _logger.LogInfo($"[DebugLogger]   ✓ Patched Log.Spam ({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                    patchedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[DebugLogger]   ✗ Failed to patch Log.Spam: {ex.Message}");
                }
            }

            // Patch ALL Log.Verbose overloads - KEY METHOD!
            foreach (var method in allMethods.Where(m => m.Name == "Verbose"))
            {
                try
                {
                    _harmony.Patch(method, prefix: new HarmonyMethod(typeof(UnityDebugPatches), nameof(LogVerbosePrefix)));
                    _logger.LogInfo($"[DebugLogger]   ✓ Patched Log.Verbose ({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))}) [KEY!]");
                    patchedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[DebugLogger]   ✗ Failed to patch Log.Verbose: {ex.Message}");
                }
            }

            // Patch ALL Log.Info overloads
            foreach (var method in allMethods.Where(m => m.Name == "Info"))
            {
                try
                {
                    _harmony.Patch(method, prefix: new HarmonyMethod(typeof(UnityDebugPatches), nameof(LogInfoPrefix)));
                    _logger.LogInfo($"[DebugLogger]   ✓ Patched Log.Info ({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                    patchedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[DebugLogger]   ✗ Failed to patch Log.Info: {ex.Message}");
                }
            }

            // Patch ALL Log.Warning overloads
            foreach (var method in allMethods.Where(m => m.Name == "Warning"))
            {
                try
                {
                    _harmony.Patch(method, prefix: new HarmonyMethod(typeof(UnityDebugPatches), nameof(LogWarningPrefix)));
                    _logger.LogInfo($"[DebugLogger]   ✓ Patched Log.Warning ({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                    patchedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[DebugLogger]   ✗ Failed to patch Log.Warning: {ex.Message}");
                }
            }

            // Patch ALL Log.Error overloads
            foreach (var method in allMethods.Where(m => m.Name == "Error"))
            {
                try
                {
                    _harmony.Patch(method, prefix: new HarmonyMethod(typeof(UnityDebugPatches), nameof(LogErrorPrefix)));
                    _logger.LogInfo($"[DebugLogger]   ✓ Patched Log.Error ({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                    patchedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[DebugLogger]   ✗ Failed to patch Log.Error: {ex.Message}");
                }
            }

            // Patch ALL Log.Exception overloads
            foreach (var method in allMethods.Where(m => m.Name == "Exception"))
            {
                try
                {
                    _harmony.Patch(method, prefix: new HarmonyMethod(typeof(UnityDebugPatches), nameof(LogExceptionPrefix)));
                    _logger.LogInfo($"[DebugLogger]   ✓ Patched Log.Exception ({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                    patchedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[DebugLogger]   ✗ Failed to patch Log.Exception: {ex.Message}");
                }
            }

            if (patchedCount > 0)
            {
                _logger.LogInfo($"[DebugLogger] ✓ Successfully patched {patchedCount} DebugLogger methods");
                _logger.LogInfo("[DebugLogger]   All verbose game logs will now be captured!");
            }
            else
            {
                _logger.LogWarning("[DebugLogger] ✗ No DebugLogger methods could be patched");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[DebugLogger] Patch error: {ex.Message}");
            _logger.LogError($"[DebugLogger] Stack trace: {ex.StackTrace}");
        }
    }

    private static Type FindType(string fullTypeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(fullTypeName);
                if (type != null)
                    return type;
            }
            catch { /* Skip assemblies that can't be searched */ }
        }
        return null;
    }

    // Helper method for patching
    private static void PatchMethod(Type targetType, string methodName, string patchMethodName, Type[] parameterTypes)
    {
        try
        {
            var originalMethod = targetType.GetMethod(methodName, parameterTypes);
            if (originalMethod != null)
            {
                var patchMethod = typeof(UnityDebugPatches).GetMethod(patchMethodName, BindingFlags.Static | BindingFlags.NonPublic);
                if (patchMethod != null)
                {
                    _harmony.Patch(originalMethod, prefix: new HarmonyMethod(patchMethod));
                    _logger.LogInfo($"  ✓ Patched {targetType.Name}.{methodName}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"  ✗ Could not patch {targetType.Name}.{methodName}: {ex.Message}");
        }
    }

    // === PREFIX HANDLERS FOR PATCHED METHODS ===

    // Helper method to convert IL2CPP object to its actual string value
    private static string Il2CppObjectToString(object obj)
    {
        if (obj == null)
            return "null";

        try
        {
            // Handle Il2CppObjectBase (base class for all IL2CPP interop objects)
            if (obj is Il2CppObjectBase il2cppBase)
            {
                // Try to unbox primitive types
                var il2cppType = il2cppBase.GetType();

                // Check if it's a boxed primitive in Il2CppSystem.Object
                if (obj is Il2CppSystem.Object il2cppSysObj)
                {
                    // Try to unbox common types
                    try
                    {
                        // Get the actual IL2CPP class to check the type
                        var il2cppClass = Il2CppClassPointerStore.GetNativeClassPointer(il2cppType);

                        // Try common unboxing
                        try { return il2cppSysObj.Unbox<int>().ToString(); } catch { }
                        try { return il2cppSysObj.Unbox<float>().ToString(); } catch { }
                        try { return il2cppSysObj.Unbox<double>().ToString(); } catch { }
                        try { return il2cppSysObj.Unbox<bool>().ToString(); } catch { }
                        try { return il2cppSysObj.Unbox<long>().ToString(); } catch { }

                        // If it's a string
                        if (il2cppSysObj is Il2CppSystem.String il2cppStr)
                        {
                            return il2cppStr.ToString();
                        }
                    }
                    catch { }

                    // Fallback: call IL2CPP's ToString
                    return il2cppSysObj.ToString();
                }

                // For other Il2CppObjectBase types, just call ToString
                return il2cppBase.ToString();
            }

            // For managed types, just use normal ToString
            return obj.ToString() ?? "null";
        }
        catch (Exception ex)
        {
            return $"<error:{ex.Message}>";
        }
    }

    // Helper method to unpack IL2CPP arrays and format log messages
    private static string FormatLogMessage(string format, object[] args)
    {
        if (args == null || args.Length == 0)
            return format;

        try
        {
            // Extract the actual format arguments
            var formatArgs = new List<object>();

            foreach (var arg in args)
            {
                if (arg == null)
                {
                    formatArgs.Add("null");
                    continue;
                }

                var argType = arg.GetType();

                // Check if it's an Il2CppReferenceArray (the params object[] from IL2CPP)
                if (arg is Il2CppReferenceArray<Il2CppSystem.Object> il2cppArray)
                {
                    for (int i = 0; i < il2cppArray.Count; i++)
                    {
                        formatArgs.Add(Il2CppObjectToString(il2cppArray[i]));
                    }
                }
                else if (argType.IsGenericType && argType.Name.Contains("Il2CppReferenceArray"))
                {
                    // Fallback for other Il2CppReferenceArray types
                    var countProp = argType.GetProperty("Count") ?? argType.GetProperty("Length");
                    if (countProp != null)
                    {
                        int count = (int)countProp.GetValue(arg);
                        var indexer = argType.GetProperty("Item");

                        for (int i = 0; i < count; i++)
                        {
                            var element = indexer?.GetValue(arg, new object[] { i });
                            formatArgs.Add(Il2CppObjectToString(element));
                        }
                    }
                    else
                    {
                        formatArgs.Add(Il2CppObjectToString(arg));
                    }
                }
                else
                {
                    formatArgs.Add(Il2CppObjectToString(arg));
                }
            }

            // Try to format the string with the extracted arguments
            if (formatArgs.Count > 0 && format.Contains("{0}"))
            {
                try
                {
                    return string.Format(format, formatArgs.ToArray());
                }
                catch
                {
                    // If formatting fails, just append the args
                    return $"{format} [{string.Join(", ", formatArgs)}]";
                }
            }

            return format;
        }
        catch
        {
            return format;
        }
    }

    // Console prefix
    private static void ConsoleWriteLinePrefix(string value)
    {
        if (_logger != null && !string.IsNullOrEmpty(value))
        {
            _logger.LogInfo($"[CONSOLE] {value}");
        }
    }

    private static void ConsoleWritePrefix(string value)
    {
        if (_logger != null && !string.IsNullOrEmpty(value))
        {
            _logger.LogInfo($"[CONSOLE] {value}");
        }
    }

    // DebugLogger.Log prefix handlers
    // These methods intercept IL2CPP Log calls and forward them to BepInEx logger

    private static void LogSpamPrefix(params object[] __args)
    {
        try
        {
            if (_logger == null || __args == null || __args.Length == 0) return;

            string format = __args[0]?.ToString() ?? "";
            string message = FormatLogMessage(format, __args.Skip(1).ToArray());
            _logger.LogInfo($"[SPAM] {message}");
        }
        catch { /* Silently fail */ }
    }

    private static void LogVerbosePrefix(params object[] __args)
    {
        try
        {
            if (_logger == null || __args == null || __args.Length == 0) return;

            string format = __args[0]?.ToString() ?? "";
            string message = FormatLogMessage(format, __args.Skip(1).ToArray());
            _logger.LogInfo($"[VERBOSE] {message}");
        }
        catch { /* Silently fail */ }
    }

    private static void LogInfoPrefix(params object[] __args)
    {
        try
        {
            if (_logger == null || __args == null || __args.Length == 0) return;

            string format = __args[0]?.ToString() ?? "";
            string message = FormatLogMessage(format, __args.Skip(1).ToArray());
            _logger.LogInfo($"[INFO] {message}");
        }
        catch { /* Silently fail */ }
    }

    private static void LogWarningPrefix(params object[] __args)
    {
        try
        {
            if (_logger == null || __args == null || __args.Length == 0) return;

            string format = __args[0]?.ToString() ?? "";
            string message = FormatLogMessage(format, __args.Skip(1).ToArray());
            _logger.LogWarning($"[WARNING] {message}");
        }
        catch { /* Silently fail */ }
    }

    private static void LogErrorPrefix(params object[] __args)
    {
        try
        {
            if (_logger == null || __args == null || __args.Length == 0) return;

            string format = __args[0]?.ToString() ?? "";
            string message = FormatLogMessage(format, __args.Skip(1).ToArray());
            _logger.LogError($"[ERROR] {message}");
        }
        catch { /* Silently fail */ }
    }

    private static void LogExceptionPrefix(params object[] __args)
    {
        try
        {
            if (_logger == null || __args == null || __args.Length == 0) return;

            var exception = __args[0] as Exception;
            if (exception != null)
            {
                _logger.LogError($"[EXCEPTION] {exception.Message}");
                _logger.LogError($"Stack trace:\n{exception.StackTrace}");
            }
        }
        catch { /* Silently fail */ }
    }

    // Generic game method prefix
    private static bool GameMethodPrefix(params object[] __args)
    {
        try
        {
            if (_logger != null && __args != null && __args.Length > 0)
            {
                string format = __args[0]?.ToString() ?? "";
                string message = FormatLogMessage(format, __args.Skip(1).ToArray());
                if (!string.IsNullOrEmpty(message))
                {
                    _logger.LogInfo($"[GAME METHOD] {message}");
                }
            }
        }
        catch
        {
            // Silently fail
        }

        return true; // Always continue execution
    }
}