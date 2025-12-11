using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UIElements;
using FM26Access.Core;
using FM26Access.UI;
using SI.Bindable;

namespace FM26Access.Navigation;

/// <summary>
/// Provides UI scanning utilities and element discovery for accessibility features.
/// NOTE: As of v0.2, keyboard navigation is handled by the game's native FMNavigationManager.
/// This class now primarily provides element discovery for debug/scan features.
/// Use FocusListener to track the game's focused element.
/// </summary>
public class NavigationController : MonoBehaviour
{
    // IL2CPP requires this constructor
    public NavigationController(IntPtr ptr) : base(ptr) { }

    public static NavigationController Instance { get; private set; }

    /// <summary>
    /// When true, announces verbose technical details instead of user-friendly labels.
    /// Toggle with Ctrl+Shift+D.
    /// </summary>
    public static bool DebugMode { get; set; } = false;

    private List<AccessibleElement> _focusableElements = new();
    private int _currentIndex = -1;
    private VisualElement _currentPanelRoot;
    private string _currentPanelName = "";

    // Debounce for panel refresh
    private float _lastRefreshTime;
    private const float RefreshCooldown = 0.3f;

    public void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        Plugin.Log.LogInfo("NavigationController initialized");
    }

    /// <summary>
    /// Refreshes the focusable element list for a panel.
    /// Call this when a new panel becomes active or binding completes.
    /// </summary>
    [HideFromIl2Cpp]
    public void RefreshFocusableElements(VisualElement panelRoot, string panelName = "")
    {
        if (panelRoot == null)
        {
            Plugin.Log.LogWarning("RefreshFocusableElements called with null root");
            return;
        }

        // Debounce rapid refreshes
        if (Time.time - _lastRefreshTime < RefreshCooldown)
            return;
        _lastRefreshTime = Time.time;

        _currentPanelRoot = panelRoot;
        _currentPanelName = panelName;

        // Discover and order elements
        _focusableElements = DiscoverFocusableElements(panelRoot);

        Plugin.Log.LogInfo($"NavigationController: Found {_focusableElements.Count} focusable elements in {panelName}");

        // Reset to first element if we have any
        if (_focusableElements.Count > 0)
        {
            _currentIndex = 0;
            // Announce panel and first element
            var panelAnnouncement = string.IsNullOrEmpty(panelName) ? "Screen loaded" : panelName;
            NVDAOutput.Speak($"{panelAnnouncement}. {_focusableElements[0].BuildAnnouncement()}");
        }
        else
        {
            _currentIndex = -1;
            NVDAOutput.Speak("No focusable elements found");
        }
    }

    /// <summary>
    /// Discovers all focusable elements and orders them by visual position.
    /// Uses UQuery to find elements even inside dynamically-bound containers.
    /// </summary>
    [HideFromIl2Cpp]
    private List<AccessibleElement> DiscoverFocusableElements(VisualElement root)
    {
        var elements = new List<AccessibleElement>();

        // Recursively collect all focusable elements
        CollectFocusableElements(root, elements);

        // Sort by visual position (top-to-bottom, left-to-right)
        elements.Sort((a, b) =>
        {
            if (a.Element == null || b.Element == null) return 0;

            try
            {
                var posA = a.Element.worldBound;
                var posB = b.Element.worldBound;

                // Primary: vertical position (top to bottom)
                // Use threshold for "same row" detection
                float yDiff = posA.y - posB.y;
                if (Math.Abs(yDiff) > 20)
                    return yDiff.CompareTo(0);

                // Secondary: horizontal position (left to right)
                return posA.x.CompareTo(posB.x);
            }
            catch
            {
                return 0;
            }
        });

        return elements;
    }

    /// <summary>
    /// Recursively collects focusable elements from the visual tree.
    /// </summary>
    [HideFromIl2Cpp]
    private void CollectFocusableElements(VisualElement element, List<AccessibleElement> results)
    {
        if (element == null) return;

        try
        {
            // Check if this element is focusable
            if (IsFocusable(element))
            {
                var accessible = CreateAccessibleElement(element);
                if (accessible != null)
                    results.Add(accessible);
            }

            // Recurse into children
            for (int i = 0; i < element.childCount; i++)
            {
                try
                {
                    var child = element[i];
                    CollectFocusableElements(child, results);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"CollectFocusableElements error: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines if a VisualElement should be focusable for navigation.
    /// </summary>
    [HideFromIl2Cpp]
    private bool IsFocusable(VisualElement element)
    {
        if (element == null) return false;

        // Must be visible and enabled
        if (!element.visible || !element.enabledInHierarchy)
            return false;

        // Check IL2CPP type name for SI elements
        var typeName = TextExtractor.GetIL2CppTypeName(element);

        // === EXCLUSION FILTERS (applied first) ===

        // Filter 1: Skip SIText - it's display text, not interactive
        if (typeName == "SIText")
            return false;

        // Filter 2: Skip plain VisualElement with no name (non-interactive containers)
        if (typeName == "VisualElement" && string.IsNullOrEmpty(element.name))
            return false;

        // Filter 3: Check if element belongs to the currently active panel
        // This replaces the old blacklist approach - elements from inactive panels are filtered out
        if (!IsElementInActivePanel(element))
            return false;

        // === INCLUSION CHECKS ===

        // SI custom elements
        if (typeName.Contains("SIButton") ||
            typeName.Contains("SIDropdown") ||
            typeName.Contains("SICheckbox") ||
            typeName.Contains("SICheckBox") ||
            typeName.Contains("SIToggle") ||
            typeName.Contains("SIClickable") ||
            typeName.Contains("SITextField"))
        {
            return true;
        }

        // Standard Unity elements
        if (element is Button ||
            element is Toggle ||
            element is DropdownField ||
            element is TextField ||
            element is Slider)
        {
            return true;
        }

        // Check for focusable attribute
        if (element.focusable)
            return true;

        // Check for navigation-related USS classes
        if (element.ClassListContains("navigatable") ||
            element.ClassListContains("focusable") ||
            element.ClassListContains("si-clickable") ||
            element.ClassListContains("unity-button"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if an element belongs to the currently active panel.
    /// This replaces the old blacklist approach by ensuring we only focus elements
    /// that are part of the panel we're currently navigating.
    /// </summary>
    [HideFromIl2Cpp]
    private bool IsElementInActivePanel(VisualElement element)
    {
        // If we don't have a current panel root set, allow all elements
        if (_currentPanelRoot == null)
            return true;

        // Check if element is contained within the current panel
        try
        {
            // Walk up the element's parent chain to see if we hit the panel root
            var current = element;
            while (current != null)
            {
                if (current == _currentPanelRoot)
                    return true;
                current = current.parent;
            }

            // Element is not part of the current panel
            return false;
        }
        catch
        {
            // If we can't determine, allow the element
            return true;
        }
    }

    /// <summary>
    /// Checks if an element has valid screen bounds (filters out hidden/off-screen elements).
    /// </summary>
    [HideFromIl2Cpp]
    private bool HasValidBounds(VisualElement element)
    {
        try
        {
            var bounds = element.worldBound;

            // Skip zero-size elements
            if (bounds.width <= 1 || bounds.height <= 1)
                return false;

            // Skip elements with invalid bounds (NaN)
            if (float.IsNaN(bounds.x) || float.IsNaN(bounds.y))
                return false;

            // Skip elements positioned way off-screen (likely from inactive panel)
            if (bounds.x < -100 || bounds.y < -100)
                return false;
            if (bounds.x > 3000 || bounds.y > 3000)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates an AccessibleElement from a VisualElement.
    /// </summary>
    [HideFromIl2Cpp]
    private AccessibleElement CreateAccessibleElement(VisualElement element)
    {
        if (element == null) return null;

        var typeName = TextExtractor.GetIL2CppTypeName(element);
        var elementType = DetermineElementType(element, typeName);
        var label = TextExtractor.ExtractText(element);
        var section = TextExtractor.FindSectionHeader(element);

        var accessible = new AccessibleElement
        {
            Element = element,
            Type = elementType,
            TypeName = typeName,
            Label = label,
            SectionName = section,
            GetState = () => GetElementState(element, elementType)
        };

        return accessible;
    }

    /// <summary>
    /// Determines the ElementType based on the element and its IL2CPP type.
    /// </summary>
    [HideFromIl2Cpp]
    private ElementType DetermineElementType(VisualElement element, string typeName)
    {
        // SI custom types
        if (typeName.Contains("SIButton") || typeName.Contains("SIClickable"))
            return ElementType.Button;
        if (typeName.Contains("SICheckBox", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("SIToggle", StringComparison.OrdinalIgnoreCase))
            return ElementType.Checkbox;
        if (typeName.Contains("SIDropdown"))
            return ElementType.Dropdown;
        if (typeName.Contains("SITextField") || typeName.Contains("SITextInput"))
            return ElementType.TextField;

        // Standard Unity types
        if (element is Button)
            return ElementType.Button;
        if (element is Toggle)
            return ElementType.Checkbox;
        if (element is DropdownField)
            return ElementType.Dropdown;
        if (element is TextField)
            return ElementType.TextField;
        if (element is Slider)
            return ElementType.Slider;

        // Check class names for links
        if (element.ClassListContains("link") ||
            element.ClassListContains("hyperlink") ||
            typeName.Contains("Link"))
            return ElementType.Link;

        return ElementType.Button; // Default to button for clickable elements
    }

    /// <summary>
    /// Gets the current state string for an element (checked/unchecked, selected value, etc.)
    /// </summary>
    [HideFromIl2Cpp]
    private string GetElementState(VisualElement element, ElementType type)
    {
        return type switch
        {
            ElementType.Checkbox => TextExtractor.GetToggleStateString(element),
            ElementType.Dropdown => TextExtractor.GetDropdownValue(element),
            _ => ""
        };
    }

    // =====================================================================
    // LEGACY METHODS - Kept for compatibility but no longer used for navigation
    // Navigation is now handled by the game's native FMNavigationManager.
    // These methods are retained for potential debug/manual scanning features.
    // =====================================================================

    /// <summary>
    /// [LEGACY] Moves to the next element in the scanned list.
    /// NOTE: Navigation is now handled by the game's native system.
    /// </summary>
    [HideFromIl2Cpp]
    public void MoveNext()
    {
        // Legacy method - navigation now handled by game's FMNavigationManager
        Plugin.Log.LogDebug("MoveNext called - navigation now handled by native system");
    }

    /// <summary>
    /// [LEGACY] Moves to the previous element in the scanned list.
    /// NOTE: Navigation is now handled by the game's native system.
    /// </summary>
    [HideFromIl2Cpp]
    public void MovePrevious()
    {
        // Legacy method - navigation now handled by game's FMNavigationManager
        Plugin.Log.LogDebug("MovePrevious called - navigation now handled by native system");
    }

    /// <summary>
    /// [LEGACY] Activates the currently focused element.
    /// NOTE: Activation is now handled by the game's native system.
    /// </summary>
    [HideFromIl2Cpp]
    public void Activate()
    {
        // Legacy method - activation now handled by game's FMNavigationManager
        Plugin.Log.LogDebug("Activate called - activation now handled by native system");
    }

    /// <summary>
    /// [LEGACY] Announces the element at the current index.
    /// </summary>
    [HideFromIl2Cpp]
    public void AnnounceCurrent()
    {
        if (_currentIndex < 0 || _currentIndex >= _focusableElements.Count)
        {
            NVDAOutput.Speak("No element in scan list");
            return;
        }

        var current = _focusableElements[_currentIndex];
        string announcement = DebugMode ? current.BuildDebugAnnouncement() : current.BuildAnnouncement();
        var position = $"{_currentIndex + 1} of {_focusableElements.Count}";
        NVDAOutput.Speak($"{announcement}. {position}");
    }

    /// <summary>
    /// Gets the current focused element (if any).
    /// </summary>
    [HideFromIl2Cpp]
    public AccessibleElement GetCurrentElement()
    {
        if (_currentIndex >= 0 && _currentIndex < _focusableElements.Count)
            return _focusableElements[_currentIndex];
        return null;
    }

    /// <summary>
    /// Gets the count of focusable elements.
    /// </summary>
    public int ElementCount => _focusableElements.Count;

    /// <summary>
    /// Gets the current index.
    /// </summary>
    public int CurrentIndex => _currentIndex;

    public void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
