using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Hotbar;
using BlockPeak.Items;
using BlockPeak.Net;
using BlockPeak.UI;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Building
{
    /// <summary>
    /// Placed blocks, torches, ladders and TNT. The host owns the list: players ask the host to place/break,
    /// the host checks the limit and tells everyone. Late joiners get a snapshot.
    /// Blocks sit on a world grid and use PEAK's "Map" layer, so scouts can stand on them and climb them.
    /// </summary>
    public static class BlockWorld
    {
        public class Record
        {
            public int Id;
            public Vector3Int Cell;
            public string Key;
            public byte Facing; // 0 = standing / full block, 1..4 = attached to a wall facing +x,-x,+z,-z
            public bool Lit;
            public Vector3 Surface; // ladders/wall torches: the exact wall point they hang on
            public PlacedBlock View;
        }

        public enum PlaceState { None, Ok, Blocked }

        public struct Placement
        {
            public PlaceState State;
            public Vector3Int Cell;
            public byte Facing;
            public Vector3 Surface;
            public Vector3 Normal;
            public string Why;
        }

        private static readonly Dictionary<int, Record> byId = new Dictionary<int, Record>();
        private static readonly Dictionary<Vector3Int, Record> byCell = new Dictionary<Vector3Int, Record>();
        private static int nextId = 1;
        private static GameObject root;
        private static float lastQuickPlace;
        private static readonly HashSet<int> knownActors = new HashSet<int>();

        public static float Size => Mathf.Clamp(Balance.F(B, "blockSize", 1f), 0.25f, 2f);
        private static Newtonsoft.Json.Linq.JToken B => Balance.Section("building");
        public static int Count => byId.Count;
        public static IEnumerable<Record> All => byId.Values;

        public static void RegisterNet()
        {
            Channel.On(Op.BlockPlaceReq, HostPlace);
            Channel.On(Op.BlockPlaced, (a, s) => Spawn(Channel.Int(a[0]), V3I(Channel.Vec(a[1])), Channel.Str(a[2]), Channel.Byte(a[3]), false, true, a.Length > 4 ? Channel.Vec(a[4]) : Vector3.zero));
            Channel.On(Op.BlockBreakReq, HostBreak);
            Channel.On(Op.BlockBroken, (a, s) => Remove(Channel.Int(a[0]), Channel.Bool(a[1])));
            Channel.On(Op.BlockSnapshot, OnSnapshot);
            Channel.On(Op.TntIgniteReq, (a, s) => { if (IsAuthority) Ignite(Channel.Int(a[0]), Balance.F(Balance.ItemCfg("tnt"), "fuseSeconds", 4f)); });
            Channel.On(Op.TntLit, (a, s) =>
            {
                if (byId.TryGetValue(Channel.Int(a[0]), out var r) && r.View != null) { r.Lit = true; r.View.StartFuse(Channel.Flt(a[1])); }
            });
            Channel.On(Op.Explosion, (a, s) => Explosions.OnExplosion(Channel.Vec(a[0]), Channel.Flt(a[1]), Channel.Flt(a[2])));
        }

        private static bool IsAuthority => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;

        public static Vector3Int V3I(Vector3 v) => new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));
        public static Vector3Int CellAt(Vector3 world) => new Vector3Int(Mathf.FloorToInt(world.x / Size), Mathf.FloorToInt(world.y / Size), Mathf.FloorToInt(world.z / Size));
        public static Vector3 CellCenter(Vector3Int c) => (new Vector3(c.x, c.y, c.z) + Vector3.one * 0.5f) * Size;
        public static Vector3 CellBottom(Vector3Int c) => (new Vector3(c.x + 0.5f, c.y, c.z + 0.5f)) * Size;

        public static void Clear()
        {
            foreach (var r in byId.Values) if (r.View != null) UnityEngine.Object.Destroy(r.View.gameObject);
            byId.Clear();
            byCell.Clear();
            knownActors.Clear();
        }

        // ------------------------------------------------------------ local player: placing

        /// <summary>Where the held block would go if placed now (also drives the outline preview).</summary>
        public static Placement ComputePlacement(McItemDef def)
        {
            var p = new Placement { State = PlaceState.None };
            if (def == null || (!Game.InRun && !Game.InAirport) || Game.LocalChar == null) return p;
            float reach = Balance.F(B, "reach", 4.5f);
            var cam = Game.CamPos;
            var dir = Game.CamForward;
            if (!Physics.Raycast(cam, dir, out var hit, reach + 1.5f, Game.TerrainMask, QueryTriggerInteraction.Ignore)) return p;
            if (Vector3.Distance(Game.LocalChar.Center, hit.point) > reach + 1f) return p;

            var onBlock = hit.collider.GetComponentInParent<PlacedBlock>();
            Vector3 n = hit.normal;
            bool wall = Mathf.Abs(n.y) < 0.5f;
            p.Normal = n;
            p.Surface = hit.point;
            switch (def.Kind)
            {
                case McKind.Ladder:
                    if (!wall) return p;
                    p.Facing = FacingFor(n);
                    p.Cell = CellAt(hit.point + n * Size * 0.25f);
                    break;
                case McKind.Torch:
                    if (n.y < -0.5f) return p;
                    p.Facing = wall ? FacingFor(n) : (byte)0;
                    p.Cell = CellAt(hit.point + n * Size * 0.25f);
                    break;
                default:
                    if (onBlock != null && onBlock.Record != null && onBlock.IsFullBlock)
                        p.Cell = onBlock.Record.Cell + AxisOf(n);
                    else
                        p.Cell = CellAt(hit.point + n * Size * 0.5f);
                    break;
            }
            p.State = PlaceState.Ok;
            int limit = Balance.I(B, "placedBlockLimit", 1000);
            if (byCell.ContainsKey(p.Cell)) { p.State = PlaceState.Blocked; }
            else if (BlocksCharacter(p.Cell, def)) { p.State = PlaceState.Blocked; }
            else if (Count >= limit) { p.State = PlaceState.Blocked; p.Why = $"Block limit reached ({limit}). Break some blocks first."; }
            return p;
        }

        /// <summary>Place the held block where the camera looks. True if a request went out (the item gets used up).</summary>
        public static bool TryPlaceFromView(McItemDef def)
        {
            var p = ComputePlacement(def);
            if (p.State != PlaceState.Ok)
            {
                if (p.Why != null) Banner.Toast(p.Why);
                return false;
            }
            Channel.Host(Op.BlockPlaceReq, (Vector3)p.Cell, def.Key, p.Facing, -1, p.Surface);
            Sfx.At(PlaceSound(def), CellCenter(p.Cell), 0.8f);
            return true;
        }

        private static Vector3Int AxisOf(Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az) return new Vector3Int(n.x > 0 ? 1 : -1, 0, 0);
            if (ay >= az) return new Vector3Int(0, n.y > 0 ? 1 : -1, 0);
            return new Vector3Int(0, 0, n.z > 0 ? 1 : -1);
        }

        /// <summary>Which way a wall-mounted thing faces (direction pointing away from the wall).</summary>
        private static byte FacingFor(Vector3 n)
        {
            var a = AxisOf(new Vector3(n.x, 0, n.z));
            if (a.x > 0) return 1;
            if (a.x < 0) return 2;
            if (a.z > 0) return 3;
            return 4;
        }

        public static Vector3 FacingDir(byte f)
        {
            switch (f)
            {
                case 1: return Vector3.right;
                case 2: return Vector3.left;
                case 3: return Vector3.forward;
                case 4: return Vector3.back;
                default: return Vector3.up;
            }
        }

        private static bool BlocksCharacter(Vector3Int cell, McItemDef def)
        {
            if (def.Kind != McKind.Block && def.Kind != McKind.Tnt) return false;
            var b = new Bounds(CellCenter(cell), Vector3.one * Size * 1.05f);
            foreach (var c in Character.AllCharacters)
            {
                if (c == null || c.data.dead) continue;
                foreach (var p in c.refs.ragdoll.partList)
                    if (p != null && b.Contains(p.transform.position)) return true;
            }
            return false;
        }

        public static string PlaceSound(McItemDef def)
        {
            switch (def.Key)
            {
                case "sand": case "gravel": case "red_sandstone": return "dig/sand";
                case "oak_planks": case "spruce_planks": case "cherry_planks": case "oak_log": case "birch_log":
                case "bookshelf": case "crafting_table": case "torch": return "dig/wood";
                case "moss_block": case "tnt": case "grass_block": case "dirt": case "oak_leaves": case "hay_block":
                case "pumpkin": case "melon": case "shroomlight": return "dig/grass";
                case "snow_block": return "dig/snow";
                case "white_wool": return "dig/cloth";
                case "slime_block": return "mob/slime/small";
                case "glass": case "glowstone": case "sea_lantern": return "random/glass";
                default: return "dig/stone";
            }
        }

        /// <summary>While climbing: put a block from your hotbar into the wall below your feet (a foothold).</summary>
        public static void TickQuickPlace()
        {
            var c = Game.LocalChar;
            if (c == null || !c.data.isClimbing || !KeyInput.Down(Cfg.QuickPlaceKey.Value)) return;
            if (Time.time - lastQuickPlace < Balance.F(B, "quickPlaceCooldown", 1f)) return;
            ItemSlot slot = null;
            foreach (var s in c.player.itemSlots)
            {
                var d = s.IsEmpty() ? null : ItemDefs.ById(s.prefab.itemID);
                if (d != null && d.Kind == McKind.Block) { slot = s; break; }
            }
            if (slot == null) { Banner.Toast("No blocks in your hotbar."); return; }
            Vector3 wallN = c.data.climbNormal.sqrMagnitude > 0.1f ? c.data.climbNormal.normalized : -c.data.lookDirection;
            Vector3 feet = c.GetBodypart(BodypartType.Foot_R) != null ? c.GetBodypart(BodypartType.Foot_R).transform.position : c.Center - Vector3.up;
            Vector3Int cell = CellAt(feet + wallN * Size * 0.5f - Vector3.up * Size * 0.6f);
            if (byCell.ContainsKey(cell)) cell += Vector3Int.down;
            if (byCell.ContainsKey(cell)) return;
            if (Count >= Balance.I(B, "placedBlockLimit", 1000)) { Banner.Toast("Block limit reached."); return; }
            lastQuickPlace = Time.time;
            c.UseStamina(Balance.F(B, "quickPlaceStamina", 0.1f));
            var def = ItemDefs.ById(slot.prefab.itemID);
            Channel.Host(Op.BlockPlaceReq, (Vector3)cell, def.Key, (byte)0, (int)slot.itemSlotID, Vector3.zero);
            Sfx.At(PlaceSound(def), CellCenter(cell), 0.8f);
        }

        // ------------------------------------------------------------ host

        private static void HostPlace(object[] a, int sender)
        {
            if (!IsAuthority) return;
            var cell = V3I(Channel.Vec(a[0]));
            string key = Channel.Str(a[1]);
            byte facing = Channel.Byte(a[2]);
            int fromSlot = a.Length > 3 ? Channel.Int(a[3]) : -1;
            Vector3 surface = a.Length > 4 ? Channel.Vec(a[4]) : Vector3.zero;
            var def = ItemDefs.ByKey(key);
            if (def == null || byCell.ContainsKey(cell) || Count >= Balance.I(B, "placedBlockLimit", 1000)) return;

            if (fromSlot >= 0 && !TakeFromSlot(sender, (byte)fromSlot, def)) return;

            int id = nextId++;
            Channel.All(Op.BlockPlaced, true, id, (Vector3)cell, key, facing, surface);
            Mobs.BodyMobs.HostVibration(CellCenter(cell), 1f);
        }

        /// <summary>Host: take one item from a player's hotbar slot (quick-place) and sync their inventory.</summary>
        private static bool TakeFromSlot(int actor, byte slotId, McItemDef def)
        {
            var p = PlayerHandler.GetPlayer(actor);
            var s = p?.GetItemSlot(slotId);
            if (s == null || s.IsEmpty() || s.prefab.itemID != def.Id) return false;
            int n = Stacks.Count(s.data);
            if (n <= 1) p.EmptySlot(Zorro.Core.Optionable<byte>.Some(slotId));
            else
            {
                Stacks.SetCount(s.data, n - 1, def.Stack);
                var arr = Zorro.Core.Serizalization.IBinarySerializable.ToManagedArray(new InventorySyncData(p.itemSlots, p.backpackSlot, p.tempFullSlot));
                p.view.RPC("SyncInventoryRPC", RpcTarget.Others, arr, false);
                // The owner may be holding that stack: update the held item's copy too.
                var held = p.character != null ? p.character.data.currentItem : null;
                if (held != null && held.data != null && held.data.guid == s.data.guid) Stacks.SetCount(held.data, n - 1, def.Stack);
            }
            return true;
        }

        private static IEnumerable<Vector3Int> Neighbours(Vector3Int c)
        {
            yield return c + Vector3Int.up; yield return c + Vector3Int.down;
            yield return c + Vector3Int.left; yield return c + Vector3Int.right;
            yield return c + new Vector3Int(0, 0, 1); yield return c + new Vector3Int(0, 0, -1);
        }

        private static void HostBreak(object[] a, int sender)
        {
            if (!IsAuthority) return;
            int id = Channel.Int(a[0]);
            if (!byId.TryGetValue(id, out var r) || r.Lit) return;
            Channel.All(Op.BlockBroken, true, id, true);
            Mobs.BodyMobs.HostVibration(CellCenter(r.Cell), 1f);
            DropItem(r);
        }

        private static void DropItem(Record r)
        {
            var def = ItemDefs.ByKey(r.Key);
            var t = def != null ? Loot.TemplateFor(def) : null;
            if (t == null || !PhotonNetwork.InRoom) return;
            try
            {
                var go = PhotonNetwork.InstantiateItemRoom(t.name, CellCenter(r.Cell), Quaternion.identity);
                var item = go.GetComponent<Item>();
                item.GetData<BoolItemData>(Stacks.RolledKey);
                if (def.Stack > 1) Stacks.SetCount(item.data, 1, def.Stack);
                Stacks.MarkRolled(item.data);
                item.photonView.RPC("SetKinematicRPC", RpcTarget.AllBuffered, false, go.transform.position, go.transform.rotation);
            }
            catch (Exception e) { Health.Report("block-drop", e); }
        }

        public static void Ignite(int id, float fuse)
        {
            if (!IsAuthority || !byId.TryGetValue(id, out var r) || r.Key != "tnt" || r.Lit) return;
            r.Lit = true;
            Channel.All(Op.TntLit, true, id, fuse);
            Runner.Instance.StartCoroutine(Fuse(id, fuse));
        }

        private static System.Collections.IEnumerator Fuse(int id, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (!byId.TryGetValue(id, out var r)) yield break;
            float radius = Balance.F(Balance.ItemCfg("tnt"), "radius", 3f);
            Vector3 at = CellCenter(r.Cell);
            Channel.All(Op.BlockBroken, true, id, false);
            Channel.All(Op.Explosion, true, at, radius, Balance.F(Balance.ItemCfg("tnt"), "injury", 0.4f));
            HostExplosionAftermath(at, radius);
        }

        /// <summary>Host: blocks in the blast go away, nearby TNT chains, mobs get hurt.</summary>
        public static void HostExplosionAftermath(Vector3 at, float radius)
        {
            foreach (var r in byId.Values.ToList())
            {
                float d = Vector3.Distance(CellCenter(r.Cell), at);
                if (d > radius) continue;
                if (r.Key == "tnt") { Ignite(r.Id, UnityEngine.Random.Range(0.5f, 1.5f)); continue; }
                Channel.All(Op.BlockBroken, true, r.Id, false);
            }
            Mobs.MobDirector.HostExplosion(at, radius);
        }

        /// <summary>Host: tell a newly joined player about every block.</summary>
        public static void TickHost()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) { knownActors.Clear(); return; }
            foreach (var p in PhotonNetwork.PlayerListOthers)
            {
                if (knownActors.Contains(p.ActorNumber)) continue;
                knownActors.Add(p.ActorNumber);
                if (byId.Count == 0) continue;
                var list = byId.Values.ToList();
                Channel.To(p.ActorNumber, Op.BlockSnapshot, true,
                    list.Select(r => r.Id).ToArray(),
                    list.SelectMany(r => new float[] { r.Cell.x, r.Cell.y, r.Cell.z }).ToArray(),
                    list.Select(r => r.Key).ToArray(),
                    list.Select(r => r.Facing).ToArray(),
                    list.Select(r => r.Lit).ToArray(),
                    list.SelectMany(r => new float[] { r.Surface.x, r.Surface.y, r.Surface.z }).ToArray());
            }
        }

        private static void OnSnapshot(object[] a, int sender)
        {
            var ids = (int[])a[0];
            var cells = (float[])a[1];
            var keys = (string[])a[2];
            var facings = (byte[])a[3];
            var lit = (bool[])a[4];
            var surf = a.Length > 5 ? (float[])a[5] : null;
            for (int i = 0; i < ids.Length; i++)
                if (!byId.ContainsKey(ids[i])) Spawn(ids[i], new Vector3Int((int)cells[i * 3], (int)cells[i * 3 + 1], (int)cells[i * 3 + 2]), keys[i], facings[i], lit[i], false,
                    surf != null ? new Vector3(surf[i * 3], surf[i * 3 + 1], surf[i * 3 + 2]) : Vector3.zero);
        }

        // ------------------------------------------------------------ everyone

        private static void Spawn(int id, Vector3Int cell, string key, byte facing, bool lit, bool effects, Vector3 surface)
        {
            var def = ItemDefs.ByKey(key);
            if (def == null || byId.ContainsKey(id)) return;
            if (byCell.TryGetValue(cell, out var old)) Remove(old.Id, false);
            if (root == null) { root = new GameObject("BlockPeak.Blocks"); }
            nextId = Math.Max(nextId, id + 1);
            var r = new Record { Id = id, Cell = cell, Key = key, Facing = facing, Lit = lit, Surface = surface };
            byId[id] = r;
            byCell[cell] = r;
            r.View = PlacedBlock.Create(r, def, root.transform);
            if (lit) r.View.StartFuse(1.5f);
        }

        private static void Remove(int id, bool effects)
        {
            if (!byId.TryGetValue(id, out var r)) return;
            byId.Remove(id);
            byCell.Remove(r.Cell);
            if (r.View != null)
            {
                if (effects)
                {
                    var def = ItemDefs.ByKey(r.Key);
                    Sfx.At(def != null ? PlaceSound(def) : "dig/stone", CellCenter(r.Cell), 0.9f);
                    Fx.BlockBreak(CellCenter(r.Cell), r.View.MainTexture, Size);
                }
                UnityEngine.Object.Destroy(r.View.gameObject);
            }
        }

        /// <summary>Is there a torch within this distance? Used for mob spawning and warmth.</summary>
        public static bool LightNear(Vector3 pos, float radius)
        {
            float r2 = radius * radius;
            foreach (var r in byId.Values)
                if (r.Key == "torch" && (CellCenter(r.Cell) - pos).sqrMagnitude < r2) return true;
            return false;
        }

        /// <summary>Local player: standing next to a torch slowly warms you up.</summary>
        public static void TickWarmth()
        {
            var c = Game.LocalChar;
            if (c == null || byId.Count == 0) return;
            float radius = Balance.F(B, "torchWarmRadius", 2f);
            if (!LightNearKey(c.Center, radius, "torch")) return;
            float rate = Balance.F(B, "torchColdRemovedPerSecond", 0.005f);
            if (c.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Cold) > 0f)
                c.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.Cold, rate * Time.deltaTime);
        }

        private static bool LightNearKey(Vector3 pos, float radius, string key)
        {
            float r2 = radius * radius;
            foreach (var r in byId.Values)
                if (r.Key == key && (CellCenter(r.Cell) - pos).sqrMagnitude < r2) return true;
            return false;
        }

        /// <summary>Host (test mode): remove every placed block.</summary>
        public static void HostClearAll()
        {
            if (!IsAuthority) return;
            foreach (var r in byId.Values.ToList()) Channel.All(Op.BlockBroken, true, r.Id, false);
        }

        public static Record At(Vector3Int cell) => byCell.TryGetValue(cell, out var r) ? r : null;

        public static void RequestBreak(int id) => Channel.Host(Op.BlockBreakReq, id);
        public static void RequestIgnite(int id) => Channel.Host(Op.TntIgniteReq, id);
    }
}
