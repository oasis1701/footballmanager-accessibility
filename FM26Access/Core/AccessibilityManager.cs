using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Il2CppInterop.Runtime.Attributes;
using FM26Access.UI;
using FM26Access.Navigation;

namespace FM26Access.Core;

/// <summary>
/// Main accessibility manager that coordinates all accessibility features.
/// Attached to a persistent GameObject that survives scene loads.
/// </summary>
public class AccessibilityManager : MonoBehaviour
{
    // IL2CPP requires this constructor for injected types
    public AccessibilityManager(IntPtr ptr) : base(ptr) { }

    public static AccessibilityManager Instance { get; private set; }

    private float _lastAnnouncementTime;
    private string _currentScreen = "";
    private bool _navigationInitialized = false;

    // Unity lifecycle methods must NOT have [HideFromIl2Cpp]
    public void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        Plugin.Log.LogInfo("AccessibilityManager initialized");
    }

    public void Start()
    {
        // Initialize navigation after a short delay to let UI load
        Invoke(nameof(InitializeNavigation), 2.0f);
    }

    [HideFromIl2Cpp]
    private void InitializeNavigation()
    {
        if (_navigationInitialized) return;

        Plugin.Log.LogInfo("Initializing navigation system...");

        // Trigger initial panel scan
        var scanner = FindObjectOfType<UIScanner>();
        if (scanner != null)
        {
            scanner.RefreshNavigationForActivePanel();
            _navigationInitialized = true;
        }
        else
        {
            Plugin.Log.LogWarning("UIScanner not found for navigation init");
        }
    }

    public void Update()
    {
        // Handle keyboard shortcuts using new Input System
        HandleKeyboardInput();
    }

    [HideFromIl2Cpp]
    private void HandleKeyboardInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        bool ctrlPressed = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        bool shiftPressed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

        // NOTE: Navigation keys (arrows, Enter, Space) are now handled by the game's
        // native FMNavigationManager. We piggyback on its focus system via FocusListener
        // instead of implementing our own navigation. This avoids input conflicts.

        // === MODIFIER SHORTCUTS (mod-specific features) ===

        // Ctrl+Shift+S - Scan UI (manual trigger)
        if (ctrlPressed && shiftPressed && keyboard.sKey.wasPressedThisFrame)
        {
            TriggerUIScan();
        }

        // Ctrl+Shift+R - Refresh navigation (re-scan current panel)
        if (ctrlPressed && shiftPressed && keyboard.rKey.wasPressedThisFrame)
        {
            RefreshNavigation();
        }

        // Ctrl+Shift+D - Toggle debug mode
        if (ctrlPressed && shiftPressed && keyboard.dKey.wasPressedThisFrame)
        {
            ToggleDebugMode();
        }

        // Ctrl+Shift+W - "Where am I?" - Announce current screen/context
        if (ctrlPressed && shiftPressed && keyboard.wKey.wasPressedThisFrame)
        {
            AnnounceCurrentContext();
        }

        // Ctrl+Shift+H - Help
        if (ctrlPressed && shiftPressed && keyboard.hKey.wasPressedThisFrame)
        {
            AnnounceHelp();
        }

        // Escape - Stop speech
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            NVDAOutput.Silence();
        }
    }

    [HideFromIl2Cpp]
    private void RefreshNavigation()
    {
        Plugin.Log.LogInfo("Manual navigation refresh triggered");
        NVDAOutput.Speak("Refreshing navigation");

        var scanner = FindObjectOfType<UIScanner>();
        if (scanner != null)
        {
            scanner.RefreshNavigationForActivePanel();
        }
        else
        {
            NVDAOutput.Speak("Scanner not available");
        }
    }

    [HideFromIl2Cpp]
    private void ToggleDebugMode()
    {
        NavigationController.DebugMode = !NavigationController.DebugMode;
        var status = NavigationController.DebugMode ? "Debug mode on" : "Debug mode off";
        NVDAOutput.Speak(status);
        Plugin.Log.LogInfo(status);
    }

    [HideFromIl2Cpp]
    private void TriggerUIScan()
    {
        Plugin.Log.LogInfo("Manual UI scan triggered");
        NVDAOutput.Speak("Scanning UI");

        var scanner = FindObjectOfType<UIScanner>();
        if (scanner != null)
        {
            scanner.ForceScan();
        }
        else
        {
            Plugin.Log.LogWarning("UIScanner not found");
            NVDAOutput.Speak("Scanner not available");
        }
    }

    [HideFromIl2Cpp]
    public void AnnounceCurrentContext()
    {
        var message = string.IsNullOrEmpty(_currentScreen)
            ? "Current screen unknown"
            : $"Current screen: {_currentScreen}";

        NVDAOutput.Speak(message);
        Plugin.Log.LogInfo(message);
    }

    [HideFromIl2Cpp]
    public void AnnounceHelp()
    {
        var help = "FM26 Access keyboard shortcuts. " +
                   "Navigation: Up and Down arrows to move between elements. " +
                   "Enter or Space to activate. " +
                   "Control Shift D to toggle debug mode. " +
                   "Control Shift R to refresh navigation. " +
                   "Control Shift S to deep scan UI. " +
                   "Control Shift W for where am I. " +
                   "Control Shift H for help. " +
                   "Escape to stop speech.";
        NVDAOutput.Speak(help);
    }

    [HideFromIl2Cpp]
    public void SetCurrentScreen(string screenName)
    {
        if (_currentScreen != screenName)
        {
            _currentScreen = screenName;
            Plugin.Log.LogInfo($"Screen changed to: {screenName}");

            // Announce screen change (with debounce to avoid spam)
            if (Time.time - _lastAnnouncementTime > 0.5f)
            {
                NVDAOutput.Speak(screenName);
                _lastAnnouncementTime = Time.time;
            }
        }
    }

    [HideFromIl2Cpp]
    public void Announce(string message, bool interrupt = true)
    {
        if (interrupt)
            NVDAOutput.Speak(message);
        else
            NVDAOutput.SpeakAppend(message);
    }

    public void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
