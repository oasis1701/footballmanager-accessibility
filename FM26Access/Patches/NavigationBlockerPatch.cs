using HarmonyLib;
using FM.UI;
using UnityEngine.InputSystem;

namespace FM26Access.Patches;

/// <summary>
/// Harmony patches to block game navigation when Reading Mode is active.
/// Prevents FMNavigationManager from consuming arrow key input.
/// </summary>
[HarmonyPatch]
public static class NavigationBlockerPatch
{
    /// <summary>
    /// Blocks OnNavigate when reading mode is active.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FMNavigationManager), nameof(FMNavigationManager.OnNavigate))]
    public static bool BlockOnNavigate(InputAction.CallbackContext context)
    {
        return !ShouldBlockNavigation();
    }

    /// <summary>
    /// Blocks OnNavigateUp when reading mode is active.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FMNavigationManager), nameof(FMNavigationManager.OnNavigateUp))]
    public static bool BlockNavigateUp(InputAction.CallbackContext context)
    {
        return !ShouldBlockNavigation();
    }

    /// <summary>
    /// Blocks OnNavigateDown when reading mode is active.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FMNavigationManager), nameof(FMNavigationManager.OnNavigateDown))]
    public static bool BlockNavigateDown(InputAction.CallbackContext context)
    {
        return !ShouldBlockNavigation();
    }

    /// <summary>
    /// Blocks OnNavigateLeft when reading mode is active.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FMNavigationManager), nameof(FMNavigationManager.OnNavigateLeft))]
    public static bool BlockNavigateLeft(InputAction.CallbackContext context)
    {
        return !ShouldBlockNavigation();
    }

    /// <summary>
    /// Blocks OnNavigateRight when reading mode is active.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FMNavigationManager), nameof(FMNavigationManager.OnNavigateRight))]
    public static bool BlockNavigateRight(InputAction.CallbackContext context)
    {
        return !ShouldBlockNavigation();
    }

    /// <summary>
    /// Checks if navigation should be blocked based on reading mode state.
    /// </summary>
    private static bool ShouldBlockNavigation()
    {
        try
        {
            var readingMode = Navigation.ReadingMode.Instance;
            return readingMode != null && readingMode.IsActive;
        }
        catch
        {
            // If anything fails, don't block
            return false;
        }
    }
}
