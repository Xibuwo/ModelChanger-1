using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ChillWithYou.ModelChanger
{
    [BepInPlugin("chillwithyou.modelchanger", "Model Changer", "1.0.0")]
    public class ModelChangerPlugin : BaseUnityPlugin
    {
        internal static ModelChangerPlugin Instance;
        internal static ManualLogSource Log;

        // Config
        internal static ConfigEntry<string> Cfg_CurrentModel;
        internal static ConfigEntry<KeyCode> Cfg_MenuToggleKey;

        // Runtime state
        internal static GameObject CurrentCharacterObject;
        internal static ModelRegistry ModelRegistry;

        private GameObject _menuGO;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            InitConfig();
            ModelRegistry = new ModelRegistry();

            var harmony = new Harmony("ChillWithYou.ModelChanger");
            harmony.PatchAll();

            // Create menu GameObject
            _menuGO = new GameObject("ModelChangerMenu");
            _menuGO.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_menuGO);
            _menuGO.AddComponent<ModelChangerUI>();
            Log?.LogInfo("Model Changer loaded.");
        }

        private void InitConfig()
        {
            Cfg_CurrentModel = Config.Bind("General", "CurrentModel", "Default",
                "Currently selected character model");
            Cfg_MenuToggleKey = Config.Bind("Hotkeys", "MenuToggle", KeyCode.F10,
                "Key to toggle model changer menu");
        }
    }
}