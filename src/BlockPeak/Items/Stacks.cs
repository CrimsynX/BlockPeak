using UnityEngine;

namespace BlockPeak.Items
{
    /// <summary>
    /// Stack sizes live in PEAK's own "item uses" counter, so they travel with the item everywhere
    /// (hotbar, backpack, dropped on the ground, reconnects) without any extra syncing.
    /// </summary>
    public static class Stacks
    {
        public const DataEntryKey RolledKey = (DataEntryKey)201;   // BoolItemData: spawn count already rolled
        public const DataEntryKey DurabilityKey = (DataEntryKey)202; // FloatItemData: 0..1 (elytra)
        public const DataEntryKey CooldownKey = (DataEntryKey)203;   // FloatItemData: (unused for now)

        public static int Count(ItemInstanceData data)
        {
            if (data != null && data.TryGetDataEntry<OptionableIntItemData>(DataEntryKey.ItemUses, out var v) && v.HasData && v.Value >= 0)
                return v.Value;
            return 1;
        }

        public static void SetCount(ItemInstanceData data, int n, int max)
        {
            if (data == null) return;
            if (!data.TryGetDataEntry<OptionableIntItemData>(DataEntryKey.ItemUses, out var v))
                v = data.RegisterNewEntry<OptionableIntItemData>(DataEntryKey.ItemUses);
            v.HasData = true;
            v.Value = Mathf.Max(0, n);
            if (!data.TryGetDataEntry<FloatItemData>(DataEntryKey.UseRemainingPercentage, out var pct))
                pct = data.RegisterNewEntry<FloatItemData>(DataEntryKey.UseRemainingPercentage);
            pct.Value = max > 0 ? Mathf.Clamp01((float)n / max) : 1f;
        }

        public static bool WasRolledFlag(ItemInstanceData data) =>
            data != null && data.TryGetDataEntry<BoolItemData>(RolledKey, out var b) && b.Value;

        public static void MarkRolled(ItemInstanceData data)
        {
            if (data == null) return;
            if (!data.TryGetDataEntry<BoolItemData>(RolledKey, out var b)) b = data.RegisterNewEntry<BoolItemData>(RolledKey);
            b.Value = true;
        }

        public static float Durability(ItemInstanceData data, float fallback)
        {
            if (data != null && data.TryGetDataEntry<FloatItemData>(DurabilityKey, out var f)) return f.Value;
            return fallback;
        }

        public static void SetDurability(ItemInstanceData data, float value)
        {
            if (data == null) return;
            if (!data.TryGetDataEntry<FloatItemData>(DurabilityKey, out var f)) f = data.RegisterNewEntry<FloatItemData>(DurabilityKey);
            f.Value = Mathf.Clamp01(value);
        }
    }
}
