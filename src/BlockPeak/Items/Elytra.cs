using System;
using System.Collections.Generic;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Hotbar;
using BlockPeak.Net;
using BlockPeak.UI;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace BlockPeak.Items
{
    /// <summary>
    /// The elytra is worn, not held: selecting its hotbar slot puts it on your back (Minecraft's wing model, drawn
    /// over the backpack) and shows it in the chest slot next to the hotbar. Jump while falling to glide with
    /// Minecraft's flight physics (look down to dive, up to climb). One firework boost per elytra: press Use while
    /// gliding. 12% durability = about 52 seconds of flight, then it breaks.
    /// </summary>
    public static class Elytra
    {
        public static bool Gliding { get; private set; }
        private static Vector3 flightVel;
        private static float boostLeft;
        private static float lastSpeed;
        private static float syncTimer;
        private static AudioSource loop;
        private static readonly Dictionary<int, WingsView> wings = new Dictionary<int, WingsView>();

        private static McItemDef Def => ItemDefs.ByKey("elytra");
        private static Newtonsoft.Json.Linq.JToken Cfg => Def?.Cfg;

        public static void RegisterNet()
        {
            // Owner -> host: keep the host's copy of durability / firework use up to date (the host owns inventories).
            Channel.On(Op.ElytraState, (a, sender) =>
            {
                if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return;
                var p = PlayerHandler.GetPlayer(sender) ?? Player.localPlayer;
                var slot = p?.GetItemSlot(Channel.Byte(a[0]));
                if (slot == null || slot.IsEmpty() || Def == null || slot.prefab.itemID != Def.Id) return;
                Stacks.SetDurability(slot.data, Channel.Flt(a[1]));
                Stacks.SetFireworksUsed(slot.data, Channel.Int(a[2]));
            });
        }

        /// <summary>Is this scout wearing an elytra (its slot selected)?</summary>
        public static bool Wearing(Character c, out ItemSlot slot)
        {
            slot = null;
            if (c == null || c.player == null || Def == null) return false;
            var sel = c.refs.items.currentSelectedSlot;
            if (sel.IsNone) return false;
            var s = c.player.GetItemSlot(sel.Value);
            if (s == null || s.IsEmpty() || s.prefab.itemID != Def.Id) return false;
            slot = s;
            return true;
        }

        public static bool IsElytra(ItemSlot s) => s != null && !s.IsEmpty() && Def != null && s.prefab.itemID == Def.Id;

        // ------------------------------------------------------------------ every frame

        public static void Tick()
        {
            UpdateWings();
            var c = Game.LocalChar;
            if (c == null) { Stop(); return; }
            if (!Wearing(c, out var slot)) { Stop(); return; }
            float dur = Stacks.Durability(slot.data, Balance.F(Cfg, "startDurability", 1f));

            if (!Gliding)
            {
                // Minecraft: jump while in the air to open the wings.
                if (c.input.jumpWasPressed && !c.data.isGrounded && !c.data.isClimbingAnything && c.data.sinceGrounded > 0.15f && dur > 0f && Game.CanAct(c))
                    Start(c);
                return;
            }

            if (c.data.isGrounded || c.data.isClimbingAnything || c.data.isInWater || !Game.CanAct(c)) { Stop(); return; }

            // Firework boost: Use (or the firework key) while gliding, once per elytra.
            bool fire = c.input.usePrimaryWasPressed || KeyInput.Down(Core.Cfg.FireworkKey.Value);
            if (fire)
            {
                int used = Stacks.FireworksUsed(slot.data);
                int allowed = Balance.I(Cfg, "fireworks", 1);
                if (used < allowed)
                {
                    Stacks.SetFireworksUsed(slot.data, used + 1);
                    boostLeft = Balance.F(Cfg, "fireworkSeconds", 1.5f);
                    Channel.All(Op.Sound, false, "fireworks/launch", c.Center, 1f);
                    SyncToHost(slot, dur);
                }
                else Sfx.Ui("random/click", 0.5f);
            }

            // Durability: Minecraft loses 1 point per second of flight.
            float total = Mathf.Max(1f, Balance.F(Cfg, "glideSeconds", 52f));
            dur -= Time.deltaTime / total; // glideSeconds = flight time from full durability
            Stacks.SetDurability(slot.data, Mathf.Max(0f, dur));
            syncTimer -= Time.deltaTime;
            if (syncTimer <= 0f) { syncTimer = 1f; SyncToHost(slot, dur); }
            if (dur <= 0f) Break(c, slot);
        }

        private static void SyncToHost(ItemSlot slot, float dur)
        {
            Channel.Host(Op.ElytraState, slot.itemSlotID, Mathf.Max(0f, dur), Stacks.FireworksUsed(slot.data));
        }

        private static void Start(Character c)
        {
            Gliding = true;
            flightVel = c.data.avarageVelocity;
            lastSpeed = flightVel.magnitude;
            loop = Sfx.Loop("item/elytra/elytra_loop", c.GetBodypart(BodypartType.Torso).transform, 0.6f);
        }

        private static void Stop()
        {
            if (!Gliding) return;
            Gliding = false;
            boostLeft = 0f;
            if (loop != null) UnityEngine.Object.Destroy(loop);
        }

        private static void Break(Character c, ItemSlot slot)
        {
            Stop();
            Channel.All(Op.Sound, true, "random/break", c.Center, 1f);
            c.player.EmptySlot(Optionable<byte>.Some(slot.itemSlotID));
            c.refs.items.EquipSlot(Optionable<byte>.None);
            Banner.Toast("Your elytra broke!");
        }

        // ------------------------------------------------------------------ physics (Minecraft's fall-flying)

        public static void FixedTick()
        {
            var c = Game.LocalChar;
            if (!Gliding || c == null) return;
            float dt = Time.fixedDeltaTime;
            float k = dt * 20f; // Minecraft ticks this step
            Vector3 look = Game.CamForward.normalized;
            Vector3 v = flightVel / 20f; // blocks per tick
            float pitch = -Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)); // Minecraft pitch: positive = looking down
            float horizLook = Mathf.Sqrt(look.x * look.x + look.z * look.z);
            float horizVel = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            float cos = Mathf.Cos(pitch);
            cos = cos * cos;

            v.y += 0.08f * (-1f + cos * 0.75f) * k;
            if (v.y < 0f && horizLook > 0f)
            {
                float q = v.y * -0.1f * cos * k;
                v += new Vector3(look.x * q / horizLook, q, look.z * q / horizLook);
            }
            if (pitch < 0f && horizLook > 0f)
            {
                float q = horizVel * -Mathf.Sin(pitch) * 0.04f * k;
                v += new Vector3(-look.x * q / horizLook, q * 3.2f, -look.z * q / horizLook);
            }
            if (horizLook > 0f)
            {
                v.x += (look.x / horizLook * horizVel - v.x) * 0.1f * k;
                v.z += (look.z / horizLook * horizVel - v.z) * 0.1f * k;
            }
            if (boostLeft > 0f)
            {
                boostLeft -= dt;
                v += (look * 0.1f + (look * 1.5f - v) * 0.5f) * k;
            }
            v.x *= Mathf.Pow(0.99f, k);
            v.y *= Mathf.Pow(0.98f, k);
            v.z *= Mathf.Pow(0.99f, k);
            flightVel = v * 20f;

            foreach (var part in c.refs.ragdoll.partList)
            {
                var rig = part.Rig;
                if (rig != null) rig.linearVelocity = flightVel;
            }
            c.data.sinceGrounded = Mathf.Min(c.data.sinceGrounded, 0.4f);

            // Flying into a wall at speed hurts (Minecraft's "experienced kinetic energy").
            float speed = c.data.avarageVelocity.magnitude;
            if (lastSpeed > 12f && speed < lastSpeed * 0.45f)
            {
                float kk = Mathf.InverseLerp(12f, 30f, lastSpeed);
                c.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury,
                    Mathf.Lerp(Balance.F(Cfg, "crashInjuryMin", 0.1f), Balance.F(Cfg, "crashInjuryMax", 0.4f), kk));
                Sfx.At("damage/hit", c.Head, 0.8f);
                flightVel = c.data.avarageVelocity;
            }
            lastSpeed = Mathf.Max(speed, flightVel.magnitude * 0.9f);
        }

        // ------------------------------------------------------------------ wings on everyone's back

        private static void UpdateWings()
        {
            foreach (var c in Character.AllCharacters)
            {
                if (c == null || c.isBot) continue;
                int key = c.GetInstanceID();
                bool on = Wearing(c, out _) && !c.data.dead;
                wings.TryGetValue(key, out var view);
                if (on && view == null)
                {
                    view = WingsView.Create(c);
                    wings[key] = view;
                }
                else if (!on && view != null)
                {
                    UnityEngine.Object.Destroy(view.gameObject);
                    wings.Remove(key);
                }
            }
        }

        public static void Clear()
        {
            Stop();
            foreach (var w in wings.Values) if (w != null) UnityEngine.Object.Destroy(w.gameObject);
            wings.Clear();
        }
    }

    /// <summary>Minecraft's elytra model (entity/equipment/wings/elytra.png): two wings on the scout's back, spread while gliding.</summary>
    public class WingsView : MonoBehaviour
    {
        private Character owner;
        private Transform left, right;
        private float spread;

        public static WingsView Create(Character c)
        {
            var go = new GameObject("BP_ElytraWings");
            var w = go.AddComponent<WingsView>();
            w.owner = c;
            var tex = McAssets.Tex("entity/equipment/wings/elytra.png");
            var mat = Mat.For(tex);
            const float s = 1f / 20f; // Minecraft model pixel -> PEAK units (scout is ~1.6 high)
            // Minecraft ElytraModel: left wing texOffs(22,0) box(-10,0,0, 10,20,2) at (5,0,0); right wing mirrored.
            w.left = Wing(go.transform, "left", mat, false, s);
            w.right = Wing(go.transform, "right", mat, true, s);
            return w;
        }

        private static Transform Wing(Transform parent, string name, Material mat, bool mirror, float s)
        {
            var pivot = new GameObject("wing_" + name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = new Vector3(mirror ? 1.5f * s : -1.5f * s, 0, 0);
            var mesh = new GameObject("mesh");
            mesh.transform.SetParent(pivot, false);
            mesh.transform.localScale = Vector3.one * (s * 16f);
            var mb = new MeshBuilder { Cut = mat.mainTexture as Texture2D };
            // Unity x is Minecraft -x: the left wing's box (0..10) reaches out to the left, the right one to the right.
            if (!mirror) mb.McBox(0, 0, 0, 10, 20, 2, 22, 0, 64, 32);
            else mb.McBox(-10, 0, 0, 10, 20, 2, 22, 0, 64, 32, true);
            mesh.AddComponent<MeshFilter>().sharedMesh = mb.Build("mc_elytra_" + name);
            var r = mesh.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return pivot;
        }

        private void LateUpdate()
        {
            if (owner == null) { Destroy(gameObject); return; }
            var torso = owner.GetBodypart(BodypartType.Torso);
            var hip = owner.GetBodypart(BodypartType.Hip);
            if (torso == null || hip == null) return;
            Vector3 up = (torso.transform.position - hip.transform.position).normalized;
            if (up.sqrMagnitude < 0.01f) up = Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(owner.data.lookDirection, up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(owner.transform.forward, up);
            fwd.Normalize();
            bool hasBackpack = owner.player != null && !owner.player.backpackSlot.IsEmpty();
            float back = hasBackpack ? 0.42f : 0.2f; // sit over the backpack
            transform.position = torso.transform.position - fwd * back + up * 0.18f;
            transform.rotation = Quaternion.LookRotation(fwd, up);

            bool flying = owner.IsLocal ? Elytra.Gliding : (!owner.data.isGrounded && owner.data.sinceGrounded > 0.5f && owner.data.avarageVelocity.magnitude > 6f);
            spread = Mathf.MoveTowards(spread, flying ? 1f : 0f, Time.deltaTime * 4f);
            // Folded: hanging down, a little out and back. Spread: out to the sides like Minecraft's gliding pose.
            float z = Mathf.Lerp(12f, 78f, spread);
            float x = Mathf.Lerp(12f, 25f, spread);
            left.localRotation = Quaternion.Euler(x, 0, -z);
            right.localRotation = Quaternion.Euler(x, 0, z);

            // Hide our own wings in first person when they would block the camera.
            if (owner.IsLocal)
            {
                var cam = Game.Cam;
                bool close = cam != null && Vector3.Distance(cam.transform.position, transform.position) < 0.5f;
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = !close;
            }
        }
    }

    /// <summary>Selecting the elytra slot wears it instead of putting it in the scout's hand.</summary>
    [HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.EquipSlot))]
    internal static class CharacterItems_EquipSlot_ElytraPatch
    {
        private static bool Prefix(CharacterItems __instance, Optionable<byte> slotID)
        {
            try
            {
                var c = __instance.character;
                if (slotID.IsNone || c == null || c.player == null || !__instance.photonView.IsMine) return true;
                var slot = c.player.GetItemSlot(slotID.Value);
                if (!Elytra.IsElytra(slot)) return true;
                __instance.lastEquippedSlotTime = Time.time;
                __instance.lastSelectedSlot = slotID;
                if (c.data.currentItem != null)
                {
                    c.data.currentItem.CancelUsePrimary();
                    c.data.currentItem.CancelUseSecondary();
                }
                __instance.currentSelectedSlot = slotID;
                __instance.photonView.RPC("EquipSlotRpc", RpcTarget.All, (int)slotID.Value, -1);
                c.refs.afflictions.UpdateWeight();
                Sfx.At("item/armor/equip_generic", c.Center, 0.6f);
                return false;
            }
            catch (Exception e)
            {
                Health.Report("elytra-equip", e);
                return true;
            }
        }
    }
}
