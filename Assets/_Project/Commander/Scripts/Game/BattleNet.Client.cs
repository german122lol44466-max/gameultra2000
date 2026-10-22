using System.Collections.Generic;
using System.Linq;
using SW.Battle;
using SW.Commander.Net;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>Клиентская часть боя: копии бойцов (пупеты), визуал выстрелов и смертей, зеркала отрядов и целей.</summary>
    public partial class BattleNet
    {
        public int Requisition { get; private set; }
        public int UnitCount { get; private set; }
        public int UnitCap { get; private set; } = 60;
        public float RespawnIn { get; private set; }
        float respawnSetAt;
        public float RespawnLeft => Mathf.Max(0f, RespawnIn - (Time.time - respawnSetAt));
        public readonly Dictionary<byte, (int kills, int losses, int captures)> FinalStats = new Dictionary<byte, (int, int, int)>();

        void AttachLocalCommander(Soldier s)
        {
            LocalCommander = s;
            var cc = s.GetComponent<CommanderController>();
            if (!cc) cc = s.gameObject.AddComponent<CommanderController>();
            cc.Battle = this;
            s.Died += _ => LocalCommanderDied?.Invoke();
            LocalCommanderSpawned?.Invoke(s);
        }

        // ================================================================== спавн
        void ClUnitSpawn(NetReader r)
        {
            if (IsServer) return;
            ushort id = r.UShort();
            int ti = r.Byte();
            var team = (Team)r.Byte();
            byte owner = r.Byte();
            ushort squad = r.UShort();
            Vector3 pos = r.Pos();
            float yaw = r.Angle();
            bool commander = r.Bool();
            float hp = r.Byte() / 255f;
            if (Soldier.ById.TryGetValue(id, out var old) && old) Destroy(old.gameObject);
            var s = Instantiate(ti, team, pos, yaw);
            if (!s) return;
            bool mine = commander && owner == LocalId;
            s.Owner = owner;
            s.Init(id, ti, team, mine ? Authority.Owner : Authority.Puppet);
            s.Health = Mathf.Max(1f, s.MaxHealth * hp);
            if (squad != 0 && Squads.TryGetValue(squad, out var sq)) sq.Add(s);
            if (mine) AttachLocalCommander(s);
        }

        void ClUnitDespawn(NetReader r)
        {
            ushort id = r.UShort();
            if (IsServer) return;
            if (Soldier.ById.TryGetValue(id, out var s) && s)
            {
                s.Squad?.Remove(s);
                Destroy(s.gameObject);
            }
        }

        void ClSnapshot(NetReader r)
        {
            if (IsServer) return;
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                ushort id = r.UShort();
                Vector3 pos = r.Pos();
                float yaw = r.Angle();
                byte clip = r.Byte();
                float hp = r.Byte() / 255f;
                if (!Soldier.ById.TryGetValue(id, out var s) || !s || s.Dead || s.Auth != Authority.Puppet) continue;
                s.PushSnapshot(pos, yaw);
                s.ApplyNetClip(clip);
                s.Health = s.MaxHealth * hp;
            }
        }

        void ClAvatarStates(NetReader r)
        {
            if (IsServer) return;
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                ushort id = r.UShort();
                Vector3 pos = r.Pos();
                float yaw = r.Angle16();
                byte clip = r.Byte();
                float hp = r.Byte() / 255f;
                if (!Soldier.ById.TryGetValue(id, out var s) || !s || s.Dead) continue;
                if (s.Auth == Authority.Owner) { s.Health = s.MaxHealth * hp; continue; }
                s.PushSnapshot(pos, yaw);
                s.ApplyNetClip(clip);
                s.Health = s.MaxHealth * hp;
            }
        }

        void ClUnitDeath(NetReader r)
        {
            if (IsServer) return;
            ushort id = r.UShort();
            byte clip = r.Byte();
            Vector3 dir = r.Dir();
            float imp = r.Byte();
            Vector3 point = r.Pos();
            bool blast = r.Bool();
            if (!Soldier.ById.TryGetValue(id, out var s) || !s || s.Dead) return;
            s.PlayDeath(clip == 255 ? "" : Clips.Name(clip), dir * imp, point, blast);
            s.Squad?.Remove(s);
        }

        void ClShots(NetReader r)
        {
            if (IsServer) return;
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                ushort shooter = r.UShort();
                Vector3 from = r.Pos();
                Vector3 dir = r.Dir();
                var kind = (BoltKind)r.Byte();
                Unit su = null;
                string sound = null;
                if ((shooter & 0xFF00) == 0xFF00 && MapInfo.I)
                {
                    var t = MapInfo.I.Turrets.FirstOrDefault(x => x && x.Id == (shooter & 0xFF));
                    su = t;
                    sound = "dlt19";
                }
                else if (Soldier.ById.TryGetValue(shooter, out var s) && s && !s.Dead)
                {
                    su = s;
                    sound = s.Type?.Weapon.Sound;
                    if (s.Auth == Authority.Puppet && s.Velocity.magnitude < 0.4f) s.FireAnim();
                    // точка вылета — от ствола модели (снимок мог отстать)
                    if ((s.Muzzle - from).sqrMagnitude < 4f) from = s.Muzzle;
                }
                Bolts.I.Fire(su, from, dir, kind, 0f, 0f, false, sound);
            }
        }

        void ClExplosion(NetReader r)
        {
            if (IsServer) return;
            Vector3 at = r.Pos();
            float radius = r.Byte() / 4f;
            Fx.Explosion(at, radius);
        }

        void ClSquadState(NetReader r)
        {
            if (IsServer && Session.Online) { SquadsChanged?.Invoke(); return; }
            int n = r.Byte();
            if (n == 0)
            {
                // удалён отряд
                if (r.Remaining >= 2)
                {
                    ushort gone = r.UShort();
                    if (!IsServer) Squads.Remove(gone);
                }
                SquadsChanged?.Invoke();
                return;
            }
            if (IsServer) { SquadsChanged?.Invoke(); return; }
            for (int i = 0; i < n; i++)
            {
                ushort id = r.UShort();
                if (!Squads.TryGetValue(id, out var sq)) Squads[id] = sq = new Squad { Id = id };
                sq.Read(r);
            }
            SquadsChanged?.Invoke();
        }

        void ClObjectives(NetReader r)
        {
            if (IsServer) return;
            EmpireTickets = r.UShort();
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                byte id = r.Byte();
                var owner = (PostOwner)r.Byte();
                float prog = r.SByte() / 100f;
                bool contested = r.Bool();
                int rc = r.Byte(), ec = r.Byte();
                bool locked = r.Bool();
                var cp = CommandPost.Get(id);
                if (!cp) continue;
                cp.Owner = owner;
                cp.Progress = prog;
                cp.Contested = contested;
                cp.RepublicCount = rc;
                cp.EmpireCount = ec;
                cp.Locked = locked;
            }
        }

        void ClStructures(NetReader r)
        {
            if (IsServer) return;
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                byte id = r.Byte();
                float hp = r.Byte() / 255f;
                bool inv = r.Bool();
                var d = Destructible.All.FirstOrDefault(x => x.Id == id);
                if (!d) continue;
                d.Invulnerable = inv;
                d.SetHealth(d.MaxHealth * hp);
            }
            int t = r.Byte();
            for (int i = 0; i < t; i++)
            {
                byte id = r.Byte();
                float yaw = r.Angle();
                float hp = r.Byte() / 255f;
                var tur = MapInfo.I ? MapInfo.I.Turrets.FirstOrDefault(x => x && x.Id == id) : null;
                if (!tur || tur.Dead) continue;
                tur.NetYaw = yaw;
                if (hp <= 0f) tur.Damage(new DamageInfo { Amount = 99999f, Kind = DamageKind.Explosion, Point = tur.Center });
                else tur.Health = tur.MaxHealth * hp;
            }
        }

        void ClEconomy(NetReader r)
        {
            Requisition = r.Int();
            UnitCount = r.UShort();
            UnitCap = r.UShort();
            ushort cmd = r.UShort();
            RespawnIn = r.Float();
            respawnSetAt = Time.time;
            int kills = r.UShort();
            int tickets = r.UShort();
            if (!IsServer) EmpireTickets = tickets;
            if (!Players.TryGetValue(LocalId, out var me)) Players[LocalId] = me = new PlayerState { Id = LocalId, Team = LocalTeam };
            me.CommanderId = cmd;
            me.Kills = kills;
            me.Requisition = Requisition;
            me.UnitCount = UnitCount;
        }

        void ClVoice(NetReader r)
        {
            if (IsServer) return;
            ushort id = r.UShort();
            int cat = r.Byte();
            if (cat >= VoiceCategories.Length) return;
            if (Soldier.ById.TryGetValue(id, out var s) && s) Audio.Voice(s.Team, VoiceCategories[cat], s.Eye);
        }

        void ClMatchEnd(NetReader r)
        {
            if (IsServer) return;
            Winner = (Team)r.Byte();
            int n = r.Byte();
            for (int i = 0; i < n; i++) FinalStats[r.Byte()] = (r.UShort(), r.UShort(), r.UShort());
            MatchEnded?.Invoke(Winner.Value);
        }

        void ClNotice(NetReader r)
        {
            if (IsServer) return;
            string text = r.String();
            var c = new Color(r.Byte() / 255f, r.Byte() / 255f, r.Byte() / 255f);
            ShowNotice(text, c);
        }

        void ClCommanderDamage(NetReader r)
        {
            ushort id = r.UShort();
            float hp = r.Float();
            Vector3 dir = r.Dir();
            if (!Soldier.ById.TryGetValue(id, out var s) || !s || s.Dead) return;
            s.Health = hp;
            var cc = s.GetComponent<CommanderController>();
            if (cc) cc.OnHurt(dir);
        }

        /// <summary>Клиентский визуал гранаты другого командира.</summary>
        void ClGrenade(NetReader r)
        {
            ushort id = r.UShort();
            Vector3 origin = r.Pos();
            Vector3 vel = r.Vector3();
            Soldier.ById.TryGetValue(id, out var s);
            Grenade.Throw(s, origin, vel, false);
        }

        /// <summary>Статистика по окончании (на сервере — из состояния игроков).</summary>
        public (int kills, int losses, int captures) StatsOf(byte id)
        {
            if (IsServer && Players.TryGetValue(id, out var ps)) return (ps.Kills, ps.Losses, ps.Captures);
            return FinalStats.TryGetValue(id, out var st) ? st : (0, 0, 0);
        }
    }
}
