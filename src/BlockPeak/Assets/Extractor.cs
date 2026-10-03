using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BlockPeak.Assets
{
    /// <summary>
    /// Copies the textures and sounds named in <see cref="AssetList"/> out of the player's own Minecraft install.
    /// Works with the Modrinth App, the official launcher, Prism Launcher and CurseForge.
    /// The setup program normally does this already; the mod repeats it only if the folder is missing or outdated.
    /// Pure file IO: safe to run on a background thread.
    /// </summary>
    public static class Extractor
    {
        public class Install
        {
            public string Root;        // folder with versions/ and assets/
            public string Launcher;
            public string VersionId;   // e.g. 26.3 or 26.3-0.19.5
            public string Jar;
            public string AssetIndex;  // path to assets/indexes/<id>.json
            public string ObjectsDir;  // assets/objects
            public override string ToString() => $"{Launcher}: {VersionId} ({Jar})";
        }

        public static string OutDir => Path.Combine(Plugin.DataDir, "mc-assets");
        public static string StampFile => Path.Combine(OutDir, "source.json");

        public static bool IsUpToDate()
        {
            try
            {
                if (!File.Exists(StampFile)) return false;
                var j = JObject.Parse(File.ReadAllText(StampFile));
                return (string)j["listHash"] == AssetList.Hash;
            }
            catch { return false; }
        }

        /// <summary>Every place a Minecraft install could be, best guess first.</summary>
        public static IEnumerable<(string root, string launcher)> CandidateRoots(string userPath)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userPath)) yield return (userPath.Trim().Trim('"'), "custom path");
            yield return (Path.Combine(appData, "ModrinthApp", "meta"), "Modrinth App");
            yield return (Path.Combine(appData, "com.modrinth.theseus", "meta"), "Modrinth App (old)");
            yield return (Path.Combine(appData, ".minecraft"), "Minecraft Launcher");
            yield return (Path.Combine(appData, "PrismLauncher"), "Prism Launcher");
            yield return (Path.Combine(home, "curseforge", "minecraft", "Install"), "CurseForge");
            yield return (Path.Combine(appData, ".minecraft", "..", "..", "Local", "Packages", "Microsoft.4297127D64EC6_8wekyb3d8bbwe", "LocalCache", "Roaming", ".minecraft"), "Minecraft Launcher (Store)");
        }

        public static Install Find(string userPath, string preferVersion, out string why)
        {
            var found = new List<Install>();
            var notes = new List<string>();
            foreach (var (root, launcher) in CandidateRoots(userPath))
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    found.AddRange(ScanRoot(root, launcher));
                }
                catch (Exception e) { notes.Add(launcher + ": " + e.Message); }
            }
            if (found.Count == 0)
            {
                why = "No Minecraft install with a client jar was found. Start Minecraft " + preferVersion + " once from the Modrinth App, then run setup again." + (notes.Count > 0 ? " (" + string.Join("; ", notes) + ")" : "");
                return null;
            }
            // Prefer the requested version, then the newest jar on disk.
            var best = found
                .OrderByDescending(i => VersionMatches(i.VersionId, preferVersion) ? 1 : 0)
                .ThenByDescending(i => i.AssetIndex != null ? 1 : 0)
                .ThenByDescending(i => SafeTime(i.Jar))
                .First();
            why = VersionMatches(best.VersionId, preferVersion) ? "" : $"Minecraft {preferVersion} not found, using {best.VersionId} instead.";
            return best;
        }

        private static bool VersionMatches(string id, string want)
        {
            if (string.IsNullOrEmpty(want)) return false;
            return id == want || id.StartsWith(want + "-") || id.StartsWith(want + "_") || id.StartsWith("fabric-loader-") && id.EndsWith("-" + want);
        }

        private static DateTime SafeTime(string f)
        {
            try { return File.GetLastWriteTimeUtc(f); } catch { return DateTime.MinValue; }
        }

        private static IEnumerable<Install> ScanRoot(string root, string launcher)
        {
            string assets = Path.Combine(root, "assets");
            string versions = Path.Combine(root, "versions");

            if (Directory.Exists(versions))
            {
                foreach (var dir in Directory.GetDirectories(versions))
                {
                    string id = Path.GetFileName(dir);
                    string jar = Path.Combine(dir, id + ".jar");
                    if (!File.Exists(jar) || new FileInfo(jar).Length < 1_000_000) continue; // loader stubs are tiny
                    yield return new Install
                    {
                        Root = root,
                        Launcher = launcher,
                        VersionId = id,
                        Jar = jar,
                        AssetIndex = FindIndex(assets, Path.Combine(dir, id + ".json")),
                        ObjectsDir = Path.Combine(assets, "objects"),
                    };
                }
            }

            // Prism keeps the client jar in libraries/com/mojang/minecraft/<ver>/
            string prismLibs = Path.Combine(root, "libraries", "com", "mojang", "minecraft");
            if (Directory.Exists(prismLibs))
            {
                foreach (var dir in Directory.GetDirectories(prismLibs))
                {
                    string id = Path.GetFileName(dir);
                    string jar = Directory.GetFiles(dir, "*client*.jar").FirstOrDefault();
                    if (jar == null) continue;
                    yield return new Install
                    {
                        Root = root, Launcher = launcher, VersionId = id, Jar = jar,
                        AssetIndex = FindIndex(assets, null), ObjectsDir = Path.Combine(assets, "objects"),
                    };
                }
            }
        }

        private static string FindIndex(string assetsDir, string versionJson)
        {
            string indexes = Path.Combine(assetsDir, "indexes");
            if (!Directory.Exists(indexes)) return null;
            try
            {
                if (versionJson != null && File.Exists(versionJson))
                {
                    var j = JObject.Parse(File.ReadAllText(versionJson));
                    string id = (string)j["assets"] ?? (string)j["assetIndex"]?["id"];
                    if (!string.IsNullOrEmpty(id))
                    {
                        string p = Path.Combine(indexes, id + ".json");
                        if (File.Exists(p)) return p;
                    }
                }
            }
            catch { }
            // Fall back to the newest index on disk.
            return Directory.GetFiles(indexes, "*.json").OrderByDescending(SafeTime).FirstOrDefault();
        }

        /// <summary>Copy everything in the list. Returns a one-line summary.</summary>
        public static string Run(Install mc, Action<string> log)
        {
            Directory.CreateDirectory(OutDir);
            int tex = 0, snd = 0, missingTex = 0, missingSnd = 0;
            var missing = new List<string>();

            using (var zip = ZipFile.OpenRead(mc.Jar))
            {
                var byName = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                foreach (var e in zip.Entries) byName[e.FullName] = e;
                foreach (var entry in AssetList.Entries.Where(x => !x.IsSound))
                {
                    string key = "assets/minecraft/textures/" + entry.Path;
                    if (!byName.TryGetValue(key, out var ze)) { missingTex++; missing.Add(entry.Path); continue; }
                    string dest = Path.Combine(OutDir, "textures", entry.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    using (var src = ze.Open())
                    using (var dst = File.Create(dest)) src.CopyTo(dst);
                    tex++;
                }
            }

            if (mc.AssetIndex != null && File.Exists(mc.AssetIndex))
            {
                var objects = (JObject)JObject.Parse(File.ReadAllText(mc.AssetIndex))["objects"];
                var keys = objects.Properties().Select(p => p.Name).Where(n => n.StartsWith("minecraft/sounds/")).ToList();
                foreach (var entry in AssetList.Entries.Where(x => x.IsSound))
                {
                    IEnumerable<string> matches = entry.IsFolder
                        ? keys.Where(k => k.StartsWith("minecraft/sounds/" + entry.Path))
                        : keys.Where(k => k == "minecraft/sounds/" + (entry.Path.EndsWith(".ogg") ? entry.Path : entry.Path + ".ogg"));
                    bool any = false;
                    foreach (var k in matches)
                    {
                        string hash = (string)objects[k]["hash"];
                        string src = Path.Combine(mc.ObjectsDir, hash.Substring(0, 2), hash);
                        if (!File.Exists(src)) continue;
                        string rel = k.Substring("minecraft/sounds/".Length);
                        string dest = Path.Combine(OutDir, "sounds", rel.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(src, dest, true);
                        snd++;
                        any = true;
                    }
                    if (!any) { missingSnd++; missing.Add("sound " + entry.Path); }
                }
            }
            else
            {
                log?.Invoke("No Minecraft sound index found: BlockPeak will be quiet. Start Minecraft once so the launcher downloads sounds.");
            }

            var stamp = new JObject
            {
                ["listHash"] = AssetList.Hash,
                ["minecraft"] = mc.VersionId,
                ["launcher"] = mc.Launcher,
                ["jar"] = mc.Jar,
                ["textures"] = tex,
                ["sounds"] = snd,
                ["missing"] = new JArray(missing.Take(50)),
                ["by"] = "BlockPeak " + Plugin.Version,
                ["date"] = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(StampFile, stamp.ToString());
            string summary = $"Copied {tex} textures and {snd} sounds from {mc}. Missing: {missingTex} textures, {missingSnd} sounds.";
            if (missing.Count > 0) log?.Invoke("Not found in this Minecraft version: " + string.Join(", ", missing.Take(20)));
            return summary;
        }
    }
}
