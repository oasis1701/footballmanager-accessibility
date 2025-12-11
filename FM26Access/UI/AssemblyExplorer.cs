using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInterop.Runtime;

namespace FM26Access.UI;

/// <summary>
/// Explores loaded assemblies to discover FM26's custom UI component types.
/// </summary>
public static class AssemblyExplorer
{
    // Priority assemblies likely to contain FM26 UI types
    private static readonly string[] PriorityAssemblies = new[]
    {
        "FM.UI",
        "SI.UI",
        "SI.Bindable",
        "FMGame",
        "FM.GamePlugin",
        "RTLTMPro",
        "Assembly-CSharp"
    };

    // Keywords indicating UI-related types
    private static readonly string[] UIKeywords = new[]
    {
        "Button", "Text", "Panel", "Click", "UI", "View", "Screen",
        "Dialog", "Modal", "Menu", "Widget", "Control", "Label",
        "Input", "Toggle", "Scroll", "List", "Grid", "Container",
        "Selectable", "Interactable", "Focusable", "Canvas", "Image"
    };

    /// <summary>
    /// Scan all loaded assemblies and find potential UI types.
    /// </summary>
    public static string ExploreAssemblies()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== FM26 ASSEMBLY EXPLORATION REPORT ===");
        sb.AppendLine($"Generated: {DateTime.Now}");
        sb.AppendLine();

        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            sb.AppendLine($"Total assemblies loaded: {assemblies.Length}");
            sb.AppendLine();

            // List all assemblies, highlighting priority ones
            sb.AppendLine("=== ALL LOADED ASSEMBLIES ===");
            foreach (var asm in assemblies.OrderBy(a => a.GetName().Name))
            {
                var name = asm.GetName().Name ?? "Unknown";
                var isPriority = PriorityAssemblies.Any(p =>
                    name.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                sb.AppendLine($"  {(isPriority ? "*** " : "")}{name}");
            }
            sb.AppendLine();

            // Deep scan priority assemblies for UI types
            sb.AppendLine("=== UI-RELATED TYPES IN PRIORITY ASSEMBLIES ===");
            foreach (var priorityName in PriorityAssemblies)
            {
                var matchingAssemblies = assemblies.Where(a =>
                {
                    var name = a.GetName().Name ?? "";
                    return name.Equals(priorityName, StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith(priorityName + ".", StringComparison.OrdinalIgnoreCase);
                }).ToList();

                foreach (var asm in matchingAssemblies)
                {
                    sb.AppendLine();
                    sb.AppendLine($"--- {asm.GetName().Name} ---");
                    ScanAssemblyForUITypes(asm, sb);
                }

                if (!matchingAssemblies.Any())
                {
                    sb.AppendLine($"  [NOT FOUND: {priorityName}]");
                }
            }

            // Scan for MonoBehaviour subclasses with UI keywords across all assemblies
            sb.AppendLine();
            sb.AppendLine("=== ALL MONOBEHAVIOUR TYPES WITH UI KEYWORDS ===");
            ScanForUIMonoBehaviours(assemblies, sb);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"ERROR during exploration: {ex}");
            Plugin.Log.LogError($"AssemblyExplorer error: {ex}");
        }

        return sb.ToString();
    }

    private static void ScanAssemblyForUITypes(Assembly asm, StringBuilder sb)
    {
        try
        {
            var types = GetTypesSafely(asm);
            var uiTypes = types
                .Where(t => UIKeywords.Any(kw =>
                    (t.Name?.IndexOf(kw, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0))
                .OrderBy(t => t.FullName)
                .ToList();

            if (uiTypes.Count == 0)
            {
                sb.AppendLine("  (No UI-related types found)");
                return;
            }

            sb.AppendLine($"  Found {uiTypes.Count} UI-related types:");
            foreach (var type in uiTypes.Take(100)) // Limit output
            {
                var baseType = type.BaseType?.Name ?? "Object";
                var isComponent = IsComponentType(type);
                var marker = isComponent ? " [COMPONENT]" : "";
                sb.AppendLine($"    {type.FullName}{marker}");
                sb.AppendLine($"      Base: {baseType}");

                // List properties that might contain text/data
                try
                {
                    var textProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p =>
                        {
                            var name = p.Name?.ToLower() ?? "";
                            return name.Contains("text") || name.Contains("label") ||
                                   name.Contains("value") || name.Contains("title") ||
                                   name.Contains("content");
                        })
                        .Take(5);

                    foreach (var prop in textProps)
                    {
                        sb.AppendLine($"      Property: {prop.Name} ({prop.PropertyType.Name})");
                    }
                }
                catch { }
            }

            if (uiTypes.Count > 100)
            {
                sb.AppendLine($"    ... and {uiTypes.Count - 100} more types");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  ERROR scanning assembly: {ex.Message}");
        }
    }

    private static void ScanForUIMonoBehaviours(Assembly[] assemblies, StringBuilder sb)
    {
        var monoBehaviourTypes = new List<Type>();

        foreach (var asm in assemblies)
        {
            try
            {
                var types = GetTypesSafely(asm);
                var mbTypes = types.Where(t =>
                    IsComponentType(t) &&
                    UIKeywords.Any(kw =>
                        (t.Name?.IndexOf(kw, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0));
                monoBehaviourTypes.AddRange(mbTypes);
            }
            catch { }
        }

        if (monoBehaviourTypes.Count == 0)
        {
            sb.AppendLine("  (No MonoBehaviour UI types found)");
            return;
        }

        var grouped = monoBehaviourTypes
            .GroupBy(t => t.Assembly.GetName().Name ?? "Unknown")
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine($"\n  [{group.Key}]");
            foreach (var type in group.OrderBy(t => t.Name).Take(50))
            {
                sb.AppendLine($"    {type.FullName}");
            }
            if (group.Count() > 50)
            {
                sb.AppendLine($"    ... and {group.Count() - 50} more");
            }
        }
    }

    private static Type[] GetTypesSafely(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Return the types that did load successfully
            return ex.Types.Where(t => t != null).Cast<Type>().ToArray();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static bool IsComponentType(Type type)
    {
        try
        {
            var current = type;
            int depth = 0;
            while (current != null && depth < 20)
            {
                var name = current.Name;
                var fullName = current.FullName ?? "";

                if (name == "MonoBehaviour" || name == "Component" ||
                    fullName == "UnityEngine.MonoBehaviour" ||
                    fullName == "UnityEngine.Component")
                {
                    return true;
                }
                current = current.BaseType;
                depth++;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
