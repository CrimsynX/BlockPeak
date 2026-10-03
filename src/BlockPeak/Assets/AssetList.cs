using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace BlockPeak.Assets
{
    /// <summary>
    /// The list of Minecraft files BlockPeak copies from the player's own Minecraft install
    /// (config/minecraft-assets.txt, embedded in the DLL). The setup program reads the same file.
    /// </summary>
    public static class AssetList
    {
        public struct Entry
        {
            public bool IsSound;
            public string Path;      // "item/cookie.png" or "random/pop" or "mob/zombie/"
            public bool IsFolder => Path.EndsWith("/");
        }

        private static List<Entry> entries;
        private static string rawText;

        public static string RawText
        {
            get { Load(); return rawText; }
        }

        public static IReadOnlyList<Entry> Entries
        {
            get { Load(); return entries; }
        }

        /// <summary>Short hash of the list, so the mod can tell when an update needs new files.</summary>
        public static string Hash
        {
            get
            {
                Load();
                unchecked
                {
                    uint h = 2166136261;
                    foreach (char c in rawText) { if (c == '\r') continue; h = (h ^ c) * 16777619; }
                    return h.ToString("x8");
                }
            }
        }

        private static void Load()
        {
            if (entries != null) return;
            entries = new List<Entry>();
            rawText = "";
            try
            {
                // A copy next to the DLL wins (lets people add files without rebuilding).
                string beside = System.IO.Path.Combine(Plugin.PluginDir ?? "", "minecraft-assets.txt");
                if (File.Exists(beside)) rawText = File.ReadAllText(beside);
                else
                {
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BlockPeak.minecraft-assets.txt"))
                    using (var r = new StreamReader(s)) rawText = r.ReadToEnd();
                }
            }
            catch (Exception e)
            {
                Core.Health.Report("asset-list", e);
                return;
            }

            foreach (var raw in rawText.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("format")) continue;
                int sp = line.IndexOf(' ');
                if (sp < 0) continue;
                string kind = line.Substring(0, sp).Trim();
                string path = line.Substring(sp + 1).Trim().Replace('\\', '/');
                if (kind == "tex") entries.Add(new Entry { IsSound = false, Path = path });
                else if (kind == "snd") entries.Add(new Entry { IsSound = true, Path = path });
            }
        }
    }
}
