using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace BlockPeak.Core
{
    /// <summary>Small, null-safe helpers around PEAK's own singletons.</summary>
    public static class Game
    {
        public static Character LocalChar => Character.localCharacter;
        public static Player LocalPlayer => Player.localPlayer;

        public static bool InAirport => SceneManager.GetActiveScene().name == "Airport";

        /// <summary>True on the mountain (not airport, not menus).</summary>
        public static bool InRun
        {
            get
            {
                if (!PhotonNetwork.InRoom || InAirport) return false;
                return MapHandler.Exists && LocalChar != null;
            }
        }

        public static bool IsHost => PhotonNetwork.IsMasterClient;

        public static bool IsNight
        {
            get
            {
                var d = DayNightManager.instance;
                return d != null && d.isDay < 0.5f;
            }
        }

        /// <summary>0..24 in-game hours, -1 if unknown.</summary>
        public static float TimeOfDay => DayNightManager.instance != null ? DayNightManager.instance.timeOfDay % 24f : -1f;

        public static Biome.BiomeType CurrentBiome
        {
            get
            {
                if (!MapHandler.Exists) return Biome.BiomeType.Shore;
                try { return Singleton<MapHandler>.Instance.GetCurrentBiome(); }
                catch { return Biome.BiomeType.Shore; }
            }
        }

        public static Segment CurrentSegment
        {
            get
            {
                if (!MapHandler.Exists) return Segment.Beach;
                try { return Singleton<MapHandler>.Instance.GetCurrentSegment(); }
                catch { return Segment.Beach; }
            }
        }

        public static ItemDatabase ItemDb => SingletonAsset<ItemDatabase>.Instance;

        public static Camera Cam => MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;

        public static Vector3 CamPos => Cam != null ? Cam.transform.position : (LocalChar != null ? LocalChar.Head : Vector3.zero);
        public static Vector3 CamForward => Cam != null ? Cam.transform.forward : Vector3.forward;

        /// <summary>Terrain + map, the layers PEAK climbs on and stands on.</summary>
        public static int TerrainMask => HelperFunctions.terrainMapMask;

        public static int MapLayer
        {
            get
            {
                int l = LayerMask.NameToLayer("Map");
                return l < 0 ? 0 : l;
            }
        }

        public static int DefaultLayer => 0;

        public static bool CanAct(Character c)
        {
            return c != null && c.data != null && c.data.fullyConscious && !c.data.passedOut && !c.data.dead;
        }
    }
}

