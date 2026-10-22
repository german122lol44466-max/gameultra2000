using System.Collections.Generic;
using System.Linq;
using SW.Battle;
using SW.Commander.Net;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>Серверная часть боя: спавн, ИИ, экономика, захват пунктов, победа, рассылка состояния.</summary>
    public partial class BattleNet
    {
        ushort nextUnitId = 1, nextSquadId = 1;
        float snapAt, avatarAt, structAt, objAt, squadAt, econAt, shotsAt;
        readonly List<(ushort shooter, Vector3 from, Vector3 dir, BoltKind kind)> shots = new List<(ushort, Vector3, Vector3, BoltKind)>();
        readonly NetWriter sw = new NetWriter(4096);
        bool serverStarted;
        public ImperialCommander Empire { get; private set; }

        // ================================================================== начало
        void OnMatchBegan()
        {
            MatchStart = Time.time;
            if (!IsServer) return;
            if (serverStarted) return;
            serverStarted = true;
            Soldier.Killed += OnSoldierKilled;
            Bolts.Fired += OnBoltFired;
            Bolts.Exploded += OnExploded;
            Destructible.Destroyed += OnStructureDestroyed;
            foreach (var cp in CommandPost.All) cp.Captured += OnPostCaptured;
            var map = MapInfo.I;
            if (map && map.EmpireBase) map.EmpireBase.Locked = map.CommandCenter && !map.CommandCenter.Dead;
            if (map && map.CommandCenter) map.CommandCenter.Invulnerable = map.ShieldGenerators.Any(g => g && !g.Dead);
            EmpireTickets = new[] { 260, 420, 650 }[Mathf.Clamp(Settings.EmpireStrength, 0, 2)] + Settings.Difficulty * 60;
            Empire = gameObject.AddComponent<ImperialCommander>();
            Empire.Begin(this);
            ShowNoticeAll("Уничтожьте генераторы щита и командный центр Империи", TeamColors.Republic);
        }

        void OnPlayerJoined(PlayerInfo p)
        {
            if (!IsServer) return;
            if (!serverStarted) OnMatchBegan();
            bool fresh = !Players.ContainsKey(p.Id);
            if (fresh)
                Players[p.Id] = new PlayerState { Id = p.Id, Team = p.Team, Requisition = Settings.StartRequisition };
            if (p.Conn >= 0) SendFullState(p);
            var ps = Players[p.Id];
            if (!Soldier.ById.TryGetValue(ps.CommanderId, out var c) || !c || c.Dead) SpawnCommander(p, ps, null);
            if (fresh) SpawnStartingArmy(p, ps);
            SendEconomy(p, ps);
        }

        void OnPlayerLeft(PlayerInfo p)
        {
            if (!IsServer) return;
            if (Players.TryGetValue(p.Id, out var ps) && Soldier.ById.TryGetValue(ps.CommanderId, out var c) && c) Despawn(c);
            foreach (var sq in Squads.Values.Where(s => s.Owner == p.Id).ToList())
            {
                sq.Owner = 255;
                sq.SetOrder(OrderType.Defend, sq.Anchor);
                sq.Dirty = true;
            }
            Players.Remove(p.Id);
        }

        CommandPost BaseOf(Team t)
        {
            var map = MapInfo.I;
            if (map) return t == Team.Republic ? map.RepublicBase : map.EmpireBase;
            return CommandPost.All.FirstOrDefault(c => c.OwnedBy(t));
        }

        void SpawnCommander(PlayerInfo p, PlayerState ps, CommandPost at)
        {
            at ??= BaseOf(p.Team);
            Vector3 pos = at ? at.SpawnPoint(p.Id) : Vector3.zero;
            pos.y = MapInfo.GroundHeight(pos) + 0.1f;
            float yaw = at ? FacingFrom(at) : 0f;
            var s = SpawnUnit(Catalog.I.CommanderIndex(p.Team), p.Team, p.Id, pos, yaw, null, commander: true);
            if (!s) return;
            ps.CommanderId = s.NetId;
        }

        /// <summary>Направление «к фронту» от пункта (в сторону центра карты).</summary>
        static float FacingFrom(CommandPost cp)
        {
            Vector3 d = -cp.transform.position;
            d.y = 0;
            return d.sqrMagnitude < 1f ? 0f : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        void SpawnStartingArmy(PlayerInfo p, PlayerState ps)
        {
            var at = BaseOf(p.Team);
            if (!at) return;
            var cat = Catalog.I;
            string[] names = { "Альфа", "Браво", "Чарли", "Дельта" };
            int[][] comp =
            {
                new[] { Role.Officer, Role.Rifleman, Role.Rifleman, Role.Rifleman, Role.Rifleman, Role.Heavy, Role.Rifleman, Role.Rifleman }.Select(r => (int)r).ToArray(),
                new[] { Role.Rifleman, Role.Rifleman, Role.Rifleman, Role.Rocket, Role.Rifleman, Role.Sniper, Role.Rifleman, Role.Rifleman }.Select(r => (int)r).ToArray(),
            };
            float face = FacingFrom(at);
            for (int k = 0; k < comp.Length; k++)
            {
                var sq = NewSquad(p.Id, p.Team, $"{names[(p.Id + k) % names.Length]}-{p.Id + 1}");
                sq.Color = (byte)((p.Color * 2 + k) % TeamColors.Squad.Length);
                sq.SetFormation(FormationType.Line);
                sq.Stance = Stance.Attention;
                sq.Gait = Gait.March;
                Vector3 basePos = at.SpawnPoint(k * 3 + p.Id) + Quaternion.Euler(0, face, 0) * new Vector3(0, 0, 6f + k * 4f);
                foreach (int role in comp[k])
                {
                    int ti = cat.Units.FindIndex(u => u.Team == p.Team && (int)u.Role == role && u.Recruitable);
                    if (ti < 0) ti = cat.RiflemanIndex(p.Team);
                    var pos = basePos + new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));
                    pos.y = MapInfo.GroundHeight(pos);
                    SpawnUnit(ti, p.Team, p.Id, pos, face, sq);
                }
                sq.ResetAnchor();
                sq.Anchor = basePos;
                sq.Yaw = face;
                sq.SetOrder(OrderType.Hold, basePos, face);
            }
        }

        public Squad NewSquad(byte owner, Team team, string name)
        {
            var sq = new Squad { Id = nextSquadId++, Owner = owner, Team = team, Name = name };
            Squads[sq.Id] = sq;
            return sq;
        }

        /// <summary>Создать бойца на сервере и разослать UnitSpawn.</summary>
        public Soldier SpawnUnit(int typeIndex, Team team, byte owner, Vector3 pos, float yaw, Squad squad, bool commander = false)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var nh, 6f, UnityEngine.AI.NavMesh.AllAreas)) pos = nh.position;
            var s = Instantiate(typeIndex, team, pos, yaw);
            if (!s) return null;
            ushort id = nextUnitId++;
            if (nextUnitId == 0) nextUnitId = 1;
            bool mine = owner == LocalId;
            var auth = commander ? (mine ? Authority.Owner : Authority.Puppet) : Authority.Server;
            s.Owner = owner;
            s.Init(id, typeIndex, team, auth);
            if (!commander) s.gameObject.AddComponent<SoldierBrain>();
            squad?.Add(s);
            if (commander && mine) AttachLocalCommander(s);
            if (commander && !mine) s.Damaged += d => NotifyCommanderHealth(s, d.Direction);
            w.Reset(Msg.UnitSpawn);
            WriteSpawn(w, s);
            Session.SendToAll(w, true);
            if (squad != null) squad.Dirty = true;
            return s;
        }

        static void WriteSpawn(NetWriter w, Soldier s)
        {
            w.UShort(s.NetId); w.Byte((byte)s.TypeIndex); w.Byte((byte)s.Team); w.Byte(s.Owner); w.UShort(s.Squad?.Id ?? 0);
            w.Pos(s.transform.position); w.Angle(s.transform.eulerAngles.y); w.Bool(s.IsCommander); w.Byte((byte)Mathf.Clamp(Mathf.RoundToInt(s.Health / s.MaxHealth * 255f), 0, 255));
        }

        void Despawn(Soldier s)
        {
            w.Reset(Msg.UnitDespawn).UShort(s.NetId);
            Session.SendToAll(w, true);
            s.Squad?.Remove(s);
            Destroy(s.gameObject);
        }

        void SendFullState(PlayerInfo p)
        {
            foreach (var s in Soldier.Living.ToList())
            {
                if (!s || s.Dead) continue;
                w.Reset(Msg.UnitSpawn);
                WriteSpawn(w, s);
                Session.SendTo(p, w, true);
            }
            foreach (var sq in Squads.Values)
            {
                w.Reset(Msg.SquadState);
                w.Byte(1);
                sq.Write(w);
                Session.SendTo(p, w, true);
            }
            SendObjectives(p);
            SendStructures(p, true);
        }

        // ================================================================== цикл
        void Update()
        {
            if (Session == null) return;
            if (IsServer && serverStarted) ServerUpdate();
            CorpseCleanup();
        }

        void ServerUpdate()
        {
            float dt = Time.deltaTime, now = Time.time;
            foreach (var sq in Squads.Values) sq.Tick(dt);
            foreach (var cp in CommandPost.All) cp.Tick(dt);
            // удалить пустые отряды ИИ
            foreach (var id in Squads.Where(kv => kv.Value.Members.Count == 0 && kv.Value.IsAI && now - MatchStart > 5f).Select(kv => kv.Key).ToList())
            {
                Squads.Remove(id);
                w.Reset(Msg.SquadState).Byte(0).UShort(id);          // 0 записей + id = удалён
                Session.SendToAll(w, true, true);
            }
            if (Winner == null && now > econAt)
            {
                econAt = now + 1f;
                Economy();
            }
            if (now > snapAt) { snapAt = now + 0.1f; SendSnapshot(); }
            if (now > avatarAt) { avatarAt = now + 0.066f; SendAvatars(); }
            if (now > structAt) { structAt = now + 0.25f; SendStructures(null, false); }
            if (now > objAt) { objAt = now + 0.5f; SendObjectives(null); }
            if (now > squadAt) { squadAt = now + 0.25f; SendSquads(false); }
            if (now > shotsAt) { shotsAt = now + 0.05f; FlushShots(); }
            if (Winner == null) CheckVictory();
        }

        void Economy()
        {
            int repPosts = CommandPost.All.Count(c => c.Owner == PostOwner.Republic && !c.IsBase);
            int empPosts = CommandPost.All.Count(c => c.Owner == PostOwner.Empire && !c.IsBase);
            foreach (var ps in Players.Values)
            {
                int posts = ps.Team == Team.Republic ? repPosts : empPosts;
                ps.Requisition += 4f + posts * 2.5f;
                ps.UnitCount = CountOwned(ps.Id);
                var p = Session.Get(ps.Id);
                if (p != null) SendEconomy(p, ps);
            }
            if (Mathf.FloorToInt(Time.time) % 10 == 0) EmpireTickets += empPosts;      // аванпосты Империи дают подкрепления
            // командиры восстанавливают здоровье вне боя (как в Battlefront)
            foreach (var c in Soldier.Living)
            {
                if (!c || !c.IsCommander || c.Dead || Time.time - c.LastHitTime < 6f || c.Health >= c.MaxHealth) continue;
                c.Health = Mathf.Min(c.MaxHealth, c.Health + 15f);
                if (c.Auth != Authority.Owner) NotifyCommanderHealth(c, Vector3.zero);
            }
        }

        void SendEconomy(PlayerInfo p, PlayerState ps)
        {
            w.Reset(Msg.PlayerEconomy);
            w.Int(Mathf.FloorToInt(ps.Requisition)); w.UShort((ushort)ps.UnitCount); w.UShort((ushort)Settings.UnitCap);
            w.UShort(ps.CommanderId); w.Float(Mathf.Max(0f, ps.RespawnAt - Time.time)); w.UShort((ushort)ps.Kills); w.UShort((ushort)EmpireTickets);
            Session.SendTo(p, w, true);
        }

        void SendSnapshot()
        {
            if (!Session.Online) return;
            int max = Session.MaxUnreliable - 8;
            sw.Reset(Msg.Snapshot);
            int countPos = sw.Length;
            sw.Byte(0);
            int count = 0;
            foreach (var s in Soldier.Living)
            {
                if (!s || s.Dead || s.Auth != Authority.Server) continue;
                if (sw.Length + 11 > max)
                {
                    sw.Buffer[countPos] = (byte)count;
                    Session.SendToAll(sw, false);
                    sw.Reset(Msg.Snapshot);
                    countPos = sw.Length;
                    sw.Byte(0);
                    count = 0;
                }
                sw.UShort(s.NetId);
                sw.Pos(s.transform.position);
                sw.Angle(s.transform.eulerAngles.y);
                sw.Byte(s.NetClip);
                sw.Byte((byte)Mathf.Clamp(Mathf.CeilToInt(s.Health / s.MaxHealth * 255f), 0, 255));
                count++;
            }
            if (count > 0)
            {
                sw.Buffer[countPos] = (byte)count;
                Session.SendToAll(sw, false);
            }
        }

        void SendAvatars()
        {
            if (!Session.Online) return;
            sw.Reset(Msg.AvatarStates);
            var list = Soldier.Living.Where(s => s && s.IsCommander && !s.Dead).ToList();
            sw.Byte((byte)list.Count);
            foreach (var s in list)
            {
                sw.UShort(s.NetId); sw.Pos(s.transform.position); sw.Angle16(s.transform.eulerAngles.y); sw.Byte(s.NetClip);
                sw.Byte((byte)Mathf.Clamp(Mathf.CeilToInt(s.Health / s.MaxHealth * 255f), 0, 255));
            }
            Session.SendToAll(sw, false);
        }

        void SendStructures(PlayerInfo to, bool reliable)
        {
            if (!Session.Online) return;
            sw.Reset(Msg.StructureState);
            sw.Byte((byte)Destructible.All.Count);
            foreach (var d in Destructible.All)
            {
                sw.Byte(d.Id);
                sw.Byte((byte)Mathf.Clamp(Mathf.CeilToInt(d.Health / d.MaxHealth * 255f), 0, 255));
                sw.Bool(d.Invulnerable);
            }
            var turrets = MapInfo.I ? MapInfo.I.Turrets : new Turret[0];
            sw.Byte((byte)turrets.Length);
            foreach (var t in turrets)
            {
                sw.Byte(t ? t.Id : (byte)0);
                sw.Angle(t ? t.NetYaw : 0f);
                sw.Byte(t ? (byte)Mathf.Clamp(Mathf.CeilToInt(t.Health / t.MaxHealth * 255f), 0, 255) : (byte)0);
            }
            if (to != null) Session.SendTo(to, sw, true);
            else Session.SendToAll(sw, reliable);
        }

        void SendObjectives(PlayerInfo to)
        {
            sw.Reset(Msg.Objectives);
            sw.UShort((ushort)Mathf.Clamp(EmpireTickets, 0, 65535));
            sw.Byte((byte)CommandPost.All.Count);
            foreach (var cp in CommandPost.All)
            {
                sw.Byte(cp.Id); sw.Byte((byte)cp.Owner); sw.SByte((sbyte)Mathf.RoundToInt(cp.Progress * 100f)); sw.Bool(cp.Contested);
                sw.Byte((byte)Mathf.Min(255, cp.RepublicCount)); sw.Byte((byte)Mathf.Min(255, cp.EmpireCount)); sw.Bool(cp.Locked);
            }
            if (to != null) Session.SendTo(to, sw, true);
            else if (Session.Online) Session.SendToAll(sw, false);
        }

        void SendSquads(bool all)
        {
            var dirty = Squads.Values.Where(s => all || s.Dirty).ToList();
            // меняющиеся каждый кадр поля (якорь, бой) — раз в 2 с всем
            if (!all && Time.frameCount % 8 == 0) dirty = Squads.Values.ToList();
            if (dirty.Count == 0) return;
            foreach (var sq in dirty) sq.Dirty = false;
            if (!Session.Online) { SquadsChanged?.Invoke(); return; }
            for (int i = 0; i < dirty.Count; i += 8)
            {
                w.Reset(Msg.SquadState);
                var part = dirty.Skip(i).Take(8).ToList();
                w.Byte((byte)part.Count);
                foreach (var sq in part) sq.Write(w);
                Session.SendToAll(w, true);
            }
            SquadsChanged?.Invoke();
        }

        // ================================================================== события боя
        void OnBoltFired(Unit shooter, Vector3 from, Vector3 dir, BoltKind kind)
        {
            ushort id = shooter is Soldier s ? s.NetId : (ushort)0;
            if (shooter is Turret t) id = (ushort)(0xFF00 | t.Id);
            shots.Add((id, from, dir, kind));
        }

        void FlushShots()
        {
            if (shots.Count == 0) return;
            if (!Session.Online) { shots.Clear(); return; }
            int max = Session.MaxUnreliable - 8;
            int i = 0;
            while (i < shots.Count)
            {
                sw.Reset(Msg.Shots);
                int countPos = sw.Length;
                sw.Byte(0);
                int n = 0;
                while (i < shots.Count && sw.Length + 12 < max && n < 255)
                {
                    var (sh, from, dir, kind) = shots[i++];
                    sw.UShort(sh); sw.Pos(from); sw.Dir(dir); sw.Byte((byte)kind);
                    n++;
                }
                sw.Buffer[countPos] = (byte)n;
                foreach (var p in Session.Players)
                {
                    if (p.Conn < 0 || !p.InMatch) continue;
                    Session.SendTo(p, sw, false);
                }
            }
            shots.Clear();
        }

        void OnExploded(Vector3 at, float radius)
        {
            w.Reset(Msg.Explosion).Pos(at).Byte((byte)Mathf.Clamp(Mathf.RoundToInt(radius * 4f), 1, 255));
            Session.SendToAll(w, true);
        }

        void OnSoldierKilled(Soldier s, DamageInfo d, string clip)
        {
            if (!IsServer) return;
            w.Reset(Msg.UnitDeath);
            w.UShort(s.NetId);
            w.Byte(string.IsNullOrEmpty(clip) ? (byte)255 : Clips.Id(clip));
            w.Dir(d.Direction);
            w.Byte((byte)Mathf.Clamp(d.Impulse, 0, 255));
            w.Pos(d.Point);
            w.Bool(d.Kind == DamageKind.Explosion);
            Session.SendToAll(w, true);
            // счёт и снабжение
            if (d.Attacker is Soldier killer && Players.TryGetValue(killer.Owner, out var kp)) { kp.Kills++; kp.Requisition += s.IsCommander ? 60f : 8f; }
            if (s.Owner != 255 && Players.TryGetValue(s.Owner, out var lp)) lp.Losses++;
            if (s.Team == Team.Empire && !s.IsCommander) { }
            if (Random.value < 0.35f)
            {
                var near = Soldier.Living.Where(x => x && !x.Dead && x.Team == s.Team && (x.transform.position - s.transform.position).sqrMagnitude < 400f).FirstOrDefault();
                if (near) Voice(near, "mandown");
            }
            else if (d.Attacker is Soldier k2 && !k2.Dead && Random.value < 0.25f) Voice(k2, "killedenemy");
            if (s.IsCommander && Players.TryGetValue(s.Owner, out var ps))
            {
                ps.RespawnAt = Time.time + 10f;
                var p = Session.Get(s.Owner);
                if (p != null) SendEconomy(p, ps);
            }
            if (s.Squad != null) s.Squad.Dirty = true;
        }

        void OnPostCaptured(CommandPost cp, PostOwner owner)
        {
            string who = owner == PostOwner.Republic ? "Республика" : owner == PostOwner.Empire ? "Империя" : "никто";
            string text = owner == PostOwner.Neutral ? $"Пункт {cp.Letter} «{cp.Title}» нейтрализован" : $"{who} захватывает пункт {cp.Letter} «{cp.Title}»";
            ShowNoticeAll(text, CommandPost.ColorOf(owner));
            if (owner == PostOwner.Republic)
                foreach (var ps in Players.Values.Where(x => x.Team == Team.Republic)) { ps.Requisition += 120f; ps.Captures++; }
            if (owner == PostOwner.Empire)
                foreach (var ps in Players.Values.Where(x => x.Team == Team.Empire)) { ps.Requisition += 120f; ps.Captures++; }
            Empire?.OnPostCaptured(cp, owner);
            SendObjectives(null);
        }

        void OnStructureDestroyed(Destructible d, Unit by)
        {
            var map = MapInfo.I;
            ShowNoticeAll($"{d.Title} уничтожен!", d.Team == Team.Empire ? TeamColors.Republic : TeamColors.Empire);
            if (map && map.CommandCenter)
            {
                bool shields = map.ShieldGenerators.Any(g => g && !g.Dead);
                if (!shields && map.CommandCenter.Invulnerable)
                {
                    map.CommandCenter.Invulnerable = false;
                    ShowNoticeAll("Щит командного центра отключён — уничтожьте командный центр!", TeamColors.Republic);
                }
            }
            if (by is Soldier s && Players.TryGetValue(s.Owner, out var ps)) ps.Requisition += 150f;
            SendStructures(null, true);
        }

        void CheckVictory()
        {
            var map = MapInfo.I;
            if (!map) return;
            if (map.CommandCenter && map.CommandCenter.Dead) { EndMatch(Team.Republic); return; }
            if (map.RepublicBase && map.RepublicBase.Owner == PostOwner.Empire) { EndMatch(Team.Empire); return; }
            if (map.RepublicHQ && map.RepublicHQ.Dead) { EndMatch(Team.Empire); return; }
            if (EmpireTickets <= 0 && Time.time - MatchStart > 30f && !Soldier.Living.Any(s => s && !s.Dead && s.Team == Team.Empire && !s.IsCommander))
                EndMatch(Team.Republic);
        }

        void EndMatch(Team winner)
        {
            Winner = winner;
            w.Reset(Msg.MatchEnd).Byte((byte)winner);
            WriteStats(w);
            Session.SendToAll(w, true);
            MatchEnded?.Invoke(winner);
        }

        void WriteStats(NetWriter w)
        {
            w.Byte((byte)Players.Count);
            foreach (var ps in Players.Values)
            {
                w.Byte(ps.Id); w.UShort((ushort)ps.Kills); w.UShort((ushort)ps.Losses); w.UShort((ushort)ps.Captures);
            }
        }

        public void ShowNoticeAll(string text, Color c)
        {
            w.Reset(Msg.Notice).String(text).Byte((byte)(c.r * 255)).Byte((byte)(c.g * 255)).Byte((byte)(c.b * 255));
            Session.SendToAll(w, true);
            ShowNotice(text, c);
        }

        readonly Queue<Soldier> corpses = new Queue<Soldier>();
        float corpseAt;

        void CorpseCleanup()
        {
            if (Time.time < corpseAt) return;
            corpseAt = Time.time + 2f;
            foreach (var u in FindObjectsByType<Soldier>(FindObjectsSortMode.None))
                if (u.Dead && !corpses.Contains(u)) corpses.Enqueue(u);
            while (corpses.Count > 0 && (corpses.Count > 90 || (corpses.Peek() && Time.time - corpses.Peek().DiedAt > 90f) || !corpses.Peek()))
            {
                var c = corpses.Dequeue();
                if (c) Destroy(c.gameObject);
            }
        }

        // ================================================================== сообщения клиентов
        void OnAvatarInput(PlayerInfo from, NetReader r)
        {
            ushort id = r.UShort();
            Vector3 pos = r.Pos();
            float yaw = r.Angle16();
            byte clip = r.Byte();
            if (!Soldier.ById.TryGetValue(id, out var s) || !s || s.Dead || s.Owner != from.Id) return;
            if (s.Auth == Authority.Owner) return;           // это командир хоста
            s.PushSnapshot(pos, yaw);
            s.NetClip = clip;
            s.ApplyNetClip(clip);
        }

        bool OwnsSquad(PlayerInfo from, Squad sq) => sq != null && (sq.Owner == from.Id) && sq.Team == from.Team;

        void OnOrder(PlayerInfo from, NetReader r)
        {
            int n = r.Byte();
            var ids = new List<ushort>();
            for (int i = 0; i < n; i++) ids.Add(r.UShort());
            var order = (OrderType)r.Byte();
            Vector3 pos = r.Pos();
            bool hasYaw = r.Bool();
            float yaw = r.Angle16();
            int post = r.Short();
            if (!Players.TryGetValue(from.Id, out var ps)) return;
            int k = 0;
            foreach (var id in ids)
            {
                if (!Squads.TryGetValue(id, out var sq) || !OwnsSquad(from, sq)) continue;
                Vector3 p = pos;
                // несколько отрядов в одну точку — рядом, а не друг в друге
                if (ids.Count > 1 && order != OrderType.Follow)
                {
                    float off = (k - (ids.Count - 1) * 0.5f) * 14f;
                    var right = Quaternion.Euler(0, hasYaw ? yaw : 0f, 0) * Vector3.right;
                    p += right * off;
                }
                if (order == OrderType.Capture && post >= 0)
                {
                    var cp = CommandPost.Get(post);
                    if (cp) p = cp.transform.position;
                }
                sq.SetOrder(order, p, hasYaw ? yaw : float.NaN, order == OrderType.Follow ? ps.CommanderId : (ushort)0, post);
                if (order == OrderType.Follow && sq.Gait == Gait.March) sq.Gait = Gait.Walk;
                k++;
            }
        }

        void OnSquadConfig(PlayerInfo from, NetReader r)
        {
            ushort id = r.UShort();
            var f = (FormationType)r.Byte();
            float spacing = r.Float();
            var st = (Stance)r.Byte();
            var g = (Gait)r.Byte();
            var roe = (Roe)r.Byte();
            string name = r.String();
            byte color = r.Byte();
            if (!Squads.TryGetValue(id, out var sq) || !OwnsSquad(from, sq)) return;
            if (sq.Formation != f || !Mathf.Approximately(sq.Spacing, spacing)) sq.SetFormation(f, Mathf.Clamp(spacing, 0.8f, 10f));
            sq.Stance = st;
            sq.Gait = g;
            sq.Roe = roe;
            if (!string.IsNullOrWhiteSpace(name)) sq.Name = name.Length > 24 ? name.Substring(0, 24) : name;
            sq.Color = (byte)(color % TeamColors.Squad.Length);
            sq.Dirty = true;
            var who = sq.Members.FirstOrDefault(m => m && !m.Dead);
            if (who && Random.value < 0.6f) Voice(who, "combat");
        }

        void OnRecruit(PlayerInfo from, NetReader r)
        {
            int ti = r.Byte();
            int count = Mathf.Clamp(r.Byte(), 1, 12);
            ushort squadId = r.UShort();
            int post = r.Short();
            if (!Players.TryGetValue(from.Id, out var ps)) return;
            var type = Catalog.I.Get(ti);
            if (type == null || type.Team != from.Team || !type.Recruitable) return;
            int have = CountOwned(from.Id);
            count = Mathf.Min(count, Settings.UnitCap - have, Mathf.FloorToInt(ps.Requisition / Mathf.Max(1, type.Cost)));
            if (count <= 0)
            {
                SendNotice(from, have >= Settings.UnitCap ? "Достигнут предел численности армии" : "Не хватает снабжения", new Color(1f, 0.6f, 0.2f));
                return;
            }
            var cp = CommandPost.Get(post);
            if (!cp || !cp.OwnedBy(from.Team)) cp = BaseOf(from.Team);
            Squad sq = null;
            if (squadId != 0 && Squads.TryGetValue(squadId, out var s0) && OwnsSquad(from, s0)) sq = s0;
            if (sq == null)
            {
                int n = Squads.Values.Count(s => s.Owner == from.Id) + 1;
                sq = NewSquad(from.Id, from.Team, $"Отряд {n}");
                sq.Color = (byte)((from.Color * 2 + n) % TeamColors.Squad.Length);
                sq.SetFormation(FormationType.Wedge);
                sq.Stance = Stance.Ready;
                sq.Gait = Gait.Walk;
            }
            float face = cp ? FacingFrom(cp) : 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = cp ? cp.SpawnPoint(i + Random.Range(0, 8)) : Vector3.zero;
                pos.y = MapInfo.GroundHeight(pos);
                SpawnUnit(ti, from.Team, from.Id, pos, face, sq);
            }
            ps.Requisition -= count * type.Cost;
            if (sq.Members.Count == count)
            {
                sq.ResetAnchor();
                sq.SetOrder(OrderType.Hold, sq.Anchor, face);
            }
            sq.Dirty = true;
            SendNotice(from, $"Прибыло подкрепление: {type.Title} x{count} → {sq.Name}", TeamColors.Republic);
            ps.UnitCount = CountOwned(from.Id);
            SendEconomy(from, ps);
        }

        void OnSquadEdit(PlayerInfo from, NetReader r)
        {
            var op = (EditOp)r.Byte();
            ushort squadId = r.UShort();
            string name = r.String();
            int n = r.Byte();
            var ids = new List<ushort>();
            for (int i = 0; i < n; i++) ids.Add(r.UShort());
            Squads.TryGetValue(squadId, out var sq);
            switch (op)
            {
                case EditOp.CreateSquad:
                {
                    int k = Squads.Values.Count(s => s.Owner == from.Id) + 1;
                    var ns = NewSquad(from.Id, from.Team, string.IsNullOrWhiteSpace(name) ? $"Отряд {k}" : name);
                    ns.Color = (byte)((from.Color * 2 + k) % TeamColors.Squad.Length);
                    ns.SetFormation(FormationType.Wedge);
                    MoveSoldiers(from, ns, ids);
                    break;
                }
                case EditOp.MoveSoldiers:
                    if (OwnsSquad(from, sq)) MoveSoldiers(from, sq, ids);
                    break;
                case EditOp.Dismiss:
                    foreach (var id in ids)
                        if (Soldier.ById.TryGetValue(id, out var s) && s && s.Owner == from.Id && !s.IsCommander)
                        {
                            if (Players.TryGetValue(from.Id, out var ps)) ps.Requisition += (s.Type?.Cost ?? 50) * 0.5f;
                            Despawn(s);
                        }
                    break;
                case EditOp.DeleteSquad:
                    if (OwnsSquad(from, sq) && sq.Members.Count == 0)
                    {
                        Squads.Remove(sq.Id);
                        w.Reset(Msg.SquadState).Byte(0).UShort(sq.Id);
                        Session.SendToAll(w, true, true);
                    }
                    break;
            }
            SendSquads(false);
        }

        void MoveSoldiers(PlayerInfo from, Squad to, List<ushort> ids)
        {
            foreach (var id in ids)
            {
                if (!Soldier.ById.TryGetValue(id, out var s) || !s || s.Owner != from.Id || s.IsCommander) continue;
                s.Squad?.Remove(s);
                to.Add(s);
            }
            if (to.Members.Count == ids.Count) { to.ResetAnchor(); to.SetOrder(OrderType.Hold, to.Anchor); }
            to.Dirty = true;
        }

        void OnCommanderFire(PlayerInfo from, NetReader r)
        {
            ushort id = r.UShort();
            Vector3 origin = r.Pos();
            Vector3 dir = r.Dir();
            if (!Soldier.ById.TryGetValue(id, out var s) || !s || s.Dead || s.Owner != from.Id || s.Type == null) return;
            if ((origin - s.transform.position).sqrMagnitude > 9f) origin = s.Muzzle;
            var wpn = s.Type.Weapon;
            int before = shots.Count;
            Bolts.I.Fire(s, origin, dir, wpn.Bolt, wpn.Damage * 1.25f, wpn.Splash, true, wpn.Sound);
            // стрелку не отправляем — он уже нарисовал свой выстрел
            if (shots.Count > before) shots.RemoveAt(shots.Count - 1);
            RelayShot(from, s.NetId, origin, dir, wpn.Bolt);
        }

        void RelayShot(PlayerInfo except, ushort shooter, Vector3 from, Vector3 dir, BoltKind kind)
        {
            sw.Reset(Msg.Shots).Byte(1).UShort(shooter).Pos(from).Dir(dir).Byte((byte)kind);
            Session.SendToAll(sw, false, false, except.Id);
        }

        void OnCommanderGrenade(PlayerInfo from, NetReader r)
        {
            ushort id = r.UShort();
            Vector3 origin = r.Pos();
            Vector3 vel = r.Vector3();
            if (!Soldier.ById.TryGetValue(id, out var s) || !s || s.Dead || s.Owner != from.Id) return;
            Grenade.Throw(s, origin, vel, true);
            sw.Reset(Msg.CommanderGrenade).UShort(id).Pos(origin).Vector3(vel);
            Session.SendToAll(sw, true, false, from.Id);
        }

        void OnCommanderRespawn(PlayerInfo from, NetReader r)
        {
            int post = r.Short();
            if (!Players.TryGetValue(from.Id, out var ps)) return;
            if (Soldier.ById.TryGetValue(ps.CommanderId, out var c) && c && !c.Dead) return;
            if (Time.time < ps.RespawnAt) return;
            var cp = CommandPost.Get(post);
            if (!cp || !cp.OwnedBy(from.Team) || (cp.Contested && !cp.IsBase)) cp = BaseOf(from.Team);
            SpawnCommander(from, ps, cp);
            SendEconomy(from, ps);
        }

        void SendNotice(PlayerInfo p, string text, Color c)
        {
            w.Reset(Msg.Notice).String(text).Byte((byte)(c.r * 255)).Byte((byte)(c.g * 255)).Byte((byte)(c.b * 255));
            Session.SendTo(p, w, true);
        }

        /// <summary>Сервер: урон командиру удалённого игрока — сообщить владельцу.</summary>
        public void NotifyCommanderHealth(Soldier s, Vector3 dir)
        {
            var p = Session.Get(s.Owner);
            if (p == null || p.Conn < 0) return;
            w.Reset(Msg.CommanderDamage).UShort(s.NetId).Float(s.Health).Dir(dir);
            Session.SendTo(p, w, true);
        }
    }
}
