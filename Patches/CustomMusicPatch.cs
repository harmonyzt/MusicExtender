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

        public static AudioClip[] GetCombinedPlaylist(AudioClip[] vanillaMusic, bool customOnly)
        {
            if (_customMusicClips == null || _customMusicClips.Length == 0)
                return vanillaMusic ?? [];

            if (vanillaMusic == null || vanillaMusic.Length == 0)
                return customOnly ? _customMusicClips : [];

            if (customOnly)
            {
                return _customMusicClips.OrderBy(x => UnityEngine.Random.value).ToArray();
            }

            return vanillaMusic
                .Concat(_customMusicClips)
                .OrderBy(x => UnityEngine.Random.value)
                .ToArray();
        }
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
    
    [HarmonyPatch(typeof(GUISounds), nameof(GUISounds.PlayMenuBackgroundMusic))]
    public static class MenuMusicPlayPatch
    {
        // ReSharper disable once InconsistentNaming
        [HarmonyPrefix]
        private static bool Prefix(GUISounds __instance)
        {
            try
            {
                if (!MusicManager.IsInitialized && !MusicManager.LoadMusicBundle())
                {
                    return true;
                }

                var musicField = AccessTools.Field(typeof(GUISounds), "_mainMenuMusic");
                if (musicField == null)
                {
                    Plugin.LogSource.LogError("Cant find GUISounds._mainMenuMusic");
                    return true;
                }

                // Get current vanilla music array
                var vanillaMusic = musicField.GetValue(__instance) as AudioClip[];
                if (vanillaMusic == null || vanillaMusic.Length == 0)
                {
                    Plugin.LogSource.LogWarning("_mainMenuMusic is empty");
                    return true;
                }
                
                if (MusicManager.IsInitialized && MusicManager.CustomMusicClips != null && MusicManager.CustomMusicClips.Length > 0)
                {
                    var finalMusic = MusicManager.GetCombinedPlaylist(vanillaMusic, Plugin.CustomOnly.Value);
                    if (finalMusic is { Length: > 0 })
                    {
                        musicField.SetValue(__instance, finalMusic);

                        if (musicField.GetValue(__instance) is AudioClip[] updatedMusic && updatedMusic.Length > 0)
                        {
                            var audioSourceField = AccessTools.Field(typeof(GUISounds), "audioSource_3");
                            var currentIndexField = AccessTools.Field(typeof(GUISounds), "_currentMusicIndex");
                            
                            if (audioSourceField != null && currentIndexField != null)
                            {
                                var audioSource = audioSourceField.GetValue(__instance) as AudioSource;
                                
                                if (audioSource)
                                {
                                    int currentIndex = (int)currentIndexField.GetValue(__instance);
                                    int newIndex;
                                    do
                                    {
                                        newIndex = UnityEngine.Random.Range(0, updatedMusic.Length);
                                    } while (updatedMusic.Length > 1 && newIndex == currentIndex);
                                    
                                    currentIndexField.SetValue(__instance, newIndex);
                                    AudioClip clip = updatedMusic[newIndex];
                                    
                                    // Stop and play new clip just in case because i cant figure out why the fuck we get no audio on the end of the raid
                                    __instance.StopAudioCallbackCoroutine();
                                    
                                    audioSource.Stop();
                                    audioSource.clip = clip;
                                    audioSource.volume = 1f;
                                    audioSource.Play();
                                    
                                    var waitMethod = typeof(StaticManager).GetMethod("WaitSeconds", new Type[] { typeof(float), typeof(Action) });
                                    if (waitMethod != null)
                                    {
                                        var coroutine = waitMethod.Invoke(StaticManager.Instance, new object[] { clip.length, new Action(__instance.PlayMenuBackgroundMusic) });
                                        var delayedField = AccessTools.Field(typeof(GUISounds), "_delayedAudioCallbackCoroutine");
                                        if (delayedField != null)
                                        {
                                            delayedField.SetValue(__instance, coroutine);
                                        }
                                    }
                                    
                                    Plugin.LogSource.LogInfo($"Playing music track: {clip.name}");
                                    return false;
                                }
                            }
                        }
                    }
                }

                // original method
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in MenuMusicPlayPatch: {ex}");
                return true;
            }
        }
    }
    
    [HarmonyPatch(typeof(GUISounds), nameof(GUISounds.StopMenuBackgroundMusicWithDelay))]
    public static class StopMenuBackgroundMusicWithDelayPatch
    {
        // ReSharper disable once InconsistentNaming
        [HarmonyPrefix]
        private static void Prefix(GUISounds __instance, float transitionTime, Action callback)
        {
            //Plugin.LogSource.LogDebug($"StopMenuBackgroundMusicWithDelay called, trans time: {transitionTime}");
            
            // if we have actually custom music, delay it ourselves (and fade)
            if (MusicManager.IsInitialized && MusicManager.CustomMusicClips != null && MusicManager.CustomMusicClips.Length > 0)
            {
                try
                {
                    var audioSourceField = AccessTools.Field(typeof(GUISounds), "audioSource_3");
                    if (audioSourceField != null)
                    {
                        var audioSource = audioSourceField.GetValue(__instance) as AudioSource;
                        if (audioSource != null && audioSource.isPlaying)
                        {
                            MusicFader.FadeAudioSource(audioSource, 0f, transitionTime, __instance);
                            __instance.StopAudioCallbackCoroutine();
                            
                            if (callback != null)
                            {
                                __instance.StartCoroutine(DelayedCallback(transitionTime, callback));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError($"Error in StopMenuBackgroundMusicWithDelayPatch: {ex}");
                }
            }
        }

        // helper
        private static IEnumerator DelayedCallback(float delay, Action callback)
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
    
    [HarmonyPatch(typeof(GUISounds), nameof(GUISounds.PlayMenuBackgroundMusicDelayed))]
    public static class PlayMenuBackgroundMusicDelayedPatch
    {
        [HarmonyPrefix]
        // ReSharper disable once InconsistentNaming
        private static void Prefix(GUISounds __instance, float delay, Action callback)
        {
            Plugin.LogSource.LogDebug($"PlayMenuBackgroundMusicDelayed called with delay: {delay}");
            
            // If we have custom music, handle it ourselves
            if (MusicManager.IsInitialized && MusicManager.CustomMusicClips != null && MusicManager.CustomMusicClips.Length > 0)
            {
                try
                {
                    var playDelayField = AccessTools.Field(typeof(GUISounds), "_playMusicDelayCoroutine");
                    if (playDelayField != null)
                    {
                        var existingCoroutine = playDelayField.GetValue(__instance) as Coroutine;
                        if (existingCoroutine != null)
                        {
                            __instance.StopCoroutine(existingCoroutine);
                            playDelayField.SetValue(__instance, null);
                        }
                    }
                    __instance.StartCoroutine(DelayedPlayMusic(__instance, delay, callback));
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError($"Error in PlayMenuBackgroundMusicDelayedPatch: {ex}");
                }
            }
        }

        // Helper
        private static IEnumerator DelayedPlayMusic(GUISounds instance, float delay, Action callback)
        {
            yield return new WaitForSeconds(delay);
            
            instance.PlayMenuBackgroundMusic();
            callback?.Invoke();
        }
    }
}