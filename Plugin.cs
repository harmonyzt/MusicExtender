using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace MusicExtender
{
    [BepInPlugin("com.harmonyzt.MusicExtender", "MusicExtender", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        public static ConfigEntry<bool> CustomOnly;
        public static ConfigEntry<bool> PlayInHideout;
        private static Harmony _harmony;

        private void Awake()
        {
            LogSource = Logger;
            
            // yuup. that's me x)
            _harmony = new Harmony("com.harmonyzt.MusicExtender");
            _harmony.PatchAll();
            
            CustomOnly = Config.Bind(
                "Music",
                "Custom Music Only",
                false,
                "REQUIRES RESTART. When enabled, only custom music will play. " +
                "When disabled, custom music is mixed with the vanilla music."
            );
            
            PlayInHideout = Config.Bind(
                "Music",
                "Play in Hideout",
                false,
                "Will keep playing music once you enter hideout."
            );
            
            Logger.LogInfo("Music Extender is loaded!");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}