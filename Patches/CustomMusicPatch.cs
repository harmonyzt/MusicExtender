using System;
using System.Collections;
using System.Collections.Generic;
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
        private const string ResourcesFolder = "Resources";
        private const string CoreBundleName = "music.bundle";
        private const string CustomPacksFolder = "Custom";

        private static readonly List<AssetBundle> LoadedBundles = new List<AssetBundle>();
        private static AudioClip[] _customMusicClips;
        private static AudioClip[] _playlist;
        private static bool _isInitialized;

        public static bool IsInitialized => _isInitialized;
        public static AudioClip[] CustomMusicClips => _customMusicClips;
        public static AudioClip[] Playlist => _playlist;

        /// Addon music packs
        public static int PackTrackCount { get; private set; }

        // Load everything
        public static bool LoadMusicBundle()
        {
            if (_isInitialized)
                return true;

            _isInitialized = true;

            try
            {
                string modPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                if (string.IsNullOrEmpty(modPath))
                {
                    Plugin.LogSource.LogError("Could not resolve the MusicExtender plugin folder.");
                    return false;
                }

                var clips = new List<AudioClip>();
                string resourcesPath = Path.Combine(modPath, ResourcesFolder);

                var coreClips = LoadClips(Path.Combine(resourcesPath, CoreBundleName));

                if (coreClips != null)
                {
                    clips.AddRange(coreClips);
                }

                if (Plugin.PlayCustomPacks == null || Plugin.PlayCustomPacks.Value)
                {
                    LoadCustomPacks(Path.Combine(resourcesPath, CustomPacksFolder), clips);
                }

                if (clips.Count == 0)
                {
                    Plugin.LogSource.LogWarning("No custom music loaded. Falling back to vanilla music.");
                    return false;
                }

                _customMusicClips = clips.ToArray();

                Plugin.LogSource.LogInfo(
                    $"Loaded {clips.Count} custom music tracks " +
                    $"({clips.Count - PackTrackCount} bundled, {PackTrackCount} from custom packs)");

                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error loading custom music: {ex}");
                return false;
            }
        }

        private static void LoadCustomPacks(string packDirectory, List<AudioClip> destination)
        {
            if (!Directory.Exists(packDirectory))
            {
                return;
            }

            string[] bundleFiles;

            try
            {
                bundleFiles = Directory.GetFiles(packDirectory, "*.bundle", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Could not scan music packs in '{packDirectory}': {ex}");

                return;
            }

            if (bundleFiles.Length == 0)
            {
                Plugin.LogSource.LogInfo($"No custom music packs found in {packDirectory}.");

                return;
            }

            // Stable order so logs and clip order are reproducible between runs.
            Array.Sort(bundleFiles, StringComparer.OrdinalIgnoreCase);

            foreach (string bundleFile in bundleFiles)
            {
                var packClips = LoadClips(bundleFile);

                if (packClips == null)
                {
                    continue;
                }

                destination.AddRange(packClips);
                PackTrackCount += packClips.Length;

                Plugin.LogSource.LogDebug(
                    $"Loaded music pack '{Path.GetFileName(bundleFile)}' with {packClips.Length} tracks");
            }
        }


        private static AudioClip[] LoadClips(string bundlePath)
        {
            if (!File.Exists(bundlePath))
            {
                Plugin.LogSource.LogWarning($"Music bundle not found: {bundlePath}");
                return null;
            }

            AssetBundle bundle = null;

            try
            {
                bundle = AssetBundle.LoadFromFile(bundlePath);

                if (bundle == null)
                {
                    Plugin.LogSource.LogError($"Failed to load music bundle: {bundlePath}");
                    return null;
                }

                var clips = bundle.LoadAllAssets<AudioClip>();

                if (clips == null || clips.Length == 0)
                {
                    Plugin.LogSource.LogWarning($"Bundle has no AudioClip assets: {bundlePath}");
                    bundle.Unload(true);
                    return null;
                }

                LoadedBundles.Add(bundle);
                return clips;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error loading music bundle '{bundlePath}': {ex}");

                if (bundle != null)
                    bundle.Unload(true);

                return null;
            }
        }

        public static bool BuildPlaylist(AudioClip[] vanillaMusic, bool customOnly)
        {
            var customClips = _customMusicClips?.Where(clip => clip != null).ToArray();

            if (customClips == null || customClips.Length == 0)
                return false;

            if (customOnly)
            {
                _playlist = Shuffle(customClips);
            }
            else
            {
                var vanillaClips = (vanillaMusic ?? Array.Empty<AudioClip>())
                    .Where(clip => clip != null);

                _playlist = Shuffle(vanillaClips.Concat(customClips));
            }

            return _playlist.Length > 0;
        }

        private static AudioClip[] Shuffle(IEnumerable<AudioClip> clips)
        {
            return clips.OrderBy(_ => UnityEngine.Random.value).ToArray();
        }
    }

    public static class MusicFader
    {
        private static Coroutine _currentFadeCoroutine;

        public static void FadeAudioSource(AudioSource audioSource, float targetVolume, float duration,
            MonoBehaviour coroutineHost)
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

                var musicField = AccessTools.Field(typeof(GUISounds), "_mainMenuMusic");
                var audioSourceField = AccessTools.Field(typeof(GUISounds), "audioSource_3");
                var currentIndexField = AccessTools.Field(typeof(GUISounds), "_currentMusicIndex");

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
                {
                    return true;
                }

                var audioSource = audioSourceField.GetValue(__instance) as AudioSource;

                if (!audioSource)
                {
                    return true;
                }

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

                __instance.StopAudioCallbackCoroutine();

                // Stop current playback FOR THE LOVE OF GOD
                audioSource.Stop();
                audioSource.clip = clip;
                audioSource.volume = 1f;
                audioSource.Play();

                // Schedule the NEXT track
                var callback = new Action(__instance.PlayMenuBackgroundMusic);
                var nextTrackCoroutine = StaticManager.Instance.WaitSeconds(clip.length, callback);
                var delayedField = AccessTools.Field(typeof(GUISounds), "_delayedAudioCallbackCoroutine");

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
            if (!MusicManager.IsInitialized || MusicManager.CustomMusicClips == null ||
                MusicManager.CustomMusicClips.Length == 0)
            {
                return true;
            }

            try
            {
                var audioSourceField = AccessTools.Field(typeof(GUISounds), "audioSource_3");
                var audioSource = audioSourceField?.GetValue(__instance) as AudioSource;

                __instance.StopAudioCallbackCoroutine();

                if (audioSource != null)
                {
                    MusicFader.FadeAudioSource(audioSource, 0f, transitionTime, __instance);
                }

                if (callback != null)
                {
                    __instance.StartCoroutine(DelayedCallback(transitionTime, callback));
                }

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
                var playDelayField = AccessTools.Field(typeof(GUISounds), "_playMusicDelayCoroutine");

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

            instance.PlayMenuBackgroundMusic();
            callback?.Invoke();
        }
    }
}