using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine.UIElements;
using SI.Bindable;
using FM26Access.Patches;

namespace FM26Access.UI;

/// <summary>
/// Utility class to extract text content from UI Toolkit elements,
/// including Sports Interactive custom elements (SIText, SIButton, etc.).
/// </summary>
public static class TextExtractor
{
    // Cache for BindingRoot lookups to avoid repeated traversals
    private static readonly System.Collections.Generic.Dictionary<IntPtr, VisualElement> _bindingRootCache = new();
    private static DateTime _lastCacheClear = DateTime.Now;
    private const int CACHE_LIFETIME_SECONDS = 30;

    /// <summary>
    /// Finds the nearest BindingRoot or AsyncBindingRoot ancestor.
    /// BindingRoot elements group semantically related UI content in FM26's binding system.
    /// </summary>
    [HideFromIl2Cpp]
    private static VisualElement FindBindingRoot(VisualElement element)
    {
        if (element == null) return null;

        // Check cache first (with periodic clearing)
        if ((DateTime.Now - _lastCacheClear).TotalSeconds > CACHE_LIFETIME_SECONDS)
        {
            _bindingRootCache.Clear();
            _lastCacheClear = DateTime.Now;
        }

        var elementPtr = element.Pointer;
        if (_bindingRootCache.TryGetValue(elementPtr, out var cached))
            return cached;

        var current = element.parent;
        int depth = 0;
        const int maxDepth = 30;

        while (current != null && depth < maxDepth)
        {
            var typeName = GetIL2CppTypeName(current);
            if (typeName.Contains("BindingRoot") || typeName.Contains("AsyncBindingRoot"))
            {
                _bindingRootCache[elementPtr] = current;
                return current;
            }
            current = current.parent;
            depth++;
        }

        // Cache null result too
        _bindingRootCache[elementPtr] = null;
        return null;
    }

    /// <summary>
    /// Finds label text by looking for elements with semantic names within a BindingRoot.
    /// FM26 uses names like "desc", "label", "text" for label elements.
    /// </summary>
    [HideFromIl2Cpp]
    private static string FindLabelBySemanticName(VisualElement bindingRoot, VisualElement exclude)
    {
        if (bindingRoot == null) return "";

        try
        {
            // Priority 1: Element named "desc" (common in FM26 UI)
            var descText = bindingRoot.Q<TextElement>(name: "desc", className: (string)null);
            if (descText != null && descText != exclude && !string.IsNullOrWhiteSpace(descText.text))
                return descText.text;

            // Priority 2: Element named "label" or "title"
            foreach (var name in new[] { "label", "title", "text", "description" })
            {
                var labelEl = bindingRoot.Q<TextElement>(name: name, className: (string)null);
                if (labelEl != null && labelEl != exclude && !string.IsNullOrWhiteSpace(labelEl.text))
                    return labelEl.text;
            }

            // Priority 3: Any SIText element within the BindingRoot
            var allTexts = bindingRoot.Query<TextElement>(name: (string)null, className: (string)null).ToList();
            foreach (var textEl in allTexts)
            {
                // Skip the element we're trying to find a label for
                if (textEl == exclude) continue;

                // Check if this is an SIText element
                var typeName = GetIL2CppTypeName(textEl);
                if (typeName.Contains("SIText"))
                {
                    var text = textEl.text?.Trim();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length > 1 && text.Length < 200)
                        return text;
                }
            }
        }
        catch { }

        return "";
    }

    /// <summary>
    /// Searches within a BindingRoot for label text, using the semantic structure.
    /// </summary>
    [HideFromIl2Cpp]
    private static string FindLabelWithinBindingRoot(VisualElement element)
    {
        var bindingRoot = FindBindingRoot(element);
        if (bindingRoot == null) return "";

        return FindLabelBySemanticName(bindingRoot, element);
    }

    /// <summary>
    /// Finds section header that appears above an element in the hierarchy.
    /// Looks for text ending with "..." or ":" in preceding siblings.
    /// Returns ONLY the first header found - stops immediately.
    /// </summary>
    [HideFromIl2Cpp]
    public static string FindSectionContext(VisualElement element)
    {
        if (element == null) return "";

        try
        {
            var current = element.parent;
            int depth = 0;
            const int maxDepth = 8; // Need 8 levels to find headers like "I was born on..."

            while (current != null && depth < maxDepth)
            {
                depth++;

                // Look for text elements in siblings that come BEFORE the current element's subtree
                var parent = current.parent;
                if (parent != null)
                {
                    // Find index of current element in parent
                    int currentIndex = -1;
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        if (parent[i] == current)
                        {
                            currentIndex = i;
                            break;
                        }
                    }

                    // Search siblings BEFORE current (closest first, going backwards)
                    for (int i = currentIndex - 1; i >= 0; i--)
                    {
                        var sibling = parent[i];
                        var header = ExtractSectionHeaderFromElement(sibling);
                        if (!string.IsNullOrWhiteSpace(header))
                        {
                            // Found a header - return immediately, don't keep searching
                            return header;
                        }
                    }
                }

                current = parent;
            }

            return "";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"FindSectionContext failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Extracts section header text from an element if it looks like a header.
    /// Headers end with "..." or ":" and are short (< 100 chars).
    /// Only returns the FIRST header found - no description gathering.
    /// </summary>
    [HideFromIl2Cpp]
    private static string ExtractSectionHeaderFromElement(VisualElement element)
    {
        if (element == null) return "";

        try
        {
            // Get all text elements
            var allTexts = element.Query<TextElement>(name: (string)null, className: (string)null).ToList();

            foreach (var textEl in allTexts)
            {
                var text = textEl.text?.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                // Skip very long text (not a header)
                if (text.Length > 100) continue;

                // Skip very short text (probably not meaningful)
                if (text.Length < 3) continue;

                // Check if it looks like a section header
                if (text.EndsWith("...") || text.EndsWith(":"))
                {
                    return CleanText(text);
                }
            }

            // Also check if the element itself is a TextElement
            if (element is TextElement te)
            {
                var text = te.text?.Trim();
                if (!string.IsNullOrWhiteSpace(text) && text.Length >= 3 && text.Length <= 100)
                {
                    if (text.EndsWith("...") || text.EndsWith(":"))
                    {
                        return CleanText(text);
                    }
                }
            }
        }
        catch { }

        return "";
    }

    /// <summary>
    /// Extracts description text from an element (text that follows a header).
    /// Description text is short (< 150 chars) and doesn't look like a header.
    /// </summary>
    [HideFromIl2Cpp]
    private static string ExtractDescriptionText(VisualElement element)
    {
        if (element == null) return "";

        try
        {
            // Get all text elements
            var allTexts = element.Query<TextElement>(name: (string)null, className: (string)null).ToList();

            foreach (var textEl in allTexts)
            {
                var text = textEl.text?.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                // Skip if it looks like a header (ends with : or ...)
                if (text.EndsWith("...") || text.EndsWith(":")) continue;

                // Skip very long text
                if (text.Length > 150) continue;

                // Skip very short text
                if (text.Length < 5) continue;

                // This looks like description text
                return CleanText(text);
            }
        }
        catch { }

        return "";
    }

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

            // NOTE: BindingRoot search removed from here - it was too aggressive
            // and found unrelated text like "jump to top" from scroll controls.
            // BindingRoot search is still used in FindFormFieldLabel and FindCheckboxLabel
            // as a targeted first attempt for those specific element types.

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

            // 3.5. FORM FIELD LABELS (for text inputs, date pickers, navigatable elements)
            if (typeName.Contains("BindableTextEditBox") ||
                typeName.Contains("DateSelectorWidget") ||
                typeName.Contains("NavigatableVisualElement") ||
                typeName.Contains("SITextField") ||
                typeName.Contains("SITextInput") ||
                element is TextField)
            {
                var formLabel = FindFormFieldLabel(element);
                if (!string.IsNullOrWhiteSpace(formLabel))
                    return formLabel;
            }

            // 3.6. BUTTONS WITHOUT LABELS (find adjacent descriptive text)
            if (typeName.Contains("SIButton") || element is Button)
            {
                var formLabel = FindFormFieldLabel(element);
                if (!string.IsNullOrWhiteSpace(formLabel))
                    return formLabel;
            }

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
    /// First checks the Harmony patch cache for intercepted binding values,
    /// then falls back to reading the field directly.
    /// </summary>
    [HideFromIl2Cpp]
    private static string TryGetSIButtonText(VisualElement element)
    {
        try
        {
            // Priority 1: Check Harmony patch cache (may have intercepted binding value)
            var cachedLabel = SIButtonTextPatch.GetCachedLabel(element);
            if (!string.IsNullOrWhiteSpace(cachedLabel))
                return cachedLabel;

            // Priority 2: Read directly from the field
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
            // NOTE: BindingRoot search removed - it was too aggressive and found
            // unrelated text like "jump to top" from scroll controls.
            // Original sibling search works better for checkbox labels.

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
            string rawValue = null;

            // Standard Unity DropdownField
            if (element is DropdownField dropdown)
                rawValue = dropdown.value;
            else
            {
                // Try getting value property via reflection
                var type = element.GetType();
                var prop = type.GetProperty("value");
                if (prop != null)
                {
                    var val = prop.GetValue(element);
                    if (val != null)
                        rawValue = val.ToString();
                }

                // Try text property for SI dropdowns (shows selected text)
                if (string.IsNullOrEmpty(rawValue))
                {
                    var textEl = element.Q<TextElement>();
                    if (textEl != null && !string.IsNullOrWhiteSpace(textEl.text))
                        rawValue = textEl.text;
                }
            }

            // Strip Rich Text tags (color, link, style, etc.) before returning
            if (!string.IsNullOrEmpty(rawValue))
                return StripRichTextTags(rawValue);
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
    /// Cleans raw text by stripping Rich Text tags, trimming and removing excessive whitespace.
    /// </summary>
    [HideFromIl2Cpp]
    private static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // Strip Rich Text tags first, then normalize whitespace
        var stripped = StripRichTextTags(text);

        // Normalize whitespace, remove newlines for speech
        return stripped
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("  ", " ")
            .Trim();
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

    /// <summary>
    /// Finds the label text associated with a form field (text input, date picker, etc.)
    /// by searching adjacent siblings in the visual hierarchy.
    /// </summary>
    [HideFromIl2Cpp]
    public static string FindFormFieldLabel(VisualElement element)
    {
        if (element == null) return "";

        try
        {
            // NOTE: BindingRoot search removed - it was too aggressive and found
            // unrelated text like "jump to top" from scroll controls.
            // Original sibling search works better for form field labels.

            var current = element;
            int depth = 0;
            const int maxDepth = 4;      // Search up to 4 levels in hierarchy
            const int maxSiblings = 5;   // Search up to 5 siblings in each direction

            while (current != null && depth < maxDepth)
            {
                depth++;

                var parent = current.parent;
                if (parent == null)
                {
                    current = null;
                    continue;
                }

                // Find index of current element in parent
                int currentIndex = -1;
                for (int i = 0; i < parent.childCount; i++)
                {
                    if (parent[i] == current)
                    {
                        currentIndex = i;
                        break;
                    }
                }

                // Check FOLLOWING siblings at ALL depths (for labels on right side)
                int followingLimit = Math.Min(parent.childCount, currentIndex + maxSiblings + 1);
                for (int i = currentIndex + 1; i < followingLimit; i++)
                {
                    var label = ExtractLabelFromSibling(parent[i]);
                    if (!string.IsNullOrWhiteSpace(label))
                        return label;
                }

                // Search PRECEDING siblings (closest first)
                int precedingLimit = Math.Max(0, currentIndex - maxSiblings);
                for (int i = currentIndex - 1; i >= precedingLimit; i--)
                {
                    var label = ExtractLabelFromSibling(parent[i]);
                    if (!string.IsNullOrWhiteSpace(label))
                        return label;
                }

                // Move up to parent for next iteration
                current = parent;
            }
        }
        catch { }

        return "";
    }

    /// <summary>
    /// Extracts label text from a sibling element.
    /// Simplified: only checks for non-empty text with reasonable length.
    /// </summary>
    [HideFromIl2Cpp]
    private static string ExtractLabelFromSibling(VisualElement sibling)
    {
        if (sibling == null) return "";

        // Skip invisible siblings
        if (!sibling.visible ||
            sibling.resolvedStyle.display == DisplayStyle.None)
            return "";

        // Query all text elements in sibling
        var allTexts = sibling.Query<TextElement>(name: (string)null, className: (string)null).ToList();

        foreach (var textEl in allTexts)
        {
            var text = textEl.text?.Trim();

            // Simplified criteria: just check for reasonable text
            if (!string.IsNullOrWhiteSpace(text) &&
                text.Length >= 2 &&
                text.Length <= 100)
            {
                return CleanText(text);
            }
        }

        return "";
    }

    /// <summary>
    /// Checks if text contains a sentence period (but allows "..." ellipsis).
    /// Used to filter out sentences/descriptions while allowing labels like "I was born on..."
    /// </summary>
    [HideFromIl2Cpp]
    private static bool ContainsSentencePeriod(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // Remove ellipsis patterns, then check for remaining periods
        var withoutEllipsis = text.Replace("...", "").Replace("…", "");
        return withoutEllipsis.Contains(".");
    }

    // =============================================
    // READING MODE SUPPORT
    // =============================================

    /// <summary>
    /// Determines if an element should be included in Reading Mode.
    /// Includes all text-bearing elements, not just focusable ones.
    /// </summary>
    [HideFromIl2Cpp]
    public static bool IsReadableElement(VisualElement element)
    {
        if (element == null) return false;

        var typeName = GetIL2CppTypeName(element);

        // Skip pure container/wrapper elements
        var name = element.name?.ToLowerInvariant() ?? "";
        if (IsContainerName(name)) return false;

        // Include text elements
        if (element is TextElement) return true;
        if (typeName.Contains("SIText")) return true;
        if (typeName.Contains("Label")) return true;

        // Include interactive elements (they have labels)
        if (typeName.Contains("SIButton")) return true;
        if (typeName.Contains("SICheckBox")) return true;
        if (typeName.Contains("SIDropdown")) return true;
        if (typeName.Contains("SIRadioButton")) return true;
        if (typeName.Contains("SIToggle")) return true;
        if (element is Button) return true;
        if (element is Toggle) return true;
        if (element is DropdownField) return true;

        // Include table cells and rows
        if (typeName.Contains("TableRowNavigatable")) return true;
        if (typeName.Contains("StreamedTable")) return true;

        // Check for text content as last resort
        if (element is TextElement textEl && !string.IsNullOrWhiteSpace(textEl.text))
            return true;

        return false;
    }

    /// <summary>
    /// Names that indicate container/wrapper elements to skip.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> ContainerNames = new()
    {
        "container", "wrapper", "root", "content", "body",
        "unity-content-container", "unity-drag-container",
        "unity-scrollview__content-container", "unity-scroller"
    };

    /// <summary>
    /// Checks if a name indicates a container element.
    /// </summary>
    [HideFromIl2Cpp]
    private static bool IsContainerName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        foreach (var container in ContainerNames)
        {
            if (name.Contains(container)) return true;
        }
        return false;
    }

    /// <summary>
    /// Gets a type hint for a readable element (heading, paragraph, button, etc.)
    /// Used in Reading Mode announcements.
    /// </summary>
    [HideFromIl2Cpp]
    public static string GetReadableTypeHint(VisualElement element)
    {
        if (element == null) return "text";

        var typeName = GetIL2CppTypeName(element);
        var name = element.name?.ToLowerInvariant() ?? "";

        // Check for headers
        if (name.Contains("header") || name.Contains("title") || name.Contains("heading"))
            return "heading";

        // Check CSS classes for header
        if (element.ClassListContains("header") || element.ClassListContains("title") ||
            element.ClassListContains("heading") || element.ClassListContains("h1") ||
            element.ClassListContains("h2") || element.ClassListContains("h3"))
            return "heading";

        // Interactive element types
        if (IsCloseButton(element)) return "close button";
        if (typeName.Contains("SIButton") || element is Button) return "button";
        if (typeName.Contains("SICheckBox") || element is Toggle) return "checkbox";
        if (typeName.Contains("SIDropdown") || element is DropdownField) return "dropdown";
        if (typeName.Contains("SIRadioButton")) return "radio button";
        if (typeName.Contains("Link") || element.ClassListContains("link")) return "link";

        // Table elements
        if (typeName.Contains("TableRowNavigatable")) return "table row";
        if (typeName.Contains("StreamedTable")) return "table";

        // Labels and descriptions
        if (name.Contains("label") || name.Contains("desc")) return "label";

        // Default for text elements
        if (element is TextElement) return "text";
        if (typeName.Contains("SIText")) return "text";

        return "";
    }
}
