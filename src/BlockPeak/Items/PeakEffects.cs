using System.Linq;
using BlockPeak.Core;
using Peak.Afflictions;

namespace BlockPeak.Items
{
    /// <summary>Numbers read from PEAK's own items, so BlockPeak's effects match the game's.</summary>
    public static class PeakEffects
    {
        private static float milk = -1f;

        /// <summary>How long PEAK's milk makes you invincible (read from the milk item; 8 s if not found).</summary>
        public static float MilkSeconds
        {
            get
            {
                if (milk > 0) return milk;
                milk = 8f;
                try
                {
                    var db = Game.ItemDb;
                    if (db != null)
                        foreach (var item in db.itemLookup.Values.Where(i => i != null))
                            foreach (var a in item.GetComponents<Action_ApplyAffliction>())
                            {
                                var all = new[] { a.affliction }.Concat(a.extraAfflictions ?? new Affliction[0]);
                                foreach (var af in all)
                                    if (af is Affliction_Invincibility inv && inv.isFromMilk && inv.totalTime > 0)
                                    {
                                        milk = inv.totalTime;
                                        Plugin.Log.LogInfo($"PEAK milk invincibility: {milk:0.#} s (from {item.name})");
                                        return milk;
                                    }
                            }
                }
                catch { }
                return milk;
            }
        }
    }
}
