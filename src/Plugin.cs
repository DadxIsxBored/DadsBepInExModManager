using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace DadsBepInExModManager
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.dadisbored.dadsbepinexmodmanager";
        public const string PluginName = "DadsBepInExModManager";
        public const string PluginVersion = "1.0.0";

        private Harmony _harmony;
        private ConfigManagerOverlay _overlay;
        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            _overlay = gameObject.AddComponent<ConfigManagerOverlay>();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy()
        {
            _overlay?.Shutdown();
            _harmony?.UnpatchSelf();
        }
    }
}
