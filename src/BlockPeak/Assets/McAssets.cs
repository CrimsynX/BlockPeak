using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BlockPeak.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace BlockPeak.Assets
{
    /// <summary>
    /// Loads the Minecraft textures/sounds that were copied from the player's install.
    /// Anything missing gets a generated stand-in so the mod still works (just uglier).
    /// </summary>
    public static class McAssets
    {
        public enum State { NotStarted, Extracting, LoadingSounds, Ready, Failed }

        public static State Status { get; private set; } = State.NotStarted;
        public static string StatusText { get; private set; } = "";
        public static bool HasRealTextures { get; private set; }

        /// <summary>Goes up whenever textures are reloaded, so cached UI can rebuild.</summary>
        public static int Generation { get; private set; }

        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<string, List<AudioClip>> groups = new Dictionary<string, List<AudioClip>>();

        private static volatile bool extractDone;
        private static volatile string extractMessage;

        public static string TexDir => Path.Combine(Extractor.OutDir, "textures");
        public static string SndDir => Path.Combine(Extractor.OutDir, "sounds");

        public static void Begin(MonoBehaviour host)
        {
            if (Status != State.NotStarted) return;
            if (Extractor.IsUpToDate())
            {
                HasRealTextures = true;
                host.StartCoroutine(LoadSounds());
                return;
            }

            Status = State.Extracting;
            StatusText = "Copying Minecraft textures and sounds...";
            string userPath = Cfg.MinecraftPath.Value;
            string prefer = Cfg.PreferMinecraftVersion.Value;
            var t = new Thread(() =>
            {
                try
                {
                    var mc = Extractor.Find(userPath, prefer, out string why);
                    if (mc == null) { extractMessage = "!" + why; }
                    else
                    {
                        string summary = Extractor.Run(mc, m => Plugin.Log.LogWarning(m));
                        extractMessage = (string.IsNullOrEmpty(why) ? "" : why + " ") + summary;
                    }
                }
                catch (Exception e) { extractMessage = "!" + e.Message; }
                extractDone = true;
            }) { IsBackground = true, Name = "BlockPeak asset copy" };
            t.Start();
            host.StartCoroutine(WaitForExtract(host));
        }

        private static IEnumerator WaitForExtract(MonoBehaviour host)
        {
            while (!extractDone) yield return null;
            string msg = extractMessage ?? "";
            if (msg.StartsWith("!"))
            {
                Status = State.Failed;
                StatusText = msg.Substring(1);
                Health.Report("minecraft-assets", StatusText);
                yield break;
            }
            Plugin.Log.LogInfo(msg);
            HasRealTextures = true;
            textures.Clear(); // drop any placeholders made while copying
            Generation++;
            yield return LoadSounds();
        }

        private static IEnumerator LoadSounds()
        {
            Status = State.LoadingSounds;
            StatusText = "Loading Minecraft sounds...";
            string[] files = new string[0];
            try { if (Directory.Exists(SndDir)) files = Directory.GetFiles(SndDir, "*.ogg", SearchOption.AllDirectories); }
            catch (Exception e) { Health.Report("sounds", e); }

            foreach (var f in files)
            {
                string key = f.Substring(SndDir.Length + 1).Replace('\\', '/');
                key = key.Substring(0, key.Length - 4);
                using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(f).AbsoluteUri, AudioType.OGGVORBIS))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) continue;
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip == null) continue;
                    clip.name = "mc:" + key;
                    clips[key] = clip;
                    string group = TrimDigits(key);
                    if (!groups.TryGetValue(group, out var list)) groups[group] = list = new List<AudioClip>();
                    list.Add(clip);
                }
            }
            Status = State.Ready;
            StatusText = $"{clips.Count} Minecraft sounds loaded.";
            Health.Verbose(StatusText);
        }

        private static string TrimDigits(string s)
        {
            int i = s.Length;
            while (i > 0 && char.IsDigit(s[i - 1])) i--;
            return s.Substring(0, i);
        }

        /// <summary>A random variant of a sound: "random/explode" picks explode1..4, "item/totem/use_totem" an exact file.</summary>
        public static AudioClip Sound(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (groups.TryGetValue(name, out var list) && list.Count > 0) return list[UnityEngine.Random.Range(0, list.Count)];
            if (clips.TryGetValue(name, out var c)) return c;
            return null;
        }

        /// <summary>First sound found among several names (mob sounds differ between mobs).</summary>
        public static AudioClip FirstSound(params string[] names)
        {
            foreach (var n in names)
            {
                var c = Sound(n);
                if (c != null) return c;
            }
            return null;
        }

        public static bool HasTexture(string path) => File.Exists(Path.Combine(TexDir, path.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>Texture from assets/minecraft/textures/&lt;path&gt;, pixel-perfect. Never null.</summary>
        public static Texture2D Tex(string path)
        {
            if (textures.TryGetValue(path, out var t) && t != null) return t;
            t = null;
            try
            {
                string f = Path.Combine(TexDir, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(f))
                {
                    t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!t.LoadImage(File.ReadAllBytes(f), false)) t = null;
                }
            }
            catch (Exception e) { Health.Report("texture:" + path, e); t = null; }
            if (t == null) t = Placeholder(path);
            // Animated textures (a vertical strip of frames) -> keep the first frame.
            if (t.height > t.width && t.height % t.width == 0 && !path.StartsWith("entity/") && !path.StartsWith("gui/"))
                t = Crop(t, 0, 0, t.width, t.width);
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            t.name = "mc:" + path;
            textures[path] = t;
            return t;
        }

        public static Texture2D Crop(Texture2D src, int x, int yFromTop, int w, int h)
        {
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = src.GetPixels(x, src.height - yFromTop - h, w, h);
            dst.SetPixels(px);
            dst.Apply();
            dst.filterMode = FilterMode.Point;
            return dst;
        }

        /// <summary>A recognisable stand-in when the real texture was not copied.</summary>
        private static Texture2D Placeholder(string path)
        {
            int size = path.StartsWith("entity/") ? 64 : 16;
            int height = path.StartsWith("entity/") && !path.Contains("zombie") && !path.Contains("husk") && !path.Contains("drowned") ? 32 : size;
            var t = new Texture2D(size, height, TextureFormat.RGBA32, false);
            Color a = ColorFor(path), b = a * 0.75f;
            b.a = 1f;
            bool isItem = path.StartsWith("item/");
            for (int y = 0; y < height; y++)
                for (int x = 0; x < size; x++)
                {
                    Color c = ((x / 4 + y / 4) % 2 == 0) ? a : b;
                    if (isItem)
                    {
                        // Round blob for items so they are not just squares.
                        float dx = x - 7.5f, dy = y - 7.5f;
                        if (dx * dx + dy * dy > 42f) c = Color.clear;
                    }
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        private static Color ColorFor(string path)
        {
            string p = path.ToLowerInvariant();
            if (p.Contains("sand")) return new Color(0.86f, 0.81f, 0.6f);
            if (p.Contains("plank") || p.Contains("ladder") || p.Contains("boat")) return new Color(0.64f, 0.5f, 0.3f);
            if (p.Contains("moss")) return new Color(0.35f, 0.47f, 0.18f);
            if (p.Contains("ice")) return new Color(0.55f, 0.7f, 0.95f);
            if (p.Contains("terracotta")) return new Color(0.6f, 0.37f, 0.27f);
            if (p.Contains("basalt") || p.Contains("deepslate")) return new Color(0.3f, 0.3f, 0.33f);
            if (p.Contains("stone")) return new Color(0.5f, 0.5f, 0.5f);
            if (p.Contains("tnt")) return new Color(0.8f, 0.2f, 0.15f);
            if (p.Contains("gold") || p.Contains("totem")) return new Color(0.95f, 0.8f, 0.2f);
            if (p.Contains("pearl")) return new Color(0.1f, 0.45f, 0.4f);
            if (p.Contains("cookie") || p.Contains("beef") || p.Contains("flesh")) return new Color(0.6f, 0.35f, 0.2f);
            if (p.Contains("zombie") || p.Contains("creeper") || p.Contains("slime")) return new Color(0.35f, 0.65f, 0.3f);
            if (p.Contains("skeleton") || p.Contains("stray")) return new Color(0.8f, 0.8f, 0.8f);
            if (p.Contains("magma")) return new Color(0.5f, 0.15f, 0.05f);
            return new Color(0.7f, 0.2f, 0.7f);
        }
    }
}
