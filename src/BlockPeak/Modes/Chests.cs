using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Building;
using BlockPeak.Core;
using BlockPeak.Items;
using BlockPeak.Net;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Modes
{
    public enum ChestKind : byte { Single = 0, Double = 1, Copper = 2 }

    /// <summary>
    /// "Minecraft chests" custom-run option: Minecraft items are only found in Minecraft chests placed next to
    /// PEAK's luggage all over the mountain. Normal and large chests hold everyday Minecraft items; copper chests
    /// hold the powerful ones (elytra, totem, enchanted golden apple, ender pearls). The host places, opens and
    /// fills them; everyone sees the same chests.
    /// </summary>
    public static class Chests
    {
        public class Record
        {
            public int Id;
            public Vector3 Pos;
            public float Yaw;
            public ChestKind Kind;
            public bool Opened;
            public ChestView View;
        }

        private static readonly Dictionary<int, Record> byId = new Dictionary<int, Record>();
        private static readonly HashSet<int> knownActors = new HashSet<int>();
        private static GameObject root;
        private static int nextId = 1;
        private static bool placed;
        private static float placeAt = -1f, nextCheck;
        private static readonly HashSet<int> seenLuggage = new HashSet<int>();

        private static JToken C => Balance.Section("modes")["chests"] ?? new JObject();
        private static bool IsHost => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;

        public static void RegisterNet()
        {
            Channel.On(Op.ChestSpawn, (a, s) =>
            {
                placed = true; // if this player becomes host later, it must not place a second set
                var ids = (int[])a[0];
                var pos = (float[])a[1];
                var kinds = (byte[])a[2];
                var opened = (bool[])a[3];
                for (int i = 0; i < ids.Length; i++)
                    Add(new Record { Id = ids[i], Pos = new Vector3(pos[i * 4], pos[i * 4 + 1], pos[i * 4 + 2]), Yaw = pos[i * 4 + 3], Kind = (ChestKind)kinds[i], Opened = opened[i] });
            });
            Channel.On(Op.ChestOpenReq, (a, s) => HostOpen(Channel.Int(a[0])));
            Channel.On(Op.ChestOpened, (a, s) =>
            {
                if (byId.TryGetValue(Channel.Int(a[0]), out var r)) { r.Opened = true; if (r.View != null) r.View.Open(true); }
            });
        }

        public static void Clear()
        {
            foreach (var r in byId.Values) if (r.View != null) UnityEngine.Object.Destroy(r.View.gameObject);
            byId.Clear();
            knownActors.Clear();
            placed = false;
            placeAt = -1f;
            seenLuggage.Clear();
        }

        // ------------------------------------------------------------------ host: placing

        public static void TickHost()
        {
            if (!IsHost || !Game.InRun) return;
            string freq = CustomOptions.ChestFrequency;
            if (freq == null) return;
            if (placeAt < 0f) placeAt = Time.time + 6f;
            if (Time.time < placeAt || Time.time < nextCheck) return;
            nextCheck = Time.time + 1f;
            // PEAK switches on each part of the mountain only as the scouts get near it; luggage there has no ground
            // under it until then, so keep looking for newly switched-on luggage.
            if (!placed || seenLuggage.Count < Luggage.ALL_LUGGAGE.Count)
                try { HostPlaceNew(freq); } catch (Exception e) { Health.Report("chests-place", e); }
            placed = true;
            // Late joiners get every chest.
            if (PhotonNetwork.InRoom)
                foreach (var p in PhotonNetwork.PlayerListOthers)
                {
                    if (knownActors.Contains(p.ActorNumber)) continue;
                    knownActors.Add(p.ActorNumber);
                    if (byId.Count > 0) Send(byId.Values.ToList(), p.ActorNumber);
                }
        }

        private static void HostPlaceNew(string freq)
        {
            float chance = Balance.F(C["frequency"], freq, freq == "common" ? 0.3f : freq == "rare" ? 0.08f : 0.16f);
            float startRadius = Balance.F(Balance.Section("modes")["minecraftItemsOnly"], "startAreaRadius", 40f);
            var made = new List<Record>();
            foreach (var l in Luggage.ALL_LUGGAGE)
            {
                if (l == null || !l.gameObject.activeInHierarchy) continue;
                if (!seenLuggage.Add(l.GetInstanceID())) continue;
                if (l is RespawnChest || UnityEngine.Random.value >= chance) continue;
                Vector3 lp = l.transform.position;
                if (SpawnPoint.allSpawnPoints != null && SpawnPoint.allSpawnPoints.Any(s => s != null && Vector3.Distance(s.transform.position, lp) < startRadius)) continue;
                var kind = RollKind();
                if (!FindSpot(lp, kind, out var at, out var yaw)) continue;
                var r = new Record { Id = nextId++, Pos = at, Yaw = yaw, Kind = kind };
                made.Add(r);
            }
            if (made.Count == 0) return;
            foreach (var r in made) Add(r);
            if (PhotonNetwork.InRoom) Send(made, -1);
            Health.Verbose($"Placed {made.Count} more Minecraft chests ({freq}); {byId.Count} in total.");
        }

        private static ChestKind RollKind()
        {
            var w = C["kinds"];
            float s = Balance.F(w, "single", 60f), d = Balance.F(w, "double", 30f), c = Balance.F(w, "copper", 10f);
            float r = UnityEngine.Random.value * (s + d + c);
            return r < s ? ChestKind.Single : r < s + d ? ChestKind.Double : ChestKind.Copper;
        }

        /// <summary>Flat ground 1.5-4 m from the luggage with room for the chest.</summary>
        private static bool FindSpot(Vector3 near, ChestKind kind, out Vector3 at, out float yaw)
        {
            float S = BlockWorld.Size;
            at = Vector3.zero; yaw = 0f;
            for (int i = 0; i < 10; i++)
            {
                Vector2 off = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(1.5f, 4f);
                Vector3 from = near + new Vector3(off.x, 3f, off.y);
                if (!Physics.Raycast(from, Vector3.down, out var hit, 9f, Game.TerrainMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.8f) continue;
                yaw = Mathf.Atan2(near.x - hit.point.x, near.z - hit.point.z) * Mathf.Rad2Deg + UnityEngine.Random.Range(-30f, 30f);
                Vector3 half = new Vector3(kind == ChestKind.Double ? S : S * 0.5f, S * 0.45f, S * 0.5f);
                Vector3 center = hit.point + Vector3.up * (S * 0.5f + 0.05f);
                if (Physics.CheckBox(center, half * 0.9f, Quaternion.Euler(0, yaw, 0), Game.TerrainMask, QueryTriggerInteraction.Ignore)) continue;
                at = hit.point;
                return true;
            }
            return false;
        }

        private static void Send(List<Record> list, int toActor)
        {
            var args = new object[]
            {
                list.Select(r => r.Id).ToArray(),
                list.SelectMany(r => new[] { r.Pos.x, r.Pos.y, r.Pos.z, r.Yaw }).ToArray(),
                list.Select(r => (byte)r.Kind).ToArray(),
                list.Select(r => r.Opened).ToArray(),
            };
            if (toActor < 0) Channel.Others(Op.ChestSpawn, true, args);
            else Channel.To(toActor, Op.ChestSpawn, true, args);
        }

        private static void Add(Record r)
        {
            if (byId.ContainsKey(r.Id)) return;
            if (root == null) { root = new GameObject("BlockPeak.Chests"); UnityEngine.Object.DontDestroyOnLoad(root); }
            byId[r.Id] = r;
            nextId = Mathf.Max(nextId, r.Id + 1);
            try
            {
                r.View = ChestView.Create(r, root.transform);
                if (r.Opened) r.View.Open(false);
            }
            catch (Exception e) { Health.Report("chest-view", e); }
        }

        // ------------------------------------------------------------------ opening

        public static void RequestOpen(Record r)
        {
            if (r == null || r.Opened) return;
            if (IsHost) HostOpen(r.Id); else Channel.Host(Op.ChestOpenReq, r.Id);
        }

        private static void HostOpen(int id)
        {
            if (!IsHost || !byId.TryGetValue(id, out var r) || r.Opened) return;
            r.Opened = true;
            Channel.All(Op.ChestOpened, true, id);
            BlockPeak.Mobs.BodyMobs.HostVibration(r.Pos, 1f);
            foreach (var def in RollLoot(r.Kind)) SpawnItem(def, r);
        }

        /// <summary>
        /// Rarity: every Minecraft item has a tier (common, uncommon, rare). Normal and large chests roll common and
        /// uncommon items; copper chests mostly roll the rare (powerful) ones.
        /// </summary>
        private static List<McItemDef> RollLoot(ChestKind kind)
        {
            string k = kind == ChestKind.Copper ? "copper" : kind == ChestKind.Double ? "double" : "single";
            var (min, max) = Balance.Range(C["items"], k, kind == ChestKind.Double ? 4 : kind == ChestKind.Copper ? 1 : 2, kind == ChestKind.Double ? 7 : 3);
            var tierWeights = C["tierWeights"]?[k] as JObject;
            var tiers = C["tiers"] as JObject;
            var result = new List<McItemDef>();
            if (tierWeights == null || tiers == null) return result;
            int n = UnityEngine.Random.Range(min, max + 1);
            for (int i = 0; i < n; i++)
            {
                float total = tierWeights.Properties().Sum(p => (float)p.Value), roll = UnityEngine.Random.value * total;
                string tier = tierWeights.Properties().First().Name;
                foreach (var p in tierWeights.Properties()) { roll -= (float)p.Value; if (roll <= 0f) { tier = p.Name; break; } }
                var keys = (tiers[tier] as JArray)?.Select(t => (string)t).ToList();
                if (keys == null || keys.Count == 0) continue;
                string key = keys[UnityEngine.Random.Range(0, keys.Count)];
                var def = key == "blocks" ? Loot.BlockFor(Loot.PoolName(SpawnPool.LuggageBeach)) : ItemDefs.ByKey(key);
                if (key == "blocks") def = ItemDefs.Blocks.ElementAt(UnityEngine.Random.Range(0, ItemDefs.Blocks.Count()));
                if (def != null && ItemRegistry.Templates.ContainsKey(def.Id)) result.Add(def);
            }
            return result;
        }

        private static void SpawnItem(McItemDef def, Record r)
        {
            var t = Loot.TemplateFor(def);
            if (t == null || !PhotonNetwork.InRoom) return;
            try
            {
                Vector3 at = r.Pos + Vector3.up * (BlockWorld.Size * 1.1f) + UnityEngine.Random.insideUnitSphere * 0.25f;
                var go = PhotonNetwork.InstantiateItemRoom(t.name, at, Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0));
                var item = go.GetComponent<Item>();
                item.photonView.RPC("SetKinematicRPC", RpcTarget.AllBuffered, false, go.transform.position, go.transform.rotation);
            }
            catch (Exception e) { Health.Report("chest-item", e); }
        }
    }

    /// <summary>Minecraft's chest model (entity/chest textures, 64x64): single, large (two halves) or copper.</summary>
    public class ChestView : MonoBehaviour, IInteractibleConstant
    {
        public Chests.Record Rec;
        private readonly List<Transform> lids = new List<Transform>();
        private float openAmount, openTarget;

        public static ChestView Create(Chests.Record r, Transform parent)
        {
            float S = BlockWorld.Size;
            var go = new GameObject("mc_chest_" + r.Id);
            go.transform.SetParent(parent, false);
            go.transform.position = r.Pos;
            go.transform.rotation = Quaternion.Euler(0, r.Yaw, 0);
            go.layer = Game.MapLayer;
            var v = go.AddComponent<ChestView>();
            v.Rec = r;
            var model = new GameObject("model").transform;
            model.SetParent(go.transform, false);
            model.localScale = Vector3.one * S;
            if (r.Kind == ChestKind.Double)
            {
                v.Half(model, "entity/chest/normal_left.png", -16f, 1, 16, 15, 16);
                v.Half(model, "entity/chest/normal_right.png", 0f, 0, 15, 0, 1);
            }
            else v.Half(model, r.Kind == ChestKind.Copper ? "entity/chest/copper.png" : "entity/chest/normal.png", -8f, 1, 15, 7, 9);

            var box = go.AddComponent<BoxCollider>();
            float w = r.Kind == ChestKind.Double ? 30f / 16f : 14f / 16f;
            box.center = new Vector3(0, 7f / 16f * S, 0);
            box.size = new Vector3(w * S, 14f / 16f * S, 14f / 16f * S);
            foreach (Transform t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = Game.MapLayer;
            return v;
        }

        /// <summary>
        /// One chest half: base x0..x1 (Minecraft pixels, 0..16 per block), lid on top, latch from lx0 to lx1.
        /// Built y-up with the latch on the +z side; texture regions laid out the way Minecraft's chest textures are.
        /// </summary>
        private void Half(Transform model, string texPath, float shiftX, float x0, float x1, float lx0, float lx1)
        {
            var tex = McAssets.Tex(texPath);
            var mat = Mat.For(tex);
            float w = x1 - x0;
            // base
            var mb = new MeshBuilder();
            ChestBox(mb, new Vector3(x0 + shiftX, 0, 1 - 8), new Vector3(x1 + shiftX, 10, 15 - 8), 0, 19, w, 10, 14);
            Piece(model, "base", Vector3.zero, mb, mat);
            // lid (+ latch), hinged at the back top edge
            var lid = new GameObject("lid").transform;
            lid.SetParent(model, false);
            lid.localPosition = new Vector3(0, 9f / 16f, -7f / 16f);
            var lb = new MeshBuilder();
            ChestBox(lb, new Vector3(x0 + shiftX, 0, 0), new Vector3(x1 + shiftX, 5, 14), 0, 0, w, 5, 14);
            ChestBox(lb, new Vector3(lx0 + shiftX, -3, 14), new Vector3(lx1 + shiftX, 1, 15), 0, 0, lx1 - lx0, 4, 1);
            Piece(lid, "lid", Vector3.zero, lb, mat);
            lids.Add(lid);
        }

        private static void Piece(Transform parent, string name, Vector3 pos, MeshBuilder mb, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mb.Build("mc_chest_" + name);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>A box in pixels (y up, latch side +z) using Minecraft's cube texture layout at (u, v).</summary>
        private static void ChestBox(MeshBuilder mb, Vector3 minPx, Vector3 maxPx, int u, int v, float w, float h, float d)
        {
            const float T = 64f;
            Rect Flip(Rect r) => Rect.MinMaxRect(r.xMin, r.yMax, r.xMax, r.yMin);
            Rect sideA = Flip(MeshBuilder.PxRect(u, v + d, d, h, T, T));
            Rect north = Flip(MeshBuilder.PxRect(u + d, v + d, w, h, T, T));
            Rect sideB = Flip(MeshBuilder.PxRect(u + d + w, v + d, d, h, T, T));
            Rect south = Flip(MeshBuilder.PxRect(u + d + w + d, v + d, w, h, T, T));
            Rect under = MeshBuilder.PxRect(u + d, v, w, d, T, T);
            Rect over = MeshBuilder.PxRect(u + d + w, v, w, d, T, T);
            // Box faces: Right(+x), Left(-x), Top(+y), Bottom(-y), Front(+z), Back(-z)
            mb.Box(minPx / 16f, maxPx / 16f, new[] { sideB, sideA, over, under, south, north });
        }

        public void Open(bool sound)
        {
            openTarget = 1f;
            if (sound) { Sfx.At("random/chestopen", transform.position, 0.9f); return; }
            openAmount = 1f;
            foreach (var l in lids) if (l != null) l.localRotation = Quaternion.Euler(-85f, 0f, 0f);
        }

        private void Update()
        {
            if (Mathf.Approximately(openAmount, openTarget)) return;
            openAmount = Mathf.MoveTowards(openAmount, openTarget, Time.deltaTime * 2f);
            float eased = 1f - Mathf.Pow(1f - openAmount, 3f);
            foreach (var l in lids) if (l != null) l.localRotation = Quaternion.Euler(-eased * 85f, 0f, 0f);
        }

        // ---- PEAK interaction (hold E)
        public bool holdOnFinish => false;
        public bool IsInteractible(Character interactor) => Rec != null && !Rec.Opened;
        public void Interact(Character interactor) { }
        public void HoverEnter() { }
        public void HoverExit() { }
        public Vector3 Center() => transform.position + Vector3.up * 0.5f * BlockWorld.Size;
        public Transform GetTransform() => transform;
        public string GetInteractionText() => "open";
        public string GetName() => Rec == null ? "Chest" : Rec.Kind == ChestKind.Copper ? "Copper Chest" : Rec.Kind == ChestKind.Double ? "Large Chest" : "Chest";
        public bool IsConstantlyInteractable(Character interactor) => IsInteractible(interactor);
        public float GetInteractTime(Character interactor) => 0.5f;
        public void Interact_CastFinished(Character interactor) => Chests.RequestOpen(Rec);
        public void CancelCast(Character interactor) { }
        public void ReleaseInteract(Character interactor) { }
    }
}
