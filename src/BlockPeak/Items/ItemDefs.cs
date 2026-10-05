using System.Collections.Generic;
using System.Linq;
using BlockPeak.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BlockPeak.Items
{
    public enum McKind { Block, Torch, Ladder, Tnt, Food, EnderPearl, Elytra, Boat, WindCharge, GoatHorn, Totem, Sword, Potion, Bow }

    /// <summary>One Minecraft item that can turn up in PEAK.</summary>
    public class McItemDef
    {
        public string Key;            // balance.json key, e.g. "cookie"
        public string Name;           // shown in game
        public McKind Kind;
        public string Texture;        // item sprite (textures/...)
        public string Side, Top, Bottom; // block faces
        public string CfgKey;         // section under "items" in balance.json
        public string Prompt = "BP_USE";
        public string Effect;         // potions: "speed" or "jump"
        public Color Tint = Color.white;
        // Blocks
        public Color FaceTint = Color.white;   // grass tops and leaves are tinted in Minecraft
        public bool TintSides;                  // leaves: every face; grass: only the top
        public bool Cutout;                     // glass, leaves: see-through pixels
        public float Light;                     // light range (glowstone, sea lantern, magma...)
        public Color LightColor = new Color(1f, 0.85f, 0.6f);
        public string Special;                  // "slime" (bouncy), "magma" (hot)
        public ushort Id;
        public int Index;

        public JToken Cfg => Balance.ItemCfg(CfgKey ?? Key);
        public int Stack => System.Math.Max(1, Balance.I(Cfg, "stack", 1));
        public (int min, int max) Find => Balance.Range(Cfg, "find", 1, 1);
        public bool IsPlaceable => Kind == McKind.Block || Kind == McKind.Torch || Kind == McKind.Ladder || Kind == McKind.Tnt;
        public string PrefabName => "BlockPeak_" + Key;

        private Texture2D sideTex, topTex, bottomTex, atlas;

        public Texture2D SideTex => sideTex != null ? sideTex : sideTex = Face(Side, TintSides);
        public Texture2D TopTex => topTex != null ? topTex : topTex = Face(Top ?? Side, true);
        public Texture2D BottomTex => bottomTex != null ? bottomTex : bottomTex = Face(Bottom ?? Top ?? Side, TintSides);

        private Texture2D Face(string path, bool tint)
        {
            var t = Assets.McAssets.Tex(path);
            return tint && FaceTint != Color.white ? Assets.Meshes.Tinted(t, FaceTint) : t;
        }

        /// <summary>The block's side|top|bottom texture strip (tints and transparency applied).</summary>
        public Texture2D BlockAtlas => atlas != null ? atlas : atlas = Assets.Meshes.BlockAtlas(SideTex, TopTex, BottomTex, Cutout);

        public Mesh BlockMesh => Cutout ? Assets.Meshes.CubeCut(BlockAtlas) : Assets.Meshes.Cube(1f, "mc_cube");

        /// <summary>Forget cached textures (after the real Minecraft textures were copied mid-session).</summary>
        public void ResetLooks() { sideTex = topTex = bottomTex = atlas = null; }
        public string LocName => "BP_" + Key.ToUpperInvariant();

        /// <summary>Weight units for a stack of n (PEAK counts 1 unit = one 2.5% tick on the bar). Fractions are fine.</summary>
        public float WeightFor(int n)
        {
            var c = Cfg;
            if (Kind == McKind.Block)
            {
                // Light / normal / heavy blocks: "blockWeights": { "<key>": blocks per weight unit }
                float perBlock = Balance.F(Balance.ItemCfg("blocks")["blockWeights"], Key, Balance.F(c, "weightPer", 7.5f));
                return perBlock > 0 ? n / perBlock : 0f;
            }
            float each = Balance.F(c, "weightEach", 0);
            float per = Balance.F(c, "weightPer", 0);
            if (each > 0) return each * n;
            if (per > 0) return Mathf.Ceil(n / per);
            return 1;
        }
    }

    public static class ItemDefs
    {
        public const ushort FirstId = 47000;

        public static readonly List<McItemDef> All = new List<McItemDef>();
        private static readonly Dictionary<ushort, McItemDef> byId = new Dictionary<ushort, McItemDef>();
        private static readonly Dictionary<string, McItemDef> byKey = new Dictionary<string, McItemDef>();
        private static readonly Dictionary<string, McItemDef> byPrefab = new Dictionary<string, McItemDef>();

        static ItemDefs()
        {
            Block("sand", "Sand", "block/sand.png");
            Block("oak_planks", "Oak Planks", "block/oak_planks.png");
            Block("moss_block", "Moss Block", "block/moss_block.png");
            Block("packed_ice", "Packed Ice", "block/packed_ice.png");
            Block("terracotta", "Terracotta", "block/terracotta.png");
            Block("basalt", "Basalt", "block/basalt_side.png", "block/basalt_top.png");
            Block("deepslate", "Deepslate", "block/deepslate.png");
            Block("stone_bricks", "Stone Bricks", "block/stone_bricks.png");
            Add(new McItemDef { Key = "torch", Name = "Torch", Kind = McKind.Torch, Texture = "block/torch.png", Prompt = "BP_PLACE" });
            Skip(); // 9 was the redstone torch (removed in 0.2.0; ids of later items stay the same)
            Skip(); // was the ladder (removed in 0.3.0)
            Add(new McItemDef { Key = "tnt", Name = "TNT", Kind = McKind.Tnt, Side = "block/tnt_side.png", Top = "block/tnt_top.png", Bottom = "block/tnt_bottom.png", Prompt = "BP_PLACE" });
            Add(new McItemDef { Key = "ender_pearl", Name = "Ender Pearl", Kind = McKind.EnderPearl, Texture = "item/ender_pearl.png", Prompt = "BP_THROW" });
            Add(new McItemDef { Key = "elytra", Name = "Elytra", Kind = McKind.Elytra, Texture = "item/elytra.png", Prompt = "BP_GLIDE" });
            Add(new McItemDef { Key = "goat_horn", Name = "Goat Horn", Kind = McKind.GoatHorn, Texture = "item/goat_horn.png", Prompt = "BP_TOOT" });
            Add(new McItemDef { Key = "totem_of_undying", Name = "Totem of Undying", Kind = McKind.Totem, Texture = "item/totem_of_undying.png", Prompt = "BP_TOTEM" });
            Food("golden_apple", "Golden Apple", "item/golden_apple.png");
            Food("enchanted_golden_apple", "Enchanted Golden Apple", "item/golden_apple.png");
            Food("cookie", "Cookie", "item/cookie.png");
            Food("steak", "Steak", "item/cooked_beef.png");
            Add(new McItemDef { Key = "boat", Name = "Oak Boat", Kind = McKind.Boat, Texture = "item/oak_boat.png", Prompt = "BP_RIDE" });
            Skip(); // 21 was the water bucket (removed in 0.2.0)
            Add(new McItemDef { Key = "wind_charge", Name = "Wind Charge", Kind = McKind.WindCharge, Texture = "item/wind_charge.png", Prompt = "BP_THROW" });
            Add(new McItemDef { Key = "stone_sword", Name = "Stone Sword", Kind = McKind.Sword, Texture = "item/stone_sword.png", Prompt = "BP_ATTACK" });
            Food("rotten_flesh", "Rotten Flesh", "item/rotten_flesh.png");
            Add(new McItemDef { Key = "potion_swiftness", Name = "Potion of Swiftness", Kind = McKind.Potion, Texture = "item/potion.png", Effect = "speed", Tint = new Color(0.2f, 0.92f, 1f), Prompt = "BP_DRINK" });
            Add(new McItemDef { Key = "potion_leaping", Name = "Potion of Leaping", Kind = McKind.Potion, Texture = "item/potion.png", Effect = "jump", Tint = new Color(0.99f, 1f, 0.52f), Prompt = "BP_DRINK" });
            Add(new McItemDef { Key = "bow", Name = "Bow", Kind = McKind.Bow, Texture = "item/bow.png", Prompt = "BP_SHOOT" });

            // ---- more blocks (0.4.0); added at the end so older item ids stay the same
            var grass = new Color(0.57f, 0.74f, 0.35f);   // Minecraft plains grass colour
            var foliage = new Color(0.47f, 0.67f, 0.19f); // plains foliage colour
            Block("stone", "Stone", "block/stone.png");
            Block("cobblestone", "Cobblestone", "block/cobblestone.png");
            Block("mossy_cobblestone", "Mossy Cobblestone", "block/mossy_cobblestone.png");
            Block("dirt", "Dirt", "block/dirt.png");
            Block("grass_block", "Grass Block", "block/grass_block_side.png", "block/grass_block_top.png", "block/dirt.png").FaceTint = grass;
            Block("oak_log", "Oak Log", "block/oak_log.png", "block/oak_log_top.png");
            Block("birch_log", "Birch Log", "block/birch_log.png", "block/birch_log_top.png");
            Block("spruce_planks", "Spruce Planks", "block/spruce_planks.png");
            Block("cherry_planks", "Cherry Planks", "block/cherry_planks.png");
            Block("bricks", "Bricks", "block/bricks.png");
            Block("sandstone", "Sandstone", "block/sandstone.png", "block/sandstone_top.png", "block/sandstone_bottom.png");
            Block("red_sandstone", "Red Sandstone", "block/red_sandstone.png", "block/red_sandstone_top.png", "block/red_sandstone_bottom.png");
            Block("gravel", "Gravel", "block/gravel.png");
            Block("snow_block", "Snow Block", "block/snow.png");
            Block("blue_ice", "Blue Ice", "block/blue_ice.png");
            Block("obsidian", "Obsidian", "block/obsidian.png");
            Block("netherrack", "Netherrack", "block/netherrack.png");
            Block("blackstone", "Blackstone", "block/blackstone.png", "block/blackstone_top.png");
            Block("end_stone", "End Stone", "block/end_stone.png");
            Block("mud_bricks", "Mud Bricks", "block/mud_bricks.png");
            Block("tuff", "Tuff", "block/tuff.png");
            Block("calcite", "Calcite", "block/calcite.png");
            Block("white_wool", "White Wool", "block/white_wool.png");
            Block("hay_block", "Hay Bale", "block/hay_block_side.png", "block/hay_block_top.png");
            Block("bookshelf", "Bookshelf", "block/bookshelf.png", "block/oak_planks.png");
            Block("crafting_table", "Crafting Table", "block/crafting_table_front.png", "block/crafting_table_top.png", "block/oak_planks.png");
            Block("pumpkin", "Pumpkin", "block/pumpkin_side.png", "block/pumpkin_top.png");
            Block("melon", "Melon", "block/melon_side.png", "block/melon_top.png");
            Block("amethyst_block", "Block of Amethyst", "block/amethyst_block.png");
            Block("copper_block", "Block of Copper", "block/copper_block.png");
            Block("gold_block", "Block of Gold", "block/gold_block.png");
            Block("diamond_block", "Block of Diamond", "block/diamond_block.png");
            var glass = Block("glass", "Glass", "block/glass.png"); glass.Cutout = true;
            var leaves = Block("oak_leaves", "Oak Leaves", "block/oak_leaves.png"); leaves.Cutout = true; leaves.FaceTint = foliage; leaves.TintSides = true;
            var glow = Block("glowstone", "Glowstone", "block/glowstone.png"); glow.Light = 12f; glow.LightColor = new Color(1f, 0.85f, 0.55f);
            var lantern = Block("sea_lantern", "Sea Lantern", "block/sea_lantern.png"); lantern.Light = 12f; lantern.LightColor = new Color(0.75f, 0.95f, 1f);
            var shroom = Block("shroomlight", "Shroomlight", "block/shroomlight.png"); shroom.Light = 11f; shroom.LightColor = new Color(1f, 0.65f, 0.35f);
            var magma = Block("magma_block", "Magma Block", "block/magma.png"); magma.Light = 5f; magma.LightColor = new Color(1f, 0.45f, 0.15f); magma.Special = "magma";
            var slime = Block("slime_block", "Slime Block", "block/slime_block.png"); slime.Special = "slime";
        }

        private static int nextIndex;

        private static void Skip() => nextIndex++;

        private static McItemDef Block(string key, string name, string side, string top = null, string bottom = null)
        {
            var d = new McItemDef { Key = key, Name = name, Kind = McKind.Block, Side = side, Top = top ?? side, Bottom = bottom ?? top ?? side, CfgKey = "blocks", Prompt = "BP_PLACE" };
            Add(d);
            return d;
        }

        private static void Food(string key, string name, string tex)
        {
            Add(new McItemDef { Key = key, Name = name, Kind = McKind.Food, Texture = tex, Prompt = "BP_EAT" });
        }

        private static void Add(McItemDef d)
        {
            d.Index = nextIndex++;
            d.Id = (ushort)(FirstId + d.Index);
            All.Add(d);
            byId[d.Id] = d;
            byKey[d.Key] = d;
            byPrefab[d.PrefabName] = d;
        }

        public static McItemDef ById(ushort id) => byId.TryGetValue(id, out var d) ? d : null;
        public static McItemDef ByKey(string key) => key != null && byKey.TryGetValue(key, out var d) ? d : null;
        public static McItemDef ByPrefab(string prefabName) => prefabName != null && byPrefab.TryGetValue(prefabName, out var d) ? d : null;
        public static McItemDef Of(Item item) => item == null ? null : ById(item.itemID);
        public static bool IsMc(ushort id) => byId.ContainsKey(id);

        public static IEnumerable<McItemDef> Blocks => All.Where(d => d.Kind == McKind.Block);

        /// <summary>Changes whenever the item list changes; players must match.</summary>
        public static string Signature
        {
            get
            {
                unchecked
                {
                    uint h = 2166136261;
                    foreach (var d in All) foreach (char c in d.Key + d.Id) h = (h ^ c) * 16777619;
                    return h.ToString("x8");
                }
            }
        }
    }
}
