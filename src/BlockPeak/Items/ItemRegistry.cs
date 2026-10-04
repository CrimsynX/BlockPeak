using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Core;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Items
{
    /// <summary>
    /// Creates the Minecraft items at runtime by cloning a plain PEAK item (so the scout's hands, physics,
    /// pickup, backpack and networking all work exactly like PEAK's own items), swapping its looks and behaviour,
    /// and registering it in PEAK's item database and Photon's prefab list under a fixed id.
    /// </summary>
    public static class ItemRegistry
    {
        public static bool Ready { get; private set; }
        public static string GenericBaseName { get; private set; } = "";
        public static string FoodBaseName { get; private set; } = "";
        public static readonly Dictionary<ushort, Item> Templates = new Dictionary<ushort, Item>();
        public static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();

        private static GameObject holder;
        private static float nextCheck;
        private static bool texturesWereReal;

        private static readonly string[] RemoveByName =
        {
            "LootData", "Breakable", "EventOnItemCollision", "KnockOutPlayerOnImpact", "Bonkable", "Constructable",
            "MagicBean", "Mandrake", "Beehive", "Balloon", "Snowball", "StickyItemComponent", "RescueHook", "Lantern",
            "Flare", "Dynamite", "Candle", "JetpackItem", "Rocketpack", "RopeSpool", "RopeShooter", "VineShooter",
            "ClimbingSpikeComponent", "Glider", "Parasol", "MagicBugle", "BugleSFX", "ItemTorch", "Frisbee", "Basketball",
            "TumbleWeed", "ShelfShroom", "Antigrav", "BingBongShieldWhileHolding", "LanternLight", "Mirage", "FakeItem",
        };

        /// <summary>Call every frame; does real work only when needed.</summary>
        public static void Tick()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + (Ready ? 3f : 1f);
            try
            {
                if (!Ready) Register();
                else EnsureStillRegistered();
                // Real textures arrived after we built placeholder visuals: rebuild once.
                if (Ready && McAssets.HasRealTextures && !texturesWereReal && McAssets.Status != McAssets.State.Extracting)
                {
                    texturesWereReal = true;
                    RefreshVisuals();
                }
            }
            catch (Exception e)
            {
                Health.Report("items", e);
                nextCheck = Time.unscaledTime + 10f;
            }
        }

        private static void Register()
        {
            var db = Game.ItemDb;
            if (db == null || db.itemLookup == null || db.itemLookup.Count < 10) return;
            if (!(PhotonNetwork.PrefabPool is DefaultPool pool))
            {
                Health.Report("items", "Photon prefab pool is not the default one; Minecraft items cannot be networked.");
                return;
            }
            texturesWereReal = McAssets.HasRealTextures;

            var vanilla = db.itemLookup.Values.Where(i => i != null && !ItemDefs.IsMc(i.itemID)).ToList();
            var tpl = Balance.Section("templates");
            Item generic = ByName(vanilla, Balance.S(tpl, "generic", "")) ?? PickBase(vanilla, false);
            Item food = ByName(vanilla, Balance.S(tpl, "food", "")) ?? PickBase(vanilla, true) ?? generic;
            if (generic == null)
            {
                Health.Report("items", "Could not find a PEAK item to base Minecraft items on.");
                return;
            }
            GenericBaseName = generic.name;
            FoodBaseName = food.name;
            Plugin.Log.LogInfo($"Minecraft items are built on PEAK's '{generic.name}' (food: '{food.name}').");

            if (holder == null)
            {
                holder = new GameObject("BlockPeak.ItemTemplates");
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
            }

            foreach (var def in ItemDefs.All)
            {
                if (db.itemLookup.TryGetValue(def.Id, out var existing) && existing != null && !Templates.ContainsKey(def.Id))
                {
                    Health.Report("items", $"Item id {def.Id} is already used by {existing.name}; {def.Name} is disabled.");
                    continue;
                }
                try
                {
                    var t = Build(def, def.Kind == McKind.Food || def.Kind == McKind.Potion ? food : generic);
                    Templates[def.Id] = t;
                    db.itemLookup[def.Id] = t;
                    pool.ResourceCache["0_Items/" + def.PrefabName] = t.gameObject;
                }
                catch (Exception e) { Health.Report("item:" + def.Key, e); }
            }
            AddText();
            LootData.AllSpawnWeightData = null; // rebuilt lazily; our items have no LootData so PEAK's pools are untouched
            Ready = Templates.Count > 0;
            Diagnostics.DumpItems(vanilla, generic, food);
            Plugin.Log.LogInfo($"Registered {Templates.Count} Minecraft items (ids {ItemDefs.FirstId}-{ItemDefs.FirstId + ItemDefs.All.Count - 1}).");
        }

        private static void EnsureStillRegistered()
        {
            var db = Game.ItemDb;
            if (db == null) return;
            if (!LocalizedText.mainTable.ContainsKey("NAME_" + ItemDefs.All[0].LocName)) AddText(); // language reload
            var pool = PhotonNetwork.PrefabPool as DefaultPool;
            foreach (var kv in Templates)
            {
                if (kv.Value == null) continue;
                if (!db.itemLookup.ContainsKey(kv.Key)) db.itemLookup[kv.Key] = kv.Value;
                string key = "0_Items/" + kv.Value.gameObject.name;
                if (pool != null && !pool.ResourceCache.ContainsKey(key)) pool.ResourceCache[key] = kv.Value.gameObject;
            }
        }

        private static Item ByName(List<Item> items, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return items.FirstOrDefault(i => string.Equals(i.name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static readonly string[] PreferGeneric = { "Compass", "Bandages", "Guidebook", "Cure-All", "CureAll", "Antidote", "Marshmallow", "Pirate Compass" };
        private static readonly string[] PreferFood = { "Granola Bar", "GranolaBar", "Airline Food", "AirlineFood", "Trail Mix", "TrailMix", "Cookie", "Energy Drink", "Lollipop" };

        /// <summary>The simplest ordinary item: no special scripts, pocketable, droppable, throwable.</summary>
        private static Item PickBase(List<Item> items, bool food)
        {
            Item best = null;
            float bestScore = float.MinValue;
            foreach (var it in items)
            {
                try
                {
                    if (it is Backpack || it.GetComponent<PhotonView>() == null) continue;
                    if (it.GetComponentInChildren<MeshRenderer>(true) == null) continue;
                    var actions = it.GetComponents<ItemActionBase>();
                    bool eats = actions.Any(a => a is Action_RestoreHunger || (a is Action_ModifyStatus m && m.statusType == CharacterAfflictions.STATUSTYPE.Hunger && m.changeAmount < 0));
                    if (food && (!eats || it.GetComponent<ItemUseFeedback>() == null)) continue;

                    float score = 0;
                    if (it.GetType() != typeof(Item)) score -= 50;
                    score -= 6 * it.GetComponents<ItemComponent>().Count(c => !(c is ItemCooking) && !(c is TrackableNetworkObject) && !(c is ItemScaleSyncer));
                    score -= 2 * actions.Length;
                    if (it.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) score -= 10;
                    if (it.GetComponentInChildren<Animator>(true) != null) score -= 8;
                    if (it.GetComponentInChildren<ParticleSystem>(true) != null) score -= 3;
                    if (it.transform.Find("Hand_R") == null) score -= 15;
                    if (!it.UIData.canPocket) score -= 30;
                    if (!it.UIData.canDrop || !it.UIData.canThrow || !it.UIData.canBackpack) score -= 10;
                    score -= Mathf.Abs(it.CarryWeight - 1) * 2;
                    if (it.totalUses > 1 && !food) score -= 2;
                    int pref = Array.FindIndex(food ? PreferFood : PreferGeneric, n => string.Equals(n, it.name, StringComparison.OrdinalIgnoreCase));
                    if (pref >= 0) score += 20 - pref;
                    if (score > bestScore) { bestScore = score; best = it; }
                }
                catch { }
            }
            return best;
        }

        // ------------------------------------------------------------------ building one item

        private static Item Build(McItemDef def, Item baseItem)
        {
            var go = UnityEngine.Object.Instantiate(baseItem.gameObject, holder.transform);
            go.name = def.PrefabName;
            go.SetActive(false);
            var item = go.GetComponent<Item>();
            bool keepFeedback = def.Kind == McKind.Food || def.Kind == McKind.Potion;

            // 1) Where did the original model sit (so PEAK's hand points still line up)?
            Bounds local = LocalBounds(go.transform);

            // 2) Strip PEAK behaviour and looks.
            foreach (var a in go.GetComponentsInChildren<ItemActionBase>(true)) UnityEngine.Object.DestroyImmediate(a);
            foreach (var c in go.GetComponentsInChildren<ItemComponent>(true))
                if (!(c is TrackableNetworkObject) && !(c is ItemScaleSyncer)) SafeDestroy(c); // ItemCooking is re-added fresh by Item.Awake
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (RemoveByName.Contains(n) || n.StartsWith("CookingBehavior") || (!keepFeedback && n == "ItemUseFeedback")) SafeDestroy(mb);
            }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystemRenderer>(true)) SafeDestroy(ps);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) SafeDestroy(ps);
            foreach (var l in go.GetComponentsInChildren<LODGroup>(true)) SafeDestroy(l);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) SafeDestroy(r);
            foreach (var f in go.GetComponentsInChildren<MeshFilter>(true)) SafeDestroy(f);
            foreach (var l in go.GetComponentsInChildren<Light>(true)) SafeDestroy(l);
            foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) SafeDestroy(a);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) SafeDestroy(col);
            var view = go.GetComponent<PhotonView>();
            if (view != null && view.ObservedComponents != null) view.ObservedComponents.RemoveAll(c => c == null);

            // 3) Minecraft looks + one simple box collider.
            var visual = new GameObject("BP_Visual");
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = local.size.sqrMagnitude > 0.0001f ? local.center : Vector3.zero;
            var mf = visual.AddComponent<MeshFilter>();
            var mr = visual.AddComponent<MeshRenderer>();
            ApplyLooks(def, mf, mr, visual.transform);
            var box = go.AddComponent<BoxCollider>();
            var vb = mf.sharedMesh.bounds;
            box.center = visual.transform.localPosition + visual.transform.localRotation * Vector3.Scale(vb.center, visual.transform.localScale);
            var size = visual.transform.localRotation * Vector3.Scale(vb.size, visual.transform.localScale);
            box.size = new Vector3(Mathf.Max(0.08f, Mathf.Abs(size.x)), Mathf.Max(0.08f, Mathf.Abs(size.y)), Mathf.Max(0.08f, Mathf.Abs(size.z)));
            item.colliders = new Collider[] { box };
            item.mainRenderer = mr;
            item.addtlRenderers = new Renderer[0];

            // 4) Item settings.
            item.itemID = def.Id;
            item.totalUses = def.Stack > 1 ? def.Stack : -1;
            item.isSecretlyOtherItemPrefab = null;
            item.offsetLuggageSpawn = false;
            item.itemTags = def.Kind == McKind.Food ? Item.ItemTags.PackagedFood : Item.ItemTags.None;
            item.blocksSprint = false;
            item.carryWeight = Mathf.Max(1, Mathf.CeilToInt(def.WeightFor(1)));
            bool consumable = def.Kind == McKind.Food || def.Kind == McKind.Potion;
            item.usingTimePrimary = consumable ? Balance.F(def.Cfg, "eatSeconds", 1.6f) : 0f;
            item.showUseProgress = consumable;
            var ui = item.UIData;
            ui.itemName = def.LocName;
            ui.icon = IconFor(def);
            ui.altIcon = null;
            ui.hasAltIcon = false;
            ui.hasColorBlindIcon = false;
            ui.hasMainInteract = def.Kind != McKind.Totem;
            ui.mainInteractPrompt = def.Prompt;
            ui.hasSecondInteract = false;
            ui.secondaryInteractPrompt = "";
            ui.hasScrollingInteract = false;
            ui.canDrop = ui.canPocket = ui.canBackpack = ui.canThrow = true;
            ui.isShootable = false;
            ui.hideFuel = true;

            // 5) Minecraft behaviour.
            var mc = go.AddComponent<McItem>();
            mc.key = def.Key;
            if (def.Stack > 1) go.AddComponent<Action_ReduceUses>(); // provides the ReduceUsesRPC that stacks use
            Behaviours.Attach(def, go);
            return item;
        }

        private static void SafeDestroy(UnityEngine.Object o)
        {
            try { UnityEngine.Object.DestroyImmediate(o); }
            catch (Exception e) { Health.Verbose("could not strip " + o + ": " + e.Message); }
        }

        private static Bounds LocalBounds(Transform root)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        public static Texture2D IconFor(McItemDef def)
        {
            if (Icons.TryGetValue(def.Key, out var t) && t != null && texturesWereReal == McAssets.HasRealTextures) return t;
            if (def.Kind == McKind.Block || def.Kind == McKind.Tnt)
                t = Meshes.BlockIcon(McAssets.Tex(def.Side), McAssets.Tex(def.Top ?? def.Side));
            else if (def.Kind == McKind.Potion)
                t = Potion(McAssets.Tex("item/potion.png"), McAssets.Tex("item/potion_overlay.png"), def.Tint);
            else
                t = McAssets.Tex(def.Texture);
            if (def.Key == "enchanted_golden_apple") t = Glint(t);
            Icons[def.Key] = t;
            return t;
        }

        /// <summary>Minecraft draws potions as a bottle plus a tinted liquid layer.</summary>
        private static Texture2D Potion(Texture2D bottle, Texture2D overlay, Color tint)
        {
            try
            {
                int w = bottle.width, h = bottle.height;
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "mc_potion_" + ColorUtility.ToHtmlStringRGB(tint) };
                var b = bottle.GetPixels();
                var o = overlay.width == w && overlay.height == h ? overlay.GetPixels() : null;
                var px = new Color[b.Length];
                for (int i = 0; i < b.Length; i++)
                {
                    Color c = b[i];
                    if (o != null && o[i].a > 0.1f)
                    {
                        Color liquid = o[i] * tint;
                        liquid.a = 1f;
                        c = Color.Lerp(c, liquid, o[i].a);
                        c.a = Mathf.Max(b[i].a, o[i].a);
                    }
                    px[i] = c;
                }
                dst.SetPixels(px);
                dst.Apply();
                return dst;
            }
            catch { return bottle; }
        }

        /// <summary>Purple shimmer baked into the enchanted apple's icon/texture.</summary>
        private static Texture2D Glint(Texture2D src)
        {
            try
            {
                var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = src.name + "_glint" };
                var px = src.GetPixels();
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a < 0.1f) continue;
                    int x = i % src.width, y = i / src.width;
                    float k = ((x + y) % 6 < 2) ? 0.35f : 0.12f;
                    px[i] = Color.Lerp(px[i], new Color(0.75f, 0.45f, 1f, px[i].a), k);
                }
                dst.SetPixels(px);
                dst.Apply();
                return dst;
            }
            catch { return src; }
        }

        /// <summary>Where the item's model sits, from the original PEAK model (so the scout's grip points fit).</summary>
        public class VisualAnchor : MonoBehaviour
        {
            public Vector3 basePos;
        }

        private static Vector3 V3(Newtonsoft.Json.Linq.JToken t, Vector3 fallback)
        {
            var a = t as Newtonsoft.Json.Linq.JArray;
            if (a == null || a.Count < 3) return fallback;
            try { return new Vector3((float)a[0], (float)a[1], (float)a[2]); } catch { return fallback; }
        }

        public static void ApplyLooks(McItemDef def, MeshFilter mf, MeshRenderer mr, Transform t)
        {
            var anchor = t.GetComponent<VisualAnchor>() ?? t.gameObject.AddComponent<VisualAnchor>();
            if (anchor.basePos == Vector3.zero) anchor.basePos = t.localPosition;
            Vector3 offset = Vector3.zero, rot = Vector3.zero;
            float scale;
            switch (def.Kind)
            {
                case McKind.Block:
                case McKind.Tnt:
                {
                    var atlas = Meshes.BlockAtlas(McAssets.Tex(def.Side), McAssets.Tex(def.Top ?? def.Side), McAssets.Tex(def.Bottom ?? def.Top ?? def.Side));
                    mf.sharedMesh = Meshes.Cube(1f, "mc_cube");
                    mr.sharedMaterial = Mat.For(atlas);
                    scale = 0.3f;
                    offset = new Vector3(0, -0.15f, 0);
                    break;
                }
                case McKind.Torch:
                {
                    var tex = McAssets.Tex(def.Texture);
                    mf.sharedMesh = Meshes.Torch(1f);
                    mr.sharedMaterial = Mat.Glowing(tex, new Color(1f, 0.8f, 0.4f));
                    scale = 0.7f;
                    offset = new Vector3(0, -0.22f, 0);
                    break;
                }
                default:
                {
                    var tex = IconFor(def);
                    mf.sharedMesh = Meshes.ItemSprite(tex);
                    mr.sharedMaterial = Mat.For(tex);
                    scale = def.Kind == McKind.Sword ? 0.6f : def.Kind == McKind.Elytra || def.Kind == McKind.Boat ? 0.5f : 0.38f;
                    if (def.Kind == McKind.Sword)
                    {
                        // The sprite's blade runs bottom-left (handle) to top-right (tip): turn it upright,
                        // lean the tip forward a little and put the handle in the hand like Minecraft.
                        rot = new Vector3(25f, 0f, 45f);
                        offset = new Vector3(0f, 0.2f, 0.05f);
                    }
                    break;
                }
            }
            // Per-item tuning from balance.json: "hold": { "rotation": [x,y,z], "offset": [x,y,z], "scale": s }
            var hold = def.Cfg["hold"];
            if (hold != null)
            {
                rot = V3(hold["rotation"], rot);
                offset = V3(hold["offset"], offset);
                scale = Balance.F(hold, "scale", scale);
            }
            t.localPosition = anchor.basePos + offset;
            t.localRotation = Quaternion.Euler(rot);
            t.localScale = Vector3.one * scale;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        /// <summary>Rebuild looks/icons after the real Minecraft textures were copied mid-session.</summary>
        private static void RefreshVisuals()
        {
            Icons.Clear();
            foreach (var kv in Templates)
            {
                var def = ItemDefs.ById(kv.Key);
                if (def == null || kv.Value == null) continue;
                kv.Value.UIData.icon = IconFor(def);
                var vis = kv.Value.transform.Find("BP_Visual");
                if (vis != null)
                {
                    ApplyLooks(def, vis.GetComponent<MeshFilter>(), vis.GetComponent<MeshRenderer>(), vis);
                }
            }
        }

        private static void AddText()
        {
            var table = LocalizedText.mainTable;
            int langs = Enum.GetValues(typeof(LocalizedText.Language)).Length;
            void Put(string key, string text)
            {
                table[key.ToUpperInvariant()] = Enumerable.Repeat(text, langs).ToList();
            }
            foreach (var d in ItemDefs.All) Put("NAME_" + d.LocName, d.Name);
            Put("BP_PLACE", "Place");
            Put("BP_EAT", "Eat");
            Put("BP_THROW", "Throw");
            Put("BP_GLIDE", "Hold to glide while falling");
            Put("BP_TOOT", "Toot");
            Put("BP_RIDE", "Hold to ride");
            Put("BP_DRINK", "Drink");
            Put("BP_WEAR", "Wear");
            Put("BP_FIREWORK", "Firework boost");
            Put("BP_ATTACK", "Attack");
            Put("BP_USE", "Use");
            Put("BP_TOTEM", "Saves you once");
        }
    }
}
