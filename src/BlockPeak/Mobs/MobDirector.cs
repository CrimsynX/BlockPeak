using System.Collections.Generic;
using System.Linq;
using BlockPeak.Core;
using UnityEngine;

namespace BlockPeak.Mobs
{
    /// <summary>
    /// One place that knows every kind of mob: night mobs and the warden on PEAK zombie bodies (<see cref="BodyMobs"/>),
    /// and the lightweight horde used by Zombie Chase (<see cref="McMobs"/>).
    /// </summary>
    public static class MobDirector
    {
        public static readonly List<string> Summonable = new List<string>
        {
            "zombie", "husk", "drowned", "skeleton", "stray", "bogged", "spider", "creeper", "slime", "magma_cube", "warden",
        };

        public static int HostSummon(Character near, string type, int count)
        {
            if (near == null) return 0;
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                float dist = 5f + i * 1.5f;
                if (BodyMobs.HostSpawnNear(near, type, dist) != null) made++;
            }
            return made;
        }

        public static int HostKillAll()
        {
            int n = McMobs.Count;
            McMobs.HostClearAll();
            n += BodyMobs.HostKillAll();
            return n;
        }

        public static bool LocalMelee(Vector3 origin, Vector3 dir, float reach, float damage)
        {
            if (BodyMobs.LocalMelee(origin, dir, reach, damage)) return true;
            return McMobs.LocalMelee(origin, dir, reach, damage);
        }

        public static void HostExplosion(Vector3 at, float radius)
        {
            McMobs.HostExplosion(at, radius);
            BodyMobs.HostExplosion(at, radius);
        }

        public static void HostPush(Vector3 at, float radius, float launch)
        {
            McMobs.HostPush(at, radius, launch);
            BodyMobs.HostPush(at, radius, launch);
        }

        public static void Scare(Vector3 at, float radius, float seconds)
        {
            McMobs.Scare(at, radius, seconds);
            BodyMobs.Scare(at, radius, seconds);
        }

        public static bool AnyNear(Vector3 p, float r) => BodyMobs.AnyNear(p, r) || McMobs.AnyMobNear(p, r);
    }
}
