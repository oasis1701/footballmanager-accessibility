using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using FM26Access.Navigation;

namespace FM26Access.UI;

/// <summary>
/// Deep scans the Unity scene hierarchy to discover ALL UI elements.
/// FM26 uses custom UI components, not standard UGUI.
/// </summary>
public class UIScanner : MonoBehaviour
{
    // IL2CPP requires this constructor for injected types
    public UIScanner(IntPtr ptr) : base(ptr) { }

    private string _outputPath = "";

    /// <summary>
    /// Gets the actual IL2CPP type name for a component, not the Mono proxy type.
    /// </summary>
    [HideFromIl2Cpp]
    private string GetIl2CppTypeName(Component comp)
    {
        try
        {
            IntPtr objectPtr = comp.Pointer;
            if (objectPtr == IntPtr.Zero) return "Unknown";

            IntPtr classPtr = IL2CPP.il2cpp_object_get_class(objectPtr);
            if (classPtr == IntPtr.Zero) return "Unknown";

            IntPtr namePtr = IL2CPP.il2cpp_class_get_name(classPtr);
            string name = Marshal.PtrToStringAnsi(namePtr) ?? "Unknown";

            IntPtr nsPtr = IL2CPP.il2cpp_class_get_namespace(classPtr);
            string ns = Marshal.PtrToStringAnsi(nsPtr) ?? "";

            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"GetIl2CppTypeName failed: {ex.Message}");
            return comp.GetType().Name;
        }
    }

    // Unity lifecycle methods must NOT have [HideFromIl2Cpp]
    public void Awake()
    {
        // Set output path in BepInEx folder
        var pluginPath = Path.GetDirectoryName(typeof(UIScanner).Assembly.Location);
        var bepInExPath = Path.GetDirectoryName(pluginPath);
        bepInExPath = Path.GetDirectoryName(bepInExPath);
        _outputPath = Path.Combine(bepInExPath ?? "", "UIDiscovery.txt");
        Plugin.Log.LogInfo("UIScanner initialized - press Ctrl+Shift+S to scan");
    }

    public void Update()
    {
        // No auto-scan - user triggers manually with Ctrl+Shift+S
    }

    [HideFromIl2Cpp]
    public void PerformFullScan()
    {
        Plugin.Log.LogInfo("Starting DEEP UI Discovery scan...");

        var sb = new StringBuilder();
        sb.AppendLine("=== FM26 DEEP UI DISCOVERY REPORT ===");
        sb.AppendLine($"Generated: {DateTime.Now}");
        sb.AppendLine($"Unity Version: {Application.unityVersion}");
        sb.AppendLine($"Game Version: {Application.version}");
        sb.AppendLine($"Active Scene: {SceneManager.GetActiveScene().name}");
        sb.AppendLine();

        // Scan all loaded scenes
        ScanAllScenes(sb);
        sb.AppendLine();

        // Scan DontDestroyOnLoad objects
        ScanDontDestroyOnLoadObjects(sb);
        sb.AppendLine();

        // UI Toolkit (UIDocument) scan - THIS IS WHERE FM26 UI ACTUALLY LIVES
        ScanUIDocuments(sb);
        sb.AppendLine();

        // Component type summary across all objects
        sb.AppendLine("=== COMPONENT TYPE SUMMARY ===");
        ScanAllComponentTypes(sb);
        sb.AppendLine();

        // Assembly exploration for FM26 UI types (shortened - remove if too verbose)
        sb.AppendLine(AssemblyExplorer.ExploreAssemblies());

        // Write to file
        try
        {
            File.WriteAllText(_outputPath, sb.ToString());
            Plugin.Log.LogInfo($"UI Discovery saved to: {_outputPath}");
            Core.NVDAOutput.Speak("Deep scan complete.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to write UI Discovery: {ex}");
            Core.NVDAOutput.Speak("Scan failed");
        }
    }

    [HideFromIl2Cpp]
    private void DeepScanGameObject(GameObject obj, StringBuilder sb, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;

        var indent = new string(' ', depth * 2);
        var components = obj.GetComponents<Component>();

        // Build component list with full type names
        var compList = new List<string>();
        foreach (var comp in components)
        {
            if (comp != null)
            {
                var typeName = GetIl2CppTypeName(comp);
                compList.Add(typeName);
            }
        }

        // Mark if object is active
        var activeMarker = obj.activeInHierarchy ? "" : " [INACTIVE]";

        sb.AppendLine($"{indent}{obj.name}{activeMarker}");
        sb.AppendLine($"{indent}  Components: [{string.Join(", ", compList)}]");

        // For each component, try to extract useful info
        foreach (var comp in components)
        {
            if (comp == null) continue;

            var typeName = GetIl2CppTypeName(comp);

            // Look for text content in any component with "Text" in the name
            if (typeName.Contains("Text") || typeName.Contains("TMP"))
            {
                TryExtractText(comp, sb, indent);
            }

            // Look for button/clickable components
            if (typeName.Contains("Button") || typeName.Contains("Click") ||
                typeName.Contains("Pointer") || typeName.Contains("Selectable"))
            {
                sb.AppendLine($"{indent}  ** INTERACTIVE: {typeName}");
            }
        }

        // Recurse into children
        for (int i = 0; i < obj.transform.childCount; i++)
        {
            var child = obj.transform.GetChild(i);
            DeepScanGameObject(child.gameObject, sb, depth + 1, maxDepth);
        }
    }

    [HideFromIl2Cpp]
    private void TryExtractText(Component comp, StringBuilder sb, string indent)
    {
        try
        {
            // Try to get text via reflection-like approach
            var type = comp.GetType();

            // Try common text property names
            foreach (var propName in new[] { "text", "Text", "m_text", "m_Text" })
            {
                try
                {
                    var prop = type.GetProperty(propName);
                    if (prop != null)
                    {
                        var value = prop.GetValue(comp);
                        if (value != null)
                        {
                            var textValue = value.ToString();
                            if (!string.IsNullOrWhiteSpace(textValue))
                            {
                                var truncated = textValue.Length > 100
                                    ? textValue.Substring(0, 100) + "..."
                                    : textValue;
                                truncated = truncated.Replace("\n", "\\n").Replace("\r", "");
                                sb.AppendLine($"{indent}  >> TEXT: \"{truncated}\"");
                                return;
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    [HideFromIl2Cpp]
    private void ScanAllComponentTypes(StringBuilder sb)
    {
        var typeCounts = new Dictionary<string, int>();
        var interestingTypes = new List<string>();

        var allObjects = FindObjectsOfType<GameObject>();
        foreach (var obj in allObjects)
        {
            var components = obj.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;

                var typeName = GetIl2CppTypeName(comp);

                if (!typeCounts.ContainsKey(typeName))
                    typeCounts[typeName] = 0;
                typeCounts[typeName]++;

                // Flag interesting types
                if (typeName.Contains("Button") || typeName.Contains("Click") ||
                    typeName.Contains("Text") || typeName.Contains("TMP") ||
                    typeName.Contains("Input") || typeName.Contains("Toggle") ||
                    typeName.Contains("Scroll") || typeName.Contains("Panel"))
                {
                    if (!interestingTypes.Contains(typeName))
                        interestingTypes.Add(typeName);
                }
            }
        }

        sb.AppendLine($"Total unique component types: {typeCounts.Count}");
        sb.AppendLine();

        sb.AppendLine("Potentially interesting UI types found:");
        foreach (var t in interestingTypes)
        {
            sb.AppendLine($"  - {t} (x{typeCounts[t]})");
        }

        sb.AppendLine();
        sb.AppendLine("All component types:");
        foreach (var kvp in typeCounts)
        {
            sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
        }
    }

    /// <summary>
    /// Scans all loaded scenes and their root objects.
    /// </summary>
    [HideFromIl2Cpp]
    private void ScanAllScenes(StringBuilder sb)
    {
        sb.AppendLine("=== ALL LOADED SCENES ===");

        var sceneCount = SceneManager.sceneCount;
        sb.AppendLine($"Total loaded scenes: {sceneCount}");

        for (int i = 0; i < sceneCount; i++)
        {
            try
            {
                var scene = SceneManager.GetSceneAt(i);
                sb.AppendLine($"\n--- Scene: {scene.name} (loaded: {scene.isLoaded}) ---");

                if (scene.isLoaded)
                {
                    var rootObjects = scene.GetRootGameObjects();
                    sb.AppendLine($"Root objects: {rootObjects.Length}");

                    foreach (var root in rootObjects)
                    {
                        sb.AppendLine($"========================================");
                        sb.AppendLine($"=== {root.name} ===");
                        sb.AppendLine($"========================================");
                        DeepScanGameObject(root, sb, 0, 10);
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"ERROR scanning scene {i}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Scans objects in the DontDestroyOnLoad scene.
    /// </summary>
    [HideFromIl2Cpp]
    private void ScanDontDestroyOnLoadObjects(StringBuilder sb)
    {
        sb.AppendLine("=== DONTDESTROYONLOAD OBJECTS ===");

        try
        {
            // Find all objects including inactive ones
            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

            // Filter to root objects in DontDestroyOnLoad
            var ddolRoots = allObjects.Where(go =>
            {
                try
                {
                    // Check if transform parent is null (root object)
                    if (go.transform.parent != null) return false;

                    // Check if in DontDestroyOnLoad scene
                    var sceneName = go.scene.name;
                    return sceneName == "DontDestroyOnLoad" ||
                           string.IsNullOrEmpty(sceneName) ||
                           !go.scene.IsValid();
                }
                catch { return false; }
            })
            .Where(go => go.name != "FM26AccessManager") // Exclude our own object
            .ToList();

            sb.AppendLine($"Found {ddolRoots.Count} DontDestroyOnLoad root objects");

            foreach (var root in ddolRoots.Take(30)) // Limit to prevent huge output
            {
                sb.AppendLine($"========================================");
                sb.AppendLine($"=== [DDOL] {root.name} ===");
                sb.AppendLine($"========================================");
                DeepScanGameObject(root, sb, 0, 8);
            }

            if (ddolRoots.Count > 30)
            {
                sb.AppendLine($"\n... and {ddolRoots.Count - 30} more DDOL objects");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"ERROR scanning DDOL: {ex.Message}");
            Plugin.Log.LogError($"DDOL scan error: {ex}");
        }
    }

    /// <summary>
    /// Scans all UIDocument components and their VisualElement trees.
    /// FM26 uses UI Toolkit (UIElements), not traditional UGUI.
    /// </summary>
    [HideFromIl2Cpp]
    private void ScanUIDocuments(StringBuilder sb)
    {
        sb.AppendLine("=== UI TOOLKIT (UIDOCUMENT) SCAN ===");

        try
        {
            // Find all UIDocument components in the scene
            var uiDocuments = Resources.FindObjectsOfTypeAll<UIDocument>();
            sb.AppendLine($"Found {uiDocuments.Length} UIDocument(s)");

            int docIndex = 0;
            foreach (var doc in uiDocuments)
            {
                try
                {
                    var goName = doc.gameObject?.name ?? "Unknown";
                    var sceneName = doc.gameObject?.scene.name ?? "Unknown";
                    sb.AppendLine();
                    sb.AppendLine($"--- UIDocument #{docIndex}: {goName} (Scene: {sceneName}) ---");

                    var root = doc.rootVisualElement;
                    if (root != null)
                    {
                        sb.AppendLine($"Root element childCount: {root.childCount}");
                        ScanVisualElement(root, sb, 0, 15); // Max depth 15
                    }
                    else
                    {
                        sb.AppendLine("  (rootVisualElement is null)");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  ERROR scanning UIDocument: {ex.Message}");
                }
                docIndex++;
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"ERROR: {ex.Message}");
            Plugin.Log.LogError($"UIDocument scan error: {ex}");
        }
    }

    /// <summary>
    /// Recursively scans a VisualElement and its children.
    /// </summary>
    [HideFromIl2Cpp]
    private void ScanVisualElement(VisualElement element, StringBuilder sb, int depth, int maxDepth)
    {
        if (element == null || depth > maxDepth) return;

        try
        {
            var indent = new string(' ', depth * 2);

            // Get element type name (use IL2CPP method for accurate type)
            string typeName;
            try
            {
                IntPtr objectPtr = element.Pointer;
                if (objectPtr != IntPtr.Zero)
                {
                    IntPtr classPtr = IL2CPP.il2cpp_object_get_class(objectPtr);
                    if (classPtr != IntPtr.Zero)
                    {
                        IntPtr namePtr = IL2CPP.il2cpp_class_get_name(classPtr);
                        typeName = Marshal.PtrToStringAnsi(namePtr) ?? element.GetType().Name;
                    }
                    else
                    {
                        typeName = element.GetType().Name;
                    }
                }
                else
                {
                    typeName = element.GetType().Name;
                }
            }
            catch
            {
                typeName = element.GetType().Name;
            }

            // Get element name
            var name = string.IsNullOrEmpty(element.name) ? "(unnamed)" : element.name;

            // Build status markers
            var markers = new List<string>();
            if (!element.visible) markers.Add("HIDDEN");
            if (!element.enabledSelf) markers.Add("DISABLED");
            if (!element.enabledInHierarchy) markers.Add("DISABLED-HIERARCHY");

            var markerStr = markers.Count > 0 ? $" [{string.Join(", ", markers)}]" : "";

            // Output element info
            sb.AppendLine($"{indent}{typeName}: \"{name}\"{markerStr}");

            // Try to extract text content
            TryExtractVisualElementText(element, sb, indent);

            // Get USS classes - manually enumerate for IL2CPP compatibility
            try
            {
                var classes = new List<string>();
                var enumerable = element.GetClasses();
                // Use Add via reflection or just check if classList has any
                if (element.ClassListContains("unity-button") ||
                    element.ClassListContains("button") ||
                    element.ClassListContains("accept") ||
                    element.ClassListContains("confirm") ||
                    element.ClassListContains("primary"))
                {
                    sb.AppendLine($"{indent}  Classes: [contains button/accept/confirm/primary class]");
                }
            }
            catch { }

            // Mark interactive elements
            if (typeName.Contains("Button") || typeName.Contains("Toggle") ||
                typeName.Contains("TextField") || typeName.Contains("Slider") ||
                typeName.Contains("Dropdown") || typeName.Contains("Foldout"))
            {
                sb.AppendLine($"{indent}  ** INTERACTIVE **");
            }

            // Recurse into children - use childCount and ElementAt for IL2CPP compatibility
            try
            {
                var childCount = element.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    var child = element[i]; // VisualElement indexer
                    ScanVisualElement(child, sb, depth + 1, maxDepth);
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            var indent = new string(' ', depth * 2);
            sb.AppendLine($"{indent}ERROR: {ex.Message}");
        }
    }

    /// <summary>
    /// Tries to extract text content from a VisualElement.
    /// </summary>
    [HideFromIl2Cpp]
    private void TryExtractVisualElementText(VisualElement element, StringBuilder sb, string indent)
    {
        try
        {
            // Try to cast to TextElement (base class for Label, Button text, etc.)
            if (element is TextElement textEl)
            {
                var text = textEl.text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var truncated = text.Length > 100 ? text.Substring(0, 100) + "..." : text;
                    truncated = truncated.Replace("\n", "\\n").Replace("\r", "");
                    sb.AppendLine($"{indent}  TEXT: \"{truncated}\"");
                }
                return;
            }

            // Try reflection for 'text' property on other elements
            var type = element.GetType();
            var textProp = type.GetProperty("text");
            if (textProp != null)
            {
                var value = textProp.GetValue(element);
                if (value != null)
                {
                    var text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var truncated = text.Length > 100 ? text.Substring(0, 100) + "..." : text;
                        truncated = truncated.Replace("\n", "\\n").Replace("\r", "");
                        sb.AppendLine($"{indent}  TEXT: \"{truncated}\"");
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Force a UI scan - triggered by Ctrl+Shift+S
    /// </summary>
    [HideFromIl2Cpp]
    public void ForceScan()
    {
        Plugin.Log.LogInfo("ForceScan called");
        PerformFullScan();
    }

    /// <summary>
    /// Finds the PanelManager UIDocument and returns its root element.
    /// </summary>
    [HideFromIl2Cpp]
    public VisualElement GetPanelManagerRoot()
    {
        try
        {
            var uiDocuments = Resources.FindObjectsOfTypeAll<UIDocument>();
            foreach (var doc in uiDocuments)
            {
                if (doc.gameObject?.name == "PanelManager")
                {
                    return doc.rootVisualElement;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"GetPanelManagerRoot failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Finds the currently active/visible Panel element.
    /// FM26 shows panels in the "Card" container.
    /// </summary>
    [HideFromIl2Cpp]
    public (VisualElement panel, string panelName) GetActivePanel()
    {
        try
        {
            var root = GetPanelManagerRoot();
            if (root == null)
            {
                Plugin.Log.LogWarning("PanelManager root not found");
                return (null, "");
            }

            // FM26 structure: PanelManager-container > Card > Body or directly > Panel
            // Look for visible Panel elements
            var cardContainer = root.Q(name: "Card", className: (string)null);
            if (cardContainer == null)
            {
                Plugin.Log.LogWarning("Card container not found");
                return (null, "");
            }

            // Find visible Panel children
            for (int i = 0; i < cardContainer.childCount; i++)
            {
                var child = cardContainer[i];
                var typeName = TextExtractor.GetIL2CppTypeName(child);

                if (typeName == "Panel" && child.visible && child.enabledInHierarchy)
                {
                    var panelName = child.name ?? "Unknown Panel";
                    Plugin.Log.LogInfo($"Found active panel: {panelName}");
                    return (child, panelName);
                }

                // Also check children of child (Body > Panel structure)
                for (int j = 0; j < child.childCount; j++)
                {
                    var grandChild = child[j];
                    var grandTypeName = TextExtractor.GetIL2CppTypeName(grandChild);

                    if (grandTypeName == "Panel" && grandChild.visible && grandChild.enabledInHierarchy)
                    {
                        var panelName = grandChild.name ?? "Unknown Panel";
                        Plugin.Log.LogInfo($"Found active panel (nested): {panelName}");
                        return (grandChild, panelName);
                    }
                }
            }

            // Fallback: just return the Card container itself
            Plugin.Log.LogInfo("No specific panel found, using Card container");
            return (cardContainer, "Card");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"GetActivePanel failed: {ex}");
            return (null, "");
        }
    }

    /// <summary>
    /// Gets the topmost visible container - either an active dialog or the highest layer panel.
    /// Dialogs are always considered topmost when visible.
    /// Used by Reading Mode to determine what content to read.
    /// </summary>
    [HideFromIl2Cpp]
    public (VisualElement container, string name, bool isDialog) GetTopmostContainer()
    {
        try
        {
            var root = GetPanelManagerRoot();
            if (root == null)
            {
                Plugin.Log.LogWarning("GetTopmostContainer: PanelManager root not found");
                return (null, "", false);
            }

            // Look for dialog panels first (they appear on top layers)
            // Dialog panels typically have "Dialog" in their name or type
            var dialogPanel = FindTopmostDialog(root);
            if (dialogPanel != null)
            {
                var dialogName = dialogPanel.name ?? "Dialog";
                Plugin.Log.LogInfo($"GetTopmostContainer: Found dialog: {dialogName}");
                return (dialogPanel, dialogName, true);
            }

            // Fall back to active panel
            var (panel, panelName) = GetActivePanel();
            if (panel != null)
            {
                return (panel, panelName, false);
            }

            // Last resort: use the root itself
            return (root, "Screen", false);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"GetTopmostContainer failed: {ex}");
            return (null, "", false);
        }
    }

    /// <summary>
    /// Searches for the topmost visible dialog panel.
    /// </summary>
    [HideFromIl2Cpp]
    private VisualElement FindTopmostDialog(VisualElement root)
    {
        try
        {
            // Look for panels on higher layers that might be dialogs
            // FM26 uses layer-based panel stacking
            var candidates = new System.Collections.Generic.List<(VisualElement element, int layerIndex)>();

            // Search the root for Panel elements
            SearchForDialogs(root, candidates, 0);

            // Return the one on the highest layer (if any are dialogs)
            if (candidates.Count > 0)
            {
                // Sort by layer index descending
                candidates.Sort((a, b) => b.layerIndex.CompareTo(a.layerIndex));
                return candidates[0].element;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"FindTopmostDialog error: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Recursively searches for dialog panels.
    /// </summary>
    [HideFromIl2Cpp]
    private void SearchForDialogs(VisualElement element, System.Collections.Generic.List<(VisualElement, int)> candidates, int depth)
    {
        if (element == null || depth > 10) return;

        var typeName = TextExtractor.GetIL2CppTypeName(element);
        var name = element.name?.ToLowerInvariant() ?? "";

        // Check if this looks like a dialog
        if (element.visible && element.enabledInHierarchy)
        {
            if (name.Contains("dialog") || name.Contains("modal") || name.Contains("popup") ||
                typeName.Contains("Dialog") || typeName.Contains("Modal"))
            {
                // Estimate layer from depth or position
                candidates.Add((element, depth));
            }
        }

        // Search children
        for (int i = 0; i < element.childCount; i++)
        {
            SearchForDialogs(element[i], candidates, depth + 1);
        }
    }

    /// <summary>
    /// Refreshes navigation for the current active panel.
    /// Waits briefly for bindings to complete before scanning.
    /// </summary>
    [HideFromIl2Cpp]
    public void RefreshNavigationForActivePanel()
    {
        // Use Invoke to wait for bindings to resolve (200ms should be enough)
        Invoke(nameof(DoRefreshNavigation), 0.2f);
    }

    /// <summary>
    /// Actually performs the navigation refresh after delay.
    /// </summary>
    public void DoRefreshNavigation()
    {
        try
        {
            var (panel, panelName) = GetActivePanel();

            if (panel != null)
            {
                // Use the panel root for navigation if available
                var navController = NavigationController.Instance;
                if (navController != null)
                {
                    navController.RefreshFocusableElements(panel, panelName);
                }
                else
                {
                    Plugin.Log.LogWarning("NavigationController.Instance is null");
                }
            }
            else
            {
                Core.NVDAOutput.Speak("Could not find active panel");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"DoRefreshNavigation failed: {ex}");
        }
    }

    /// <summary>
    /// Counts all descendant elements (used to detect when bindings complete).
    /// </summary>
    [HideFromIl2Cpp]
    private int CountDescendants(VisualElement element)
    {
        if (element == null) return 0;

        int count = 1;
        for (int i = 0; i < element.childCount; i++)
        {
            count += CountDescendants(element[i]);
        }
        return count;
    }
}
