using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine.UIElements;
using SI.Bindable;

namespace FM26Access.UI;

/// <summary>
/// Utility class to extract text content from UI Toolkit elements,
/// including Sports Interactive custom elements (SIText, SIButton, etc.).
/// </summary>
public static class TextExtractor
{
    /// <summary>
    /// Extracts human-readable text from any VisualElement.
    /// Handles SI custom elements, localized text, and standard Unity UI.
    /// Priority: SI properties → meaningful child text → sibling context → cleaned name
    /// </summary>
    [HideFromIl2Cpp]
    public static string ExtractText(VisualElement element)
    {
        if (element == null) return "";

        try
        {
            var typeName = GetIL2CppTypeName(element);

            // 1. SI-SPECIFIC PROPERTIES (highest priority - these have the actual visible text)
            if (typeName.Contains("SIButton"))
            {
                var text = TryGetSIButtonText(element);
                if (!string.IsNullOrWhiteSpace(text))
                    return CleanText(text);
            }

            if (typeName.Contains("SICheckBox") || typeName.Contains("SICheckbox"))
            {
                var text = TryGetSICheckBoxLabel(element);
                if (!string.IsNullOrWhiteSpace(text))
                    return CleanText(text);
            }

            // 2. DIRECT TEXT ELEMENT (for labels, plain text, etc.)
            if (element is TextElement textEl)
            {
                var text = textEl.text;
                if (!string.IsNullOrWhiteSpace(text) && !IsInternalName(text))
                    return CleanText(text);
            }

            // 3. MEANINGFUL CHILD TEXT (search all TextElement children for real content)
            var childText = FindMeaningfulChildText(element);
            if (!string.IsNullOrWhiteSpace(childText))
                return CleanText(childText);

            // 4. SIBLING CONTEXT (for checkboxes/toggles without direct labels)
            if (typeName.Contains("SICheckBox") || typeName.Contains("SICheckbox") || element is Toggle)
            {
                var siblingLabel = FindCheckboxLabel(element);
                if (!string.IsNullOrWhiteSpace(siblingLabel))
                    return CleanText(siblingLabel);
            }

            // 5. ELEMENT NAME (last resort, heavily filtered)
            if (!string.IsNullOrEmpty(element.name) && element.name != "(unnamed)")
            {
                var cleanedName = CleanElementName(element.name);
                if (!string.IsNullOrEmpty(cleanedName))
                    return cleanedName;
            }

            return "";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"TextExtractor.ExtractText failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Tries to get text from SIButton.m_staticText field.
    /// Note: StaticText property only has a setter in IL2CPP, so we use the field directly.
    /// </summary>
    [HideFromIl2Cpp]
    private static string TryGetSIButtonText(VisualElement element)
    {
        try
        {
            var siButton = element.TryCast<SIButton>();
            if (siButton != null)
            {
                // Use m_staticText field (has both getter and setter in IL2CPP)
                var staticText = siButton.m_staticText;
                if (!string.IsNullOrWhiteSpace(staticText))
                    return staticText;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"TryGetSIButtonText: {ex.Message}");
        }
        return "";
    }

    /// <summary>
    /// Tries to get label from SICheckBox.
    /// Note: Label property only has a setter in IL2CPP, so we search for child text.
    /// </summary>
    [HideFromIl2Cpp]
    private static string TryGetSICheckBoxLabel(VisualElement element)
    {
        // SICheckBox.Label property doesn't have a getter in IL2CPP interop
        // The label is typically in a child element or sibling, handled by FindCheckboxLabel
        return "";
    }

    /// <summary>
    /// Searches all child TextElements for meaningful text content.
    /// Prioritizes short meaningful labels (Yes, No, OK) over longer text.
    /// </summary>
    [HideFromIl2Cpp]
    private static string FindMeaningfulChildText(VisualElement element)
    {
        try
        {
            var allTexts = element.Query<TextElement>(name: (string)null, className: (string)null).ToList();

            // First pass: look for short, meaningful UI labels
            foreach (var textEl in allTexts)
            {
                var text = textEl.text?.Trim();
                if (IsShortMeaningfulLabel(text))
                    return text;
            }

            // Second pass: any non-empty, non-internal text
            foreach (var textEl in allTexts)
            {
                var text = textEl.text?.Trim();
                if (!string.IsNullOrWhiteSpace(text) &&
                    text.Length > 1 &&
                    text.Length < 200 &&
                    !IsInternalName(text))
                {
                    return text;
                }
            }
        }
        catch { }
        return "";
    }

    /// <summary>
    /// Checks if text is a short, meaningful UI label (Yes, No, OK, etc.)
    /// </summary>
    [HideFromIl2Cpp]
    private static bool IsShortMeaningfulLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.ToLowerInvariant().Trim();
        return lower == "yes" || lower == "no" || lower == "ok" ||
               lower == "cancel" || lower == "apply" || lower == "save" ||
               lower == "close" || lower == "back" || lower == "next" ||
               lower == "confirm" || lower == "decline" || lower == "accept" ||
               lower == "continue" || lower == "skip" || lower == "done";
    }

    /// <summary>
    /// Checks if text looks like an internal element name rather than user-visible text.
    /// </summary>
    [HideFromIl2Cpp]
    private static bool IsInternalName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var lower = text.ToLowerInvariant();

        // Common internal naming patterns
        return lower.Contains("default") ||
               lower.Contains("container") ||
               lower.Contains("wrapper") ||
               lower.Contains("element") ||
               lower.Contains("root") ||
               lower.Contains("content") ||
               lower.EndsWith("button") ||
               lower.EndsWith("toggle") ||
               lower.EndsWith("checkbox") ||
               lower.EndsWith("field") ||
               lower.EndsWith("input") ||
               lower.Contains("sibutton") ||
               lower.Contains("sicheckbox") ||
               lower.Contains("sitoggle");
    }

    /// <summary>
    /// Finds the label text associated with a checkbox by searching siblings and ancestors.
    /// Searches deeper in the hierarchy to find dynamically-bound text.
    /// </summary>
    [HideFromIl2Cpp]
    private static string FindCheckboxLabel(VisualElement checkbox)
    {
        if (checkbox == null) return "";

        try
        {
            // Walk up the tree looking for text
            var current = checkbox.parent;
            int depth = 0;
            const int maxDepth = 5;

            while (current != null && depth < maxDepth)
            {
                depth++;

                // Search all descendants of siblings for text
                for (int i = 0; i < current.childCount; i++)
                {
                    var sibling = current[i];

                    // Skip the checkbox itself and its ancestors
                    if (sibling == checkbox || IsAncestorOf(sibling, checkbox))
                        continue;

                    // Query ALL TextElements in the sibling subtree
                    var allTexts = sibling.Query<TextElement>(name: (string)null, className: (string)null).ToList();

                    // First pass: look for text containing "acknowledge" (specific to privacy checkbox)
                    foreach (var textEl in allTexts)
                    {
                        var text = textEl.text;
                        if (!string.IsNullOrWhiteSpace(text) &&
                            text.Length > 10 &&
                            text.Length < 500 &&
                            text.Contains("acknowledge", StringComparison.OrdinalIgnoreCase))
                        {
                            return text;
                        }
                    }

                    // Second pass: look for any reasonably-sized text (likely checkbox label)
                    foreach (var textEl in allTexts)
                    {
                        var text = textEl.text;
                        if (!string.IsNullOrWhiteSpace(text) &&
                            text.Length > 20 &&
                            text.Length < 500)
                        {
                            return text;
                        }
                    }
                }

                current = current.parent;
            }
        }
        catch { }

        return "";
    }

    /// <summary>
    /// Checks if potentialAncestor is an ancestor of element.
    /// </summary>
    [HideFromIl2Cpp]
    private static bool IsAncestorOf(VisualElement potentialAncestor, VisualElement element)
    {
        var current = element.parent;
        while (current != null)
        {
            if (current == potentialAncestor)
                return true;
            current = current.parent;
        }
        return false;
    }

    /// <summary>
    /// Gets checkbox/toggle state as a string.
    /// </summary>
    [HideFromIl2Cpp]
    public static string GetToggleStateString(VisualElement element)
    {
        var state = GetToggleState(element);
        if (state.HasValue)
            return state.Value ? "Checked" : "Not checked";
        return "";
    }

    /// <summary>
    /// Gets checkbox/toggle state as nullable bool.
    /// Uses multiple detection methods for IL2CPP compatibility.
    /// </summary>
    [HideFromIl2Cpp]
    public static bool? GetToggleState(VisualElement element)
    {
        if (element == null) return null;

        try
        {
            // Method 1: Standard Unity Toggle
            if (element is Toggle toggle)
                return toggle.value;

            // Method 2: SICheckBox.m_value field (the actual checked state!)
            var typeName = GetIL2CppTypeName(element);
            if (typeName.Contains("SICheckBox") || typeName.Contains("SICheckbox"))
            {
                try
                {
                    var siCheckBox = element.TryCast<SICheckBox>();
                    if (siCheckBox != null)
                    {
                        return siCheckBox.m_value;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogDebug($"GetToggleState SICheckBox.m_value failed: {ex.Message}");
                }
            }

            // Method 3: Look for checkmark/tick child element and check visibility
            VisualElement checkmark = null;
            checkmark = element.Q(name: "checkmark", className: (string)null);
            if (checkmark == null)
                checkmark = element.Q(name: "Checkmark", className: (string)null);
            if (checkmark == null)
                checkmark = element.Q(name: (string)null, className: "checkmark");
            if (checkmark == null)
                checkmark = element.Q(name: (string)null, className: "unity-checkmark");
            if (checkmark == null)
                checkmark = element.Q(name: "tick", className: (string)null);
            if (checkmark == null)
                checkmark = element.Q(name: "Tick", className: (string)null);

            if (checkmark != null)
            {
                // Check multiple visibility indicators
                var style = checkmark.resolvedStyle;
                bool isVisible = checkmark.visible &&
                                 style.display != DisplayStyle.None &&
                                 style.opacity > 0.1f;
                return isVisible;
            }

            // Method 4: Check for CSS classes that indicate checked state
            if (element.ClassListContains("checked") ||
                element.ClassListContains("unity-toggle__input--checked") ||
                element.ClassListContains("selected") ||
                element.ClassListContains("is-checked"))
                return true;

            // Default to unchecked if no indicator found
            return false;
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Gets radio button state as a string.
    /// </summary>
    [HideFromIl2Cpp]
    public static string GetRadioButtonStateString(VisualElement element)
    {
        var state = GetRadioButtonState(element);
        if (state.HasValue)
            return state.Value ? "Selected" : "Not selected";
        return "";
    }

    /// <summary>
    /// Gets radio button state as nullable bool using SIRadioButton.m_value field.
    /// </summary>
    [HideFromIl2Cpp]
    public static bool? GetRadioButtonState(VisualElement element)
    {
        if (element == null) return null;

        try
        {
            var siRadioButton = element.TryCast<SIRadioButton>();
            if (siRadioButton != null)
            {
                return siRadioButton.m_value;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"GetRadioButtonState failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Gets button selection state for toggle-style buttons (Yes/No buttons).
    /// Checks for CSS classes or visual indicators that show selection.
    /// </summary>
    [HideFromIl2Cpp]
    public static string GetButtonSelectionState(VisualElement element)
    {
        if (element == null) return "";

        try
        {
            // Check for common selection/active CSS classes
            if (element.ClassListContains("selected") ||
                element.ClassListContains("active") ||
                element.ClassListContains("is-selected") ||
                element.ClassListContains("is-active") ||
                element.ClassListContains("toggled") ||
                element.ClassListContains("pressed") ||
                element.ClassListContains("checked") ||
                element.ClassListContains("on") ||
                element.ClassListContains("choice-selected") ||
                element.ClassListContains("toggle-on") ||
                element.ClassListContains("button-selected") ||
                element.ClassListContains("option-selected"))
            {
                return "Selected";
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"GetButtonSelectionState failed: {ex.Message}");
        }

        return "";
    }

    /// <summary>
    /// Gets dropdown selected value as string.
    /// </summary>
    [HideFromIl2Cpp]
    public static string GetDropdownValue(VisualElement element)
    {
        if (element == null) return "";

        try
        {
            // Standard Unity DropdownField
            if (element is DropdownField dropdown)
                return dropdown.value ?? "";

            // Try getting value property via reflection
            var type = element.GetType();
            var prop = type.GetProperty("value");
            if (prop != null)
            {
                var val = prop.GetValue(element);
                if (val != null)
                    return val.ToString();
            }

            // Try text property for SI dropdowns (shows selected text)
            var textEl = element.Q<TextElement>();
            if (textEl != null && !string.IsNullOrWhiteSpace(textEl.text))
                return textEl.text;
        }
        catch { }

        return "";
    }

    /// <summary>
    /// Gets the IL2CPP type name for a VisualElement.
    /// </summary>
    [HideFromIl2Cpp]
    public static string GetIL2CppTypeName(VisualElement element)
    {
        if (element == null) return "Unknown";

        try
        {
            IntPtr objectPtr = element.Pointer;
            if (objectPtr == IntPtr.Zero) return element.GetType().Name;

            IntPtr classPtr = IL2CPP.il2cpp_object_get_class(objectPtr);
            if (classPtr == IntPtr.Zero) return element.GetType().Name;

            IntPtr namePtr = IL2CPP.il2cpp_class_get_name(classPtr);
            return Marshal.PtrToStringAnsi(namePtr) ?? element.GetType().Name;
        }
        catch
        {
            return element.GetType().Name;
        }
    }

    /// <summary>
    /// Cleans raw text by trimming and removing excessive whitespace.
    /// </summary>
    [HideFromIl2Cpp]
    private static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // Trim, normalize whitespace, remove newlines for speech
        return text
            .Trim()
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("  ", " ");
    }

    /// <summary>
    /// Strips Rich Text tags from text (color, link, style, size, etc.)
    /// FM26 uses Unity Rich Text markup in table cells.
    /// </summary>
    [HideFromIl2Cpp]
    public static string StripRichTextTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // Pattern matches XML-like tags: <tag>, </tag>, <tag=value>, etc.
        var pattern = @"<\/?[a-zA-Z][^>]*>";
        var stripped = Regex.Replace(text, pattern, string.Empty);

        // Clean up multiple spaces left behind
        return Regex.Replace(stripped, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Detects if an element is a close/X button by checking name patterns,
    /// parent names, and child text for X characters.
    /// </summary>
    [HideFromIl2Cpp]
    public static bool IsCloseButton(VisualElement element)
    {
        if (element == null) return false;

        var typeName = GetIL2CppTypeName(element);

        // Must be a button type
        if (!typeName.Contains("Button") && !typeName.Contains("Clickable"))
            return false;

        var name = element.name?.ToLowerInvariant() ?? "";

        // Check element name for close patterns
        if (name.Contains("close") || name.Contains("dismiss") ||
            name.Contains("exit") || name == "x" ||
            name.EndsWith("-x") || name.StartsWith("x-"))
            return true;

        // Check parent name for close patterns (for unnamed buttons)
        var parentName = element.parent?.name?.ToLowerInvariant() ?? "";
        if (parentName.Contains("close") || parentName.Contains("dismiss"))
            return true;

        // Check if button has empty label and contains X text child
        var label = ExtractText(element);
        if (string.IsNullOrWhiteSpace(label))
        {
            // Look for X or × character in children
            try
            {
                var allTexts = element.Query<TextElement>(name: (string)null, className: (string)null).ToList();
                foreach (var textEl in allTexts)
                {
                    var text = textEl.text?.Trim();
                    if (text == "X" || text == "x" || text == "×" || text == "✕" || text == "✖")
                        return true;
                }
            }
            catch { }
        }

        return false;
    }

    /// <summary>
    /// Detects if an element is a table header by checking type name.
    /// StreamedTable is the actual type used for table headers in FM26.
    /// </summary>
    [HideFromIl2Cpp]
    public static bool IsTableHeader(VisualElement element)
    {
        if (element == null) return false;
        var typeName = GetIL2CppTypeName(element);

        // StreamedTable is the actual type for table headers in FM26
        return typeName.Contains("StreamedTable") ||
               typeName.Contains("Header") ||
               typeName.Contains("ColumnHeader") ||
               typeName.Contains("TableHeader");
    }

    /// <summary>
    /// Extracts all column header names from a header row element.
    /// </summary>
    [HideFromIl2Cpp]
    public static string ExtractTableHeaderData(VisualElement headerElement)
    {
        if (headerElement == null) return "";

        try
        {
            // Walk up to find the header container (parent of focused header cell)
            var headerContainer = headerElement.parent;
            if (headerContainer == null) return "";

            var headerTexts = new System.Collections.Generic.List<string>();

            // Collect text from all sibling header cells
            for (int i = 0; i < headerContainer.childCount; i++)
            {
                var cell = headerContainer[i];
                var cellText = ExtractCellText(cell);
                if (!string.IsNullOrWhiteSpace(cellText))
                    headerTexts.Add(cellText);
            }

            return headerTexts.Count > 0
                ? "Table headers: " + string.Join(", ", headerTexts)
                : "";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"ExtractTableHeaderData failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Checks if the element is a TableRowNavigatable by IL2CPP type name.
    /// </summary>
    [HideFromIl2Cpp]
    public static bool IsTableRowNavigatable(VisualElement element)
    {
        if (element == null) return false;
        var typeName = GetIL2CppTypeName(element);
        return typeName.Contains("TableRowNavigatable") ||
               typeName.Contains("DropdownRowNavigatable");
    }

    /// <summary>
    /// Walks up the parent chain to find a TableRowNavigatable ancestor.
    /// Returns null if the element is not inside a table row.
    /// </summary>
    [HideFromIl2Cpp]
    public static VisualElement FindTableRowAncestor(VisualElement element)
    {
        if (element == null) return null;

        // First check if element itself is a table row
        if (IsTableRowNavigatable(element))
            return element;

        // Walk up parent chain (limit depth to avoid issues)
        var current = element.parent;
        int depth = 0;
        const int maxDepth = 20;

        while (current != null && depth < maxDepth)
        {
            if (IsTableRowNavigatable(current))
                return current;
            current = current.parent;
            depth++;
        }

        return null;
    }

    /// <summary>
    /// Extracts text from all cells in a table row, left-to-right order.
    /// Returns comma-separated cell values suitable for NVDA announcement.
    /// </summary>
    [HideFromIl2Cpp]
    public static string ExtractTableRowData(VisualElement tableRow)
    {
        if (tableRow == null) return "";

        try
        {
            var cellTexts = new System.Collections.Generic.List<string>();

            // Get all direct children of the row (the cells)
            for (int i = 0; i < tableRow.childCount; i++)
            {
                var cell = tableRow[i];
                var cellText = ExtractCellText(cell);

                if (!string.IsNullOrWhiteSpace(cellText))
                {
                    cellTexts.Add(cellText);
                }
            }

            if (cellTexts.Count == 0)
                return "";

            return string.Join(", ", cellTexts);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"ExtractTableRowData failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Extracts text from a single table cell, handling nested elements.
    /// </summary>
    [HideFromIl2Cpp]
    private static string ExtractCellText(VisualElement cell)
    {
        if (cell == null) return "";

        try
        {
            // Skip invisible cells
            if (!cell.visible ||
                cell.resolvedStyle.display == DisplayStyle.None ||
                cell.resolvedStyle.opacity < 0.1f)
            {
                return "";
            }

            // Collect all text from TextElement descendants
            var textParts = new System.Collections.Generic.List<string>();
            CollectTextFromCell(cell, textParts);

            if (textParts.Count == 0)
                return "";

            // Join with space, strip rich text, clean up
            var rawText = string.Join(" ", textParts);
            var strippedText = StripRichTextTags(rawText);
            return CleanText(strippedText);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Recursively collects text from all TextElement descendants in a cell.
    /// </summary>
    [HideFromIl2Cpp]
    private static void CollectTextFromCell(VisualElement element, System.Collections.Generic.List<string> textParts)
    {
        if (element == null) return;

        // If this is a text element, get its text
        if (element is TextElement textEl)
        {
            var text = textEl.text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                textParts.Add(text);
            }
        }

        // Recurse into children
        for (int i = 0; i < element.childCount; i++)
        {
            CollectTextFromCell(element[i], textParts);
        }
    }

    /// <summary>
    /// Converts element names to readable text, with aggressive filtering of internal names.
    /// Returns empty string for names that look like internal identifiers.
    /// </summary>
    [HideFromIl2Cpp]
    private static string CleanElementName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        // Skip entirely internal names
        if (IsInternalName(name))
            return "";

        // Special case: Yes/No buttons with -SIButton suffix
        if (name.EndsWith("-SIButton", StringComparison.OrdinalIgnoreCase))
        {
            var cleanName = name.Substring(0, name.Length - "-SIButton".Length);
            if (cleanName.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return "Yes";
            if (cleanName.Equals("no", StringComparison.OrdinalIgnoreCase))
                return "No";
        }

        // Remove common suffixes
        var cleaned = name;
        foreach (var suffix in new[] { "-SIButton", "-SICheckBox", "-SIToggle", "-SIDropdown",
                                        "Default", "Container", "Button", "Toggle", "Checkbox",
                                        "Switch", "Input", "Field", "Panel", "Root", "Content" })
        {
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(0, cleaned.Length - suffix.Length);
        }

        // Remove common prefixes
        foreach (var prefix in new[] { "SI", "button-", "toggle-", "checkbox-" })
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(prefix.Length);
        }

        // If nothing meaningful left, return empty
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length < 2)
            return "";

        // Check again if the cleaned version is internal
        if (IsInternalName(cleaned))
            return "";

        // Convert kebab-case and snake_case to spaces
        cleaned = cleaned
            .Replace("-", " ")
            .Replace("_", " ");

        // Capitalize first letter of each word
        if (cleaned.Length > 0)
        {
            var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
            }
            cleaned = string.Join(" ", words);
        }

        return cleaned;
    }

    /// <summary>
    /// Finds the section header text for an element by walking up the tree.
    /// Searches for header text in sibling elements that come before the current element.
    /// </summary>
    [HideFromIl2Cpp]
    public static string FindSectionHeader(VisualElement element)
    {
        if (element == null) return "";

        try
        {
            // Special case: dropdowns with descriptive parent names (Age Gate)
            var parentName = element.parent?.name ?? "";
            if (parentName.Contains("months-dropdown"))
                return "Age Gate Month";
            if (parentName.Contains("years-dropdown"))
                return "Age Gate Year";

            var current = element;
            int depth = 0;
            const int maxDepth = 10;

            while (current != null && depth < maxDepth)
            {
                depth++;

                // Search siblings that come BEFORE this element for header text
                var parent = current.parent;
                if (parent != null)
                {
                    // Find index of current element
                    int currentIndex = -1;
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        if (parent[i] == current)
                        {
                            currentIndex = i;
                            break;
                        }
                    }

                    // Search siblings BEFORE current (from closest to furthest)
                    for (int i = currentIndex - 1; i >= 0; i--)
                    {
                        var sibling = parent[i];

                        // Look for text elements that look like headers
                        try
                        {
                            var allTexts = sibling.Query<TextElement>(name: (string)null, className: (string)null).ToList();
                            foreach (var textEl in allTexts)
                            {
                                var text = textEl.text;
                                if (!string.IsNullOrWhiteSpace(text) &&
                                    text.Length > 5 &&
                                    text.Length < 150 &&
                                    !text.Contains(".") &&
                                    (text.Contains("Choice") ||
                                     text.Contains("Regarding") ||
                                     text.Contains("Age Gate") ||
                                     text.Contains("Privacy Polic") ||  // More specific - avoids "Transfer policy"
                                     text.Contains("Match Day Advertising") ||
                                     text.Contains("Marketing Campaigns")))
                                {
                                    return CleanSectionHeader(text);
                                }
                            }
                        }
                        catch { }
                    }
                }

                current = parent;
            }
        }
        catch { }

        return "";
    }

    /// <summary>
    /// Cleans up verbose section header text to a concise form.
    /// </summary>
    [HideFromIl2Cpp]
    private static string CleanSectionHeader(string header)
    {
        if (string.IsNullOrEmpty(header)) return "";

        // Map verbose headers to concise forms
        if (header.Contains("Match Day Advertising"))
            return "Match Day Advertising";
        if (header.Contains("Marketing Campaigns"))
            return "Marketing Campaigns";
        if (header.Contains("Age Gate"))
            return "Age Gate";
        if (header.Contains("Privacy Polic"))
            return "Privacy Policies";

        // Fallback: remove common prefixes
        return header
            .Replace("Your Choice Regarding ", "")
            .Replace("Our ", "")
            .Trim();
    }
}
