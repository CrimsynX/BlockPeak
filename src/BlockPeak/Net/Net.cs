using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Core;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace BlockPeak.Net
{
    public enum Op : byte
    {
        BlockPlaceReq = 1,
        BlockPlaced = 2,
        BlockBreakReq = 3,
        BlockBroken = 4,
        BlockSnapshot = 5,
        TntIgniteReq = 6,
        TntLit = 7,
        Explosion = 8,
        MobSpawn = 10,
        MobStates = 11,
        MobRemove = 12,
        MobHitReq = 13,
        MobHurt = 14,
        HurtPlayer = 15,
        Arrow = 16,
        Projectile = 20,
        WindBurst = 21,
        Horn = 22,
        Sound = 23,
        TotemPop = 24,
        Splash = 25,
        ItemCountSet = 26,
        Hello = 30,
        TestGive = 31,
        TestMob = 32,
        TestClear = 33,
        DebugCmd = 34,
        DebugReply = 35,
        ElytraState = 36,
        BoatPlaceReq = 37,
        BoatSpawned = 38,
        BoatMountReq = 39,
        BoatRider = 40,
        BoatState = 41,
        BoatPickupReq = 42,
        BoatRemoved = 43,
        BoatSnapshot = 44,
        BodyHit = 46,
        BodyHurt = 47,
        BodyDied = 48,
        BodyFlags = 49,
        ModeCountdown = 50,
        RainSpawn = 51,
        ModeMessage = 52,
        ArrowHitPlayer = 53,
        BowArrows = 54,
        ChestSpawn = 55,
        ChestOpenReq = 56,
        ChestOpened = 57,
        ChestSnapshot = 58,
    }

    /// <summary>
    /// BlockPeak's own messages, sent with Photon RaiseEvent on one event code so they never clash with PEAK's RPCs.
    /// Payload: object[] { (byte)op, args... }.
    /// </summary>
    public static class Channel
    {
        public const byte EventCode = 173;

        public delegate void Handler(object[] args, int senderActor);

        private static readonly Dictionary<Op, Handler> handlers = new Dictionary<Op, Handler>();
        private static bool hooked;
        private static LoadBalancingClient hookedClient;

        public static void On(Op op, Handler h) => handlers[op] = h;

        public static void EnsureHooked()
        {
            var client = PhotonNetwork.NetworkingClient;
            if (client == null) return;
            if (hooked && hookedClient == client) return;
            if (hookedClient != null) hookedClient.EventReceived -= OnEvent;
            client.EventReceived += OnEvent;
            hookedClient = client;
            hooked = true;
        }

        private static void OnEvent(EventData e)
        {
            if (e.Code != EventCode) return;
            try
            {
                if (!(e.CustomData is object[] data) || data.Length == 0) return;
                var op = (Op)(byte)data[0];
                var args = new object[data.Length - 1];
                Array.Copy(data, 1, args, 0, args.Length);
                Dispatch(op, args, e.Sender);
            }
            catch (Exception ex) { Health.Report("net-receive", ex); }
        }

        private static void Dispatch(Op op, object[] args, int sender)
        {
            if (handlers.TryGetValue(op, out var h))
            {
                try { h(args, sender); }
                catch (Exception ex) { Health.Report("net:" + op, ex); }
            }
        }

        private static object[] Pack(Op op, object[] args)
        {
            var data = new object[args.Length + 1];
            data[0] = (byte)op;
            Array.Copy(args, 0, data, 1, args.Length);
            return data;
        }

        private static int LocalActor => PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;

        /// <summary>Everyone including us (we handle ours immediately).</summary>
        public static void All(Op op, bool reliable, params object[] args)
        {
            Others(op, reliable, args);
            Dispatch(op, args, LocalActor);
        }

        public static void Others(Op op, bool reliable, params object[] args)
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode) return;
            PhotonNetwork.RaiseEvent(EventCode, Pack(op, args), new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                reliable ? SendOptions.SendReliable : SendOptions.SendUnreliable);
        }

        /// <summary>To the host. If we are the host it runs right away.</summary>
        public static void Host(Op op, params object[] args)
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode)
            {
                Dispatch(op, args, LocalActor);
                return;
            }
            PhotonNetwork.RaiseEvent(EventCode, Pack(op, args), new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient }, SendOptions.SendReliable);
        }

        public static void To(int actor, Op op, bool reliable, params object[] args)
        {
            if (actor == LocalActor || !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode)
            {
                Dispatch(op, args, LocalActor);
                return;
            }
            PhotonNetwork.RaiseEvent(EventCode, Pack(op, args), new RaiseEventOptions { TargetActors = new[] { actor } },
                reliable ? SendOptions.SendReliable : SendOptions.SendUnreliable);
        }

        // ---- small helpers for reading args ----
        public static int Int(object o) => o is int i ? i : Convert.ToInt32(o);
        public static float Flt(object o) => o is float f ? f : Convert.ToSingle(o);
        public static byte Byte(object o) => o is byte b ? b : Convert.ToByte(o);
        public static string Str(object o) => o as string ?? "";
        public static Vector3 Vec(object o) => o is Vector3 v ? v : Vector3.zero;
        public static bool Bool(object o) => o is bool b && b;
    }

    /// <summary>
    /// Version handshake through Photon player properties: everyone advertises their BlockPeak version,
    /// hotbar size and item list. The Airport banner warns about mismatches.
    /// </summary>
    public static class Handshake
    {
        public const string Key = "bp_ver";
        private static float nextPublish;
        public static string MismatchText { get; private set; } = "";
        public static List<string> MissingMod { get; } = new List<string>();

        public static string LocalSignature => $"{Plugin.Version}|{Cfg.HotbarSlots.Value}|{Items.ItemDefs.Signature}";

        public static void Tick()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) { MismatchText = ""; MissingMod.Clear(); return; }
            if (Time.unscaledTime >= nextPublish)
            {
                nextPublish = Time.unscaledTime + 3f;
                var props = PhotonNetwork.LocalPlayer.CustomProperties;
                if (!props.TryGetValue(Key, out var cur) || (cur as string) != LocalSignature)
                    PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { [Key] = LocalSignature });
                Check();
            }
        }

        private static void Check()
        {
            MissingMod.Clear();
            var problems = new List<string>();
            foreach (var p in PhotonNetwork.PlayerListOthers)
            {
                if (!p.CustomProperties.TryGetValue(Key, out var v) || !(v is string sig))
                {
                    MissingMod.Add(p.NickName);
                    continue;
                }
                if (sig == LocalSignature) continue;
                var a = sig.Split('|');
                var mine = LocalSignature.Split('|');
                if (a.Length > 0 && a[0] != mine[0]) problems.Add($"{p.NickName} has BlockPeak {a[0]} (you have {mine[0]})");
                else if (a.Length > 1 && a[1] != mine[1]) problems.Add($"{p.NickName} uses {a[1]} hotbar slots (you use {mine[1]})");
                else problems.Add($"{p.NickName} has a different BlockPeak item list");
            }
            if (MissingMod.Count > 0) problems.Add("No BlockPeak: " + string.Join(", ", MissingMod) + " (they need to run the setup)");
            MismatchText = string.Join("\n", problems);
        }
    }
}
