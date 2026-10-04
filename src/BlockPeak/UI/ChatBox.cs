using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Hotbar;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlockPeak.UI
{
    /// <summary>
    /// Minecraft-style command chat for debug mode. "/" opens it, Enter runs the command, Esc closes.
    /// Up/Down = earlier commands, Tab = complete. Private: nothing is sent to other players
    /// (commands that need the host are run by the host, the answer comes back only to you).
    /// </summary>
    public static class ChatBox
    {
        public static bool Open { get; private set; }

        private class Line
        {
            public string Text;
            public Color Color;
            public float Time;
            public Texture2D Tex;
        }

        private static readonly List<Line> lines = new List<Line>();
        private static readonly List<string> history = new List<string>();
        private static int historyIndex = -1;
        private static string input = "";
        private static bool hooked;
        private static float caretBlink;
        private static Texture2D black;

        public static void Print(string text, Color? color = null)
        {
            foreach (var part in text.Split('\n'))
                lines.Add(new Line { Text = part, Color = color ?? Color.white, Time = UnityEngine.Time.unscaledTime });
            while (lines.Count > 60) lines.RemoveAt(0);
        }

        public static void Error(string text) => Print(text, new Color(1f, 0.33f, 0.33f));

        public static void Tick()
        {
            if (!Cfg.Debug) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (!Open)
            {
                if (Game.LocalChar != null && !TestMenu.Open && (kb.slashKey.wasPressedThisFrame || kb.numpadDivideKey.wasPressedThisFrame))
                    SetOpen(true, "/");
                return;
            }
            if (kb.escapeKey.wasPressedThisFrame) { SetOpen(false); return; }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                string cmd = input.Trim();
                SetOpen(false);
                if (cmd.Length > 0)
                {
                    history.Add(cmd);
                    if (history.Count > 50) history.RemoveAt(0);
                    Commands.Run(cmd);
                }
                return;
            }
            if (kb.backspaceKey.wasPressedThisFrame && input.Length > 0) input = input.Substring(0, input.Length - 1);
            if (kb.upArrowKey.wasPressedThisFrame && history.Count > 0)
            {
                historyIndex = historyIndex < 0 ? history.Count - 1 : Mathf.Max(0, historyIndex - 1);
                input = history[historyIndex];
            }
            if (kb.downArrowKey.wasPressedThisFrame && historyIndex >= 0)
            {
                historyIndex++;
                if (historyIndex >= history.Count) { historyIndex = -1; input = "/"; }
                else input = history[historyIndex];
            }
            if (kb.tabKey.wasPressedThisFrame) input = Commands.Complete(input);
        }

        private static void SetOpen(bool open, string start = "")
        {
            Open = open;
            historyIndex = -1;
            input = start;
            var kb = Keyboard.current;
            if (open && !hooked && kb != null) { kb.onTextInput += OnText; hooked = true; }
            if (!open && hooked && kb != null) { kb.onTextInput -= OnText; hooked = false; }
        }

        private static void OnText(char c)
        {
            if (!Open || char.IsControl(c)) return;
            if (input.Length < 120) input += c;
        }

        private static int Scale()
        {
            int s = Cfg.HotbarScale.Value;
            if (s > 0) return s;
            int auto = 1;
            while (auto < 4 && Screen.width >= 320 * (auto + 1) && Screen.height >= 240 * (auto + 1)) auto++;
            return auto;
        }

        public static void Draw()
        {
            if (!Cfg.Debug) return;
            if (black == null) { black = new Texture2D(1, 1); black.SetPixel(0, 0, Color.black); black.Apply(); }
            int s = Scale();
            float now = UnityEngine.Time.unscaledTime;
            float lineH = 9 * s;
            float bottom = Screen.height - 14 * s - 2 * s;
            float width = Mathf.Min(320 * s, Screen.width - 4 * s);

            // Messages: fade out after 10 s unless the chat is open (like Minecraft).
            int shown = 0;
            for (int i = lines.Count - 1; i >= 0 && shown < (Open ? 20 : 10); i--)
            {
                var l = lines[i];
                float age = now - l.Time;
                float alpha = Open ? 1f : Mathf.Clamp01((10f - age) / 1f);
                if (alpha <= 0f) break;
                float y = bottom - (shown + 1) * lineH;
                DrawRect(new Rect(2 * s, y, width, lineH), new Color(0, 0, 0, 0.5f * alpha));
                if (l.Tex == null) l.Tex = McFont.Render(l.Text, l.Color);
                DrawText(l.Tex, l.Text, 3 * s, y + s * 0.5f, s, l.Color, alpha);
                shown++;
            }

            if (!Open) return;
            // Input line at the very bottom, full width.
            var inputRect = new Rect(2 * s, Screen.height - 14 * s, Screen.width - 4 * s, 12 * s);
            DrawRect(inputRect, new Color(0, 0, 0, 0.5f));
            caretBlink += UnityEngine.Time.unscaledDeltaTime;
            string shownText = input + ((caretBlink % 0.6f) < 0.3f ? "_" : "");
            var tex = McFont.Render(shownText, Color.white);
            DrawText(tex, shownText, 4 * s, inputRect.y + 2 * s, s, Color.white, 1f);
        }

        private static void DrawRect(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, black);
            GUI.color = old;
        }

        private static GUIStyle fallback;

        private static void DrawText(Texture2D tex, string text, float x, float y, int s, Color c, float alpha)
        {
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            if (tex != null) GUI.DrawTexture(new Rect(x, y, tex.width * s, tex.height * s), tex);
            else
            {
                if (fallback == null) fallback = new GUIStyle(GUI.skin.label) { fontSize = 8 * s };
                fallback.normal.textColor = c;
                GUI.Label(new Rect(x, y - 2, 2000, 10 * s), text, fallback);
            }
            GUI.color = old;
        }
    }
}
