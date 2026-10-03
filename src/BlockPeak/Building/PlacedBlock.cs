using System.Collections;
using System.Collections.Generic;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Items;
using BlockPeak.UI;
using UnityEngine;

namespace BlockPeak.Building
{
    /// <summary>A block/torch/ladder/TNT in the world. Hold the interact key to break it (or to light TNT).</summary>
    public class PlacedBlock : MonoBehaviour, IInteractibleConstant
    {
        public BlockWorld.Record Record;
        public McItemDef Def;
        public Texture2D MainTexture;
        private MeshRenderer mr;
        private Material normalMat, flashMat;
        private AudioSource fuseSound;

        public bool IsFullBlock => Def != null && (Def.Kind == McKind.Block || Def.Kind == McKind.Tnt);

        private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Texture2D> atlases = new Dictionary<string, Texture2D>();

        public static PlacedBlock Create(BlockWorld.Record r, McItemDef def, Transform parent)
        {
            float S = BlockWorld.Size;
            var go = new GameObject("mc_" + def.Key);
            go.transform.SetParent(parent, false);
            int layer = Game.MapLayer;
            go.layer = layer;
            var pb = go.AddComponent<PlacedBlock>();
            pb.Record = r;
            pb.Def = def;
            var mf = go.AddComponent<MeshFilter>();
            pb.mr = go.AddComponent<MeshRenderer>();
            Vector3 face = BlockWorld.FacingDir(r.Facing);

            switch (def.Kind)
            {
                case McKind.Block:
                case McKind.Tnt:
                {
                    var atlas = Atlas(def);
                    pb.MainTexture = McAssets.Tex(def.Side);
                    mf.sharedMesh = Meshes.Cube(1f, "mc_cube");
                    pb.mr.sharedMaterial = MatFor(def.Key, atlas);
                    go.transform.position = BlockWorld.CellBottom(r.Cell);
                    go.transform.localScale = Vector3.one * S;
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0, 0.5f, 0);
                    box.size = Vector3.one;
                    break;
                }
                case McKind.Torch:
                case McKind.RedstoneTorch:
                {
                    var tex = McAssets.Tex(def.Texture);
                    pb.MainTexture = tex;
                    mf.sharedMesh = Meshes.Torch(1f);
                    bool red = def.Kind == McKind.RedstoneTorch;
                    pb.mr.sharedMaterial = Mat.Glowing(tex, red ? new Color(1f, 0.2f, 0.1f) : new Color(1f, 0.8f, 0.4f));
                    go.transform.localScale = Vector3.one * S;
                    if (r.Facing == 0) go.transform.position = BlockWorld.CellBottom(r.Cell);
                    else
                    {
                        // Leaning against the wall like Minecraft's wall torch.
                        go.transform.position = BlockWorld.CellCenter(r.Cell) - face * 0.42f * S - Vector3.up * 0.32f * S;
                        go.transform.rotation = Quaternion.AngleAxis(22.5f, Vector3.Cross(Vector3.up, face));
                    }
                    var box = go.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.center = new Vector3(0, 0.3f, 0);
                    box.size = new Vector3(0.3f, 0.65f, 0.3f);
                    var lightGo = new GameObject("light");
                    lightGo.transform.SetParent(go.transform, false);
                    lightGo.transform.localPosition = new Vector3(0, 0.7f, 0);
                    var l = lightGo.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = red ? new Color(1f, 0.25f, 0.15f) : new Color(1f, 0.78f, 0.45f);
                    l.range = Balance.F(Balance.Section("building"), "torchLightRange", 8f) * (red ? 0.5f : 1f) * S;
                    l.intensity = red ? 1.5f : 2.5f;
                    l.shadows = LightShadows.None;
                    break;
                }
                case McKind.Ladder:
                {
                    var tex = McAssets.Tex(def.Texture);
                    pb.MainTexture = tex;
                    mf.sharedMesh = Meshes.ItemSprite(tex);
                    pb.mr.sharedMaterial = Mat.For(tex);
                    go.transform.position = BlockWorld.CellCenter(r.Cell) - face * (0.5f * S - 0.04f);
                    go.transform.rotation = Quaternion.LookRotation(-face, Vector3.up);
                    go.transform.localScale = Vector3.one * S;
                    var box = go.AddComponent<BoxCollider>();
                    box.size = new Vector3(0.9f, 1f, 0.08f);
                    var climb = go.AddComponent<ClimbModifierSurface>();
                    climb.staminaUsageMultiplier = Balance.F(Balance.Section("building"), "ladderStaminaMultiplier", 0.5f);
                    climb.speedMultiplier = 1.3f;
                    break;
                }
            }
            pb.normalMat = pb.mr.sharedMaterial;
            return pb;
        }

        private static Texture2D Atlas(McItemDef def)
        {
            if (atlases.TryGetValue(def.Key, out var a) && a != null) return a;
            a = Meshes.BlockAtlas(McAssets.Tex(def.Side), McAssets.Tex(def.Top ?? def.Side), McAssets.Tex(def.Bottom ?? def.Top ?? def.Side));
            atlases[def.Key] = a;
            return a;
        }

        private static Material MatFor(string key, Texture2D atlas)
        {
            if (mats.TryGetValue(key, out var m) && m != null && m.mainTexture == atlas) return m;
            m = Mat.For(atlas);
            mats[key] = m;
            return m;
        }

        public void StartFuse(float seconds)
        {
            if (fuseSound != null) return;
            fuseSound = Sfx.Loop("random/fuse", transform, 0.8f);
            if (fuseSound != null) fuseSound.loop = false;
            StartCoroutine(Flash(seconds));
        }

        private IEnumerator Flash(float seconds)
        {
            if (flashMat == null) flashMat = Mat.Glowing(Texture2D.whiteTexture, Color.white);
            float end = Time.time + seconds;
            bool on = false;
            while (Time.time < end + 2f && this != null)
            {
                on = !on;
                mr.sharedMaterial = on ? flashMat : normalMat;
                float left = end - Time.time;
                transform.localScale = Vector3.one * BlockWorld.Size * (left < 0.4f ? 1.08f : 1f);
                yield return new WaitForSeconds(0.25f);
            }
        }

        // ---------------------------------------------------------------- IInteractibleConstant

        public bool holdOnFinish => false;
        public bool IsInteractible(Character interactor) => Record != null && !Record.Lit;
        public void Interact(Character interactor) { }
        public void HoverEnter() { }
        public void HoverExit() { }
        public Vector3 Center() => transform.position + transform.rotation * (Vector3.up * 0.5f * BlockWorld.Size);
        public Transform GetTransform() => transform;
        public string GetInteractionText() => Def != null && Def.Kind == McKind.Tnt ? "light" : "break";
        public string GetName() => Def != null ? Def.Name : "Block";
        public bool IsConstantlyInteractable(Character interactor) => IsInteractible(interactor);

        public float GetInteractTime(Character interactor)
        {
            if (Def == null) return 1f;
            switch (Def.Kind)
            {
                case McKind.Tnt: return 0.4f;
                case McKind.Torch:
                case McKind.RedstoneTorch: return 0.25f;
                case McKind.Ladder: return 0.5f;
                default:
                    float t = Balance.F(Balance.Section("building"), "breakSeconds", 1f);
                    if (Def.Key == "sand" || Def.Key == "moss_block") t *= 0.6f;
                    if (Def.Key == "deepslate" || Def.Key == "basalt") t *= 1.4f;
                    return t;
            }
        }

        public void Interact_CastFinished(Character interactor)
        {
            if (Record == null) return;
            if (Def != null && Def.Kind == McKind.Tnt) BlockWorld.RequestIgnite(Record.Id);
            else BlockWorld.RequestBreak(Record.Id);
        }

        public void CancelCast(Character interactor) { }
        public void ReleaseInteract(Character interactor) { }
    }

    /// <summary>TNT explosions: Minecraft sound, PEAK's dynamite blast effect, damage and knockback for the local scout.</summary>
    public static class Explosions
    {
        private static GameObject blastPrefab;
        private static bool searched;

        public static void OnExplosion(Vector3 at, float radius, float injury)
        {
            Sfx.At("random/explode", at, 1f, 1f, 80f);
            SpawnBlastVisual(at);
            var c = Game.LocalChar;
            if (c == null || c.data.dead) return;
            float reach = radius * 1.6f;
            float d = Vector3.Distance(c.Center, at);
            if (d > reach) return;
            float k = 1f - d / reach;
            c.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, injury * k * k);
            Vector3 dir = (c.Center - at).normalized + Vector3.up * 0.6f;
            c.AddForce(dir.normalized * (14f * k / Time.fixedDeltaTime), 0.9f, 1.1f);
            if (k > 0.45f) c.Fall(1.5f * k);
        }

        private static void SpawnBlastVisual(Vector3 at)
        {
            try
            {
                if (!searched)
                {
                    searched = true;
                    var db = Game.ItemDb;
                    if (db != null)
                        foreach (var it in db.itemLookup.Values)
                        {
                            var dyn = it != null ? it.GetComponent<Dynamite>() : null;
                            if (dyn != null && dyn.explosionPrefab != null) { blastPrefab = dyn.explosionPrefab; break; }
                        }
                }
                if (blastPrefab != null)
                {
                    var holder = new GameObject("bp_blast_holder");
                    holder.SetActive(false);
                    var e = Object.Instantiate(blastPrefab, at, Quaternion.identity, holder.transform);
                    foreach (var aoe in e.GetComponentsInChildren<AOE>(true)) Object.DestroyImmediate(aoe);
                    e.transform.SetParent(null, true);
                    Object.Destroy(holder);
                    Object.Destroy(e, 12f);
                    return;
                }
            }
            catch (System.Exception ex) { Health.Report("explosion-fx", ex); }
            Fx.Burst(at, new[] { new Color(0.3f, 0.3f, 0.3f), new Color(0.6f, 0.6f, 0.6f), new Color(1f, 0.6f, 0.1f) }, 50, 7f, 1.5f);
        }
    }
}
