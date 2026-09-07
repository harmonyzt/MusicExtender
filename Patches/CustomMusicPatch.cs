using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using EFT;

namespace MusicExtender.Patches
{
    public static class MusicManager
    {
        private static AssetBundle _musicBundle;
        private static AudioClip[] _customMusicClips;
        private static bool _isInitialized;

        public static bool IsInitialized => _isInitialized;
        public static AudioClip[] CustomMusicClips => _customMusicClips;
        private static AudioClip[] _playlist;
        public static AudioClip[] Playlist => _playlist;
        

        public static bool LoadMusicBundle()
        {
            if (_isInitialized)
                return true;

            try
            {
                string modPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (modPath != null)
                {
                    string bundlePath = Path.Combine(modPath, "Resources", "music.bundle");

                    if (!File.Exists(bundlePath))
                    {
                        Plugin.LogSource.LogWarning($"Music bundle not found: {bundlePath}. Make sure you put bundle in the Resources folder.");
                        _isInitialized = true;
                        return false;
                    }

                    _musicBundle = AssetBundle.LoadFromFile(bundlePath);

                    if (!_musicBundle)
                    {
                        Plugin.LogSource.LogError($"Failed to load music bundle: {bundlePath}");
                        _isInitialized = true;
                        return false;
                    }
                }

                _customMusicClips = _musicBundle.LoadAllAssets<AudioClip>();

                if (_customMusicClips == null || _customMusicClips.Length == 0)
                {
                    Plugin.LogSource.LogWarning("Music bundle contains NO AudioClip assets. Make sure you have built the bundle right.");
                    _isInitialized = true;
                    return false;
                }

                Plugin.LogSource.LogInfo($"Loaded {_customMusicClips.Length} custom music tracks");
                _isInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error loading music bundle: {ex}");
                _isInitialized = true;
                return false;
            }
        }
        
        public static bool BuildPlaylist(AudioClip[] vanillaMusic, bool customOnly)
        {
            if (_customMusicClips == null || _customMusicClips.Length == 0)
                return false;

            if (customOnly)
            {
                _playlist = _customMusicClips
                    .OrderBy(x => UnityEngine.Random.value)
                    .ToArray();
            }
            else
            {
                _playlist = vanillaMusic
                    .Concat(_customMusicClips)
                    .OrderBy(x => UnityEngine.Random.value)
                    .ToArray();
            }

            return _playlist.Length > 0;
        }

        // public static AudioClip[] GetCombinedPlaylist(AudioClip[] vanillaMusic, bool customOnly)
        // {
        //     if (_customMusicClips == null || _customMusicClips.Length == 0)
        //         return vanillaMusic ?? [];
        //
        //     if (vanillaMusic == null || vanillaMusic.Length == 0)
        //         return customOnly ? _customMusicClips : [];
        //
        //     if (customOnly)
        //     {
        //         return _customMusicClips.OrderBy(x => UnityEngine.Random.value).ToArray();
        //     }
        //
        //     return vanillaMusic
        //         .Concat(_customMusicClips)
        //         .OrderBy(x => UnityEngine.Random.value)
        //         .ToArray();
        // }
    }

    public static class MusicFader
    {
        private static Coroutine _currentFadeCoroutine;

        public static void FadeAudioSource(AudioSource audioSource, float targetVolume, float duration, MonoBehaviour coroutineHost)
        {
            if (_currentFadeCoroutine != null)
            {
                coroutineHost.StopCoroutine(_currentFadeCoroutine);
                _currentFadeCoroutine = null;
            }

            // Start new fade
            _currentFadeCoroutine = coroutineHost.StartCoroutine(FadeCoroutine(audioSource, targetVolume, duration));
        }

        private static IEnumerator FadeCoroutine(AudioSource audioSource, float targetVolume, float duration)
        {
            if (!audioSource)
                yield break;

            float startVolume = audioSource.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                audioSource.volume = Mathf.Lerp(startVolume, targetVolume, t);
                yield return null;
            }

            audioSource.volume = targetVolume;

            // if fading out completely, stop the audio
            if (targetVolume <= 0.01f)
            {
                audioSource.Stop();
                audioSource.clip = null;
            }
        }
    }
    
    [HarmonyPatch(typeof(GUISounds), nameof(GUISounds.method_3))]
    public static class MenuMusicPlayPatch
    {
        [HarmonyPrefix]
        // ReSharper disable once InconsistentNaming
        private static bool Prefix(GUISounds __instance)
        {
            try
            {
                if (!MusicManager.IsInitialized && !MusicManager.LoadMusicBundle())
                {
                    return true;
                }

                if (MusicManager.CustomMusicClips == null || MusicManager.CustomMusicClips.Length == 0)
                {
                    return true;
                }

                var musicField = AccessTools.Field(typeof(GUISounds), "audioClip_0");
                var audioSourceField = AccessTools.Field(typeof(GUISounds), "audioSource_3");
                var currentIndexField = AccessTools.Field( typeof(GUISounds), "int_0");

                if (musicField == null ||
                    audioSourceField == null ||
                    currentIndexField == null)
                {
                    Plugin.LogSource.LogError("Could not find GUISounds music fields.");

                    return true;
                }

                var vanillaMusic = musicField.GetValue(__instance) as AudioClip[];

                if (vanillaMusic == null ||
                    vanillaMusic.Length == 0)
                {
                    Plugin.LogSource.LogWarning("_mainMenuMusic is empty.");

                    return true;
                }

                // Build playlist
                if (MusicManager.Playlist == null)
                {
                    if (!MusicManager.BuildPlaylist(vanillaMusic, Plugin.CustomOnly.Value))
                    {
                        return true;
                    }

                    // Keep GUISounds pointing at playlist
                    musicField.SetValue(__instance, MusicManager.Playlist);
                }

                var playlist = MusicManager.Playlist;

                if (playlist == null || playlist.Length == 0)
                    return true;

                var audioSource = audioSourceField.GetValue(__instance) as AudioSource;

                if (!audioSource)
                    return true;

                int currentIndex = (int)currentIndexField.GetValue(__instance);
                int newIndex;

                if (playlist.Length == 1)
                {
                    newIndex = 0;
                }
                else
                {
                    do
                    {
                        newIndex = UnityEngine.Random.Range(0, playlist.Length);
                    } while (newIndex == currentIndex);
                }

                currentIndexField.SetValue(__instance, newIndex);

                AudioClip clip = playlist[newIndex];

                if (!clip)
                    return false;
                
                __instance.method_8();

                // Stop current playback FOR THE LOVE OF GOD
                audioSource.Stop();
                audioSource.clip = clip;
                audioSource.volume = 1f;
                audioSource.Play();

                // Schedule the NEXT track
                var callback = new Action(__instance.method_3);
                var nextTrackCoroutine = StaticManager.Instance.WaitSeconds(clip.length,callback);
                var delayedField = AccessTools.Field(typeof(GUISounds), "coroutine_0");

                delayedField?.SetValue(__instance, nextTrackCoroutine);

                Plugin.LogSource.LogInfo($"Playing music track: {clip.name}");
                
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError(
                    $"Error in MenuMusicPlayPatch: {ex}");

                return true;
            }
        }
        
        
    }
        
    [HarmonyPatch(typeof(GUISounds), nameof(GUISounds.StopMenuBackgroundMusicWithDelay))]
    public static class StopMenuBackgroundMusicWithDelayPatch
    {
        [HarmonyPrefix]
        // ReSharper disable once InconsistentNaming
        private static bool Prefix(GUISounds __instance, float transitionTime, Action callback)
        {
            if (!MusicManager.IsInitialized || MusicManager.CustomMusicClips == null || MusicManager.CustomMusicClips.Length == 0)
            {
                return true;
            }

            try
            {
                var audioSourceField = AccessTools.Field( typeof(GUISounds), "audioSource_3");
                var audioSource = audioSourceField?.GetValue(__instance) as AudioSource;
                
                __instance.method_8();

                if (audioSource != null)
                {
                    MusicFader.FadeAudioSource(audioSource, 0f, transitionTime, __instance);
                }

                if (callback != null)
                {
                    __instance.StartCoroutine(DelayedCallback( transitionTime, callback));
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in StopMenuBackgroundMusicWithDelayPatch: {ex}");

                return true;
            }
        }

        private static IEnumerator DelayedCallback(
            float delay,
            Action callback)
        {
            yield return new WaitForSeconds(delay);

            callback?.Invoke();
        }
    }

    // Keep music playing in hideout if enabled
    [HarmonyPatch(typeof(GUISounds), "method_9")]
    public static class HideoutMusicPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !Plugin.PlayInHideout.Value;
        }
    }
    
    [HarmonyPatch(typeof(GUISounds), nameof(GUISounds.method_4))]
    public static class PlayMenuBackgroundMusicDelayedPatch
    {
        [HarmonyPrefix]
        // ReSharper disable once InconsistentNaming
        private static bool Prefix(GUISounds __instance, float delay, Action callback)
        {
            if (!MusicManager.IsInitialized && !MusicManager.LoadMusicBundle())
            {
                return true;
            }

            if (MusicManager.CustomMusicClips == null || MusicManager.CustomMusicClips.Length == 0)
            {
                return true;
            }

            try
            {
                // Cancel vanilla timer
                var playDelayField = AccessTools.Field(typeof(GUISounds),"ienumerator_0");

                if (playDelayField != null)
                {
                    var existing = playDelayField.GetValue(__instance) as IEnumerator;

                    if (existing != null)
                    {
                        __instance.StopCoroutine(existing);
                        playDelayField.SetValue(__instance, null);
                    }
                }

                // Start only our timer
                __instance.StartCoroutine(DelayedPlayMusic(__instance, delay, callback));
                
                // Prevent vanilla PlayMenuBackgroundMusicDelayed
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in PlayMenuBackgroundMusicDelayedPatch: {ex}");
                
                return true;
            }
        }

        private static IEnumerator DelayedPlayMusic(GUISounds instance, float delay, Action callback)
        {
            yield return new WaitForSeconds(delay);

            instance.method_3();
            callback?.Invoke();
        }
    }
}