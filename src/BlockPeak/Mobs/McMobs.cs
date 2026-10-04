using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Core;
using BlockPeak.Net;
using BlockPeak.UI;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using Unity.Collections;
using UnityEngine;

namespace BlockPeak.Mobs
{
    /// <summary>
    /// The Zombie Chase horde: hundreds of lightweight zombies. Built for speed:
    /// - no GameObjects per zombie: drawn with GPU instancing (one draw call per pose, 8 walk poses);
    /// - the host moves them with batched ground raycasts (RaycastCommand jobs) and thinks for each zombie
    ///   only a few times a second, staggered over frames; zombies far from everyone think even less;
    /// - a spatial grid keeps them from stacking into one blob;
    /// - the network carries a compact 10-bytes-per-zombie update 8 times a second; players interpolate.
    /// Also owns the shared "a mob hurt this scout" message used by every mob.
    /// </summary>
    public static class McMobs
    {
        private static readonly string[] TypeIndex = { "zombie", "husk", "drowned" };

        private class Mob
        {
            public ushort Id;
            public byte Type;
            public Vector3 Pos, TargetPos;
            public float Yaw, TargetYaw;
            public float VertVel;
            public float Hp;
            public float Phase;
            public bool Moving;
            public float HurtUntil;
            public float AttackCd, ThinkAt, WallAt;
            public Vector3 Dir;
            public bool Blocked;
            public Character Target;
            public float FleeUntil;
            public Vector3 FleeFrom;
            public Vector3 Knock;
            public float LastSeen;
        }

        private static readonly Dictionary<ushort, Mob> mobs = new Dictionary<ushort, Mob>();
        private static readonly List<Mob> list = new List<Mob>();
        private static ushort nextId = 1;
        private static float nextSync;
        private static Vector3 origin;
        public static bool ForceNight;

        private static JToken M => Balance.Section("mobs");
        private static JToken H => Balance.Section("modes")["zombieChase"] ?? new JObject();
        public static int Count => mobs.Count;
        private static bool IsHost => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;

        public static void RegisterNet()
        {
            Channel.On(Op.MobStates, OnStates);
            Channel.On(Op.MobRemove, (a, s) => RemoveLocal((ushort)Channel.Int(a[0]), Channel.Byte(a[1])));
            Channel.On(Op.MobHitReq, (a, s) => HostHit((ushort)Channel.Int(a[0]), Channel.Flt(a[1]), Channel.Vec(a[2])));
            Channel.On(Op.MobHurt, (a, s) =>
            {
                if (mobs.TryGetValue((ushort)Channel.Int(a[0]), out var m)) { m.HurtUntil = Time.time + 0.3f; HurtSound(m); }
            });
            Channel.On(Op.HurtPlayer, OnHurtMe);
        }

        public static void Clear()
        {
            mobs.Clear();
            list.Clear();
        }

        public static void HostClearAll()
        {
            if (!IsHost) return;
            foreach (var m in list.ToList()) Remove(m, 0, true);
        }

        // ============================================================ spawning (host)

        /// <summary>Host: put a horde of zombies in a ring around the scouts.</summary>
        public static int HostSpawnHorde(int count)
        {
            if (!IsHost) return 0;
            var players = BodyMobs.Players.ToList();
            if (players.Count == 0) return 0;
            float min = Balance.F(H, "spawnMinDistance", 30f), max = Balance.F(H, "spawnMaxDistance", 70f);
            int made = 0;
            for (int i = 0; i < count && mobs.Count < 1000; i++)
            {
                var p = players[i % players.Count];
                for (int tries = 0; tries < 6; tries++)
                {
                    float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f), dist = UnityEngine.Random.Range(min, max);
                    Vector3 probe = p.Center + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * dist;
                    if (!Physics.Raycast(probe + Vector3.up * 60f, Vector3.down, out var hit, 160f, Game.TerrainMask, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.normal.y < 0.5f) continue;
                    byte type = (byte)(UnityEngine.Random.value < 0.15f ? 1 : UnityEngine.Random.value < 0.1f ? 2 : 0);
                    Add(nextId++, type, hit.point, Quaternion.LookRotation(BodyMobs.Flat(p.Center - hit.point)).eulerAngles.y);
                    made++;
                    break;
                }
            }
            return made;
        }

        private static Mob Add(ushort id, byte type, Vector3 pos, float yaw)
        {
            var m = new Mob
            {
                Id = id, Type = type, Pos = pos, TargetPos = pos, Yaw = yaw, TargetYaw = yaw,
                Hp = Balance.F(H, "zombieHealth", 10f), Phase = UnityEngine.Random.value * 6f,
                ThinkAt = Time.time + UnityEngine.Random.value * 0.3f, LastSeen = Time.time,
            };
            mobs[id] = m;
            list.Add(m);
            return m;
        }

        private static void Remove(Mob m, byte reason, bool broadcast)
        {
            if (!mobs.Remove(m.Id)) return;
            list.Remove(m);
            if (broadcast) Channel.All(Op.MobRemove, true, (int)m.Id, reason);
        }

        private static void RemoveLocal(ushort id, byte reason)
        {
            if (!mobs.TryGetValue(id, out var m)) return;
            mobs.Remove(id);
            list.Remove(m);
            if (reason == 1)
            {
                Sfx.At(McAssets.Sound("mob/zombie/death") != null ? "mob/zombie/death" : "random/pop", m.Pos, 0.6f);
                Fx.Burst(m.Pos + Vector3.up, new[] { Color.white, new Color(0.8f, 0.8f, 0.8f) }, 8, 1.5f, 0.7f, false, 0.1f);
            }
        }

        // ============================================================ frame update

        public static void Tick()
        {
            if (!Game.InRun && !(Cfg.Debug && Game.InAirport)) { if (mobs.Count > 0) Clear(); return; }
            if (mobs.Count == 0) return;
            float dt = Time.deltaTime;
            if (IsHost) TickHost(dt);
            else
            {
                float k = 1f - Mathf.Exp(-10f * dt);
                foreach (var m in list)
                {
                    m.Pos = Vector3.Lerp(m.Pos, m.TargetPos, k);
                    m.Yaw = Mathf.LerpAngle(m.Yaw, m.TargetYaw, k);
                    if (m.Moving) m.Phase += dt * 8f;
                }
            }
            Render();
        }

        // ------------------------------------------------------------ host simulation

        private static readonly Dictionary<long, List<Mob>> grid = new Dictionary<long, List<Mob>>();
        private static long Cell(Vector3 p) => ((long)Mathf.FloorToInt(p.x / 2f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 2f);

        private static void TickHost(float dt)
        {
            var players = BodyMobs.Players.Where(p => !p.data.fullyPassedOut).ToList();
            float speed = Balance.F(H, "zombieSpeed", 3.2f);
            float now = Time.time;

            // Spatial grid for separation
            foreach (var l in grid.Values) l.Clear();
            foreach (var m in list)
            {
                long c = Cell(m.Pos);
                if (!grid.TryGetValue(c, out var cell)) grid[c] = cell = new List<Mob>();
                cell.Add(m);
            }

            int n = list.Count;
            var cmds = new NativeArray<RaycastCommand>(n, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(n, Allocator.TempJob);
            var qp = new QueryParameters(Game.TerrainMask, false, QueryTriggerInteraction.Ignore, false);
            try
            {
                for (int i = 0; i < n; i++)
                {
                    var m = list[i];
                    // Think: pick a target and a direction a few times a second (less often when far from everyone).
                    if (now >= m.ThinkAt)
                    {
                        float nearest = float.MaxValue;
                        Character best = null;
                        foreach (var p in players)
                        {
                            float d = (p.Center - m.Pos).sqrMagnitude;
                            if (d < nearest) { nearest = d; best = p; }
                        }
                        m.Target = best;
                        float far = Mathf.Sqrt(nearest);
                        m.ThinkAt = now + (far > 80f ? 1f : far > 30f ? 0.4f : 0.15f) + UnityEngine.Random.value * 0.05f;
                        Vector3 want = Vector3.zero;
                        if (now < m.FleeUntil) want = BodyMobs.Flat(m.Pos - m.FleeFrom);
                        else if (best != null && far > 1.0f) want = BodyMobs.Flat(best.Center - m.Pos);
                        // Separation from neighbours
                        Vector3 push = Vector3.zero;
                        long c0 = Cell(m.Pos);
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                long c = c0 + ((long)dx << 32) + dz;
                                if (!grid.TryGetValue(c, out var cell)) continue;
                                foreach (var o in cell)
                                {
                                    if (o == m) continue;
                                    Vector3 away = m.Pos - o.Pos; away.y = 0;
                                    float d2 = away.sqrMagnitude;
                                    if (d2 < 1.2f && d2 > 1e-4f) push += away / d2;
                                }
                            }
                        m.Dir = (want + push * 0.35f);
                        if (m.Dir.sqrMagnitude > 1f) m.Dir.Normalize();
                        // Wall check (staggered)
                        if (now >= m.WallAt)
                        {
                            m.WallAt = now + 0.3f;
                            m.Blocked = m.Dir.sqrMagnitude > 0.01f && Physics.Raycast(m.Pos + Vector3.up * 0.5f, m.Dir.normalized, out var wall, 0.8f, Game.TerrainMask, QueryTriggerInteraction.Ignore) && wall.normal.y < 0.5f;
                        }
                        // Attack
                        if (best != null && far < 1.5f && Mathf.Abs(best.Center.y - m.Pos.y - 1f) < 1.8f && now >= m.AttackCd)
                        {
                            m.AttackCd = now + 1.2f;
                            HostHurtPlayer(best, "horde_zombie", false, m.Pos);
                        }
                    }

                    m.Moving = m.Dir.sqrMagnitude > 0.01f;
                    if (m.Moving)
                    {
                        m.Yaw = Mathf.MoveTowardsAngle(m.Yaw, Quaternion.LookRotation(new Vector3(m.Dir.x, 0, m.Dir.z)).eulerAngles.y, 300f * dt);
                        if (m.Blocked) { if (m.VertVel <= 0f && grounded(m)) m.VertVel = 6.5f; }
                        else m.Pos += m.Dir * speed * dt;
                        m.Phase += dt * 8f;
                    }
                    m.Pos += m.Knock * dt;
                    m.Knock = Vector3.Lerp(m.Knock, Vector3.zero, 5f * dt);
                    m.VertVel -= 20f * dt;
                    m.Pos.y += m.VertVel * dt;
                    cmds[i] = new RaycastCommand(m.Pos + Vector3.up * 1.6f, Vector3.down, qp, 4f);
                }

                // Ground snap for everyone at once (runs on worker threads).
                RaycastCommand.ScheduleBatch(cmds, hits, 32, 1).Complete();
                for (int i = 0; i < n; i++)
                {
                    var m = list[i];
                    var h = hits[i];
                    if (h.colliderInstanceID != 0 && m.Pos.y <= h.point.y + 0.05f)
                    {
                        m.Pos.y = h.point.y;
                        if (m.VertVel < 0f) m.VertVel = 0f;
                    }
                    m.TargetPos = m.Pos;
                    m.TargetYaw = m.Yaw;
                }
            }
            finally
            {
                cmds.Dispose();
                hits.Dispose();
            }

            // Fell out of the world
            foreach (var m in list.Where(x => x.Pos.y < -500f).ToList()) Remove(m, 0, true);

            // Groans now and then
            if (n > 0 && UnityEngine.Random.value < dt * Mathf.Min(1.5f, n * 0.02f))
            {
                var m = list[UnityEngine.Random.Range(0, n)];
                Channel.All(Op.Sound, false, "mob/zombie/say", m.Pos + Vector3.up, 0.6f);
            }

            if (Time.time >= nextSync)
            {
                nextSync = Time.time + 0.125f;
                SendStates();
            }
        }

        private static bool grounded(Mob m) => m.VertVel == 0f;

        private static void SendStates()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode || PhotonNetwork.PlayerListOthers.Length == 0) return;
            if (list.Count == 0) return;
            origin = list[0].Pos;
            origin = new Vector3(Mathf.Round(origin.x), Mathf.Round(origin.y), Mathf.Round(origin.z));
            const int per = 10;
            int maxPerPacket = 400;
            for (int start = 0; start < list.Count; start += maxPerPacket)
            {
                int count = Mathf.Min(maxPerPacket, list.Count - start);
                var buf = new byte[count * per];
                for (int i = 0; i < count; i++)
                {
                    var m = list[start + i];
                    int o = i * per;
                    buf[o] = (byte)(m.Id & 0xFF); buf[o + 1] = (byte)(m.Id >> 8);
                    WriteShort(buf, o + 2, (m.Pos.x - origin.x) * 20f);
                    WriteShort(buf, o + 4, (m.Pos.y - origin.y) * 20f);
                    WriteShort(buf, o + 6, (m.Pos.z - origin.z) * 20f);
                    buf[o + 8] = (byte)(Mathf.Repeat(m.Yaw, 360f) / 360f * 255f);
                    buf[o + 9] = (byte)((m.Moving ? 1 : 0) | (m.Type << 1) | (start + i + count >= list.Count && i == count - 1 ? 0x80 : 0));
                }
                Channel.Others(Op.MobStates, false, origin, buf, start == 0, start + count >= list.Count);
            }
        }

        private static void WriteShort(byte[] b, int o, float v)
        {
            short s = (short)Mathf.Clamp(Mathf.RoundToInt(v), short.MinValue, short.MaxValue);
            b[o] = (byte)(s & 0xFF); b[o + 1] = (byte)((s >> 8) & 0xFF);
        }

        private static float ReadShort(byte[] b, int o) => (short)(b[o] | (b[o + 1] << 8));

        private static readonly HashSet<ushort> seenThisUpdate = new HashSet<ushort>();

        private static void OnStates(object[] a, int sender)
        {
            if (IsHost) return;
            Vector3 org = Channel.Vec(a[0]);
            var buf = (byte[])a[1];
            bool first = Channel.Bool(a[2]), last = Channel.Bool(a[3]);
            if (first) seenThisUpdate.Clear();
            for (int o = 0; o + 10 <= buf.Length; o += 10)
            {
                ushort id = (ushort)(buf[o] | (buf[o + 1] << 8));
                var pos = org + new Vector3(ReadShort(buf, o + 2), ReadShort(buf, o + 4), ReadShort(buf, o + 6)) / 20f;
                float yaw = buf[o + 8] / 255f * 360f;
                byte flags = buf[o + 9];
                seenThisUpdate.Add(id);
                if (!mobs.TryGetValue(id, out var m)) m = Add(id, (byte)((flags >> 1) & 3), pos, yaw);
                m.TargetPos = pos;
                m.TargetYaw = yaw;
                m.Moving = (flags & 1) != 0;
            }
            if (last)
                foreach (var m in list.Where(x => !seenThisUpdate.Contains(x.Id)).ToList()) { mobs.Remove(m.Id); list.Remove(m); }
        }

        // ------------------------------------------------------------ rendering (GPU instancing)

        private const int Poses = 8;
        private static Mesh[][] poseMeshes;          // [type][pose]
        private static Material[] mats, hurtMats;
        private static readonly List<Matrix4x4>[,] batches = new List<Matrix4x4>[3, Poses * 2];
        private static bool renderFailed;

        private static void BuildPoses()
        {
            poseMeshes = new Mesh[TypeIndex.Length][];
            mats = new Material[TypeIndex.Length];
            hurtMats = new Material[TypeIndex.Length];
            for (int t = 0; t < TypeIndex.Length; t++)
            {
                var holder = new GameObject("horde_pose_builder");
                var model = MobModels.Build(TypeIndex[t], holder.transform);
                poseMeshes[t] = new Mesh[Poses];
                for (int p = 0; p < Poses; p++)
                {
                    float swing = Mathf.Sin(p / (float)Poses * Mathf.PI * 2f) * 35f;
                    if (model.LegR) model.LegR.localRotation = Quaternion.Euler(swing, 0, 0);
                    if (model.LegL) model.LegL.localRotation = Quaternion.Euler(-swing, 0, 0);
                    if (model.ArmR) model.ArmR.localRotation = Quaternion.Euler(-90f + swing * 0.1f, 0, 0);
                    if (model.ArmL) model.ArmL.localRotation = Quaternion.Euler(-90f - swing * 0.1f, 0, 0);
                    var combine = new List<CombineInstance>();
                    var rootInv = holder.transform.worldToLocalMatrix;
                    foreach (var r in model.Renderers)
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        combine.Add(new CombineInstance { mesh = mf.sharedMesh, transform = rootInv * r.transform.localToWorldMatrix });
                    }
                    var mesh = new Mesh { name = "horde_" + TypeIndex[t] + "_" + p };
                    mesh.CombineMeshes(combine.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    poseMeshes[t][p] = mesh;
                }
                var baseMat = model.Renderers.Count > 0 ? model.Renderers[0].sharedMaterial : Mat.For(McAssets.Tex(MobModels.TextureFor(TypeIndex[t])));
                mats[t] = new Material(baseMat) { enableInstancing = true };
                hurtMats[t] = new Material(mats[t]) { enableInstancing = true };
                foreach (var prop in new[] { "_BaseColor", "_Color" }) if (hurtMats[t].HasProperty(prop)) hurtMats[t].SetColor(prop, new Color(1f, 0.35f, 0.35f));
                UnityEngine.Object.Destroy(holder);
            }
            for (int t = 0; t < 3; t++) for (int p = 0; p < Poses * 2; p++) batches[t, p] = new List<Matrix4x4>(256);
        }

        private static void Render()
        {
            if (renderFailed || list.Count == 0) return;
            try
            {
                if (poseMeshes == null) BuildPoses();
                var cam = Game.Cam;
                Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
                float maxD = Balance.F(H, "drawDistance", 150f);
                for (int t = 0; t < 3; t++) for (int p = 0; p < Poses * 2; p++) batches[t, p].Clear();
                float now = Time.time;
                foreach (var m in list)
                {
                    if ((m.Pos - camPos).sqrMagnitude > maxD * maxD) continue;
                    int pose = m.Moving ? Mathf.Abs((int)(m.Phase / (Mathf.PI * 2f) * Poses)) % Poses : 0;
                    int bucket = pose + (now < m.HurtUntil ? Poses : 0);
                    batches[Mathf.Min(m.Type, (byte)2), bucket].Add(Matrix4x4.TRS(m.Pos, Quaternion.Euler(0, m.Yaw, 0), Vector3.one)); // size is baked into the pose meshes
                }
                bool instancing = SystemInfo.supportsInstancing;
                for (int t = 0; t < 3; t++)
                    for (int b = 0; b < Poses * 2; b++)
                    {
                        var mats4 = batches[t, b];
                        if (mats4.Count == 0) continue;
                        var mesh = poseMeshes[t][b % Poses];
                        var mat = b >= Poses ? hurtMats[t] : mats[t];
                        if (instancing)
                        {
                            var bounds = new Bounds(mats4[0].GetColumn(3), Vector3.one * 3f);
                            foreach (var mtx in mats4) bounds.Encapsulate(new Bounds(mtx.GetColumn(3), Vector3.one * 3f));
                            var rp = new RenderParams(mat) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true, worldBounds = bounds };
                            for (int start = 0; start < mats4.Count; start += 1023)
                            {
                                int count = Mathf.Min(1023, mats4.Count - start);
                                Graphics.RenderMeshInstanced(rp, mesh, 0, mats4, count, start);
                            }
                        }
                        else
                            foreach (var mtx in mats4) Graphics.DrawMesh(mesh, mtx, mat, 0);
                    }
            }
            catch (Exception e)
            {
                renderFailed = true;
                Health.Report("horde-render", e);
            }
        }

        // ============================================================ hurting players (shared by every mob)

        /// <summary>Host: a mob (or its arrow) hit this scout. The scout's own game applies the status.</summary>
        public static void HostHurtPlayer(Character victim, string mobType, bool ranged, Vector3? from = null)
        {
            if (victim == null || victim.photonView == null || victim.photonView.Owner == null) return;
            var cfg = M["types"]?[mobType ?? "skeleton"] ?? M["types"]?["zombie"];
            string status = Balance.S(cfg, "status", "Injury");
            float amount = Balance.F(cfg, "amount", 0.05f);
            string extra = Balance.S(cfg, "extraStatus", "");
            float extraAmount = Balance.F(cfg, "extraAmount", 0f);
            float knock = Balance.F(cfg, "knockback", ranged ? 2f : 3.5f);
            Vector3 dir = from.HasValue ? BodyMobs.Flat(victim.Center - from.Value) : Vector3.zero;
            Channel.To(victim.photonView.Owner.ActorNumber, Op.HurtPlayer, true, status, amount, extra, extraAmount, dir * knock, mobType ?? "");
        }

        private static void OnHurtMe(object[] a, int sender)
        {
            var c = Game.LocalChar;
            if (c == null || c.data.dead) return;
            var aff = c.refs.afflictions;
            if (Enum.TryParse(Channel.Str(a[0]), out CharacterAfflictions.STATUSTYPE st)) aff.AddStatus(st, Channel.Flt(a[1]));
            string extra = Channel.Str(a[2]);
            if (!string.IsNullOrEmpty(extra) && Enum.TryParse(extra, out CharacterAfflictions.STATUSTYPE st2)) aff.AddStatus(st2, Channel.Flt(a[3]));
            Vector3 knock = Channel.Vec(a[4]);
            if (knock.sqrMagnitude > 0.01f && !Creative.On) c.AddForce((knock + Vector3.up * knock.magnitude * 0.4f) / Time.fixedDeltaTime, 0.9f, 1.1f);
            Sfx.At("damage/hit", c.Head, 0.8f);
        }

        // ============================================================ hurting the horde

        public static bool LocalMelee(Vector3 origin, Vector3 dir, float reach, float damage)
        {
            Mob best = null;
            float bestD = reach + 0.01f;
            foreach (var m in list)
            {
                Vector3 center = m.Pos + Vector3.up * 0.9f;
                Vector3 to = center - origin;
                float along = Vector3.Dot(to, dir);
                if (along < 0 || along > reach + 0.4f) continue;
                if (Vector3.Distance(origin + dir * along, center) > 0.9f) continue;
                if (along < bestD) { bestD = along; best = m; }
            }
            if (best == null) return false;
            Channel.Host(Op.MobHitReq, (int)best.Id, damage, dir);
            return true;
        }

        private static void HostHit(ushort id, float dmg, Vector3 dir)
        {
            if (!IsHost || !mobs.TryGetValue(id, out var m)) return;
            Damage(m, dmg, BodyMobs.Flat(dir) * 7f);
        }

        private static void Damage(Mob m, float dmg, Vector3 knock)
        {
            m.Hp -= dmg;
            m.Knock += knock;
            m.VertVel = Mathf.Max(m.VertVel, 3f);
            Channel.All(Op.MobHurt, false, (int)m.Id);
            if (m.Hp <= 0f) Remove(m, 1, true);
        }

        private static void HurtSound(Mob m) => Sfx.At("mob/zombie/hurt", m.Pos + Vector3.up, 0.6f);

        public static void HostExplosion(Vector3 at, float radius)
        {
            if (!IsHost) return;
            foreach (var m in list.ToList())
            {
                float d = Vector3.Distance(m.Pos, at);
                if (d > radius * 1.6f) continue;
                Damage(m, 20f * (1f - d / (radius * 1.6f)), BodyMobs.Flat(m.Pos - at) * 12f);
            }
        }

        public static void HostPush(Vector3 at, float radius, float launch)
        {
            foreach (var m in list)
            {
                if (Vector3.Distance(m.Pos, at) > radius) continue;
                m.Knock += BodyMobs.Flat(m.Pos - at) * launch;
                m.VertVel = Mathf.Max(m.VertVel, launch * 0.6f);
            }
        }

        public static void Scare(Vector3 at, float radius, float seconds)
        {
            foreach (var m in list)
            {
                if ((m.Pos - at).sqrMagnitude > radius * radius) continue;
                m.FleeUntil = Time.time + seconds;
                m.FleeFrom = at;
                m.ThinkAt = 0f;
            }
        }

        public static bool AnyMobNear(Vector3 p, float r)
        {
            foreach (var m in list)
                if (Items.Projectiles.DistToSegment(p, m.Pos, m.Pos + Vector3.up * 1.8f) < r) return true;
            return false;
        }

        // ============================================================ punching with empty hands

        private static float lastPunch;

        public static void TickPunch()
        {
            var c = Game.LocalChar;
            if (c == null || c.data.currentItem != null || !c.input.usePrimaryWasPressed || !Game.CanAct(c)) return;
            if (Time.time - lastPunch < 0.5f || (mobs.Count == 0 && BodyMobs.Count == 0)) return;
            lastPunch = Time.time;
            if (MobDirector.LocalMelee(Game.CamPos, Game.CamForward, 2.6f, Balance.F(M, "punchDamage", 2f)))
                Sfx.At("entity/player/attack/weak", c.Head, 0.7f);
        }
    }
}
