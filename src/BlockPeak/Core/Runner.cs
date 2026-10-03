using System;
using BlockPeak.Assets;
using BlockPeak.Building;
using BlockPeak.Hotbar;
using BlockPeak.Items;
using BlockPeak.Mobs;
using BlockPeak.Net;
using BlockPeak.UI;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlockPeak.Core
{
    /// <summary>Drives everything once per frame. Each feature is guarded so one failure does not stop the rest.</summary>
    public class Runner : MonoBehaviour
    {
        public static Runner Instance { get; private set; }

        private readonly HotbarHud hud = new HotbarHud();
        private string lastScene = "";
        private bool wasInRoom;
        private float nextBalanceSync;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            Health.Guard("assets", () => McAssets.Begin(this));
            Health.Guard("net", () =>
            {
                BlockWorld.RegisterNet();
                McMobs.RegisterNet();
                Projectiles.RegisterNet();
                LocalEffects.RegisterNet();
                TestMenu.RegisterNet();
            });
        }

        private void Update()
        {
            Health.Guard("net-hook", Channel.EnsureHooked);
            Health.Guard("items", ItemRegistry.Tick);
            Health.Guard("scene", CheckScene);
            Health.Guard("room", CheckRoom);
            Health.Guard("handshake", Handshake.Tick);
            hud.Update();
            Health.Guard("blocks", () =>
            {
                BlockWorld.TickHost();
                BlockWorld.TickQuickPlace();
                BlockWorld.TickWarmth();
            });
            Health.Guard("mobs", () =>
            {
                McMobs.Tick();
                McMobs.TickPunch();
            });
            Health.Guard("projectiles", Projectiles.Tick);
            Health.Guard("fx", Fx.Tick);
            Health.Guard("banner", Banner.Tick);
            Health.Guard("test-menu", TestMenu.Tick);
        }

        private void LateUpdate()
        {
            Health.Guard("test-menu", TestMenu.LateTick);
        }

        private void OnGUI()
        {
            Health.Guard("banner-draw", Banner.Draw);
            Health.Guard("test-menu-draw", TestMenu.Draw);
        }

        private void CheckScene()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene == lastScene) return;
            Health.Verbose($"Scene {lastScene} -> {scene}");
            lastScene = scene;
            BlockWorld.Clear();
            McMobs.Clear();
            Projectiles.Clear();
            Fx.Clear();
            LocalEffects.Reset();
            McChests.Reset();
            if (scene != "Airport" && !scene.ToLowerInvariant().Contains("title") && !scene.ToLowerInvariant().Contains("menu")) Loot.ResetRun();
        }

        private void CheckRoom()
        {
            bool inRoom = PhotonNetwork.InRoom;
            if (inRoom != wasInRoom)
            {
                wasInRoom = inRoom;
                nextBalanceSync = 0f;
                if (!inRoom) { BlockWorld.Clear(); McMobs.Clear(); }
            }
            if (inRoom && Time.unscaledTime >= nextBalanceSync)
            {
                nextBalanceSync = Time.unscaledTime + 5f;
                Balance.SyncWithRoom();
            }
        }

        private void OnDestroy()
        {
            hud.Destroy();
        }
    }
}
