using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace BlockPeak.Core
{
    /// <summary>
    /// Writes BepInEx/config/BlockPeak/peak-items.txt: every PEAK item with its scripts, so the base items can be
    /// tuned in balance.json ("templates") after a PEAK update without recompiling.
    /// </summary>
    public static class Diagnostics
    {
        public static void DumpItems(List<Item> items, Item generic, Item food)
        {
            try
            {
                string path = Path.Combine(Plugin.DataDir, "peak-items.txt");
                var sb = new StringBuilder();
                sb.AppendLine($"# PEAK {Application.version} items as seen by BlockPeak {Plugin.Version} ({DateTime.Now:yyyy-MM-dd HH:mm})");
                sb.AppendLine($"# Minecraft items are built on: generic = {generic?.name}, food = {food?.name}");
                sb.AppendLine("# Override in balance.json: \"templates\": { \"generic\": \"<name>\", \"food\": \"<name>\" }");
                sb.AppendLine();
                foreach (var it in items.OrderBy(i => i.itemID))
                {
                    var comps = it.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)
                        .Where(n => n != "Transform").Distinct();
                    sb.AppendLine($"{it.itemID,5}  {it.name,-28} weight {it.CarryWeight}  uses {it.totalUses}  hands {(it.transform.Find("Hand_R") != null ? "yes" : "no")}  [{string.Join(", ", comps)}]");
                }
                File.WriteAllText(path, sb.ToString());
                Health.Verbose("Wrote " + path);
            }
            catch (Exception e) { Health.Verbose("item dump failed: " + e.Message); }
        }
    }
}
