using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace MusicExtender
{
    [BepInPlugin("com.harmonyzt.MusicExtender", "MusicExtender", "1.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        public static ConfigEntry<bool> CustomOnly;
        private static Harmony _harmony;

        private void Awake()
        {
            LogSource = Logger;
            
            _harmony = new Harmony("com.harmonyzt.MusicExtender");
            _harmony.PatchAll();
            
            CustomOnly = Config.Bind(
                "Music",
                "CustomOnly",
                false,
                "REQUIRES RESTART. When enabled, only custom music will play. " +
                "When disabled, custom music is mixed with the vanilla music."
            );
            
            Logger.LogInfo("Music Extender is loaded!");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}