using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Items;
using BlockPeak.Net;
using BlockPeak.UI;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Building
{
    /// <summary>
    /// Minecraft boats: place one (click with the boat), hold Interact on it to get in, steer with the movement keys
    /// (forward/back, left/right turn), jump or crouch to get out. Fast on water, faster on snow and ice, slow on
    /// ground. No fall damage while you are in a boat. Crouch + hold Interact on an empty boat picks it up.
    /// The host keeps the list of boats; whoever rides a boat moves it and tells everyone.
    /// </summary>
    public static class Boats
    {
        public class Boat
        {
            public int Id;
            public Vector3 Pos, TargetPos;
            public float Yaw, TargetYaw;
            public int Rider = -1; // actor number
            public float VertVel;
            public float Speed;
            public BoatView View;
        }

        private static readonly Dictionary<int, Boat> boats = new Dictionary<int, Boat>();
        private static int nextId = 1;
        private static readonly HashSet<int> knownActors = new HashSet<int>();
        private static float nextSend;
        public static Boat Riding { get; private set; }
        public static Vector2 SteerInput;
        public static bool ExitPressed;

        private static int Me => PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : 0;
        private static bool IsHost => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;
        private static Newtonsoft.Json.Linq.JToken Cfg => Balance.ItemCfg("boat");

        public static void RegisterNet()
        {
            Channel.On(Op.BoatPlaceReq, (a, s) =>
            {
                if (!IsHost) return;
                int id = nextId++;
                Channel.All(Op.BoatSpawned, true, id, Channel.Vec(a[0]), Channel.Flt(a[1]));
            });
            Channel.On(Op.BoatSpawned, (a, s) => Spawn(Channel.Int(a[0]), Channel.Vec(a[1]), Channel.Flt(a[2]), -1));
            Channel.On(Op.BoatMountReq, (a, s) =>
            {
                if (!IsHost || !boats.TryGetValue(Channel.Int(a[0]), out var b)) return;
                if (b.Rider >= 0 && b.Rider != s && PhotonNetwork.CurrentRoom?.GetPlayer(b.Rider) != null) return;
                Channel.All(Op.BoatRider, true, b.Id, s);
            });
            Channel.On(Op.BoatRider, (a, s) =>
            {
                if (!boats.TryGetValue(Channel.Int(a[0]), out var b)) return;
                int rider = Channel.Int(a[1]);
                b.Rider = rider;
                if (rider == Me) { Riding = b; b.Speed = 0f; Sfx.At("entity/boat/paddle_land", b.Pos, 0.6f); }
                else if (Riding == b) Riding = null;
            });
            Channel.On(Op.BoatState, (a, s) =>
            {
                if (!boats.TryGetValue(Channel.Int(a[0]), out var b) || b == Riding) return;
                b.TargetPos = Channel.Vec(a[1]);
                b.TargetYaw = Channel.Flt(a[2]);
                if (IsHost) { b.Pos = Vector3.Lerp(b.Pos, b.TargetPos, 0.5f); }
            });
            Channel.On(Op.BoatPickupReq, (a, s) =>
            {
                if (!IsHost || !boats.TryGetValue(Channel.Int(a[0]), out var b) || b.Rider >= 0) return;
                Channel.All(Op.BoatRemoved, true, b.Id);
                DropItem(b.Pos + Vector3.up * 0.5f);
            });
            Channel.On(Op.BoatRemoved, (a, s) => Remove(Channel.Int(a[0])));
            Channel.On(Op.BoatSnapshot, (a, s) =>
            {
                var ids = (int[])a[0];
                var data = (float[])a[1];
                var riders = (int[])a[2];
                for (int i = 0; i < ids.Length; i++)
                    if (!boats.ContainsKey(ids[i])) Spawn(ids[i], new Vector3(data[i * 4], data[i * 4 + 1], data[i * 4 + 2]), data[i * 4 + 3], riders[i]);
            });
        }

        public static bool TryPlaceFromView()
        {
            var c = Game.LocalChar;
            if (c == null) return false;
            float reach = Balance.F(Balance.Section("building"), "reach", 4.5f) + 1f;
            Vector3 from = Game.CamPos, dir = Game.CamForward;
            Vector3? at = null;
            if (Physics.Raycast(from, dir, out var hit, reach, Game.TerrainMask, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.5f)
                at = hit.point;
            // Water surfaces count too.
            var water = WaterAt(from + dir * Mathf.Min(reach, hit.collider != null ? hit.distance : reach), out float surface);
            if (water && (!at.HasValue || surface > at.Value.y)) at = new Vector3((from + dir * reach).x, surface, (from + dir * reach).z);
            if (!at.HasValue) return false;
            float yaw = Quaternion.LookRotation(Flat(dir)).eulerAngles.y;
            Channel.Host(Op.BoatPlaceReq, at.Value, yaw);
            Sfx.At("dig/wood", at.Value, 0.8f);
            return true;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized; }

        /// <summary>Is there PEAK water here, and where is its surface?</summary>
        public static bool WaterAt(Vector3 p, out float surface)
        {
            surface = float.MinValue;
            bool found = false;
            foreach (var z in WaterZones())
            {
                var b = z.zoneBounds;
                if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
                if (p.y < b.min.y - 2f || p.y > b.max.y + 6f) continue;
                if (b.max.y > surface) { surface = b.max.y; found = true; }
            }
            return found;
        }

        private static WaterZone[] zones;
        private static float zonesAt;

        private static WaterZone[] WaterZones()
        {
            if (zones == null || Time.time - zonesAt > 10f)
            {
                zones = UnityEngine.Object.FindObjectsByType<WaterZone>(FindObjectsSortMode.None);
                zonesAt = Time.time;
            }
            return zones;
        }

        private static void Spawn(int id, Vector3 pos, float yaw, int rider)
        {
            if (boats.ContainsKey(id)) return;
            nextId = Math.Max(nextId, id + 1);
            var b = new Boat { Id = id, Pos = pos, TargetPos = pos, Yaw = yaw, TargetYaw = yaw, Rider = rider };
            b.View = BoatView.Create(b);
            boats[id] = b;
            if (rider == Me) Riding = b;
        }

        private static void Remove(int id)
        {
            if (!boats.TryGetValue(id, out var b)) return;
            boats.Remove(id);
            if (Riding == b) Riding = null;
            if (b.View != null) UnityEngine.Object.Destroy(b.View.gameObject);
        }

        private static void DropItem(Vector3 at)
        {
            var def = ItemDefs.ByKey("boat");
            var t = def != null ? Loot.TemplateFor(def) : null;
            if (t == null || !PhotonNetwork.InRoom) return;
            var go = PhotonNetwork.InstantiateItemRoom(t.name, at, Quaternion.identity);
            var item = go.GetComponent<Item>();
            item.GetData<BoolItemData>(Stacks.RolledKey);
            Stacks.MarkRolled(item.data);
            item.photonView.RPC("SetKinematicRPC", RpcTarget.AllBuffered, false, go.transform.position, go.transform.rotation);
        }

        public static void RequestMount(Boat b) => Channel.Host(Op.BoatMountReq, b.Id);
        public static void RequestPickup(Boat b) => Channel.Host(Op.BoatPickupReq, b.Id);

        public static void Clear()
        {
            foreach (var b in boats.Values) if (b.View != null) UnityEngine.Object.Destroy(b.View.gameObject);
            boats.Clear();
            Riding = null;
            knownActors.Clear();
        }

        // ------------------------------------------------------------------ per frame

        public static void Tick()
        {
            // Host: snapshot for late joiners.
            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
            {
                foreach (var p in PhotonNetwork.PlayerListOthers)
                {
                    if (!knownActors.Add(p.ActorNumber) || boats.Count == 0) continue;
                    var list = boats.Values.ToList();
                    Channel.To(p.ActorNumber, Op.BoatSnapshot, true, list.Select(x => x.Id).ToArray(),
                        list.SelectMany(x => new[] { x.Pos.x, x.Pos.y, x.Pos.z, x.Yaw }).ToArray(), list.Select(x => x.Rider).ToArray());
                }
            }
            // Riders that left the game free their boat.
            foreach (var b in boats.Values)
            {
                if (b.Rider >= 0 && PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.GetPlayer(b.Rider) == null) b.Rider = -1;
                if (b != Riding)
                {
                    b.Pos = Vector3.Lerp(b.Pos, b.TargetPos, 1f - Mathf.Exp(-10f * Time.deltaTime));
                    b.Yaw = Mathf.LerpAngle(b.Yaw, b.TargetYaw, 1f - Mathf.Exp(-10f * Time.deltaTime));
                }
                if (b.View != null) b.View.Apply();
            }

            var c = Game.LocalChar;
            if (Riding == null || c == null) return;
            if (c.data.dead || c.data.passedOut || ExitPressed)
            {
                ExitPressed = false;
                Channel.All(Op.BoatRider, true, Riding.Id, -1);
                Riding = null;
                return;
            }
            if (Time.time >= nextSend)
            {
                nextSend = Time.time + 0.1f;
                Channel.Others(Op.BoatState, false, Riding.Id, Riding.Pos, Riding.Yaw);
            }
        }

        /// <summary>Rider: move the boat and keep the scout in it.</summary>
        public static void FixedTick()
        {
            var b = Riding;
            var c = Game.LocalChar;
            if (b == null || c == null) return;
            float dt = Time.fixedDeltaTime;
            bool onWater = WaterAt(b.Pos + Vector3.up * 0.5f, out float surface);
            float ground = float.MinValue;
            Vector3 groundNormal = Vector3.up;
            bool icy = false;
            if (Physics.Raycast(b.Pos + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, Game.TerrainMask, QueryTriggerInteraction.Ignore))
            {
                ground = hit.point.y;
                groundNormal = hit.normal;
                var pb = hit.collider.GetComponentInParent<PlacedBlock>();
                var r = hit.collider.GetComponent<Renderer>();
                string m = r != null && r.sharedMaterial != null ? r.sharedMaterial.name.ToLowerInvariant() : "";
                icy = (pb != null && pb.Def != null && pb.Def.Key == "packed_ice") || m.Contains("snow") || m.Contains("ice") || Game.CurrentBiome == Biome.BiomeType.Alpine;
            }
            float floor = Mathf.Max(ground, onWater ? surface - 0.1f : float.MinValue);
            bool grounded = floor > float.MinValue && b.Pos.y <= floor + 0.05f;

            float max = onWater && surface >= ground ? Balance.F(Cfg, "waterSpeed", 9f) : icy ? Balance.F(Cfg, "snowSpeed", 7f) : Balance.F(Cfg, "landSpeed", 2f);
            float turn = SteerInput.x * 90f * dt;
            b.Yaw += turn;
            float want = SteerInput.y * max;
            if (want < 0) want *= 0.4f;
            b.Speed = Mathf.MoveTowards(b.Speed, want, (grounded ? 6f : 1f) * dt);
            Vector3 fwd = Quaternion.Euler(0, b.Yaw, 0) * Vector3.forward;
            // Downhill sliding on snow/ice like Minecraft ice boats.
            if (icy && grounded) b.Speed += Vector3.Dot(Vector3.ProjectOnPlane(Vector3.down, groundNormal), fwd) * 6f * dt;
            Vector3 move = fwd * b.Speed * dt;
            if (Physics.Raycast(b.Pos + Vector3.up * 0.5f, fwd * Mathf.Sign(b.Speed), out var wall, Mathf.Abs(b.Speed * dt) + 0.8f, Game.TerrainMask, QueryTriggerInteraction.Ignore) && wall.normal.y < 0.5f)
            {
                move = Vector3.ProjectOnPlane(move, wall.normal);
                b.Speed *= 0.5f;
            }
            Vector3 next = b.Pos + move;
            if (grounded && b.VertVel <= 0f) { b.VertVel = 0f; next.y = Mathf.MoveTowards(b.Pos.y, floor, 10f * dt); }
            else { b.VertVel -= 20f * dt; next.y += b.VertVel * dt; if (floor > float.MinValue && next.y < floor) { next.y = floor; b.VertVel = 0f; } }
            b.Pos = next;
            b.TargetPos = next;
            b.TargetYaw = b.Yaw;

            // Keep the scout seated: pull every body part along with the boat.
            Vector3 seat = b.Pos + Vector3.up * 0.55f;
            var hip = c.GetBodypart(BodypartType.Hip);
            Vector3 offset = seat - hip.transform.position;
            Vector3 vel = fwd * b.Speed + Vector3.up * b.VertVel + offset / dt * 0.5f;
            foreach (var part in c.refs.ragdoll.partList)
            {
                var rig = part.Rig;
                if (rig != null) rig.linearVelocity = vel;
            }
            c.data.sinceGrounded = 0f;
            c.refs.movement.CapFallDamage(0f, 0.5f); // no fall damage while in a boat
            BlockEffects.GuardFall(0.5f);
            if (Mathf.Abs(b.Speed) > 0.5f && Time.time % 0.9f < dt) Sfx.At(onWater ? "entity/boat/paddle_water" : "entity/boat/paddle_land", b.Pos, 0.35f);
        }
    }

    /// <summary>While in a boat, the movement keys steer the boat instead of walking; jump/crouch gets you out.</summary>
    [HarmonyPatch(typeof(CharacterInput), "Sample")]
    internal static class CharacterInput_Sample_BoatPatch
    {
        private static void Postfix(CharacterInput __instance)
        {
            if (Boats.Riding == null || Character.localCharacter == null || Character.localCharacter.input != __instance) return;
            Boats.SteerInput = __instance.movementInput;
            if (__instance.jumpWasPressed || __instance.crouchWasPressed) Boats.ExitPressed = true;
            __instance.movementInput = Vector2.zero;
            __instance.jumpWasPressed = false;
            __instance.jumpIsPressed = false;
            __instance.sprintIsPressed = false;
        }
    }

    /// <summary>The boat model (entity/boat/oak.png) and its interaction (board / crouch to pick up).</summary>
    public class BoatView : MonoBehaviour, IInteractibleConstant
    {
        public Boats.Boat Boat;

        public static BoatView Create(Boats.Boat b)
        {
            var go = new GameObject("mc_boat_" + b.Id);
            int layer = LayerMask.NameToLayer("Default");
            go.layer = layer < 0 ? 0 : layer;
            var v = go.AddComponent<BoatView>();
            v.Boat = b;
            var tex = McAssets.Tex("entity/boat/oak.png");
            var mat = Mat.For(tex);
            var model = new GameObject("model").transform;
            model.SetParent(go.transform, false);
            float sc = BlockWorld.Size * 0.85f;
            model.localScale = Vector3.one * sc;
            model.localPosition = new Vector3(0, 6f / 16f * sc, 0);   // Minecraft draws the boat 6px up
            model.localRotation = Quaternion.Euler(0, 90f, 0);         // Minecraft boats point along x
            const float W = 128, H = 64;
            const float PI = Mathf.PI;
            // Minecraft 26.x BoatModel: parts with their pivots and rotations (radians, applied Z, Y, X).
            McPart(model, "bottom", 0, 3, 1, PI / 2, 0, 0, mat, b => b.McBox(-14, -9, -3, 28, 16, 3, 0, 0, W, H));
            McPart(model, "back", -15, 4, 4, 0, 3 * PI / 2, 0, mat, b => b.McBox(-13, -7, -1, 18, 6, 2, 0, 19, W, H));
            McPart(model, "front", 15, 4, 0, 0, PI / 2, 0, mat, b => b.McBox(-8, -7, -1, 16, 6, 2, 0, 27, W, H));
            McPart(model, "right", 0, 4, -9, 0, PI, 0, mat, b => b.McBox(-14, -7, -1, 28, 6, 2, 0, 35, W, H));
            McPart(model, "left", 0, 4, 9, 0, 0, 0, mat, b => b.McBox(-14, -7, -1, 28, 6, 2, 0, 43, W, H));
            v.paddleL = McPart(model, "paddleL", 3, -5, 9, 0, 0, PI / 16, mat, b =>
            {
                b.McBox(-1, 0, -5, 2, 2, 18, 62, 0, W, H);
                b.McBox(-1.001f, -3, 8, 1, 6, 7, 62, 0, W, H);
            });
            v.paddleR = McPart(model, "paddleR", 3, -5, -9, 0, PI, PI / 16, mat, b =>
            {
                b.McBox(-1, 0, -5, 2, 2, 18, 62, 20, W, H);
                b.McBox(0.001f, -3, 8, 1, 6, 7, 62, 20, W, H);
            });
            v.SetPaddles(0f);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0, 0.3f, 0);
            box.size = new Vector3(1.1f, 0.7f, 1.8f);
            v.Apply();
            return v;
        }

        private Transform paddleL, paddleR;
        private float rowTime;
        private Vector3 lastPos;

        /// <summary>A Minecraft model part: pivot in model pixels (y down), rotation in radians applied Z, Y, X.</summary>
        private static Transform McPart(Transform parent, string name, float px, float py, float pz, float rx, float ry, float rz, Material mat, System.Action<MeshBuilder> boxes)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(-px / 16f, -py / 16f, -pz / 16f);
            go.transform.localRotation = McRot(rx, ry, rz);
            var mb = new MeshBuilder { Cut = mat.mainTexture as Texture2D };
            boxes(mb);
            go.AddComponent<MeshFilter>().sharedMesh = mb.Build("mc_boat_" + name);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        // Minecraft's model space maps to ours by a point flip (x, y, z all negated), which keeps rotation matrices
        // the same, so the angles can be used as they are.
        private static Quaternion McRot(float rx, float ry, float rz) =>
            Quaternion.AngleAxis(rz * Mathf.Rad2Deg, Vector3.forward) * Quaternion.AngleAxis(ry * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(rx * Mathf.Rad2Deg, Vector3.right);

        /// <summary>Minecraft's paddle swing (BoatModel.animatePaddle).</summary>
        public void SetPaddles(float t)
        {
            Paddle(paddleL, t, false);
            Paddle(paddleR, t, true);
        }

        private static void Paddle(Transform p, float t, bool right)
        {
            if (p == null) return;
            float x = Mathf.Lerp(-PI3, -PI12, (Mathf.Sin(-t) + 1f) / 2f);
            float y = Mathf.Lerp(-PI4, PI4, (Mathf.Sin(-t + 1f) + 1f) / 2f);
            if (right) y = Mathf.PI - y;
            p.localRotation = McRot(x, y, Mathf.PI / 16f);
        }

        private const float PI3 = Mathf.PI / 3f, PI4 = Mathf.PI / 4f, PI12 = Mathf.PI / 12f;

        private void Update()
        {
            // Row while someone drives it.
            Vector3 d = transform.position - lastPos;
            lastPos = transform.position;
            float speed = new Vector2(d.x, d.z).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            if (Boat != null && Boat.Rider >= 0 && speed > 0.3f) { rowTime += Time.deltaTime * 8f; SetPaddles(rowTime); }
        }

        public void Apply()
        {
            transform.position = Boat.Pos;
            transform.rotation = Quaternion.Euler(0, Boat.Yaw, 0);
        }

        private static bool Crouching(Character c) => c != null && c.data.isCrouching;

        public bool holdOnFinish => false;
        public bool IsInteractible(Character interactor) => Boats.Riding == null && Boat.Rider < 0;
        public void Interact(Character interactor) { }
        public void HoverEnter() { }
        public void HoverExit() { }
        public Vector3 Center() => transform.position + Vector3.up * 0.3f;
        public Transform GetTransform() => transform;
        public string GetInteractionText() => Crouching(Character.localCharacter) ? "pick up" : "get in";
        public string GetName() => "Oak Boat";
        public bool IsConstantlyInteractable(Character interactor) => IsInteractible(interactor);
        public float GetInteractTime(Character interactor) => Crouching(interactor) ? 0.8f : 0.3f;

        public void Interact_CastFinished(Character interactor)
        {
            if (Crouching(interactor)) Boats.RequestPickup(Boat);
            else Boats.RequestMount(Boat);
        }

        public void CancelCast(Character interactor) { }
        public void ReleaseInteract(Character interactor) { }
    }
}
