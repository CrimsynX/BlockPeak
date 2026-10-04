using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Items;
using UnityEngine;

namespace BlockPeak.Building
{
    /// <summary>Minecraft-style thin outline showing where the held block / ladder / torch will go.</summary>
    public static class PlacementPreview
    {
        private static GameObject go;
        private static MeshFilter mf;
        private static MeshRenderer mr;
        private static Mesh cube, flat, small;
        private static Material ok, bad;

        private static void Ensure()
        {
            if (go != null) return;
            go = new GameObject("BlockPeak.PlacePreview");
            Object.DontDestroyOnLoad(go);
            mf = go.AddComponent<MeshFilter>();
            mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            cube = Frame(new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f), 0.012f, true);
            flat = Frame(new Vector3(-0.5f, -0.5f, -0.03f), new Vector3(0.5f, 0.5f, 0.03f), 0.012f, false);
            small = Frame(new Vector3(-0.1f, 0f, -0.1f), new Vector3(0.1f, 0.65f, 0.1f), 0.01f, true);
            ok = Mat.Solid(new Color(0.05f, 0.05f, 0.05f));
            bad = Mat.Glowing(Texture2D.whiteTexture, new Color(0.8f, 0.1f, 0.1f));
            bad = Mat.Solid(new Color(0.85f, 0.15f, 0.15f));
        }

        /// <summary>The 12 edges of a box (or the 4 front edges for a flat frame) as thin bars.</summary>
        private static Mesh Frame(Vector3 min, Vector3 max, float t, bool full)
        {
            var mb = new MeshBuilder();
            var r = new Rect(0, 0, 1, 1);
            var faces = new[] { r, r, r, r, r, r };
            void Bar(Vector3 a, Vector3 b) => mb.Box(Vector3.Min(a, b) - Vector3.one * t, Vector3.Max(a, b) + Vector3.one * t, faces);
            float x0 = min.x, y0 = min.y, z0 = min.z, x1 = max.x, y1 = max.y, z1 = max.z;
            if (!full) { float z = (z0 + z1) / 2f; z0 = z1 = z; }
            Bar(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0));
            Bar(new Vector3(x0, y1, z0), new Vector3(x1, y1, z0));
            Bar(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0));
            Bar(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0));
            if (full)
            {
                Bar(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1));
                Bar(new Vector3(x0, y1, z1), new Vector3(x1, y1, z1));
                Bar(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1));
                Bar(new Vector3(x1, y0, z1), new Vector3(x1, y1, z1));
                Bar(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1));
                Bar(new Vector3(x1, y0, z0), new Vector3(x1, y0, z1));
                Bar(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1));
                Bar(new Vector3(x1, y1, z0), new Vector3(x1, y1, z1));
            }
            return mb.Build("mc_outline");
        }

        public static void Tick()
        {
            var c = Game.LocalChar;
            var def = c != null ? ItemDefs.Of(c.data.currentItem) : null;
            if (def == null || !def.IsPlaceable || UI.TestMenu.Open || UI.ChatBox.Open)
            {
                if (go != null) go.SetActive(false);
                return;
            }
            Ensure();
            var p = BlockWorld.ComputePlacement(def);
            if (p.State == BlockWorld.PlaceState.None) { go.SetActive(false); return; }
            go.SetActive(true);
            mr.sharedMaterial = p.State == BlockWorld.PlaceState.Ok ? ok : bad;
            float S = BlockWorld.Size;
            Vector3 face = BlockWorld.FacingDir(p.Facing);
            switch (def.Kind)
            {
                case McKind.Ladder:
                {
                    mf.sharedMesh = flat;
                    Vector3 wall = new Vector3(p.Surface.x, BlockWorld.CellCenter(p.Cell).y, p.Surface.z);
                    go.transform.position = wall + face * 0.05f;
                    go.transform.rotation = Quaternion.LookRotation(-face, Vector3.up);
                    go.transform.localScale = Vector3.one * S;
                    break;
                }
                case McKind.Torch:
                    mf.sharedMesh = small;
                    go.transform.position = p.Facing == 0 ? p.Surface : p.Surface + face * 0.08f * S - Vector3.up * 0.32f * S;
                    go.transform.rotation = p.Facing == 0 ? Quaternion.identity : Quaternion.AngleAxis(22.5f, Vector3.Cross(Vector3.up, face));
                    go.transform.localScale = Vector3.one * S;
                    break;
                default:
                    mf.sharedMesh = cube;
                    go.transform.position = BlockWorld.CellCenter(p.Cell);
                    go.transform.rotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one * S * 1.004f;
                    break;
            }
        }
    }
}
