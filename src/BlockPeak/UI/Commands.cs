using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlockPeak.Core;
using BlockPeak.Mobs;
using BlockPeak.Net;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.UI
{
    /// <summary>
    /// Debug commands (only when debug mode was on when PEAK started):
    /// /time set day|night|noon|midnight|&lt;ticks&gt;, /gamemode creative|survival, /summon &lt;mob&gt; [count],
    /// /kill @e, /heal, /help. Commands that change the world are run by the host (who must also be in debug mode).
    /// </summary>
    public static class Commands
    {
        private static readonly string[] Names = { "time", "gamemode", "summon", "kill", "heal", "help" };

        public static void RegisterNet()
        {
            Channel.On(Op.DebugCmd, (a, sender) =>
            {
                if (!PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom) return;
                string reply;
                if (!Cfg.Debug) reply = "!The host is not in debug mode.";
                else reply = HostRun(Channel.Str(a[0]), sender);
                Channel.To(sender, Op.DebugReply, true, reply ?? "");
            });
            Channel.On(Op.DebugReply, (a, s) =>
            {
                string r = Channel.Str(a[0]);
                if (r.StartsWith("!")) ChatBox.Error(r.Substring(1));
                else if (r.Length > 0) ChatBox.Print(r, new Color(0.7f, 0.7f, 0.7f));
            });
        }

        public static void Run(string line)
        {
            if (!line.StartsWith("/")) { ChatBox.Error("Commands start with /. Type /help."); return; }
            var parts = line.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            string cmd = parts[0].ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    case "help":
                        ChatBox.Print("Commands:", new Color(1f, 1f, 0.33f));
                        ChatBox.Print("/time set <day|night|noon|midnight|ticks>");
                        ChatBox.Print("/gamemode <creative|survival>");
                        ChatBox.Print("/summon <mob> [count]   mobs: " + string.Join(", ", MobDirector.Summonable));
                        ChatBox.Print("/kill @e   (removes every mob)");
                        ChatBox.Print("/heal");
                        return;
                    case "gamemode":
                        Gamemode(parts);
                        return;
                    case "heal":
                        Heal();
                        return;
                    case "time":
                    case "summon":
                    case "kill":
                        Channel.Host(Op.DebugCmd, line);
                        return;
                    default:
                        ChatBox.Error($"Unknown command \"{cmd}\". Type /help.");
                        return;
                }
            }
            catch (Exception e)
            {
                ChatBox.Error("That did not work: " + e.Message);
                Health.Report("command", e);
            }
        }

        /// <summary>Host side of the world-changing commands. Returns the text for the sender ("!" = error).</summary>
        private static string HostRun(string line, int sender)
        {
            var parts = line.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLowerInvariant();
            var who = Character.AllCharacters.FirstOrDefault(c => c != null && !c.isBot && c.photonView?.Owner?.ActorNumber == sender) ?? Game.LocalChar;
            switch (cmd)
            {
                case "time":
                {
                    if (parts.Length < 3 || parts[1].ToLowerInvariant() != "set") return "!Usage: /time set <day|night|noon|midnight|ticks>";
                    float hours;
                    switch (parts[2].ToLowerInvariant())
                    {
                        case "day": hours = 7f; break;
                        case "noon": hours = 12f; break;
                        case "night": hours = 22f; break;
                        case "midnight": hours = 0f; break;
                        default:
                            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float ticks)) return "!Not a time: " + parts[2];
                            hours = ((ticks / 1000f) + 6f) % 24f; // Minecraft ticks -> hours (0 = 6:00)
                            break;
                    }
                    var dn = DayNightManager.instance;
                    if (dn == null) return "!There is no day/night here (try on the mountain).";
                    dn.setTimeOfDay(hours);
                    var view = dn.GetComponent<PhotonView>();
                    if (view != null && PhotonNetwork.InRoom) view.RPC("RPCA_SyncTime", RpcTarget.All, dn.dayCount, hours);
                    McMobs.ForceNight = false;
                    BodyMobs.ForceNight = false;
                    return $"Set the time to {Mathf.FloorToInt(hours):00}:{Mathf.FloorToInt(hours % 1f * 60f):00}";
                }
                case "summon":
                {
                    if (parts.Length < 2) return "!Usage: /summon <mob> [count]";
                    string type = parts[1].ToLowerInvariant().Replace("minecraft:", "");
                    int count = 1;
                    if (parts.Length > 2 && !int.TryParse(parts[2], out count)) return "!Not a number: " + parts[2];
                    count = Mathf.Clamp(count, 1, 50);
                    if (!MobDirector.Summonable.Contains(type)) return "!Unknown mob: " + type + ". Try: " + string.Join(", ", MobDirector.Summonable);
                    int made = MobDirector.HostSummon(who, type, count);
                    return made > 0 ? $"Summoned {made} {type}" : "!Could not summon here.";
                }
                case "kill":
                {
                    if (parts.Length < 2 || parts[1] != "@e") return "!Only /kill @e is supported (removes every mob).";
                    int n = MobDirector.HostKillAll();
                    return $"Killed {n} mob(s)";
                }
            }
            return "!Unknown command";
        }

        private static void Gamemode(string[] parts)
        {
            if (parts.Length < 2) { ChatBox.Error("Usage: /gamemode <creative|survival>"); return; }
            string m = parts[1].ToLowerInvariant();
            if (m == "creative" || m == "c" || m == "1") { Creative.On = true; ChatBox.Print("Set own game mode to Creative Mode", new Color(0.7f, 0.7f, 0.7f)); }
            else if (m == "survival" || m == "s" || m == "0") { Creative.On = false; ChatBox.Print("Set own game mode to Survival Mode", new Color(0.7f, 0.7f, 0.7f)); }
            else ChatBox.Error("Unknown game mode: " + m);
        }

        private static void Heal()
        {
            var c = Game.LocalChar;
            if (c == null) { ChatBox.Error("No scout to heal."); return; }
            var aff = c.refs.afflictions;
            foreach (var t in new[] { CharacterAfflictions.STATUSTYPE.Injury, CharacterAfflictions.STATUSTYPE.Hunger, CharacterAfflictions.STATUSTYPE.Cold, CharacterAfflictions.STATUSTYPE.Hot, CharacterAfflictions.STATUSTYPE.Poison, CharacterAfflictions.STATUSTYPE.Drowsy, CharacterAfflictions.STATUSTYPE.Spores })
                aff.SetStatus(t, 0f);
            c.AddStamina(1f);
            ChatBox.Print("Healed", new Color(0.7f, 0.7f, 0.7f));
        }

        public static string Complete(string input)
        {
            if (!input.StartsWith("/")) return input;
            var parts = input.Substring(1).Split(' ');
            if (parts.Length == 1)
            {
                var m = Names.Where(n => n.StartsWith(parts[0].ToLowerInvariant())).ToList();
                return m.Count == 1 ? "/" + m[0] + " " : input;
            }
            string last = parts[parts.Length - 1].ToLowerInvariant();
            IEnumerable<string> options = Enumerable.Empty<string>();
            switch (parts[0].ToLowerInvariant())
            {
                case "summon": if (parts.Length == 2) options = MobDirector.Summonable; break;
                case "gamemode": if (parts.Length == 2) options = new[] { "creative", "survival" }; break;
                case "time": options = parts.Length == 2 ? new[] { "set" } : new[] { "day", "night", "noon", "midnight" }; break;
                case "kill": options = new[] { "@e" }; break;
            }
            var hits = options.Where(o => o.StartsWith(last)).ToList();
            if (hits.Count != 1) { if (hits.Count > 1) ChatBox.Print(string.Join(", ", hits), new Color(0.7f, 0.7f, 0.7f)); return input; }
            parts[parts.Length - 1] = hits[0];
            return "/" + string.Join(" ", parts) + " ";
        }
    }

    /// <summary>
    /// /gamemode creative: nothing hurts you, stamina never runs out, and double-tap jump to fly
    /// (jump = up, crouch = down). Only for the local player, only in debug mode.
    /// </summary>
    public static class Creative
    {
        public static bool On;
        public static bool Flying;
        private static float lastJump = -1f;

        public static void Tick()
        {
            var c = Game.LocalChar;
            if (!On || c == null || !Cfg.Debug) { Flying = false; return; }
            c.data.currentStamina = Mathf.Max(c.data.currentStamina, 1f);
            c.data.sinceGrounded = Mathf.Min(c.data.sinceGrounded, 0.2f);
            if (c.input.jumpWasPressed)
            {
                if (Time.time - lastJump < 0.3f) { Flying = !Flying; lastJump = -1f; }
                else lastJump = Time.time;
            }
            if (c.data.isGrounded && Flying && c.input.crouchIsPressed) Flying = false;
        }

        public static void FixedTick()
        {
            var c = Game.LocalChar;
            if (!On || !Flying || c == null) return;
            Vector3 fwd = Game.CamForward; fwd.y = 0; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector2 mv = c.input.movementInput;
            float speed = c.input.sprintIsPressed ? 16f : 8f;
            Vector3 target = (fwd * mv.y + right * mv.x) * speed;
            if (c.input.jumpIsPressed) target.y = 6f;
            else if (c.input.crouchIsPressed) target.y = -6f;
            foreach (var part in c.refs.ragdoll.partList)
            {
                var rig = part.Rig;
                if (rig == null) continue;
                rig.linearVelocity = Vector3.Lerp(rig.linearVelocity, target, 0.25f);
            }
        }

        /// <summary>Creative: block every harmful status for the local scout.</summary>
        public static bool BlocksStatus(CharacterAfflictions.STATUSTYPE t) =>
            On && Cfg.Debug && t != CharacterAfflictions.STATUSTYPE.Weight && t != CharacterAfflictions.STATUSTYPE.Curse;
    }
}
