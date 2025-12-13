using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Il2CppInterop.Runtime.Attributes;
using FM26Access.Core;
using FM26Access.UI;
using FM.UI;
using SI.UI;

namespace FM26Access.Navigation;

/// <summary>
/// Reading Mode controller for navigating through all visible text elements.
/// Toggle with backslash key. Navigate with arrow keys.
/// </summary>
public class ReadingMode : MonoBehaviour
{
    // IL2CPP requires this constructor
    public ReadingMode(IntPtr ptr) : base(ptr) { }

    public static ReadingMode Instance { get; private set; }

    /// <summary>
    /// Whether reading mode is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    private List<ReadableElement> _elements = new();
    private int _currentIndex = -1;
    private VisualElement _containerRoot;
    private string _containerName = "";

    // Debug counters
    private int _traverseCount = 0;
    private int _readableCount = 0;
    private int _textFoundCount = 0;

    // Row threshold for "same row" visual positioning (in pixels)
    private const float SameRowThreshold = 15f;

    public void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        Plugin.Log.LogInfo("ReadingMode initialized");
    }

    /// <summary>
    /// Toggles reading mode on or off.
    /// </summary>
    [HideFromIl2Cpp]
    public void Toggle()
    {
        if (IsActive)
            Disable();
        else
            Enable();
    }

    /// <summary>
    /// Enables reading mode. Scans for all readable elements on screen.
    /// </summary>
    [HideFromIl2Cpp]
    public void Enable()
    {
        if (IsActive) return;

        IsActive = true;
        Plugin.Log.LogInfo("Reading mode enabled");

        try
        {
            // Use PanelManager root directly - scan all visible content
            var scanner = FindObjectOfType<UIScanner>();
            _containerRoot = scanner?.GetPanelManagerRoot();
            _containerName = "Screen";

            // Discover all readable elements
            RefreshElements();

            // Find starting position based on current focus
            FindStartingPosition();

            // Announce mode activation
            var announcement = $"Reading mode. {_elements.Count} elements.";
            NVDAOutput.Speak(announcement);

            // Announce first element after short delay
            if (_elements.Count > 0 && _currentIndex >= 0)
            {
                Invoke(nameof(AnnounceCurrent), 0.3f);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"ReadingMode.Enable failed: {ex}");
            NVDAOutput.Speak("Reading mode on. Error scanning elements.");
        }
    }

    /// <summary>
    /// Disables reading mode.
    /// </summary>
    [HideFromIl2Cpp]
    public void Disable()
    {
        if (!IsActive) return;

        IsActive = false;
        _elements.Clear();
        _currentIndex = -1;
        _containerRoot = null;
        _containerName = "";

        Plugin.Log.LogInfo("Reading mode disabled");
        NVDAOutput.Speak("Reading mode off");
    }

    /// <summary>
    /// Refreshes the list of readable elements from the current container.
    /// </summary>
    [HideFromIl2Cpp]
    public void RefreshElements()
    {
        _elements.Clear();
        _traverseCount = 0;
        _readableCount = 0;
        _textFoundCount = 0;

        if (_containerRoot == null)
        {
            // Try to get panel manager root if no specific container
            var scanner = FindObjectOfType<UIScanner>();
            if (scanner != null)
            {
                _containerRoot = scanner.GetPanelManagerRoot();
            }
        }

        if (_containerRoot != null)
        {
            Plugin.Log.LogInfo($"ReadingMode: Root found, childCount = {_containerRoot.childCount}");
            CollectReadableElements(_containerRoot, 0);
            SortElementsByPosition();
        }
        else
        {
            Plugin.Log.LogWarning("ReadingMode: GetPanelManagerRoot returned null!");
        }

        Plugin.Log.LogInfo($"ReadingMode: Traversed {_traverseCount}, IsReadable {_readableCount}, HasText {_textFoundCount}, Added {_elements.Count}");
    }

    /// <summary>
    /// Recursively collects all readable elements from the element tree.
    /// </summary>
    [HideFromIl2Cpp]
    private void CollectReadableElements(VisualElement element, int depth)
    {
        if (element == null || depth > 50) return;

        _traverseCount++;

        try
        {
            // TEMPORARILY: No filters - collect everything to diagnose
            // FM26's UI system uses visibility/display differently
            var bounds = element.worldBound;

            // Check if this element has readable text
            if (TextExtractor.IsReadableElement(element))
            {
                _readableCount++;
                var text = TextExtractor.ExtractText(element);
                text = TextExtractor.StripRichTextTags(text);

                if (!string.IsNullOrWhiteSpace(text) && text.Length > 0)
                {
                    _textFoundCount++;
                    // Note: IsDuplicateOfParent was too aggressive - removed for now
                    _elements.Add(new ReadableElement
                    {
                        Element = element,
                        Text = text,
                        TypeHint = TextExtractor.GetReadableTypeHint(element),
                        Bounds = bounds,
                        Depth = depth
                    });
                }
            }

            // Recurse into children
            for (int i = 0; i < element.childCount; i++)
            {
                CollectReadableElements(element[i], depth + 1);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"CollectReadableElements error at depth {depth}: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if this element's text is a duplicate of its parent's text.
    /// </summary>
    [HideFromIl2Cpp]
    private bool IsDuplicateOfParent(VisualElement element, string text)
    {
        try
        {
            var parent = element.parent;
            if (parent == null) return false;

            var parentText = TextExtractor.ExtractText(parent);
            parentText = TextExtractor.StripRichTextTags(parentText);

            return !string.IsNullOrWhiteSpace(parentText) && parentText == text;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sorts elements by visual position (top-to-bottom, left-to-right).
    /// </summary>
    [HideFromIl2Cpp]
    private void SortElementsByPosition()
    {
        _elements.Sort((a, b) =>
        {
            // Elements on same row (within threshold) sort by X
            var yDiff = a.Bounds.y - b.Bounds.y;
            if (Math.Abs(yDiff) <= SameRowThreshold)
            {
                return a.Bounds.x.CompareTo(b.Bounds.x);
            }
            // Otherwise sort by Y
            return yDiff.CompareTo(0);
        });
    }

    /// <summary>
    /// Finds the starting position based on the current game focus.
    /// </summary>
    [HideFromIl2Cpp]
    private void FindStartingPosition()
    {
        _currentIndex = 0; // Default to first element

        if (_elements.Count == 0) return;

        try
        {
            // Get current focus from the game
            var focusListener = FocusListener.Instance;
            var currentFocus = focusListener?.CurrentFocusedElement;

            if (currentFocus != null)
            {
                // Find element closest to current focus
                var focusBounds = currentFocus.worldBound;
                float minDistance = float.MaxValue;
                int closestIndex = 0;

                for (int i = 0; i < _elements.Count; i++)
                {
                    // Check if it's the same element
                    if (_elements[i].Element == currentFocus)
                    {
                        _currentIndex = i;
                        return;
                    }

                    // Calculate distance to focus
                    var elemBounds = _elements[i].Bounds;
                    var dx = elemBounds.center.x - focusBounds.center.x;
                    var dy = elemBounds.center.y - focusBounds.center.y;
                    var distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance < minDistance)
                    {
                        minDistance = (float)distance;
                        closestIndex = i;
                    }
                }

                // Use closest if within reasonable range
                if (minDistance < 500)
                {
                    _currentIndex = closestIndex;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"FindStartingPosition error: {ex.Message}");
        }
    }

    /// <summary>
    /// Navigates to the next element (down in visual order).
    /// </summary>
    [HideFromIl2Cpp]
    public void NavigateDown()
    {
        if (!IsActive || _elements.Count == 0) return;

        _currentIndex++;
        if (_currentIndex >= _elements.Count)
            _currentIndex = 0; // Wrap to start

        AnnounceCurrent();
    }

    /// <summary>
    /// Navigates to the previous element (up in visual order).
    /// </summary>
    [HideFromIl2Cpp]
    public void NavigateUp()
    {
        if (!IsActive || _elements.Count == 0) return;

        _currentIndex--;
        if (_currentIndex < 0)
            _currentIndex = _elements.Count - 1; // Wrap to end

        AnnounceCurrent();
    }

    /// <summary>
    /// Navigates to previous sibling or parent element.
    /// </summary>
    [HideFromIl2Cpp]
    public void NavigateLeft()
    {
        if (!IsActive || _elements.Count == 0) return;

        var current = _elements[_currentIndex];
        var parent = current.Element.parent;

        // Find siblings at same depth with same parent
        var siblingsAtDepth = _elements
            .Select((e, idx) => (e, idx))
            .Where(x => x.e.Depth == current.Depth && x.e.Element.parent == parent)
            .ToList();

        var siblingListIndex = siblingsAtDepth.FindIndex(x => x.idx == _currentIndex);

        if (siblingListIndex > 0)
        {
            // Move to previous sibling
            _currentIndex = siblingsAtDepth[siblingListIndex - 1].idx;
        }
        else if (parent != null)
        {
            // Move to parent if no previous sibling
            var parentElement = _elements.FirstOrDefault(e => e.Element == parent);
            if (parentElement != null)
            {
                _currentIndex = _elements.IndexOf(parentElement);
            }
            else
            {
                // Parent not in list, just go up
                NavigateUp();
                return;
            }
        }

        AnnounceCurrent();
    }

    /// <summary>
    /// Navigates to first child or next sibling element.
    /// </summary>
    [HideFromIl2Cpp]
    public void NavigateRight()
    {
        if (!IsActive || _elements.Count == 0) return;

        var current = _elements[_currentIndex];

        // Find first child (element whose parent is current and comes after in list)
        var firstChild = _elements
            .Select((e, idx) => (e, idx))
            .Where(x => x.idx > _currentIndex && x.e.Element.parent == current.Element)
            .FirstOrDefault();

        if (firstChild.e != null)
        {
            _currentIndex = firstChild.idx;
        }
        else
        {
            // No children, try next sibling
            var parent = current.Element.parent;
            var siblings = _elements
                .Select((e, idx) => (e, idx))
                .Where(x => x.e.Depth == current.Depth && x.e.Element.parent == parent)
                .ToList();

            var siblingListIndex = siblings.FindIndex(x => x.idx == _currentIndex);
            if (siblingListIndex >= 0 && siblingListIndex < siblings.Count - 1)
            {
                _currentIndex = siblings[siblingListIndex + 1].idx;
            }
            else
            {
                // No next sibling, just go down
                NavigateDown();
                return;
            }
        }

        AnnounceCurrent();
    }

    /// <summary>
    /// Announces the current element via NVDA.
    /// </summary>
    public void AnnounceCurrent()
    {
        if (_currentIndex < 0 || _currentIndex >= _elements.Count)
        {
            NVDAOutput.Speak("No element");
            return;
        }

        var element = _elements[_currentIndex];

        // Build announcement
        string announcement;
        if (NavigationController.DebugMode)
        {
            announcement = element.BuildDebugAnnouncement();
        }
        else
        {
            announcement = element.BuildAnnouncement();
        }

        // Add position info
        var position = $"{_currentIndex + 1} of {_elements.Count}";

        NVDAOutput.Speak($"{announcement}. {position}");

        if (NavigationController.DebugMode)
        {
            Plugin.Log.LogInfo($"Reading [{_currentIndex}]: {element.TypeHint}: {element.Text}");
        }
    }

    public void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
