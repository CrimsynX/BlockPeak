using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Mobs;
using BlockPeak.Net;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Items
{
    /// <summary>
    /// Bow with its own 16 arrows. Hold Use to draw (Minecraft's 1 second for full power), let go to shoot.
    /// A hit knocks anyone over: scouts fall and lose their grip, mobs fall and get hurt.
    /// </summary>
    public class McBow : ItemAction
    {
        private float drawStart = -1f;
        private int stage = -1;
        private Transform vis;
        private MeshFilter mf;
        private MeshRenderer mr;

        private McItemDef Def => Behaviours.Def(item);

        public override void RunAction()
        {
            var c = character;
            if (c == null || !c.IsLocal) return;
            if (Arrows <= 0) { Sfx.At("random/click", c.Head, 0.5f, 1.4f); return; }
            drawStart = Time.time;
        }

        private int Arrows => Stacks.Arrows(item.data, Balance.I(Def?.Cfg, "arrows", 16));

        private void Update()
        {
            if (item == null) return;
            if (drawStart < 0f) { SetStage(-1); return; }
            var c = character;
            if (c == null || !c.IsLocal || item.itemState != ItemState.Held) { drawStart = -1f; SetStage(-1); return; }
            float t = Time.time - drawStart;
            SetStage(t < 0.35f ? 0 : t < 0.75f ? 1 : 2);
            if (item.isUsingPrimary) return;

            // Released: shoot.
            drawStart = -1f;
            SetStage(-1);
            if (t < 0.1f) return;
            float f = Mathf.Min(1f, t / Balance.F(Def?.Cfg, "drawSeconds", 1f));
            float power = Mathf.Min(1f, (f * f + 2f * f) / 3f);
            if (power < 0.1f) return;
            Vector3 dir = Game.CamForward;
            Vector3 from = Game.CamPos + dir * 0.7f;
            float speed = Balance.F(Def?.Cfg, "arrowSpeed", 45f) * power;
            Projectiles.ShootArrow(from, dir * speed + c.data.avarageVelocity * 0.3f, power);
            int left = Arrows - 1;
            Stacks.SetArrows(item.data, left);
            Channel.Host(Op.BowArrows, c.refs.items.currentSelectedSlot.IsNone ? (byte)250 : c.refs.items.currentSelectedSlot.Value, left);
            Sfx.At("random/bow", from, 0.8f, 1f / Random.Range(1.0f, 1.3f) + power * 0.5f);
        }

        /// <summary>Show bow_pulling_0..2 while drawing, like Minecraft.</summary>
        private void SetStage(int s)
        {
            if (s == stage) return;
            stage = s;
            if (vis == null) vis = item.transform.Find("BP_Visual");
            if (vis == null) return;
            if (mf == null) mf = vis.GetComponent<MeshFilter>();
            if (mr == null) mr = vis.GetComponent<MeshRenderer>();
            var tex = McAssets.Tex(s < 0 ? "item/bow.png" : "item/bow_pulling_" + s + ".png");
            mf.sharedMesh = Meshes.ItemSprite(tex);
            mr.sharedMaterial = Mat.For(tex);
        }

        public static void RegisterNet()
        {
            // Owner -> host: the host keeps inventories, so it needs the arrow count too.
            Channel.On(Op.BowArrows, (a, sender) =>
            {
                if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return;
                var p = PlayerHandler.GetPlayer(sender) ?? Player.localPlayer;
                var slot = p?.GetItemSlot(Channel.Byte(a[0]));
                var def = ItemDefs.ByKey("bow");
                if (slot == null || slot.IsEmpty() || def == null || slot.prefab.itemID != def.Id) return;
                Stacks.SetArrows(slot.data, Channel.Int(a[1]));
            });
            // A scout hit by someone's arrow: knocked over, lets go of the wall, a little hurt.
            Channel.On(Op.ArrowHitPlayer, (a, sender) =>
            {
                var c = Game.LocalChar;
                if (c == null || c.data.dead) return;
                float power = Channel.Flt(a[0]);
                Vector3 dir = Channel.Vec(a[1]);
                KnockDown(c, power, dir);
            });
        }

        public static void KnockDown(Character c, float power, Vector3 dir)
        {
            var cfg = Balance.ItemCfg("bow");
            if (!UI.Creative.BlocksStatus(CharacterAfflictions.STATUSTYPE.Injury))
                c.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, Balance.F(cfg, "playerInjury", 0.08f) * power);
            if (c.data.isClimbing) c.refs.climbing.StopClimbing();
            if (c.data.isRopeClimbing) try { c.refs.view.RPC("StopRopeClimbingRpc", RpcTarget.All, false); } catch { }
            c.Fall(Balance.F(cfg, "knockDownSeconds", 1.5f) * Mathf.Max(0.4f, power));
            Vector3 push = new Vector3(dir.x, 0f, dir.z).normalized * Balance.F(cfg, "knockback", 5f) * power + Vector3.up * 1.5f;
            c.AddForce(push / Time.fixedDeltaTime, 0.9f, 1.1f);
            Sfx.At("random/bowhit", c.Center, 0.9f);
        }
    }
}
