using System;
using BlockPeak.Assets;
using BlockPeak.Building;
using BlockPeak.Hotbar;
using BlockPeak.Items;
using BlockPeak.Mobs;
using BlockPeak.Modes;
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
            Health.Guard("mc-visuals", McVisuals.Init);
            Health.Guard("net", () =>
            {
                BlockWorld.RegisterNet();
                McMobs.RegisterNet();
                BodyMobs.RegisterNet();
                Projectiles.RegisterNet();
                McBow.RegisterNet();
                LocalEffects.RegisterNet();
                Elytra.RegisterNet();
                Boats.RegisterNet();
                TestMenu.RegisterNet();
                Commands.RegisterNet();
                GameModes.RegisterNet();
                Rains.RegisterNet();
                Chests.RegisterNet();
                WardenFx.RegisterNet();
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
                PlacementPreview.Tick();
                BlockEffects.Tick();
            });
            Health.Guard("mobs", () =>
            {
                McMobs.Tick();
                BodyMobs.Tick();
                McMobs.TickPunch();
            });
            Health.Guard("projectiles", Projectiles.Tick);
            Health.Guard("fx", Fx.Tick);
            Health.Guard("banner", Banner.Tick);
            Health.Guard("test-menu", TestMenu.Tick);
            Health.Guard("effects", LocalEffects.Tick);
            Health.Guard("chat", ChatBox.Tick);
            Health.Guard("creative", Creative.Tick);
            Health.Guard("elytra", Elytra.Tick);
            Health.Guard("boats", Boats.Tick);
            Health.Guard("mc-visuals", McVisuals.Tick);
            Health.Guard("warden-fx", WardenFx.Tick);
            Health.Guard("modes", GameModes.Tick);
            Health.Guard("chests", Chests.TickHost);
        }

        private void FixedUpdate()
        {
            Health.Guard("creative", Creative.FixedTick);
            Health.Guard("elytra", Elytra.FixedTick);
            Health.Guard("boats", Boats.FixedTick);
        }

        private void LateUpdate()
        {
            Health.Guard("test-menu", TestMenu.LateTick);
        }

        private void OnGUI()
        {
            Health.Guard("banner-draw", Banner.Draw);
            Health.Guard("test-menu-draw", TestMenu.Draw);
            Health.Guard("chat-draw", ChatBox.Draw);
            Health.Guard("modes-draw", GameModes.Draw);
            Health.Guard("effects-draw", LocalEffects.Draw);
            Health.Guard("warden-draw", WardenFx.Draw);
            Health.Guard("options-draw", CustomOptions.Draw);
        }

        private void CheckScene()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene == lastScene) return;
            Health.Verbose($"Scene {lastScene} -> {scene}");
            lastScene = scene;
            BlockWorld.Clear();
            Elytra.Clear();
            Boats.Clear();
            McMobs.Clear();
            BodyMobs.Clear();
            Projectiles.Clear();
            Fx.Clear();
            Rains.Clear();
            Chests.Clear();
            LocalEffects.Reset();
            McChests.Reset();
            if (scene != "Airport" && !scene.ToLowerInvariant().Contains("title") && !scene.ToLowerInvariant().Contains("menu")) { Loot.ResetRun(); OnlyMinecraft.ResetRun(); }
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
