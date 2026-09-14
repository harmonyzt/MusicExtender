using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using SPTushonka.Reflection.Patching;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Collections.Generic;

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
                return _customMusicClips != null && _customMusicClips.Length > 0;

            try
            {
                string modPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(modPath))
                {
                    _isInitialized = true;
                    return false;
                }

                string bundlePath = Path.Combine(modPath, "Resources", "music.bundle");

                if (!File.Exists(bundlePath))
                {
                    Plugin.LogSource.LogWarning($"Music bundle not found: {bundlePath}.");
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

                Il2CppSystem.Type audioClipType = Il2CppType.From(typeof(AudioClip));
                var assets = _musicBundle.LoadAllAssets(audioClipType);

                var clips = new List<AudioClip>();
                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        var obj = assets[i];
                        if (obj == null) continue;
                        var clip = obj.TryCast<AudioClip>();
                        if (clip != null) clips.Add(clip);
                    }
                }

                _customMusicClips = clips.ToArray();

                if (_customMusicClips.Length == 0)
                {
                    Plugin.LogSource.LogWarning("Music bundle contains NO AudioClip assets.");
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

        // Both deprecated
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

        public static AudioClip[] GetCombinedPlaylist(AudioClip[] vanillaMusic, bool customOnly)
        {
            if (_customMusicClips == null || _customMusicClips.Length == 0)
                return vanillaMusic ?? Array.Empty<AudioClip>();

            if (vanillaMusic == null || vanillaMusic.Length == 0)
                return customOnly ? _customMusicClips : Array.Empty<AudioClip>();

            if (customOnly)
                return _customMusicClips.OrderBy(x => UnityEngine.Random.value).ToArray();

            return vanillaMusic
                .Concat(_customMusicClips)
                .OrderBy(x => UnityEngine.Random.value)
                .ToArray();
        }

        public static void SetPlaylist(AudioClip[] playlist)
        {
            _playlist = playlist;
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

            if (targetVolume <= 0.01f)
            {
                audioSource.Stop();
                audioSource.clip = null;
            }
        }
    }

    public class MenuMusicPlayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GUISounds), nameof(GUISounds.PlayMenuBackgroundMusic));
        }

        [PatchPrefix]
        private static bool Prefix(GUISounds __instance)
        {
            try
            {
                if (!MusicManager.IsInitialized && !MusicManager.LoadMusicBundle())
                    return true;

                if (MusicManager.CustomMusicClips == null || MusicManager.CustomMusicClips.Length == 0)
                    return true;

                // Read via interop property
                Il2CppReferenceArray<AudioClip> vanillaArr = __instance._mainMenuMusic;

                if (vanillaArr == null)
                {
                    Plugin.LogSource.LogWarning("_mainMenuMusic is null (Init not done yet?).");
                    return true;
                }

                AudioClip[] vanilla = vanillaArr.ToArray();

                if (vanilla.Length == 0)
                {
                    Plugin.LogSource.LogWarning("_mainMenuMusic is empty.");
                    return true;
                }

                if (MusicManager.Playlist == null)
                {
                    AudioClip[] combined = Plugin.CustomOnly.Value
                        ? MusicManager.CustomMusicClips
                            .OrderBy(_ => UnityEngine.Random.value).ToArray()
                        : vanilla
                            .Concat(MusicManager.CustomMusicClips)
                            .OrderBy(_ => UnityEngine.Random.value).ToArray();

                    MusicManager.SetPlaylist(combined);

                    var newArr = new Il2CppReferenceArray<AudioClip>(combined.Length);
                    for (int i = 0; i < combined.Length; i++)
                        newArr[i] = combined[i];

                    __instance._mainMenuMusic = newArr;

                    Plugin.LogSource.LogInfo(
                        $"Installed playlist: {combined.Length} tracks " +
                        $"({MusicManager.CustomMusicClips.Length} custom, {vanilla.Length} vanilla).");
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in MenuMusicPlayPatch: {ex}");
                return true;
            }
        }
    }

    public class StopMenuBackgroundMusicWithDelayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GUISounds), nameof(GUISounds.StopMenuBackgroundMusicWithDelay));
        }

        [PatchPrefix]
        private static bool Prefix(GUISounds __instance, float transitionTime, Action callback)
        {
            if (!MusicManager.IsInitialized || MusicManager.CustomMusicClips == null ||
                MusicManager.CustomMusicClips.Length == 0)
                return true;

            try
            {
                AudioSource audioSource = __instance._backgroundMusicAudioSource;

                __instance.StopAudioCallbackCoroutine();

                if (audioSource != null)
                    MusicFader.FadeAudioSource(audioSource, 0f, transitionTime, __instance);

                if (callback != null)
                    __instance.StartCoroutine(DelayedCallback(transitionTime, callback));

                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in StopMenuBackgroundMusicWithDelayPatch: {ex}");
                return true;
            }
        }

        private static IEnumerator DelayedCallback(float delay, Action callback)
        {
            yield return new WaitForSeconds(delay);
            callback?.Invoke();
        }
    }

    // Keep music playing in hideout if enabled
    public class HideoutMusicPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GUISounds), nameof(GUISounds.PauseMenuBackgroundMusic));
        }

        [PatchPrefix]
        private static bool Prefix()
        {
            return !Plugin.PlayInHideout.Value;
        }
    }

    public class PlayMenuBackgroundMusicDelayedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GUISounds), nameof(GUISounds.PlayMenuBackgroundMusicDelayed));
        }

        [PatchPrefix]
        private static bool Prefix(GUISounds __instance, float delay, Il2CppSystem.Action callback)
        {
            if (!MusicManager.IsInitialized && !MusicManager.LoadMusicBundle())
                return true;

            if (MusicManager.CustomMusicClips == null || MusicManager.CustomMusicClips.Length == 0)
                return true;

            try
            {
                // _playMusicDelayCoroutine is Il2CppSystem.Collections.IEnumerator
                Il2CppSystem.Collections.IEnumerator existing = __instance._playMusicDelayCoroutine;

                if (existing != null)
                {
                    __instance.StopCoroutine(existing);
                    __instance._playMusicDelayCoroutine = null;
                }

                __instance.StartCoroutine(DelayedPlayMusic(__instance, delay, callback).WrapToIl2Cpp());

                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in PlayMenuBackgroundMusicDelayedPatch: {ex}");
                return true;
            }
        }

        private static IEnumerator DelayedPlayMusic(GUISounds instance, float delay, Il2CppSystem.Action callback)
        {
            yield return new WaitForSeconds(delay);
            instance.PlayMenuBackgroundMusic();
            callback?.Invoke();
        }
    }
}