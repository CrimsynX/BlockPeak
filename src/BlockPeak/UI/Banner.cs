using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Items;
using BlockPeak.Net;
using UnityEngine;

namespace BlockPeak.UI
{
    /// <summary>Plain on-screen text: the Airport welcome, warnings, and short toasts.</summary>
    public static class Banner
    {
        private static string toast;
        private static float toastUntil;
        private static float welcomeUntil;
        private static bool wasInAirport;
        private static GUIStyle style, small;

        public static void Toast(string text, float seconds = 2.5f)
        {
            toast = text;
            toastUntil = Time.unscaledTime + seconds;
        }

        public static void Tick()
        {
            bool airport = Game.InAirport;
            if (airport && !wasInAirport) welcomeUntil = Time.unscaledTime + 14f;
            wasInAirport = airport;
        }

        public static void Draw()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 55), richText = true, wordWrap = true };
                small = new GUIStyle(style) { fontSize = Mathf.Max(12, Screen.height / 70) };
            }

            var lines = new List<string>();
            bool airport = Game.InAirport;
            if (airport && (Time.unscaledTime < welcomeUntil && Cfg.ShowWelcome.Value))
            {
                lines.Add($"<b><color=#7CFC00>BlockPeak {Plugin.Version}</color></b>  {Cfg.HotbarSlots.Value}-slot hotbar · {(ItemRegistry.Ready ? ItemRegistry.Templates.Count + " Minecraft items" : "items loading...")} · mobs at night");
                if (McAssets.Status == McAssets.State.Failed) lines.Add("<color=#FFB000>Minecraft textures not found:</color> " + McAssets.StatusText);
                else if (McAssets.Status == McAssets.State.Extracting) lines.Add(McAssets.StatusText);
                if (Balance.UsingHostCopy) lines.Add("Using the host's BlockPeak settings.");
            }
            if (airport || Time.unscaledTime < welcomeUntil)
            {
                if (!string.IsNullOrEmpty(Handshake.MismatchText)) lines.Add("<color=#FF6060>" + Handshake.MismatchText + "</color>");
                if (Health.Count > 0 && Time.unscaledTime < welcomeUntil)
                    lines.Add($"<color=#FFB000>{Health.Count} BlockPeak feature(s) had problems</color> (see BepInEx/LogOutput.log): " + string.Join(", ", Health.Problems.Select(p => p.Key).Take(4)));
            }
            if (lines.Count > 0)
            {
                float w = Mathf.Min(Screen.width * 0.6f, 900f);
                var text = string.Join("\n", lines);
                float h = style.CalcHeight(new GUIContent(text), w) + 12;
                var r = new Rect(16, 16, w, h);
                Shadowed(r, text, style);
            }
            if (toast != null && Time.unscaledTime < toastUntil)
            {
                float w = 700f;
                var r = new Rect((Screen.width - w) / 2f, Screen.height * 0.70f, w, 40);
                var centered = new GUIStyle(style) { alignment = TextAnchor.MiddleCenter };
                Shadowed(r, toast, centered);
            }
        }

        private static void Shadowed(Rect r, string text, GUIStyle s)
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), StripColor(text), s);
            GUI.color = old;
            GUI.Label(r, text, s);
        }

        private static string StripColor(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<color=[^>]+>|</color>", "");
    }
}
