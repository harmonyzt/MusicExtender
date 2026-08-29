using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace MusicExtender.Patches
{
    [HarmonyPatch(typeof(GUISounds), nameof(GUISounds.PlayMenuBackgroundMusic))]
    public static class MenuMusicPatch
    {
        private static bool _hasLoadedMusic;
        private static AssetBundle _musicBundle;

        [HarmonyPrefix]
        private static void Prefix(GUISounds __instance)
        {
            // I don't know if this ever fires again, since I am not cool at C# but I think this is needed here, just so I feel safe and lonely
            if (_hasLoadedMusic)
                return;

            try
            {
                string modPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                string bundlePath = Path.Combine(modPath, "Resources", "music.bundle");

                if (!File.Exists(bundlePath))
                {
                    Plugin.LogSource.LogWarning($"Music bundle not found: {bundlePath}");
                    
                    return;
                }

                _musicBundle = AssetBundle.LoadFromFile(bundlePath);

                if (!_musicBundle)
                {
                    Plugin.LogSource.LogError($"Failed to load music bundle: {bundlePath}");
                    
                    return;
                }

                AudioClip[] customMusicClips = _musicBundle.LoadAllAssets<AudioClip>();

                if (customMusicClips == null || customMusicClips.Length == 0)
                {
                    Plugin.LogSource.LogWarning("Music bundle contains no AudioClip assets." );
                    
                    return;
                }

                var musicField = AccessTools.Field(typeof(GUISounds), "_mainMenuMusic");

                if (musicField == null)
                {
                    Plugin.LogSource.LogError("Could not find GUISounds._mainMenuMusic" );
                    
                    return;
                }

                if (musicField.GetValue(__instance) is not AudioClip[] vanillaMusic || vanillaMusic.Length == 0)
                {
                    Plugin.LogSource.LogWarning("_mainMenuMusic is empty or has not been initialized." );
                    
                    return;
                }

                AudioClip[] finalMusic;

                if (Plugin.CustomOnly.Value)
                {
                    finalMusic = customMusicClips.OrderBy(x => UnityEngine.Random.value).ToArray();
                }
                else
                {
                    finalMusic = vanillaMusic
                        .Concat(customMusicClips)
                        .OrderBy(x => UnityEngine.Random.value)
                        .ToArray();
                }

                musicField.SetValue(__instance, finalMusic);

                _hasLoadedMusic = true;

                // For me, stays here until I figure out asset building
                // Plugin.LogSource.LogInfo(
                //     $"Music loaded. " +
                //     $"Custom tracks: {customMusicClips.Length}, " +
                //     $"Vanilla tracks: {vanillaMusic.Length}, " +
                //     $"Final playlist: {finalMusic.Length}, " +
                //     $"CustomOnly: {Plugin.CustomOnly.Value}"
                // );
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error loading custom music: {ex}" );
            }
        }
    }
    
    [HarmonyPatch(typeof(GUISounds), "method_9")]
    public static class HideoutMusicPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !Plugin.PlayInHideout.Value;
        }
    }
}