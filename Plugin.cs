using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace MusicExtender
{
    [BepInPlugin("com.harmonyzt.MusicExtender", "MusicExtender", "1.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        public static ConfigEntry<bool> CustomOnly;
        public static ConfigEntry<bool> PlayInHideout;
        public static ConfigEntry<bool> PlayCustomPacks;
        private static Harmony _harmony;

        private void Awake()
        {
            LogSource = Logger;

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

            PlayCustomPacks = Config.Bind(
                "Music",
                "Play Custom Music Packs",
                true,
                "REQUIRES RESTART. Play music packs added by addons in 'Custom' folder inside Resources. Disable to use only the tracks shipped with this mod."
            );

            // yuup. that's me x)
            _harmony = new Harmony("com.harmonyzt.MusicExtender");
            _harmony.PatchAll();

            Logger.LogInfo("Music Extender is loaded!");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}