using System;
using System.Collections.Generic;
using System.Linq;
using BlockPeak.Assets;
using BlockPeak.Building;
using BlockPeak.Core;
using BlockPeak.Items;
using BlockPeak.Net;
using BlockPeak.UI;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using UnityEngine;

namespace BlockPeak.Mobs
{
    /// <summary>
    /// Night mobs. The host spawns them, runs their simple AI and decides every hit; everyone else just draws
    /// them from 10 updates a second. Mobs are not Photon objects, so they cost almost nothing on the network.
    /// </summary>
    public static class McMobs
    {
        private static readonly string[] TypeIndex = { "zombie", "husk", "drowned", "skeleton", "stray", "bogged", "spider", "creeper", "slime", "magma_cube" };

        private class Mob
        {
            public int Id;
            public string Type;
            public JToken Cfg;
            public Vector3 Pos, TargetPos;   // TargetPos: interpolation target on clients
            public float Yaw, TargetYaw;
            public float Hp, MaxHp;
            public float VertVel;
            public float AttackCd, ShootCd;
            public float FleeUntil;
            public Vector3 FleeFrom;
            public float FarTime, StuckTime;
            public float Fuse = -1f;
            public float BurnT;
            public float VanishAt = -1f;
            public float HopCd;
            public float NextSound;
            public bool Moving, Burning, Fusing, Dying;
            public float HurtFlash;
            public Vector3 Knock;
            public MobView View;
        }

        private static readonly Dictionary<int, Mob> mobs = new Dictionary<int, Mob>();
        private static int nextId = 1;
        private static float nextSpawn, nextSync;
        private static GameObject root;
        private static bool wasNight;

        private static JToken M => Balance.Section("mobs");
        public static int Count => mobs.Count;

        public static void RegisterNet()
        {
            Channel.On(Op.MobSpawn, (a, s) =>
            {
                if (IsHost) return;
                int id = Channel.Int(a[0]);
                if (!mobs.ContainsKey(id)) Create(id, Channel.Str(a[1]), Channel.Vec(a[2]), Channel.Flt(a[3]));
            });
            Channel.On(Op.MobStates, OnStates);
            Channel.On(Op.MobRemove, (a, s) => RemoveLocal(Channel.Int(a[0]), Channel.Byte(a[1])));
            Channel.On(Op.MobHitReq, (a, s) => HostHit(Channel.Int(a[0]), Channel.Flt(a[1]), Channel.Vec(a[2])));
            Channel.On(Op.MobHurt, (a, s) =>
            {
                if (mobs.TryGetValue(Channel.Int(a[0]), out var m)) Hurt(m);
            });
            Channel.On(Op.HurtPlayer, OnHurtMe);
        }

        private static bool IsHost => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;

        public static void Clear()
        {
            foreach (var m in mobs.Values) if (m.View != null) UnityEngine.Object.Destroy(m.View.gameObject);
            mobs.Clear();
        }

        // =============================================================== frame update (everyone)

        public static void Tick()
        {
            if (!Game.InRun) { if (mobs.Count > 0) Clear(); return; }
            float dt = Time.deltaTime;
            if (IsHost) TickHost(dt);
            else
            {
                foreach (var m in mobs.Values)
                {
                    m.Pos = Vector3.Lerp(m.Pos, m.TargetPos, 1f - Mathf.Exp(-12f * dt));
                    m.Yaw = Mathf.LerpAngle(m.Yaw, m.TargetYaw, 1f - Mathf.Exp(-12f * dt));
                }
            }
            foreach (var m in mobs.Values)
            {
                if (m.View == null) continue;
                m.View.Apply(m.Pos, m.Yaw, m.Moving, m.Burning, m.Fusing, dt);
            }
        }

        // =============================================================== host

        private static void TickHost(float dt)
        {
            bool night = Game.IsNight;
            if (night != wasNight) { wasNight = night; if (!night) Dawn(); }

            if (Balance.B(M, "enabled", true) && night && Time.time >= nextSpawn)
            {
                nextSpawn = Time.time + Mathf.Max(3f, Balance.F(M, "spawnIntervalSeconds", 20f));
                TrySpawnRound();
            }

            var players = Character.AllCharacters.Where(c => c != null && !c.isBot && !c.data.dead).ToList();
            foreach (var m in mobs.Values.ToList())
            {
                if (m.Dying) continue;
                Think(m, players, dt, night);
            }

            if (Time.time >= nextSync && mobs.Count > 0)
            {
                nextSync = Time.time + 0.1f;
                var list = mobs.Values.Where(x => !x.Dying).ToList();
                Channel.Others(Op.MobStates, false,
                    list.Select(x => x.Id).ToArray(),
                    list.Select(x => (byte)Array.IndexOf(TypeIndex, x.Type)).ToArray(),
                    list.SelectMany(x => new[] { x.Pos.x, x.Pos.y, x.Pos.z, x.Yaw }).ToArray(),
                    list.Select(x => (byte)((x.Moving ? 1 : 0) | (x.Burning ? 2 : 0) | (x.Fusing ? 4 : 0))).ToArray());
            }
        }

        private static void TrySpawnRound()
        {
            if (!Game.InRun) return;
            float chance = Balance.F(M, "spawnChance", 0.35f);
            int maxPer = Balance.I(M, "maxPerPlayer", 2);
            var players = Character.AllCharacters.Where(c => c != null && !c.isBot && !c.data.dead && !c.data.passedOut).ToList();
            if (players.Count == 0 || mobs.Count >= maxPer * players.Count) return;
            string biome = Game.CurrentBiome.ToString();
            var types = (M["biomes"]?[biome] as JArray)?.Select(t => (string)t).ToList();
            if (types == null || types.Count == 0) return;

            foreach (var p in players)
            {
                if (mobs.Count >= maxPer * players.Count) break;
                int near = mobs.Values.Count(m => (m.Pos - p.Center).sqrMagnitude < 40f * 40f);
                if (near >= maxPer || UnityEngine.Random.value > chance) continue;
                string type = PickType(types);
                if (type == null) continue;
                if (FindSpawnPoint(p, out var pos))
                {
                    int id = nextId++;
                    float yaw = Quaternion.LookRotation(Flat(p.Center - pos)).eulerAngles.y;
                    var m = Create(id, type, pos, yaw);
                    Channel.Others(Op.MobSpawn, true, id, type, pos, yaw);
                    Health.Verbose($"Spawned {type} near {p.name} at {pos}");
                }
            }
        }

        private static string PickType(List<string> types)
        {
            var options = new List<string>();
            string creepers = Balance.S(M, "creepers", "Rare");
            foreach (var t in types)
            {
                if (t == "creeper")
                {
                    if (creepers == "Off") continue;
                    if (creepers == "Rare" && UnityEngine.Random.value > Balance.F(M["types"]?[t], "rare", 0.25f)) continue;
                }
                options.Add(t);
            }
            return options.Count == 0 ? null : options[UnityEngine.Random.Range(0, options.Count)];
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude < 1e-4f ? Vector3.forward : v; }

        private static bool FindSpawnPoint(Character p, out Vector3 pos)
        {
            float min = Balance.F(M, "minDistance", 16f), max = Balance.F(M, "maxDistance", 32f);
            float vert = Balance.F(M, "noSpawnVerticalFromClimber", 8f);
            float noLight = Balance.F(M, "noSpawnNearLightRadius", 12f);
            for (int i = 0; i < 12; i++)
            {
                float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f), dist = UnityEngine.Random.Range(min, max);
                Vector3 probe = p.Center + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * dist;
                if (!Physics.Raycast(probe + Vector3.up * 25f, Vector3.down, out var hit, 60f, Game.TerrainMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.6f) continue;                       // too steep to stand
                if (Mathf.Abs(hit.point.y - p.Center.y) > vert) continue;  // keep near the scout's height
                if (BlockWorld.LightNear(hit.point, noLight)) continue;     // torches keep mobs away
                if (NearCampfire(hit.point, noLight)) continue;
                if (Character.AllCharacters.Any(c => c != null && (c.Center - hit.point).sqrMagnitude < min * min * 0.8f)) continue;
                pos = hit.point;
                return true;
            }
            pos = default;
            return false;
        }

        private static bool NearCampfire(Vector3 p, float r)
        {
            try
            {
                foreach (var c in Campfire.ALL_CAMPFIRES)
                    if (c != null && (c.transform.position - p).sqrMagnitude < r * r) return true;
            }
            catch { }
            return false;
        }

        private static void Think(Mob m, List<Character> players, float dt, bool night)
        {
            var cfg = m.Cfg;
            m.AttackCd -= dt;
            m.ShootCd -= dt;
            m.HopCd -= dt;
            m.HurtFlash -= dt;

            // Daylight rules
            if (!night)
            {
                if (Balance.B(cfg, "burnsAtDawn", false))
                {
                    m.Burning = true;
                    m.BurnT += dt;
                    if (m.BurnT > 1f) { m.BurnT = 0; Damage(m, 1f, Vector3.zero); if (m.Dying) return; }
                }
                if (Balance.B(cfg, "vanishAtDawn", false))
                {
                    if (m.VanishAt < 0) m.VanishAt = Time.time + UnityEngine.Random.Range(2f, 6f);
                    if (Time.time > m.VanishAt) { Despawn(m, 0); return; }
                }
            }

            // Target
            Character target = null;
            float best = 40f * 40f;
            bool neutral = !night && Balance.B(cfg, "neutralAtDawn", false);
            if (!neutral)
                foreach (var c in players)
                {
                    if (c.data.passedOut || c.data.fullyPassedOut) continue;
                    float d = (c.Center - m.Pos).sqrMagnitude;
                    if (d < best) { best = d; target = c; }
                }

            // Despawn when nobody is around
            float nearest = players.Count == 0 ? 999f : players.Min(c => Vector3.Distance(c.Center, m.Pos));
            if (nearest > Balance.F(M, "despawnDistance", 60f)) m.FarTime += dt; else m.FarTime = 0;
            if (m.FarTime > Balance.F(M, "despawnAfterSeconds", 20f) || m.Pos.y < -500f) { Despawn(m, 0); return; }
            if (neutral && nearest > 24f) { m.FarTime += dt * 3f; }

            Vector3 want = Vector3.zero;
            float speed = Balance.F(cfg, "speed", 2.4f);
            bool fleeing = Time.time < m.FleeUntil;
            if (fleeing) want = Flat(m.Pos - m.FleeFrom).normalized;
            else if (target != null)
            {
                Vector3 to = target.Center - m.Pos;
                float flat = Flat(to).magnitude;
                bool ranged = Balance.B(cfg, "ranged", false);
                if (Balance.B(cfg, "explodes", false)) { CreeperLogic(m, target, flat, Mathf.Abs(to.y), dt); if (m.Dying) return; }
                if (ranged && flat < 16f && flat > 5f && LineOfSight(m, target))
                {
                    want = Vector3.zero; // stand and shoot
                    if (m.ShootCd <= 0f) { m.ShootCd = UnityEngine.Random.Range(2.2f, 3.2f); Shoot(m, target); }
                    m.Yaw = Quaternion.LookRotation(Flat(to)).eulerAngles.y;
                }
                else if (flat > 1.1f) want = Flat(to).normalized;
                if (m.Fusing) want = Vector3.zero;

                if (!ranged || flat < 2.5f)
                {
                    float reach = m.Type == "slime" || m.Type == "magma_cube" ? 1.3f : 1.5f;
                    if (flat < reach && Mathf.Abs(to.y) < 1.8f && m.AttackCd <= 0f && !Balance.B(cfg, "explodes", false))
                    {
                        m.AttackCd = 1.2f;
                        HostHurtPlayer(target, m.Type, false, m.Pos);
                        MobSound(m, "attack");
                    }
                }
            }
            else want = Vector3.zero;

            // Move: walk along the ground, hop small steps, spiders climb walls.
            m.Moving = want.sqrMagnitude > 0.01f;
            Vector3 step = want * speed * (fleeing ? 1.3f : 1f) * dt + m.Knock * dt;
            m.Knock = Vector3.Lerp(m.Knock, Vector3.zero, 6f * dt);
            if (m.Type == "slime" || m.Type == "magma_cube")
            {
                // Slimes hop instead of walking.
                if (m.Moving && m.HopCd <= 0f && m.VertVel <= 0f && Grounded(m))
                {
                    m.HopCd = UnityEngine.Random.Range(0.8f, 1.6f);
                    m.VertVel = m.Type == "magma_cube" ? 7f : 5f;
                    MobSound(m, "jump");
                }
                if (Grounded(m) && m.VertVel <= 0f) step = m.Knock * dt;
            }
            Vector3 next = m.Pos + step;
            if (m.Moving)
            {
                m.Yaw = Mathf.MoveTowardsAngle(m.Yaw, Quaternion.LookRotation(want).eulerAngles.y, 360f * dt);
                Vector3 knee = m.Pos + Vector3.up * 0.4f;
                if (Physics.Raycast(knee, want, out var wall, 0.6f, Game.TerrainMask, QueryTriggerInteraction.Ignore) && wall.normal.y < 0.5f)
                {
                    if (Balance.B(cfg, "climbs", false)) { next = m.Pos + Vector3.up * speed * dt; m.VertVel = 0f; }
                    else if (Grounded(m) && m.HopCd <= 0f) { m.VertVel = 6f; m.HopCd = 0.6f; next = m.Pos + want * 0.05f; }
                    else next = m.Pos;
                    m.StuckTime += dt;
                }
                else m.StuckTime = Mathf.Max(0, m.StuckTime - dt);
            }
            if (m.StuckTime > 10f) { Despawn(m, 0); return; }

            // Gravity and ground snap.
            m.VertVel -= 20f * dt;
            next.y += m.VertVel * dt;
            if (Physics.Raycast(new Vector3(next.x, Mathf.Max(next.y, m.Pos.y) + 1.2f, next.z), Vector3.down, out var ground, 3.5f, Game.TerrainMask, QueryTriggerInteraction.Ignore))
            {
                if (next.y <= ground.point.y + 0.02f)
                {
                    next.y = ground.point.y;
                    if (m.VertVel < 0) m.VertVel = 0;
                }
            }
            m.Pos = next;

            if (Time.time > m.NextSound)
            {
                m.NextSound = Time.time + UnityEngine.Random.Range(6f, 14f);
                MobSound(m, "idle");
            }
        }

        private static bool Grounded(Mob m) => Physics.Raycast(m.Pos + Vector3.up * 0.2f, Vector3.down, 0.35f, Game.TerrainMask, QueryTriggerInteraction.Ignore);

        private static bool LineOfSight(Mob m, Character c)
        {
            Vector3 eye = m.Pos + Vector3.up * 1.5f;
            return !Physics.Linecast(eye, c.Head, Game.TerrainMask, QueryTriggerInteraction.Ignore);
        }

        private static void CreeperLogic(Mob m, Character target, float flat, float dy, float dt)
        {
            if (!m.Fusing && flat < 2.4f && dy < 2f)
            {
                m.Fusing = true;
                m.Fuse = 1.5f;
                Channel.All(Op.Sound, false, "random/fuse", m.Pos, 1f);
            }
            if (!m.Fusing) return;
            if (flat > 6f) { m.Fusing = false; m.Fuse = -1; return; }
            m.Fuse -= dt;
            if (m.Fuse > 0) return;
            float radius = 3f;
            float injury = Balance.F(m.Cfg, "amount", 0.2f) * 1.8f;
            Vector3 at = m.Pos + Vector3.up * 0.8f;
            Despawn(m, 2);
            Channel.All(Op.Explosion, true, at, radius, injury);
            BlockWorld.HostExplosionAftermath(at, radius);
        }

        private static void Shoot(Mob m, Character target)
        {
            Vector3 from = m.Pos + Vector3.up * 1.4f + Quaternion.Euler(0, m.Yaw, 0) * Vector3.forward * 0.5f;
            Vector3 aim = target.Center + UnityEngine.Random.insideUnitSphere * 0.6f;
            Vector3 d = aim - from;
            float speed = 22f;
            float t = Flat(d).magnitude / speed;
            Vector3 v = Flat(d).normalized * speed;
            v.y = d.y / Mathf.Max(0.05f, t) + 0.5f * 16f * t;
            Projectiles.FireArrow(from, v, m.Type);
        }

        private static void Dawn()
        {
            foreach (var m in mobs.Values) { m.BurnT = 0; m.VanishAt = -1; }
        }

        // =============================================================== hurting players

        /// <summary>Host: a mob (or its arrow) hit this scout. The scout's own game applies the status.</summary>
        public static void HostHurtPlayer(Character victim, string mobType, bool ranged, Vector3? from = null)
        {
            if (victim == null || victim.photonView == null || victim.photonView.Owner == null) return;
            var cfg = M["types"]?[mobType ?? "skeleton"];
            string status = Balance.S(cfg, "status", "Injury");
            float amount = Balance.F(cfg, "amount", 0.05f);
            string extra = Balance.S(cfg, "extraStatus", "");
            float extraAmount = Balance.F(cfg, "extraAmount", 0f);
            float knock = Balance.F(cfg, "knockback", ranged ? 2f : 3.5f);
            Vector3 dir = from.HasValue ? Flat(victim.Center - from.Value).normalized : Vector3.zero;
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
            if (knock.sqrMagnitude > 0.01f) c.AddForce((knock + Vector3.up * knock.magnitude * 0.4f) / Time.fixedDeltaTime, 0.9f, 1.1f);
            Sfx.At("damage/hit", c.Head, 0.8f);
        }

        // =============================================================== hurting mobs

        /// <summary>Local player swings (sword or fist). True if a mob was in reach.</summary>
        public static bool LocalMelee(Vector3 origin, Vector3 dir, float reach, float damage)
        {
            Mob best = null;
            float bestD = reach + 0.01f;
            foreach (var m in mobs.Values)
            {
                if (m.Dying || m.View == null) continue;
                float h = m.View.Model.Height;
                Vector3 center = m.Pos + Vector3.up * h * 0.5f;
                Vector3 to = center - origin;
                float along = Vector3.Dot(to, dir);
                if (along < 0 || along > reach + m.View.Model.Width) continue;
                float off = Vector3.Distance(origin + dir * along, center);
                if (off > Mathf.Max(0.6f, h * 0.5f)) continue;
                if (along < bestD) { bestD = along; best = m; }
            }
            if (best == null) return false;
            Channel.Host(Op.MobHitReq, best.Id, damage, dir);
            return true;
        }

        private static void HostHit(int id, float dmg, Vector3 dir)
        {
            if (!IsHost || !mobs.TryGetValue(id, out var m) || m.Dying) return;
            Damage(m, dmg, Flat(dir).normalized * 7f);
            m.VertVel = Mathf.Max(m.VertVel, 3f);
        }

        private static void Damage(Mob m, float dmg, Vector3 knock)
        {
            m.Hp -= dmg;
            m.Knock += knock;
            Channel.All(Op.MobHurt, false, m.Id);
            if (m.Hp <= 0) Kill(m);
        }

        private static void Hurt(Mob m)
        {
            if (m.View != null) m.View.Flash();
            MobSound(m, "hurt");
        }

        private static void Kill(Mob m)
        {
            Drop(m);
            Despawn(m, 1);
        }

        private static void Drop(Mob m)
        {
            string key = null;
            if ((m.Type == "zombie" || m.Type == "husk" || m.Type == "drowned") && UnityEngine.Random.value < 0.6f) key = "rotten_flesh";
            if (m.Type == "creeper" && UnityEngine.Random.value < 0.15f) key = "tnt";
            var def = ItemDefs.ByKey(key);
            var t = def != null ? Loot.TemplateFor(def) : null;
            if (t == null || !PhotonNetwork.InRoom) return;
            try
            {
                var go = PhotonNetwork.InstantiateItemRoom(t.name, m.Pos + Vector3.up * 0.5f, Quaternion.identity);
                var item = go.GetComponent<Item>();
                item.GetData<BoolItemData>(Stacks.RolledKey);
                if (def.Stack > 1) Stacks.SetCount(item.data, 1, def.Stack);
                Stacks.MarkRolled(item.data);
                item.photonView.RPC("SetKinematicRPC", RpcTarget.AllBuffered, false, go.transform.position, go.transform.rotation);
            }
            catch (Exception e) { Health.Report("mob-drop", e); }
        }

        /// <summary>Host: TNT/creeper blast hurts mobs.</summary>
        public static void HostExplosion(Vector3 at, float radius)
        {
            if (!IsHost) return;
            foreach (var m in mobs.Values.ToList())
            {
                if (m.Dying) continue;
                float d = Vector3.Distance(m.Pos, at);
                if (d > radius * 1.6f) continue;
                Damage(m, 20f * (1f - d / (radius * 1.6f)), Flat(m.Pos - at).normalized * 12f);
            }
        }

        /// <summary>Host: wind charge pushes mobs.</summary>
        public static void HostPush(Vector3 at, float radius, float launch)
        {
            foreach (var m in mobs.Values)
            {
                float d = Vector3.Distance(m.Pos, at);
                if (d > radius) continue;
                m.Knock += Flat(m.Pos - at).normalized * launch;
                m.VertVel = Mathf.Max(m.VertVel, launch * 0.6f);
            }
        }

        /// <summary>Goat horn: mobs near the sound run away for a while.</summary>
        public static void Scare(Vector3 at, float radius, float seconds)
        {
            foreach (var m in mobs.Values)
            {
                if ((m.Pos - at).sqrMagnitude > radius * radius) continue;
                m.FleeUntil = Time.time + seconds;
                m.FleeFrom = at;
                m.Fusing = false;
            }
        }

        public static bool AnyMobNear(Vector3 p, float r)
        {
            foreach (var m in mobs.Values)
            {
                float h = m.View != null ? m.View.Model.Height : 1.8f;
                if (Projectiles.DistToSegment(p, m.Pos, m.Pos + Vector3.up * h) < r) return true;
            }
            return false;
        }

        // =============================================================== shared

        private static Mob Create(int id, string type, Vector3 pos, float yaw)
        {
            if (root == null) root = new GameObject("BlockPeak.Mobs");
            var cfg = M["types"]?[type] ?? new JObject();
            var m = new Mob
            {
                Id = id, Type = type, Cfg = cfg, Pos = pos, TargetPos = pos, Yaw = yaw, TargetYaw = yaw,
                MaxHp = Balance.F(cfg, "health", 10f), NextSound = Time.time + UnityEngine.Random.Range(1f, 5f),
            };
            m.Hp = m.MaxHp;
            var go = new GameObject("mob " + type + " #" + id);
            go.transform.SetParent(root.transform, false);
            m.View = go.AddComponent<MobView>();
            m.View.Init(type);
            m.View.Apply(pos, yaw, false, false, false, 0f);
            mobs[id] = m;
            nextId = Math.Max(nextId, id + 1);
            MobSound(m, "idle");
            return m;
        }

        /// <summary>reason: 0 = vanish quietly (poof), 1 = died, 2 = exploded.</summary>
        private static void Despawn(Mob m, byte reason)
        {
            m.Dying = true;
            Channel.All(Op.MobRemove, true, m.Id, reason);
        }

        private static void RemoveLocal(int id, byte reason)
        {
            if (!mobs.TryGetValue(id, out var m)) return;
            mobs.Remove(id);
            if (reason == 1) MobSound(m, "death");
            if (m.View != null) m.View.Die(reason);
        }

        private static void OnStates(object[] a, int sender)
        {
            if (IsHost) return;
            var ids = (int[])a[0];
            var types = (byte[])a[1];
            var data = (float[])a[2];
            var flags = (byte[])a[3];
            var seen = new HashSet<int>();
            for (int i = 0; i < ids.Length; i++)
            {
                seen.Add(ids[i]);
                var pos = new Vector3(data[i * 4], data[i * 4 + 1], data[i * 4 + 2]);
                float yaw = data[i * 4 + 3];
                if (!mobs.TryGetValue(ids[i], out var m))
                {
                    string type = types[i] < TypeIndex.Length ? TypeIndex[types[i]] : "zombie";
                    m = Create(ids[i], type, pos, yaw);
                }
                m.TargetPos = pos;
                m.TargetYaw = yaw;
                m.Moving = (flags[i] & 1) != 0;
                m.Burning = (flags[i] & 2) != 0;
                m.Fusing = (flags[i] & 4) != 0;
            }
            // Anything the host no longer lists has gone (missed remove message).
            foreach (var id in mobs.Keys.Where(k => !seen.Contains(k)).ToList()) RemoveLocal(id, 0);
        }

        private static void MobSound(Mob m, string kind)
        {
            string t = m.Type == "magma_cube" ? "magmacube" : m.Type;
            string b = "mob/" + t + "/";
            AudioClip clip = null;
            switch (kind)
            {
                case "idle": clip = McAssets.FirstSound(b + "say", b + "idle", b + "ambient", b + "big"); break;
                case "hurt": clip = McAssets.FirstSound(b + "hurt", b + "small", b + "say", b + "big"); break;
                case "death": clip = McAssets.FirstSound(b + "death", b + "big"); break;
                case "attack": clip = McAssets.FirstSound(b + "attack", b + "say"); break;
                case "jump": clip = McAssets.FirstSound(b + "jump", b + "small"); break;
            }
            if (clip == null && m.Type.Contains("zombie") == false && (kind == "hurt" || kind == "death")) clip = McAssets.Sound("mob/zombie/hurt");
            Sfx.Clip(clip, m.Pos + Vector3.up, kind == "idle" ? 0.6f : 0.9f);
        }

        // =============================================================== punching with empty hands

        private static float lastPunch;

        public static void TickPunch()
        {
            var c = Game.LocalChar;
            if (c == null || c.data.currentItem != null || !c.input.usePrimaryWasPressed || !Game.CanAct(c)) return;
            if (Time.time - lastPunch < 0.5f || mobs.Count == 0) return;
            lastPunch = Time.time;
            if (LocalMelee(Game.CamPos, Game.CamForward, 2.6f, Balance.F(M, "punchDamage", 2f)))
                Sfx.At("entity/player/attack/weak", c.Head, 0.7f);
        }
    }
}
