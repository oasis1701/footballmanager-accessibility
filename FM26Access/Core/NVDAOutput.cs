using System;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx.Logging;

namespace FM26Access.Core;

/// <summary>
/// Direct interface to NVDA screen reader using nvdaControllerClient.
/// This DLL is included with NVDA and provides speech/braille output.
/// </summary>
public static class NVDAOutput
{
    private static bool _initialized;
    private static bool _nvdaAvailable;
    private static ManualLogSource _log;

    #region NVDA Controller Client Native Imports
    // The nvdaControllerClient64.dll is located in NVDA's installation folder
    // We'll try multiple locations to find it

    [DllImport("nvdaControllerClient64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int nvdaController_speakText([MarshalAs(UnmanagedType.LPWStr)] string text);

    [DllImport("nvdaControllerClient64.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int nvdaController_cancelSpeech();

    [DllImport("nvdaControllerClient64.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int nvdaController_brailleMessage([MarshalAs(UnmanagedType.LPWStr)] string message);

    [DllImport("nvdaControllerClient64.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int nvdaController_testIfRunning();

    // Kernel32 for loading DLL from specific path
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    #endregion

    /// <summary>
    /// Initialize connection to NVDA.
    /// </summary>
    public static bool Initialize()
    {
        _log = Plugin.Log;

        try
        {
            // Try to find and load nvdaControllerClient64.dll
            var nvdaDllFound = TryLoadNvdaController();

            if (!nvdaDllFound)
            {
                _log.LogWarning("NVDA controller client not found");
                _log.LogInfo("Please ensure NVDA is installed, or copy nvdaControllerClient64.dll to the plugin folder");
                return false;
            }

            // Test if NVDA is actually running
            var result = nvdaController_testIfRunning();
            _nvdaAvailable = (result == 0);

            if (_nvdaAvailable)
            {
                _log.LogInfo("NVDA detected and ready");
                _initialized = true;
            }
            else
            {
                _log.LogWarning($"NVDA not running (error code: {result})");
                _log.LogInfo("Start NVDA to enable speech output");
            }

            return _nvdaAvailable;
        }
        catch (DllNotFoundException ex)
        {
            _log.LogError($"NVDA controller DLL not found: {ex.Message}");
            _log.LogInfo("Please ensure NVDA is installed");
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError($"Failed to initialize NVDA connection: {ex}");
            return false;
        }
    }

    private static bool TryLoadNvdaController()
    {
        // List of places to look for the NVDA controller client
        var searchPaths = new[]
        {
            // Plugin folder (user can copy it here)
            Path.GetDirectoryName(typeof(NVDAOutput).Assembly.Location) ?? "",
            // Standard NVDA installation paths
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVDA"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVDA"),
            // NVDA portable location (common)
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NVDA"),
            // User's AppData
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NVDA"),
        };

        foreach (var searchPath in searchPaths)
        {
            if (string.IsNullOrEmpty(searchPath)) continue;

            var dllPath = Path.Combine(searchPath, "nvdaControllerClient64.dll");
            if (File.Exists(dllPath))
            {
                _log.LogInfo($"Found NVDA controller at: {dllPath}");

                // Add the directory to DLL search path
                SetDllDirectory(searchPath);

                // Try to load the DLL
                var handle = LoadLibrary(dllPath);
                if (handle != IntPtr.Zero)
                {
                    _log.LogInfo("NVDA controller loaded successfully");
                    return true;
                }
                else
                {
                    var error = Marshal.GetLastWin32Error();
                    _log.LogWarning($"Failed to load DLL (error {error})");
                }
            }
        }

        // Also check if it's already loadable (e.g., in PATH)
        try
        {
            var testResult = nvdaController_testIfRunning();
            _log.LogInfo("NVDA controller already available in system");
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Speak text through NVDA, interrupting any current speech.
    /// </summary>
    public static bool Speak(string text)
    {
        if (!_initialized || string.IsNullOrEmpty(text))
            return false;

        try
        {
            // Check if NVDA is still running
            if (nvdaController_testIfRunning() != 0)
            {
                _nvdaAvailable = false;
                return false;
            }

            // Cancel current speech then speak new text
            nvdaController_cancelSpeech();
            var result = nvdaController_speakText(text);
            if (_log != null)
                _log.LogDebug($"Speaking: {text}");
            return result == 0;
        }
        catch (Exception ex)
        {
            if (_log != null)
                _log.LogError($"Speech failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Speak text through NVDA without interrupting current speech.
    /// </summary>
    public static bool SpeakAppend(string text)
    {
        if (!_initialized || string.IsNullOrEmpty(text))
            return false;

        try
        {
            if (nvdaController_testIfRunning() != 0)
            {
                _nvdaAvailable = false;
                return false;
            }

            var result = nvdaController_speakText(text);
            return result == 0;
        }
        catch (Exception ex)
        {
            if (_log != null)
                _log.LogError($"Speech append failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Output to both speech and braille.
    /// </summary>
    public static bool Output(string text, bool interrupt = true)
    {
        if (!_initialized || string.IsNullOrEmpty(text))
            return false;

        try
        {
            if (interrupt)
                nvdaController_cancelSpeech();

            var speechResult = nvdaController_speakText(text);
            var brailleResult = nvdaController_brailleMessage(text);

            return speechResult == 0 || brailleResult == 0;
        }
        catch (Exception ex)
        {
            if (_log != null)
                _log.LogError($"Output failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Send text to braille display only.
    /// </summary>
    public static bool Braille(string text)
    {
        if (!_initialized || string.IsNullOrEmpty(text))
            return false;

        try
        {
            var result = nvdaController_brailleMessage(text);
            return result == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stop any current speech.
    /// </summary>
    public static bool Silence()
    {
        if (!_initialized)
            return false;

        try
        {
            var result = nvdaController_cancelSpeech();
            return result == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if NVDA is currently running and available.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            if (!_initialized) return false;
            try
            {
                return nvdaController_testIfRunning() == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Cleanup resources.
    /// </summary>
    public static void Shutdown()
    {
        if (_initialized)
        {
            try
            {
                nvdaController_cancelSpeech();
            }
            catch
            {
                // Ignore errors during shutdown
            }
            _initialized = false;
            _nvdaAvailable = false;
        }
    }
}
