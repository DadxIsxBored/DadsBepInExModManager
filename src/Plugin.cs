using BepInEx;
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

        private void Awake()
        {
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
