using System;
using UnityEngine;
using UnityEngine.UIElements;
using Il2CppInterop.Runtime.Attributes;
using FM26Access.Core;
using FM26Access.UI;
using SI.UI;
using SI.Bindable;
using FM.UI;

namespace FM26Access.Navigation;

/// <summary>
/// Listens to the game's native focus system and announces focused elements via NVDA.
/// This piggybacks on FM26's built-in keyboard navigation instead of implementing our own.
/// </summary>
public class FocusListener : MonoBehaviour
{
    // IL2CPP requires this constructor
    public FocusListener(IntPtr ptr) : base(ptr) { }

    public static FocusListener Instance { get; private set; }

    private VisualElement _lastFocusedElement;
    private string _lastFocusedState = "";
    private float _lastAnnouncementTime;
    private const float AnnouncementDebounce = 0.05f; // 50ms debounce

    /// <summary>
    /// Gets the currently focused element tracked by this listener.
    /// Used by AccessibilityManager to activate elements on Enter key.
    /// </summary>
    public VisualElement CurrentFocusedElement => _lastFocusedElement;

    // Table row tracking to avoid re-announcing same row on cell navigation
    private VisualElement _lastAnnouncedTableRow;
    private int _lastAnnouncedRowIndex = -1;
    private IntPtr _lastAnnouncedTablePtr = IntPtr.Zero;

    public void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        Plugin.Log.LogInfo("FocusListener initialized");
    }

    public void Update()
    {
        CheckFocusChange();
        CheckStateChange();
    }

    /// <summary>
    /// Checks if the game's focused element has changed and announces it.
    /// </summary>
    [HideFromIl2Cpp]
    private void CheckFocusChange()
    {
        try
        {
            // Get the navigation manager instance
            var navManager = GetNavigationManager();
            if (navManager == null) return;

            // Get currently focused element
            var currentFocus = navManager.CurrentFocus;

            // Check if focus changed
            if (currentFocus != null && currentFocus != _lastFocusedElement)
            {
                // Debounce rapid focus changes
                if (Time.time - _lastAnnouncementTime < AnnouncementDebounce)
                    return;

                _lastFocusedElement = currentFocus;
                _lastAnnouncementTime = Time.time;

                AnnounceFocusedElement(currentFocus);
            }
        }
        catch (Exception ex)
        {
            // Silently ignore - the navigation manager might not be ready yet
            Plugin.Log.LogDebug($"FocusListener.CheckFocusChange: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if the state of the currently focused element has changed (e.g., checkbox toggled).
    /// </summary>
    [HideFromIl2Cpp]
    private void CheckStateChange()
    {
        if (_lastFocusedElement == null) return;

        try
        {
            var currentState = GetElementState(_lastFocusedElement);
            if (!string.IsNullOrEmpty(currentState) && currentState != _lastFocusedState)
            {
                _lastFocusedState = currentState;
                // Announce state change (e.g., "checked" or "not checked")
                NVDAOutput.Speak(currentState);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"FocusListener.CheckStateChange: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the FMNavigationManager instance from the game.
    /// </summary>
    [HideFromIl2Cpp]
    private FMNavigationManager GetNavigationManager()
    {
        try
        {
            // NavigationManager.Instance returns INavigationManager
            // FMNavigationManager implements this interface
            var instance = NavigationManager.Instance;
            if (instance == null) return null;

            // Try to cast to FMNavigationManager
            return instance.TryCast<FMNavigationManager>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Announces a focused element to NVDA.
    /// Special handling for table headers and rows to announce full data.
    /// </summary>
    [HideFromIl2Cpp]
    private void AnnounceFocusedElement(VisualElement element)
    {
        // Check for table header first (before table row check)
        if (TextExtractor.IsTableHeader(element))
        {
            var headerData = TextExtractor.ExtractTableHeaderData(element);
            if (!string.IsNullOrWhiteSpace(headerData))
            {
                Plugin.Log.LogInfo($"Table header: {headerData}");
                NVDAOutput.Speak(headerData);
                return;
            }
        }

        // Check if this element is inside a table row
        var tableRow = TextExtractor.FindTableRowAncestor(element);
        if (tableRow != null)
        {
            AnnounceTableRow(tableRow, element);
            return;
        }

        // Original non-table behavior
        var typeName = TextExtractor.GetIL2CppTypeName(element);
        var label = TextExtractor.ExtractText(element);
        var elementType = GetElementTypeName(element, typeName);
        var state = GetElementState(element);

        // Only get section header for certain element types (dropdowns, checkboxes)
        // Buttons typically have their own descriptive labels and don't need section context
        string section = "";
        if (typeName.Contains("SIDropdown") ||
            typeName.Contains("SICheckBox", StringComparison.OrdinalIgnoreCase) ||
            element is Toggle || element is DropdownField)
        {
            section = TextExtractor.FindSectionHeader(element);
        }

        // Update tracked state
        _lastFocusedState = state;

        // Build announcement
        var announcement = BuildAnnouncement(label, elementType, state, section);

        Plugin.Log.LogInfo($"Focus: {label} ({typeName})");
        NVDAOutput.Speak(announcement);
    }

    /// <summary>
    /// Builds a user-friendly announcement string.
    /// </summary>
    [HideFromIl2Cpp]
    private string BuildAnnouncement(string label, string type, string state, string section)
    {
        var parts = new System.Collections.Generic.List<string>();

        // Add section if available and different from label
        if (!string.IsNullOrEmpty(section) && section != label)
            parts.Add(section);

        // Add label
        if (!string.IsNullOrEmpty(label))
            parts.Add(label);

        // Add type
        if (!string.IsNullOrEmpty(type))
            parts.Add(type);

        // Add state (for checkboxes, dropdowns, etc.)
        if (!string.IsNullOrEmpty(state))
            parts.Add(state);

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Gets a user-friendly type name for the element.
    /// </summary>
    [HideFromIl2Cpp]
    private string GetElementTypeName(VisualElement element, string typeName)
    {
        // Check for close button FIRST (before generic button detection)
        if (TextExtractor.IsCloseButton(element))
            return "Close button";

        if (typeName.Contains("SIButton") || typeName.Contains("SIClickable"))
            return "button";
        if (typeName.Contains("SICheckBox", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("SIToggle", StringComparison.OrdinalIgnoreCase))
            return "checkbox";
        if (typeName.Contains("SIDropdown"))
            return "dropdown";
        if (typeName.Contains("SITextField") || typeName.Contains("SITextInput"))
            return "text field";
        if (typeName.Contains("Link") || element.ClassListContains("link"))
            return "link";

        if (element is Button)
            return "button";
        if (element is Toggle)
            return "checkbox";
        if (element is DropdownField)
            return "dropdown";
        if (element is TextField)
            return "text field";
        if (element is Slider)
            return "slider";

        return ""; // Don't announce type for unknown elements
    }

    /// <summary>
    /// Gets the current state of an element (checked/unchecked, dropdown value, etc.)
    /// </summary>
    [HideFromIl2Cpp]
    private string GetElementState(VisualElement element)
    {
        var typeName = TextExtractor.GetIL2CppTypeName(element);

        // Checkbox state
        if (typeName.Contains("SICheckBox", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("SIToggle", StringComparison.OrdinalIgnoreCase) ||
            element is Toggle)
        {
            return TextExtractor.GetToggleStateString(element);
        }

        // Radio button state
        if (typeName.Contains("SIRadioButton"))
        {
            return TextExtractor.GetRadioButtonStateString(element);
        }

        // Dropdown value
        if (typeName.Contains("SIDropdown") || element is DropdownField)
        {
            return TextExtractor.GetDropdownValue(element);
        }

        // Check for button selection state (Yes/No toggle buttons)
        if (typeName.Contains("SIButton"))
        {
            return TextExtractor.GetButtonSelectionState(element);
        }

        return "";
    }

    /// <summary>
    /// Announces a table row with all cell data.
    /// Tracks row to avoid re-announcing same row on cell navigation.
    /// </summary>
    [HideFromIl2Cpp]
    private void AnnounceTableRow(VisualElement tableRow, VisualElement focusedCell)
    {
        try
        {
            // Get row identification info
            int rowIndex = GetTableRowIndex(tableRow);
            IntPtr tablePtr = GetTableParentPointer(tableRow);

            // Check if we're still on the same row (e.g., left/right navigation between cells)
            if (IsSameRow(tableRow, rowIndex, tablePtr))
            {
                Plugin.Log.LogDebug($"Same row {rowIndex}, skipping re-announce");
                return;
            }

            // Extract full row data
            var rowData = TextExtractor.ExtractTableRowData(tableRow);

            if (string.IsNullOrWhiteSpace(rowData))
            {
                // Fallback: announce focused cell only
                var cellText = TextExtractor.StripRichTextTags(TextExtractor.ExtractText(focusedCell));
                if (!string.IsNullOrWhiteSpace(cellText))
                {
                    Plugin.Log.LogInfo($"Table row (empty), cell: {cellText}");
                    NVDAOutput.Speak(cellText);
                }
                return;
            }

            // Update tracking
            _lastAnnouncedTableRow = tableRow;
            _lastAnnouncedRowIndex = rowIndex;
            _lastAnnouncedTablePtr = tablePtr;

            // Add row position context if available
            string announcement = rowData;
            if (rowIndex >= 0)
            {
                announcement = $"Row {rowIndex + 1}: {rowData}";
            }

            Plugin.Log.LogInfo($"Table row: {announcement}");
            NVDAOutput.Speak(announcement);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"AnnounceTableRow failed: {ex.Message}");
            // Fallback to cell announcement
            var cellText = TextExtractor.StripRichTextTags(TextExtractor.ExtractText(focusedCell));
            if (!string.IsNullOrWhiteSpace(cellText))
            {
                NVDAOutput.Speak(cellText);
            }
        }
    }

    /// <summary>
    /// Gets the row index from a TableRowNavigatable.
    /// Returns -1 if unable to determine.
    /// </summary>
    [HideFromIl2Cpp]
    private int GetTableRowIndex(VisualElement tableRow)
    {
        try
        {
            var rowNav = tableRow.TryCast<SI.Bindable.TableRowNavigatable>();
            if (rowNav != null)
            {
                return rowNav.Index;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"GetTableRowIndex failed: {ex.Message}");
        }
        return -1;
    }

    /// <summary>
    /// Gets the pointer to the parent table for unique identification.
    /// </summary>
    [HideFromIl2Cpp]
    private IntPtr GetTableParentPointer(VisualElement tableRow)
    {
        try
        {
            var rowNav = tableRow.TryCast<SI.Bindable.TableRowNavigatable>();
            if (rowNav != null)
            {
                var tableParent = rowNav.TableParent;
                if (tableParent != null)
                {
                    return tableParent.Pointer;
                }
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Checks if the given row is the same as the last announced row.
    /// </summary>
    [HideFromIl2Cpp]
    private bool IsSameRow(VisualElement tableRow, int rowIndex, IntPtr tablePtr)
    {
        // Check by object reference first
        if (tableRow == _lastAnnouncedTableRow)
            return true;

        // Then check by index + table combination (handles virtualized lists)
        if (rowIndex >= 0 && tablePtr != IntPtr.Zero)
        {
            return rowIndex == _lastAnnouncedRowIndex &&
                   tablePtr == _lastAnnouncedTablePtr;
        }

        return false;
    }

    public void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
