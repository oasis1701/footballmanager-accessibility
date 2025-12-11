using System;
using UnityEngine.UIElements;

namespace FM26Access.Navigation;

/// <summary>
/// Represents a focusable UI element for accessibility navigation.
/// </summary>
public class AccessibleElement
{
    /// <summary>
    /// The underlying VisualElement.
    /// </summary>
    public VisualElement Element { get; set; }

    /// <summary>
    /// The type of interactive element.
    /// </summary>
    public ElementType Type { get; set; }

    /// <summary>
    /// Human-readable label for the element.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Parent section name for context (e.g., "Privacy Policies", "Match Day Advertising").
    /// </summary>
    public string SectionName { get; set; } = "";

    /// <summary>
    /// IL2CPP type name of the element (e.g., "SIButton", "Toggle").
    /// </summary>
    public string TypeName { get; set; } = "";

    /// <summary>
    /// Function to get current state (checked/unchecked, selected value, etc.)
    /// Returns empty string if no state applies.
    /// </summary>
    public Func<string> GetState { get; set; } = () => "";

    /// <summary>
    /// Builds the full announcement string with context.
    /// Format: "[Section], [Label], [State], [Type]"
    /// </summary>
    public string BuildAnnouncement()
    {
        var parts = new System.Collections.Generic.List<string>();

        // Add section context if available
        if (!string.IsNullOrEmpty(SectionName))
            parts.Add(SectionName);

        // Add label
        if (!string.IsNullOrEmpty(Label))
            parts.Add(Label);

        // Add state if available
        var state = GetState?.Invoke() ?? "";
        if (!string.IsNullOrEmpty(state))
            parts.Add(state);

        // Add element type
        parts.Add(GetTypeAnnouncement());

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Gets the screen-reader friendly type name.
    /// </summary>
    private string GetTypeAnnouncement()
    {
        return Type switch
        {
            ElementType.Button => "Button",
            ElementType.Checkbox => "Checkbox",
            ElementType.Link => "Link",
            ElementType.Dropdown => "Dropdown",
            ElementType.TextField => "Text field",
            ElementType.Slider => "Slider",
            ElementType.RadioButton => "Radio button",
            _ => "Element"
        };
    }

    /// <summary>
    /// Builds a verbose debug announcement with technical details.
    /// Used when debug mode is enabled to help identify problematic elements.
    /// </summary>
    public string BuildDebugAnnouncement()
    {
        var parts = new System.Collections.Generic.List<string>();

        // IL2CPP type name
        parts.Add($"Type: {TypeName}");

        // Element name property
        var elementName = Element?.name;
        if (string.IsNullOrEmpty(elementName))
            elementName = "unnamed";
        parts.Add($"Name: {elementName}");

        // Parent element name
        var parentName = "none";
        try
        {
            parentName = Element?.parent?.name ?? "none";
            if (string.IsNullOrEmpty(parentName))
                parentName = "unnamed";
        }
        catch { }
        parts.Add($"Parent: {parentName}");

        // USS classes
        var classes = GetElementClasses();
        if (!string.IsNullOrEmpty(classes))
            parts.Add($"Classes: {classes}");

        // What we extracted as label
        var labelText = string.IsNullOrEmpty(Label) ? "empty" : Label;
        parts.Add($"Label: {labelText}");

        // Section we found
        var sectionText = string.IsNullOrEmpty(SectionName) ? "none" : SectionName;
        parts.Add($"Section: {sectionText}");

        return string.Join(". ", parts);
    }

    /// <summary>
    /// Gets USS class names from the element (first few for brevity).
    /// </summary>
    private string GetElementClasses()
    {
        if (Element == null) return "";

        try
        {
            var classList = new System.Collections.Generic.List<string>();

            // Check for some common class patterns manually since IL2CPP iteration is problematic
            string[] commonClasses = new[]
            {
                "button", "unity-button", "si-button", "link", "toggle", "checkbox",
                "dropdown", "text-field", "label", "header", "primary", "secondary",
                "nav-tab", "focusable", "navigatable", "si-clickable"
            };

            foreach (var cls in commonClasses)
            {
                if (Element.ClassListContains(cls))
                {
                    classList.Add(cls);
                    if (classList.Count >= 3) // Limit to first 3
                        break;
                }
            }

            if (classList.Count == 0)
                return "";

            return string.Join(", ", classList);
        }
        catch
        {
            return "";
        }
    }
}

/// <summary>
/// Types of interactive UI elements.
/// </summary>
public enum ElementType
{
    Unknown,
    Button,
    Checkbox,
    Link,
    Dropdown,
    TextField,
    Slider,
    RadioButton,
    Text
}
