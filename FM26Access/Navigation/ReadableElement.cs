using UnityEngine;
using UnityEngine.UIElements;

namespace FM26Access.Navigation;

/// <summary>
/// Represents any readable element for Reading Mode navigation.
/// Unlike AccessibleElement, this includes non-focusable text elements.
/// </summary>
public class ReadableElement
{
    /// <summary>
    /// The underlying VisualElement.
    /// </summary>
    public VisualElement Element { get; set; }

    /// <summary>
    /// The extracted text content to announce.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Type hint for the element (heading, paragraph, button, etc.)
    /// </summary>
    public string TypeHint { get; set; } = "";

    /// <summary>
    /// World bounds for position-based sorting.
    /// </summary>
    public Rect Bounds { get; set; }

    /// <summary>
    /// Tree depth from the container root.
    /// Used for hierarchical navigation (left/right).
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// Builds the announcement string for NVDA.
    /// Format: "[Text]. [TypeHint if applicable]"
    /// </summary>
    public string BuildAnnouncement()
    {
        if (string.IsNullOrEmpty(TypeHint) || TypeHint == "text")
        {
            return Text;
        }

        return $"{Text}, {TypeHint}";
    }

    /// <summary>
    /// Builds a debug announcement with technical details.
    /// </summary>
    public string BuildDebugAnnouncement()
    {
        var elementName = Element?.name ?? "unnamed";
        if (string.IsNullOrEmpty(elementName))
            elementName = "unnamed";

        var parentName = "none";
        try
        {
            parentName = Element?.parent?.name ?? "none";
            if (string.IsNullOrEmpty(parentName))
                parentName = "unnamed";
        }
        catch { }

        return $"Type: {TypeHint}. Name: {elementName}. Parent: {parentName}. Depth: {Depth}. Text: {Text}";
    }
}
