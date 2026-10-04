using System;
using System.Linq;
using BlockPeak.Building;
using BlockPeak.Core;
using BlockPeak.Hotbar;
using BlockPeak.Items;
using BlockPeak.Mobs;
using BlockPeak.Net;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlockPeak.UI
{
    /// <summary>
    /// Creative-style test menu (Testing / TestMode in the .cfg): click an item to get a full stack, spawn any mob in
    /// front of you, toggle night for mobs, clear mobs/blocks. The host decides, so the host needs TestMode on.
    /// </summary>
    public static class TestMenu
    {
        public static bool Open { get; private set; }
        private static Vector2 scroll;
        private static GUIStyle label, title;

        public static void RegisterNet()
        {
            Channel.On(Op.TestGive, (a, s) => HostGive(Channel.Str(a[0]), s));
            Channel.On(Op.TestMob, (a, s) =>
            {
                if (!Cfg.Debug) return;
                var who = Character.AllCharacters.FirstOrDefault(c => c != null && c.photonView?.Owner?.ActorNumber == s);
                MobDirector.HostSummon(who ?? Game.LocalChar, Channel.Str(a[0]), 1);
            });
            Channel.On(Op.TestClear, (a, s) =>
            {
                if (!Cfg.Debug) return;
                if (Channel.Str(a[0]) == "mobs") MobDirector.HostKillAll();
                else BlockWorld.HostClearAll();
            });
        }

        public static void Tick()
        {
            if (!Cfg.Debug) { if (Open) SetOpen(false); return; }
            if (KeyInput.Down(Cfg.TestMenuKey.Value) && Game.LocalChar != null && !ChatBox.Open) SetOpen(!Open);
            if (Open && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) SetOpen(false);
            if (Open && Game.LocalChar == null) SetOpen(false);
        }

        public static void LateTick()
        {
            if (!Open) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static void SetOpen(bool open)
        {
            Open = open;
            if (!open)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public static void Draw()
        {
            if (!Open) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = 11 };
                title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            }
            float w = Mathf.Min(760, Screen.width - 40), h = Mathf.Min(620, Screen.height - 40);
            var area = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            GUI.Box(area, "");
            GUI.Box(area, "");
            GUILayout.BeginArea(new Rect(area.x + 12, area.y + 10, w - 24, h - 20));
            GUILayout.Label($"BlockPeak test menu   ({Cfg.TestMenuKey.Value} or Esc to close)", title);
            if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
                GUILayout.Label("You are not the host: the host must also have TestMode on.");
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("Items - click to get a full stack (dropped in front of you if your hotbar is full):");
            int perRow = Mathf.Max(4, (int)((w - 60) / 92));
            var defs = ItemDefs.All;
            for (int i = 0; i < defs.Count; i += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Math.Min(i + perRow, defs.Count); j++)
                {
                    var d = defs[j];
                    GUILayout.BeginVertical(GUILayout.Width(86));
                    var icon = ItemRegistry.Ready ? ItemRegistry.IconFor(d) : null;
                    if (GUILayout.Button(new GUIContent(icon, d.Name), GUILayout.Width(64), GUILayout.Height(64)))
                        Channel.Host(Op.TestGive, d.Key);
                    GUILayout.Label(d.Name, label, GUILayout.Width(86));
                    GUILayout.EndVertical();
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            GUILayout.Label("Mobs - click to spawn one about 6 m in front of you:");
            var types = MobDirector.Summonable.ToList();
            for (int i = 0; i < types.Count; i += 5)
            {
                GUILayout.BeginHorizontal();
                foreach (var t in types.Skip(i).Take(5))
                    if (GUILayout.Button(t.Replace('_', ' '), GUILayout.Width(130), GUILayout.Height(30))) Channel.Host(Op.TestMob, t);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            bool night = GUILayout.Toggle(BodyMobs.ForceNight, " Mobs act like it's night (host)", GUILayout.Width(260));
            if (night != BodyMobs.ForceNight) { BodyMobs.ForceNight = night; McMobs.ForceNight = night; }
            if (GUILayout.Button("Remove all mobs", GUILayout.Width(150), GUILayout.Height(28))) Channel.Host(Op.TestClear, "mobs");
            if (GUILayout.Button("Remove all blocks", GUILayout.Width(150), GUILayout.Height(28))) Channel.Host(Op.TestClear, "blocks");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("Tips: blocks, TNT, torches and ladders can be placed in the Airport too. Natural night spawns only happen on the mountain; " +
                            "the night toggle only changes how mobs behave (no burning/vanishing), not the sky.");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>Host: give a full stack of an item to a player.</summary>
        private static void HostGive(string key, int actor)
        {
            if (!Cfg.Debug) return;
            if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return;
            var def = ItemDefs.ByKey(key);
            if (def == null || !ItemRegistry.Templates.ContainsKey(def.Id)) return;
            var p = PlayerHandler.GetPlayer(actor) ?? Player.localPlayer;
            if (p == null) return;
            var data = new ItemInstanceData(Guid.NewGuid());
            ItemInstanceDataHandler.AddInstanceData(data);
            if (def.Stack > 1) Stacks.SetCount(data, def.Stack, def.Stack);
            if (def.Kind == McKind.Elytra) Stacks.SetDurability(data, Balance.F(def.Cfg, "startDurability", 0.12f));
            Stacks.MarkRolled(data);
            try
            {
                if (p.AddItem(def.Id, data, out _))
                {
                    p.character?.refs.items.RefreshAllCharacterCarryWeight();
                    return;
                }
                var c = p.character;
                if (c == null) return;
                Vector3 pos = c.Head + Vector3.ProjectOnPlane(c.data.lookDirection, Vector3.up).normalized * 1.2f;
                var go = PhotonNetwork.InstantiateItemRoom(def.PrefabName, pos, Quaternion.identity);
                var view = go.GetComponent<PhotonView>();
                view.RPC("SetItemInstanceDataRPC", RpcTarget.All, data);
                view.RPC("SetKinematicRPC", RpcTarget.AllBuffered, false, go.transform.position, go.transform.rotation);
            }
            catch (Exception e) { Health.Report("test-give", e); }
        }
    }

    /// <summary>While the test menu is open, the scout ignores mouse and keys (so you can click).</summary>
    [HarmonyPatch(typeof(CharacterInput), "Sample")]
    internal static class CharacterInput_Sample_Patch
    {
        private static bool Prefix(CharacterInput __instance)
        {
            if (!TestMenu.Open && !ChatBox.Open) return true;
            __instance.ResetInput();
            __instance.pauseWasPressed = false;
            __instance.selectSlotForwardWasPressed = false;
            __instance.selectSlotBackwardWasPressed = false;
            __instance.unselectSlotWasPressed = false;
            __instance.selectBackpackWasPressed = false;
            __instance.scrollForwardWasPressed = false;
            __instance.scrollBackwardWasPressed = false;
            __instance.scrollForwardIsPressed = false;
            __instance.scrollBackwardIsPressed = false;
            __instance.scrollInput = 0f;
            __instance.spectateLeftWasPressed = false;
            __instance.spectateRightWasPressed = false;
            return false;
        }
    }
}
