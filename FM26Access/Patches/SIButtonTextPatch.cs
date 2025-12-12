using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FM26Access.Patches;

/// <summary>
/// Harmony patch to intercept SIButton text binding.
/// Caches label text when it's set via the binding system,
/// allowing faster and more reliable label retrieval.
/// </summary>
[HarmonyPatch]
public static class SIButtonTextPatch
{
    // Cache of element pointer → label text
    // Using IntPtr as key because VisualElement doesn't have a reliable GetHashCode for IL2CPP
    private static readonly Dictionary<IntPtr, string> _labelCache = new();
    private static DateTime _lastCacheClear = DateTime.Now;
    private const int CACHE_LIFETIME_SECONDS = 60;

    /// <summary>
    /// Postfix patch for SIButton.m_staticText setter.
    /// Captures the label text when it's bound by the game.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SIButton), nameof(SIButton.m_staticText), MethodType.Setter)]
    public static void OnStaticTextSet(SIButton __instance, string value)
    {
        try
        {
            // Clear cache periodically
            if ((DateTime.Now - _lastCacheClear).TotalSeconds > CACHE_LIFETIME_SECONDS)
            {
                _labelCache.Clear();
                _lastCacheClear = DateTime.Now;
            }

            if (__instance != null && !string.IsNullOrWhiteSpace(value))
            {
                _labelCache[__instance.Pointer] = value;
            }
        }
        catch
        {
            // Silently ignore errors in patches to avoid breaking game
        }
    }

    /// <summary>
    /// Gets cached label text for an element if available.
    /// Returns null if not in cache.
    /// </summary>
    [HideFromIl2Cpp]
    public static string GetCachedLabel(VisualElement element)
    {
        if (element == null) return null;

        try
        {
            // Clear cache periodically
            if ((DateTime.Now - _lastCacheClear).TotalSeconds > CACHE_LIFETIME_SECONDS)
            {
                _labelCache.Clear();
                _lastCacheClear = DateTime.Now;
                return null;
            }

            if (_labelCache.TryGetValue(element.Pointer, out var label))
                return label;
        }
        catch
        {
            // Silently ignore errors
        }

        return null;
    }

    /// <summary>
    /// Manually adds a label to the cache (for testing or manual population).
    /// </summary>
    [HideFromIl2Cpp]
    public static void CacheLabel(VisualElement element, string label)
    {
        if (element != null && !string.IsNullOrWhiteSpace(label))
        {
            _labelCache[element.Pointer] = label;
        }
    }

    /// <summary>
    /// Clears the label cache.
    /// </summary>
    [HideFromIl2Cpp]
    public static void ClearCache()
    {
        _labelCache.Clear();
        _lastCacheClear = DateTime.Now;
    }

    /// <summary>
    /// Gets the current cache size (for debugging).
    /// </summary>
    [HideFromIl2Cpp]
    public static int GetCacheSize() => _labelCache.Count;
}
