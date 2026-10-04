using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Core;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Building
{
    /// <summary>
    /// Ladders climb with PEAK's own rope system: every vertical run of ladders on a wall gets one PEAK rope
    /// (the rope cannon's anchor + rope), hung from the top ladder down along the wall and hidden, so you climb
    /// it with PEAK's rope mechanics while seeing Minecraft ladders. The host owns the ropes.
    /// </summary>
    public static class LadderRopes
    {
        private class Column
        {
            public List<int> Records = new List<int>();
            public GameObject Anchor;
        }

        private static readonly List<Column> columns = new List<Column>();
        private static GameObject anchorPrefab;
        private static bool searched;
        private static float nextHide;
        private static readonly HashSet<int> hidden = new HashSet<int>();

        private static GameObject Prefab()
        {
            if (anchorPrefab != null || searched) return anchorPrefab;
            searched = true;
            try
            {
                var db = Game.ItemDb;
                var shooter = db?.itemLookup.Values.Where(i => i != null).Select(i => i.GetComponent<RopeShooter>())
                    .FirstOrDefault(r => r != null && r.ropeAnchorWithRopePref != null);
                anchorPrefab = shooter?.ropeAnchorWithRopePref;
                if (anchorPrefab == null) Health.Report("ladders", "PEAK's rope anchor was not found; ladders will not be climbable.");
                else Plugin.Log.LogInfo("Ladders use PEAK rope anchor '" + anchorPrefab.name + "'.");
            }
            catch (Exception e) { Health.Report("ladders", e); }
            return anchorPrefab;
        }

        private static bool IsHost => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;

        private static BlockWorld.Record Ladder(Vector3Int cell, byte facing)
        {
            var r = BlockWorld.At(cell);
            return r != null && r.Key == "ladder" && r.Facing == facing ? r : null;
        }

        /// <summary>Host: (re)make the rope for the ladder run that contains this cell.</summary>
        public static void HostRebuild(Vector3Int cell, byte facing)
        {
            if (!IsHost) return;
            try
            {
                DropStale();
                if (Ladder(cell, facing) == null) return;
                var top = cell;
                while (Ladder(top + Vector3Int.up, facing) != null) top += Vector3Int.up;
                var bottom = cell;
                while (Ladder(bottom + Vector3Int.down, facing) != null) bottom += Vector3Int.down;
                var run = new List<BlockWorld.Record>();
                for (var c = bottom; c.y <= top.y; c += Vector3Int.up) run.Add(Ladder(c, facing));
                var ids = new HashSet<int>(run.Select(r => r.Id));
                foreach (var col in columns.Where(col => col.Records.Any(ids.Contains)).ToList()) DestroyColumn(col);

                var prefab = Prefab();
                if (prefab == null) return;
                var topRec = run[run.Count - 1];
                float S = BlockWorld.Size;
                Vector3 face = BlockWorld.FacingDir(facing);
                Vector3 wall = topRec.Surface != Vector3.zero ? topRec.Surface : BlockWorld.CellCenter(topRec.Cell) - face * 0.5f * S;
                Vector3 pos = new Vector3(wall.x, BlockWorld.CellCenter(topRec.Cell).y + 0.45f * S, wall.z) + face * 0.12f;
                var go = PhotonNetwork.InstantiateRoomObject(prefab.name, pos, Quaternion.LookRotation(-face, Vector3.up));
                float segments = Mathf.Clamp(run.Count * S / 0.75f, 2f, Rope.MaxSegments);
                var proj = go.GetComponent<RopeAnchorProjectile>();
                if (proj != null) proj.photonView.RPC("GetShot", RpcTarget.AllBuffered, pos, 0.05f, segments, face);
                else go.GetComponent<RopeAnchorWithRope>()?.SpawnRope();
                columns.Add(new Column { Records = run.Select(r => r.Id).ToList(), Anchor = go });
                nextHide = 0f;
            }
            catch (Exception e) { Health.Report("ladders", e); }
        }

        /// <summary>Host: ropes whose ladders were broken go away.</summary>
        private static void DropStale()
        {
            var alive = new HashSet<int>(BlockWorld.All.Where(r => r.Key == "ladder").Select(r => r.Id));
            foreach (var col in columns.Where(col => col.Anchor == null || col.Records.Any(id => !alive.Contains(id))).ToList()) DestroyColumn(col);
        }

        private static void DestroyColumn(Column col)
        {
            columns.Remove(col);
            if (col.Anchor == null) return;
            try
            {
                var anchor = col.Anchor.GetComponent<RopeAnchor>();
                foreach (var rope in UnityEngine.Object.FindObjectsByType<Rope>(FindObjectsSortMode.None))
                    if (rope != null && rope.attachedToAnchor == anchor && rope.photonView.IsMine) PhotonNetwork.Destroy(rope.gameObject);
                var view = col.Anchor.GetComponent<PhotonView>();
                if (view != null && view.IsMine) PhotonNetwork.Destroy(col.Anchor);
            }
            catch (Exception e) { Health.Report("ladders", e); }
        }

        public static void HostDestroyAll()
        {
            if (!IsHost) return;
            foreach (var col in columns.ToList()) DestroyColumn(col);
        }

        public static void Clear()
        {
            columns.Clear();
            hidden.Clear();
        }

        /// <summary>Everyone: hide the rope and anchor visuals that belong to ladders (the ladder sprites show instead).</summary>
        public static void TickVisuals()
        {
            if (Time.time < nextHide) return;
            nextHide = Time.time + 1f;
            var ladders = BlockWorld.All.Where(r => r.Key == "ladder" && r.View != null).Select(r => r.View.transform.position).ToList();
            if (ladders.Count == 0) return;
            try
            {
                var anchors = UnityEngine.Object.FindObjectsByType<RopeAnchor>(FindObjectsSortMode.None);
                var ropes = UnityEngine.Object.FindObjectsByType<Rope>(FindObjectsSortMode.None);
                foreach (var a in anchors)
                {
                    if (a == null) continue;
                    Vector3 p = a.transform.position;
                    bool ours = ladders.Any(l => Mathf.Abs(l.x - p.x) < 1f && Mathf.Abs(l.z - p.z) < 1f && l.y < p.y + 0.2f && l.y > p.y - 1.5f);
                    if (!ours) continue;
                    HideRenderers(a.gameObject);
                    foreach (var rope in ropes)
                        if (rope != null && rope.attachedToAnchor == a) HideRenderers(rope.gameObject);
                }
            }
            catch (Exception e) { Health.Report("ladder-visuals", e); }
        }

        private static void HideRenderers(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            hidden.Add(go.GetInstanceID());
        }
    }
}
