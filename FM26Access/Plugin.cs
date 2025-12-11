using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using System;
using UnityEngine;
using FM26Access.Core;
using FM26Access.Navigation;
using FM26Access.UI;

namespace FM26Access;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    public static Plugin Instance { get; private set; }
    public new static ManualLogSource Log { get; private set; }

    private Harmony _harmony;
    private GameObject _managerObject;

    public override void Load()
    {
        Instance = this;
        Log = base.Log;

        Log.LogInfo($"FM26 Accessibility Mod v{MyPluginInfo.PLUGIN_VERSION} loading...");

        try
        {
            // Register custom MonoBehaviour types with IL2CPP
            // This MUST be done before using AddComponent
            Log.LogInfo("Registering IL2CPP types...");
            ClassInjector.RegisterTypeInIl2Cpp<AccessibilityManager>();
            ClassInjector.RegisterTypeInIl2Cpp<UIScanner>();
            ClassInjector.RegisterTypeInIl2Cpp<NavigationController>();
            ClassInjector.RegisterTypeInIl2Cpp<FocusListener>();
            Log.LogInfo("IL2CPP types registered");

            // Initialize NVDA output
            if (NVDAOutput.Initialize())
            {
                Log.LogInfo("NVDA connection established");
                NVDAOutput.Speak("FM26 Access loaded");
            }
            else
            {
                Log.LogWarning("NVDA not detected - speech output disabled");
            }

            // Create persistent manager GameObject
            _managerObject = new GameObject("FM26AccessManager");
            GameObject.DontDestroyOnLoad(_managerObject);

            // Add our components using IL2CPP-compatible method
            _managerObject.AddComponent<AccessibilityManager>();
            _managerObject.AddComponent<UIScanner>();
            _managerObject.AddComponent<NavigationController>();
            _managerObject.AddComponent<FocusListener>();

            // Apply Harmony patches if needed
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();

            Log.LogInfo("FM26 Accessibility Mod loaded successfully!");
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to initialize: {ex}");
        }
    }

    public override bool Unload()
    {
        if (_harmony != null)
            _harmony.UnpatchSelf();

        NVDAOutput.Shutdown();

        if (_managerObject != null)
        {
            GameObject.Destroy(_managerObject);
        }

        return base.Unload();
    }
}

public static class MyPluginInfo
{
    public const string PLUGIN_GUID = "com.fm26access.plugin";
    public const string PLUGIN_NAME = "FM26 Accessibility";
    public const string PLUGIN_VERSION = "0.1.0";
}
