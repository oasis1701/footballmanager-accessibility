using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using Il2CppInterop.Runtime.Attributes;
using FM26Access.UI;
using FM26Access.Navigation;
using FM.UI;
using SI.Bindable;
using SI.UI;

namespace FM26Access.Core;

/// <summary>
/// Main accessibility manager that coordinates all accessibility features.
/// Attached to a persistent GameObject that survives scene loads.
/// </summary>
public class AccessibilityManager : MonoBehaviour
{
    // IL2CPP requires this constructor for injected types
    public AccessibilityManager(IntPtr ptr) : base(ptr) { }

    // Windows API for mouse click simulation
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

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

        // NOTE: Navigation keys (arrows) are handled by the game's native FMNavigationManager.
        // We piggyback on its focus system via FocusListener.
        // However, Enter/Space for activation doesn't work reliably on all elements
        // (e.g., radio buttons in team selection), so we handle it manually.

        // === ACTIVATION KEYS (Enter/Space for element activation) ===
        // Primary: Enter key (without modifiers)
        if (!ctrlPressed && !shiftPressed)
        {
            if (keyboard.enterKey.wasPressedThisFrame ||
                keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                Plugin.Log.LogInfo("Enter key detected - attempting activation");
                TryActivateFocusedElement();
            }
        }

        // Alternative: Ctrl+Space (in case Enter is consumed by the game)
        if (ctrlPressed && !shiftPressed && keyboard.spaceKey.wasPressedThisFrame)
        {
            Plugin.Log.LogInfo("Ctrl+Space detected - attempting activation");
            TryActivateFocusedElement();
        }

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

    /// <summary>
    /// Attempts to activate the currently focused element.
    /// This handles cases where the game's native activation doesn't work
    /// (e.g., radio buttons configured to only accept click events).
    /// </summary>
    [HideFromIl2Cpp]
    private void TryActivateFocusedElement()
    {
        try
        {
            var focusListener = FocusListener.Instance;
            if (focusListener == null)
            {
                Plugin.Log.LogInfo("TryActivateFocusedElement: FocusListener not available");
                return;
            }

            var focused = focusListener.CurrentFocusedElement;
            if (focused == null)
            {
                Plugin.Log.LogInfo("TryActivateFocusedElement: No element focused");
                return;
            }

            var typeName = TextExtractor.GetIL2CppTypeName(focused);
            Plugin.Log.LogInfo($"TryActivateFocusedElement: Focused element is {typeName}");

            // Approach 1: Try dispatching a ClickEvent to the element
            // This mimics what happens when the user clicks with mouse
            if (TryDispatchClickEvent(focused))
            {
                return;
            }

            // Approach 2: Try to use NavigatableVisualElement's ClickedOrSubmitted delegate
            var navigatable = focused.TryCast<NavigatableVisualElement>();
            if (navigatable != null)
            {
                var clickDelegate = navigatable.m_elementClickedOrSubmitted;
                if (clickDelegate != null)
                {
                    Plugin.Log.LogInfo("Invoking ClickedOrSubmitted delegate on NavigatableVisualElement");
                    clickDelegate.Invoke(focused);
                    NVDAOutput.Speak("Selected");
                    return;
                }
                else
                {
                    Plugin.Log.LogInfo("NavigatableVisualElement found but delegate is null");
                }
            }

            // Approach 3: Try to find and activate a radio button (fallback for other UI elements)
            var radioButton = FindRadioButtonInElement(focused);
            if (radioButton != null)
            {
                Plugin.Log.LogInfo($"Activating radio button via keyboard");
                radioButton.OnClickedOrSubmitted(radioButton);
                NVDAOutput.Speak("Selected");
                return;
            }

            // If no activatable element found, log what we tried
            Plugin.Log.LogInfo($"TryActivateFocusedElement: No activatable element found in {typeName}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"TryActivateFocusedElement error: {ex.Message}");
        }
    }

    /// <summary>
    /// Simulates a mouse click on TableRowNavigatable elements (e.g., team selection).
    /// </summary>
    [HideFromIl2Cpp]
    private bool TryDispatchClickEvent(VisualElement element)
    {
        var typeName = TextExtractor.GetIL2CppTypeName(element);

        if (typeName == "TableRowNavigatable")
        {
            Plugin.Log.LogInfo("Element is TableRowNavigatable, attempting mouse click simulation");

            var selectionCell = FindSelectionCellInChildren(element);
            if (selectionCell != null)
            {
                try
                {
                    // Get the element's world bounds (in panel coordinates)
                    var rect = selectionCell.worldBound;
                    Plugin.Log.LogInfo($"SelectionCell worldBound: x={rect.x}, y={rect.y}, w={rect.width}, h={rect.height}");

                    // Calculate center of the element in panel coordinates
                    float centerX = rect.x + rect.width / 2;
                    float centerY = rect.y + rect.height / 2;

                    // Get the game window's screen position
                    var hwnd = GetForegroundWindow();
                    GetWindowRect(hwnd, out RECT windowRect);

                    // Convert from panel coordinates to absolute screen coordinates
                    int screenX = windowRect.Left + (int)centerX;
                    int screenY = windowRect.Top + (int)centerY;

                    Plugin.Log.LogInfo($"Window position: ({windowRect.Left}, {windowRect.Top})");
                    Plugin.Log.LogInfo($"Panel coords: ({centerX}, {centerY})");
                    Plugin.Log.LogInfo($"Simulating mouse click at screen position: ({screenX}, {screenY})");

                    // Move cursor to position and click
                    SetCursorPos(screenX, screenY);

                    // Small delay to ensure cursor position is set
                    System.Threading.Thread.Sleep(10);

                    // Simulate left mouse click
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);

                    NVDAOutput.Speak("Selected");
                    return true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogInfo($"Mouse click simulation failed: {ex.Message}");
                }
            }
            else
            {
                Plugin.Log.LogInfo("No SelectionCell found in TableRowNavigatable");
            }

            return false;
        }

        Plugin.Log.LogInfo($"TryDispatchClickEvent: Element type {typeName} not handled");
        return false;
    }

    /// <summary>
    /// Recursively searches for a SelectionCell in the element's children.
    /// </summary>
    [HideFromIl2Cpp]
    private StreamedTable.SelectionCell FindSelectionCellInChildren(VisualElement parent)
    {
        if (parent == null) return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent[i];
            var childTypeName = TextExtractor.GetIL2CppTypeName(child);

            if (childTypeName == "SelectionCell" || childTypeName.Contains("SelectionCell"))
            {
                Plugin.Log.LogInfo($"Found SelectionCell at depth, attempting cast");
                var cell = child.TryCast<StreamedTable.SelectionCell>();
                if (cell != null) return cell;
            }

            // Recursive search
            var found = FindSelectionCellInChildren(child);
            if (found != null) return found;
        }

        return null;
    }

    /// <summary>
    /// Finds an SIRadioButton in the element or its descendants.
    /// Handles both direct radio buttons and table rows containing radio buttons.
    /// </summary>
    [HideFromIl2Cpp]
    private SIRadioButton FindRadioButtonInElement(VisualElement element)
    {
        if (element == null)
            return null;

        // Check if element itself is a radio button
        var typeName = TextExtractor.GetIL2CppTypeName(element);
        if (typeName.Contains("SIRadioButton"))
        {
            return element.TryCast<SIRadioButton>();
        }

        // If it's a table row or any container, search children recursively
        return FindRadioButtonInChildren(element);
    }

    /// <summary>
    /// Recursively searches for an SIRadioButton in the element's children.
    /// </summary>
    [HideFromIl2Cpp]
    private SIRadioButton FindRadioButtonInChildren(VisualElement element)
    {
        if (element == null)
            return null;

        for (int i = 0; i < element.childCount; i++)
        {
            var child = element[i];

            var typeName = TextExtractor.GetIL2CppTypeName(child);
            if (typeName.Contains("SIRadioButton"))
            {
                return child.TryCast<SIRadioButton>();
            }

            // Recursively search children
            var found = FindRadioButtonInChildren(child);
            if (found != null)
                return found;
        }

        return null;
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
