using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using MusicExtender.Patches;

namespace MusicExtender
{
    [BepInPlugin("com.harmonyzt.MusicExtender", "MusicExtender", "2.0.0")]
    public class Plugin : BasePlugin
    {
        public static ManualLogSource LogSource;
        public static ConfigEntry<bool> CustomOnly;
        public static ConfigEntry<bool> PlayInHideout;

        public override void Load()
        {
            LogSource = Log;
            
            CustomOnly = Config.Bind(
                "Music",
                "Custom Music Only",
                false,
                "REQUIRES RESTART. When enabled, only custom music will play. " +
                "When disabled, custom music is mixed with the vanilla music.");

            PlayInHideout = Config.Bind(
                "Music",
                "Play in Hideout",
                false,
                "Will keep playing music once you enter hideout.");
            
            new MenuMusicPlayPatch().Enable();
            new StopMenuBackgroundMusicWithDelayPatch().Enable();
            new PlayMenuBackgroundMusicDelayedPatch().Enable();
            
            // method_9 no longer exists in GUISounds, RIP
            // new HideoutMusicPatch().Enable();

            LogSource.LogInfo("Music Extender is loaded.");
        }

        public override bool Unload()
        {
            return true;
        }
    }
}