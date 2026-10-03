using System.Collections.Generic;
using System.Linq;
using BlockPeak.Core;
using Newtonsoft.Json.Linq;

namespace BlockPeak.Items
{
    public enum McKind { Block, Torch, RedstoneTorch, Ladder, Tnt, Food, EnderPearl, Elytra, Boat, WaterBucket, WindCharge, GoatHorn, Totem, Sword }

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
        public ushort Id;
        public int Index;

        public JToken Cfg => Balance.ItemCfg(CfgKey ?? Key);
        public int Stack => System.Math.Max(1, Balance.I(Cfg, "stack", 1));
        public (int min, int max) Find => Balance.Range(Cfg, "find", 1, 1);
        public bool IsPlaceable => Kind == McKind.Block || Kind == McKind.Torch || Kind == McKind.RedstoneTorch || Kind == McKind.Ladder || Kind == McKind.Tnt;
        public string PrefabName => "BlockPeak_" + Key;
        public string LocName => "BP_" + Key.ToUpperInvariant();

        /// <summary>Weight units for a stack of n (PEAK counts 1 unit = one weight tick).</summary>
        public int WeightFor(int n)
        {
            var c = Cfg;
            int each = Balance.I(c, "weightEach", 0);
            int per = Balance.I(c, "weightPer", 0);
            if (each > 0) return each * n;
            if (per > 0) return (n + per - 1) / per;
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
            Add(new McItemDef { Key = "redstone_torch", Name = "Redstone Torch", Kind = McKind.RedstoneTorch, Texture = "block/redstone_torch.png", Prompt = "BP_PLACE" });
            Add(new McItemDef { Key = "ladder", Name = "Ladder", Kind = McKind.Ladder, Texture = "block/ladder.png", Prompt = "BP_PLACE" });
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
            Add(new McItemDef { Key = "water_bucket", Name = "Water Bucket", Kind = McKind.WaterBucket, Texture = "item/water_bucket.png", Prompt = "BP_POUR" });
            Add(new McItemDef { Key = "wind_charge", Name = "Wind Charge", Kind = McKind.WindCharge, Texture = "item/wind_charge.png", Prompt = "BP_THROW" });
            Add(new McItemDef { Key = "stone_sword", Name = "Stone Sword", Kind = McKind.Sword, Texture = "item/stone_sword.png", Prompt = "BP_ATTACK" });
            Food("rotten_flesh", "Rotten Flesh", "item/rotten_flesh.png");
        }

        private static void Block(string key, string name, string side, string top = null)
        {
            Add(new McItemDef { Key = key, Name = name, Kind = McKind.Block, Side = side, Top = top ?? side, Bottom = top ?? side, CfgKey = "blocks", Prompt = "BP_PLACE" });
        }

        private static void Food(string key, string name, string tex)
        {
            Add(new McItemDef { Key = key, Name = name, Kind = McKind.Food, Texture = tex, Prompt = "BP_EAT" });
        }

        private static void Add(McItemDef d)
        {
            d.Index = All.Count;
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
