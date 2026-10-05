using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Items;
using BlockPeak.Mobs;
using BlockPeak.Net;
using BlockPeak.UI;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlockPeak.Modes
{
    public enum ModeKind { None, WardenChase, ZombieChase }

    /// <summary>
    /// Warden Chase and Zombie Chase. When a custom run with one of them starts, the host waits for the first scout
    /// to move, then everyone sees a 10 second countdown, then the warden (or the horde) arrives.
    /// Also hands out the starter kit and runs the anvil/TNT rain.
    /// </summary>
    public static class GameModes
    {
        public static ModeKind Active = ModeKind.None;
        public static bool MobsAlwaysKnow => Active != ModeKind.None;

        private enum Phase { Idle, WaitingForMove, Countdown, Running }
        private static Phase phase = Phase.Idle;
        private static string runScene;
        private static float runStartedAt;
        private static readonly Dictionary<int, Vector3> startPositions = new Dictionary<int, Vector3>();
        private static Vector3 startPoint;
        private static float countdownEnd = -1f;     // everyone (local time)
        private static int lastShown = -1;
        private static string message;
        private static float messageUntil;
        private static bool kitGiven;

        private static Newtonsoft.Json.Linq.JToken Cfg => Balance.Section("modes");
        private static bool IsHost => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;

        public static void RegisterNet()
        {
            Channel.On(Op.ModeCountdown, (a, s) =>
            {
                countdownEnd = Time.time + Channel.Flt(a[0]);
                lastShown = -1;
                message = Channel.Str(a[1]);
                messageUntil = countdownEnd;
            });
            Channel.On(Op.ModeMessage, (a, s) =>
            {
                message = Channel.Str(a[0]);
                messageUntil = Time.time + Channel.Flt(a[1]);
                string sound = a.Length > 2 ? Channel.Str(a[2]) : "";
                if (sound.Length > 0) Sfx.Ui(sound, 1f);
            });
        }

        public static void Tick()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (!Game.InRun)
            {
                if (runScene != null) { Reset(); runScene = null; }
                return;
            }
            if (scene != runScene) { Reset(); runScene = scene; runStartedAt = Time.time; }

            if (IsHost) TickHost();
            if (countdownEnd > 0f)
            {
                int left = Mathf.CeilToInt(countdownEnd - Time.time);
                if (left != lastShown && left >= 0)
                {
                    lastShown = left;
                    if (left > 0) Sfx.Ui("random/click", 1f, left <= 3 ? 1.4f : 1f);
                }
                if (Time.time > countdownEnd + 2f) countdownEnd = -1f;
            }
        }

        private static void Reset()
        {
            phase = Phase.Idle;
            Active = ModeKind.None;
            startPositions.Clear();
            countdownEnd = -1f;
            kitGiven = false;
            Rains.Reset();
        }

        private static void TickHost()
        {
            if (phase == Phase.Idle)
            {
                // Decide what this run is (only once, a moment after the run starts).
                if (Time.time - runStartedAt < 2f) return;
                Active = CustomOptions.WardenChase ? ModeKind.WardenChase
                       : CustomOptions.ZombieChase ? ModeKind.ZombieChase : ModeKind.None;
                foreach (var p in BodyMobs.Players) startPositions[p.photonView.Owner.ActorNumber] = p.Center;
                startPoint = startPositions.Count > 0 ? startPositions.Values.First() : Vector3.zero;
                Rains.Configure(CustomOptions.AnvilRain, CustomOptions.TntRain);
                phase = Active == ModeKind.None ? Phase.Running : Phase.WaitingForMove;
                if (Active != ModeKind.None) Plugin.Log.LogInfo("Game mode: " + Active);
            }

            if (!kitGiven && CustomOptions.StarterKit && Time.time - runStartedAt > 4f)
            {
                kitGiven = true;
                GiveStarterKits();
            }

            if (phase == Phase.WaitingForMove)
            {
                foreach (var p in BodyMobs.Players)
                {
                    int actor = p.photonView.Owner.ActorNumber;
                    if (!startPositions.TryGetValue(actor, out var start)) { startPositions[actor] = p.Center; continue; }
                    if (Vector3.Distance(Flat(p.Center), Flat(start)) > Balance.F(Cfg, "moveToStart", 2.5f))
                    {
                        float seconds = Balance.F(Cfg, "countdownSeconds", 10f);
                        string what = Active == ModeKind.WardenChase ? "The Warden is coming" : "The horde is coming";
                        Channel.All(Op.ModeCountdown, true, seconds, what);
                        phase = Phase.Countdown;
                        Runner.Instance.StartCoroutine(AfterCountdown(seconds));
                        break;
                    }
                }
            }
            Rains.TickHost();
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static System.Collections.IEnumerator AfterCountdown(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (phase != Phase.Countdown) yield break;
            phase = Phase.Running;
            if (Active == ModeKind.WardenChase)
            {
                // The warden climbs out of the ground where the run started.
                Vector3 at = startPoint;
                if (Physics.Raycast(startPoint + Vector3.up * 20f, Vector3.down, out var hit, 60f, Game.TerrainMask, QueryTriggerInteraction.Ignore)) at = hit.point;
                var w = BodyMobs.HostSpawn("warden", at, 0f);
                Channel.All(Op.ModeMessage, true, "The Warden has risen. RUN!", 4f, "mob/warden/emerge");
                Channel.All(Op.Sound, true, "mob/warden/roar", at, 1f);
            }
            else if (Active == ModeKind.ZombieChase)
            {
                int count = Mathf.Clamp(Balance.I(Cfg["zombieChase"], "count", 120), 1, 600);
                int made = McMobs.HostSpawnHorde(count);
                Channel.All(Op.ModeMessage, true, $"{made} zombies are coming. RUN!", 4f, "mob/zombie/say");
            }
        }

        // ------------------------------------------------------------------ starter kit

        private static void GiveStarterKits()
        {
            var kit = Cfg["starterKit"] as Newtonsoft.Json.Linq.JObject;
            if (kit == null) return;
            foreach (var c in BodyMobs.Players)
            {
                var p = c.player;
                if (p == null) continue;
                foreach (var prop in kit.Properties())
                {
                    if (prop.Name.StartsWith("_")) continue;
                    var def = ItemDefs.ByKey(prop.Name == "blocks" ? Loot.BlockFor(Loot.PoolName(SpawnPool.LuggageBeach)).Key : prop.Name);
                    if (def == null || !ItemRegistry.Templates.ContainsKey(def.Id)) continue;
                    var data = new ItemInstanceData(Guid.NewGuid());
                    ItemInstanceDataHandler.AddInstanceData(data);
                    int n = Mathf.Clamp((int)prop.Value, 1, def.Stack);
                    if (def.Stack > 1) Stacks.SetCount(data, n, def.Stack);
                    if (def.Kind == McKind.Elytra) Stacks.SetDurability(data, Balance.F(def.Cfg, "startDurability", 1f));
                    if (def.Kind == McKind.Bow) Stacks.SetArrows(data, Balance.I(def.Cfg, "arrows", 16));
                    Stacks.MarkRolled(data);
                    try { p.AddItem(def.Id, data, out _); } catch (Exception e) { Health.Report("starter-kit", e); }
                }
                c.refs.items.RefreshAllCharacterCarryWeight();
            }
            Channel.All(Op.ModeMessage, true, "Starter kit!", 3f, "random/pop");
        }

        // ------------------------------------------------------------------ big title text (Minecraft /title style)

        public static void Draw()
        {
            string big = null;
            if (countdownEnd > 0f && Time.time < countdownEnd)
                big = Mathf.CeilToInt(countdownEnd - Time.time).ToString();
            string small = Time.time < messageUntil ? message : null;
            if (big == null && small == null) return;
            int s = Mathf.Max(2, Screen.height / 180);
            if (big != null) DrawCentered(big, Screen.height * 0.32f, s * 4, new Color(1f, 0.33f, 0.33f));
            if (small != null) DrawCentered(small, Screen.height * 0.32f + (big != null ? 10 * s * 4 : 0), s * 2, Color.white);
        }

        private static GUIStyle fallback;

        private static void DrawCentered(string text, float y, int scale, Color c)
        {
            var tex = McFont.Render(text, c);
            if (tex != null)
            {
                float w = tex.width * scale, h = tex.height * scale;
                GUI.DrawTexture(new Rect((Screen.width - w) / 2f, y, w, h), tex);
                return;
            }
            if (fallback == null) fallback = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            fallback.fontSize = scale * 8;
            fallback.normal.textColor = c;
            GUI.Label(new Rect(0, y, Screen.width, scale * 12), text, fallback);
        }
    }
}
