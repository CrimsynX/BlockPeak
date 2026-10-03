using BlockPeak.Core;
using UnityEngine;

namespace BlockPeak.Assets
{
    /// <summary>Plays Minecraft sounds in the world (3D) or on the HUD (2D).</summary>
    public static class Sfx
    {
        private static GameObject root;

        public static void At(string name, Vector3 pos, float volume = 1f, float pitch = 1f, float maxDistance = 32f)
        {
            Play(McAssets.Sound(name), pos, volume, pitch, true, maxDistance);
        }

        public static void Ui(string name, float volume = 1f, float pitch = 1f)
        {
            Play(McAssets.Sound(name), Game.CamPos, volume, pitch, false, 10f);
        }

        public static void Clip(AudioClip clip, Vector3 pos, float volume = 1f, float pitch = 1f)
        {
            Play(clip, pos, volume, pitch, true, 32f);
        }

        /// <summary>A looping sound attached to a transform (elytra wind, TNT fuse). Caller destroys it.</summary>
        public static AudioSource Loop(string name, Transform parent, float volume = 1f)
        {
            var clip = McAssets.Sound(name);
            if (clip == null || parent == null) return null;
            var src = parent.gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.maxDistance = 24f;
            src.volume = volume * Cfg.McSoundVolume.Value;
            src.Play();
            return src;
        }

        private static void Play(AudioClip clip, Vector3 pos, float volume, float pitch, bool spatial, float maxDistance)
        {
            if (clip == null) return;
            if (root == null)
            {
                root = new GameObject("BlockPeak.Sounds");
                Object.DontDestroyOnLoad(root);
            }
            var go = new GameObject("mc_sfx");
            go.transform.SetParent(root.transform);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume * Cfg.McSoundVolume.Value);
            src.pitch = pitch * Random.Range(0.95f, 1.05f);
            src.spatialBlend = spatial ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1.5f;
            src.maxDistance = maxDistance;
            src.dopplerLevel = 0f;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, src.pitch) + 0.1f);
        }
    }
}
