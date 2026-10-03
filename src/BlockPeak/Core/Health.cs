using System;
using System.Collections.Generic;

namespace BlockPeak.Core
{
    /// <summary>
    /// Keeps track of features that failed so the rest keeps running, and so the Airport banner can tell the player.
    /// </summary>
    public static class Health
    {
        private static readonly Dictionary<string, string> problems = new Dictionary<string, string>();
        private static readonly HashSet<string> loggedOnce = new HashSet<string>();

        public static IEnumerable<KeyValuePair<string, string>> Problems => problems;
        public static int Count => problems.Count;

        public static void Report(string feature, Exception e)
        {
            Report(feature, e?.GetBaseException().Message ?? "unknown error", e);
        }

        public static void Report(string feature, string message, Exception e = null)
        {
            problems[feature] = message;
            if (loggedOnce.Add(feature + "|" + message))
            {
                Plugin.Log.LogError($"[{feature}] {message}" + (e != null ? "\n" + e : ""));
            }
        }

        /// <summary>Run an action; on failure report it once and keep going.</summary>
        public static void Guard(string feature, Action a)
        {
            try { a(); }
            catch (Exception e) { Report(feature, e); }
        }

        public static T Guard<T>(string feature, Func<T> f, T fallback = default)
        {
            try { return f(); }
            catch (Exception e) { Report(feature, e); return fallback; }
        }

        public static void Verbose(string msg)
        {
            if (Cfg.VerboseLog != null && Cfg.VerboseLog.Value) Plugin.Log.LogInfo(msg);
        }
    }
}
