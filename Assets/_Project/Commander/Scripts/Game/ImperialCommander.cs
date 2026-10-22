using System.Collections.Generic;
using System.Linq;
using SW.Battle;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>
    /// ИИ-командующий Империи (сервер). Расставляет гарнизоны на аванпостах (часовые «смирно» у ворот,
    /// патрули по периметру, отряды обороны), держит резерв на базе, реагирует на атаки подкреплениями,
    /// отбивает потерянные пункты, на высокой сложности сам идёт в наступление. Подкрепления тратят тикеты.
    /// </summary>
    public class ImperialCommander : MonoBehaviour
    {
        BattleNet battle;
        float thinkAt, waveAt;
        int AliveCap => new[] { 110, 160, 220 }[Mathf.Clamp(battle.Settings.EmpireStrength, 0, 2)];
        readonly Dictionary<CommandPost, List<Squad>> garrisons = new Dictionary<CommandPost, List<Squad>>();
        readonly Dictionary<CommandPost, float> lostAt = new Dictionary<CommandPost, float>();
        readonly List<Squad> responders = new List<Squad>();
        readonly Dictionary<Squad, int> patrolIndex = new Dictionary<Squad, int>();
        readonly Dictionary<Squad, CommandPost> patrolPost = new Dictionary<Squad, CommandPost>();
        int squadNo = 1;
        readonly Dictionary<Squad, CommandPost> roadPatrols = new Dictionary<Squad, CommandPost>();

        static readonly string[] Units = { "TK", "FN", "TX", "TR", "SC", "DT", "LS", "ST" };

        int TypeOf(Role role)
        {
            var c = Catalog.I;
            int i = c.Units.FindIndex(u => u.Team == Team.Empire && u.Role == role && u.Prefab);
            return i >= 0 ? i : c.RiflemanIndex(Team.Empire);
        }

        int RandomTrooper()
        {
            var c = Catalog.I;
            var list = c.Units.Select((u, i) => (u, i)).Where(x => x.u.Team == Team.Empire && x.u.Prefab && x.u.Role != Role.Commander &&
                                                                    x.u.Role != Role.Officer).ToList();
            if (list.Count == 0) return c.RiflemanIndex(Team.Empire);
            // стрелки чаще остальных
            float r = Random.value;
            var pool = r < 0.6f ? list.Where(x => x.u.Role == Role.Rifleman).ToList() : list;
            if (pool.Count == 0) pool = list;
            return pool[Random.Range(0, pool.Count)].i;
        }

        public void Begin(BattleNet b)
        {
            battle = b;
            int strength = battle.Settings.EmpireStrength;
            foreach (var cp in CommandPost.All.Where(c => c.Owner == PostOwner.Empire))
            {
                bool hq = cp.IsBase;
                int defenders = (hq ? 3 : 1) + strength;
                var list = new List<Squad>();
                // часовые у ворот: «смирно»
                if (cp.Guards != null && cp.Guards.Length > 0)
                {
                    var g = Make(cp, "Часовые", cp.Guards.Length, Role.Rifleman, FormationType.Line);
                    g.Stance = Stance.Attention;
                    for (int i = 0; i < g.Members.Count && i < cp.Guards.Length; i++)
                    {
                        var s = g.Members[i];
                        if (!cp.Guards[i]) continue;
                        s.Teleport(cp.Guards[i].position, cp.Guards[i].eulerAngles.y);
                    }
                    g.Anchor = cp.Guards[0].position;
                    g.SetFormation(FormationType.Line, 4f);
                    g.SetOrder(OrderType.Hold, g.Anchor, cp.Guards[0].eulerAngles.y);
                    list.Add(g);
                }
                // оборона
                for (int k = 0; k < defenders; k++)
                {
                    var sq = Make(cp, "Гарнизон", 6 + Random.Range(0, 3), Role.Rifleman, k % 2 == 0 ? FormationType.Line : FormationType.Skirmish);
                    sq.Stance = k == 0 ? Stance.AtEase : Stance.Ready;
                    Vector3 p = cp.transform.position + Quaternion.Euler(0, k * 120f + 30f, 0) * Vector3.forward * cp.Radius * 0.7f;
                    sq.Anchor = p;
                    sq.SetOrder(OrderType.Defend, p, FacingOut(cp, p));
                    list.Add(sq);
                }
                // патруль по периметру
                if (cp.Patrol != null && cp.Patrol.Length >= 2)
                {
                    var pt = Make(cp, "Патруль", 4, Role.Rifleman, FormationType.DoubleColumn);
                    pt.Gait = Gait.Patrol;
                    pt.Stance = Stance.Ready;
                    patrolIndex[pt] = 0;
                    patrolPost[pt] = cp;
                    pt.SetOrder(OrderType.Move, cp.Patrol[0].position);
                    list.Add(pt);
                }
                garrisons[cp] = list;
            }
            // патрули по дорогам между аванпостами
            var held = CommandPost.All.Where(c => c.Owner == PostOwner.Empire && !c.IsBase).ToList();
            for (int i = 0; i < 1 + battle.Settings.EmpireStrength && held.Count >= 2; i++)
            {
                var from = held[i % held.Count];
                var rp = Make(from, "Патруль дорог", 5 + battle.Settings.Difficulty, Role.Rifleman, FormationType.DoubleColumn);
                rp.Gait = Gait.Patrol;
                rp.Stance = Stance.Ready;
                var to = held[(i + 1) % held.Count];
                rp.SetOrder(OrderType.Move, to.transform.position + Vector3.right * 20f);
                roadPatrols[rp] = to;
            }
            thinkAt = Time.time + 5f;
            waveAt = Time.time + 120f - battle.Settings.Difficulty * 20f;
        }

        static float FacingOut(CommandPost cp, Vector3 p)
        {
            Vector3 d = p - cp.transform.position;
            d.y = 0;
            return d.sqrMagnitude < 0.1f ? 0f : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        Squad Make(CommandPost at, string kind, int count, Role mainRole, FormationType f)
        {
            var sq = battle.NewSquad(255, Team.Empire, $"{kind} {Units[squadNo % Units.Length]}-{100 + squadNo}");
            squadNo++;
            sq.Color = 1;
            sq.SetFormation(f);
            int officer = TypeOf(Role.Officer);
            for (int i = 0; i < count; i++)
            {
                int ti = i == 0 && count >= 6 ? officer : (kind == "Часовые" || kind == "Патруль" ? TypeOf(Role.Rifleman) : RandomTrooper());
                Vector3 pos = at.SpawnPoint(i + squadNo * 3);
                pos.y = MapInfo.GroundHeight(pos);
                battle.SpawnUnit(ti, Team.Empire, 255, pos, Random.Range(0f, 360f), sq);
            }
            sq.ResetAnchor();
            return sq;
        }

        int AliveEmpire => Soldier.Living.Count(s => s && !s.Dead && s.Team == Team.Empire);

        void Update()
        {
            if (battle == null || battle.Winner != null) return;
            UpdatePatrols();
            if (Time.time < thinkAt) return;
            thinkAt = Time.time + 6f;
            responders.RemoveAll(s => s.Members.Count == 0);

            // 1. угрозы аванпостам: враги рядом или идёт захват
            foreach (var cp in CommandPost.All)
            {
                if (cp.Owner != PostOwner.Empire && !(cp.Owner == PostOwner.Neutral && cp.Progress < 0f)) continue;
                int enemies = Soldier.Living.Count(s => s && !s.Dead && s.Team == Team.Republic && (s.transform.position - cp.transform.position).sqrMagnitude < 70f * 70f);
                if (enemies == 0) continue;
                // гарнизон — к бою, стойку «смирно» сменить на боевую
                if (garrisons.TryGetValue(cp, out var g))
                    foreach (var sq in g.Where(s => s.Members.Count > 0))
                    {
                        if (sq.Stance == Stance.AtEase || sq.Stance == Stance.Ready) { sq.Stance = Stance.Combat; sq.Dirty = true; }
                        if (sq.Order == OrderType.Move && patrolPost.ContainsKey(sq)) { sq.SetOrder(OrderType.Defend, cp.transform.position); patrolPost.Remove(sq); }
                    }
                int sent = responders.Count(s => s.Order == OrderType.Attack && (s.OrderPos - cp.transform.position).sqrMagnitude < 900f);
                int need = Mathf.Clamp(enemies / 6, 1, 3);
                for (int i = sent; i < need; i++) SendResponse(cp, OrderType.Attack);
            }

            // 2. контратаки на потерянные пункты
            foreach (var kv in lostAt.ToList())
            {
                var cp = kv.Key;
                if (cp.Owner == PostOwner.Empire) { lostAt.Remove(cp); continue; }
                float delay = 100f - battle.Settings.Difficulty * 20f;
                if (Time.time - kv.Value < delay) continue;
                lostAt[cp] = Time.time + 60f;       // следующая волна позже
                int waves = 1 + battle.Settings.Difficulty / 2 + battle.Settings.EmpireStrength;
                for (int i = 0; i < waves; i++) SendResponse(cp, OrderType.Capture);
                battle.ShowNoticeAll($"Империя контратакует пункт {cp.Letter} «{cp.Title}»!", TeamColors.Empire);
            }

            // 3. наступление (ветеран и выше): на ближайший пункт Республики
            if (Time.time > waveAt && battle.Settings.Difficulty >= 2)
            {
                waveAt = Time.time + 240f - battle.Settings.Difficulty * 30f;
                var target = CommandPost.All.Where(c => c.Owner == PostOwner.Republic)
                    .OrderBy(c => (c.transform.position - (MapInfo.I && MapInfo.I.EmpireBase ? MapInfo.I.EmpireBase.transform.position : Vector3.zero)).sqrMagnitude)
                    .FirstOrDefault();
                if (target)
                {
                    for (int i = 0; i < 2 + battle.Settings.EmpireStrength; i++) SendResponse(target, OrderType.Capture);
                    battle.ShowNoticeAll($"Разведка: колонна Империи выдвигается к пункту {target.Letter}", TeamColors.Empire);
                }
            }
        }

        void SendResponse(CommandPost target, OrderType order)
        {
            if (battle.EmpireTickets < 6 || AliveEmpire >= AliveCap) return;
            // откуда: ближайший имперский пункт без угрозы, иначе база
            var from = CommandPost.All.Where(c => c.Owner == PostOwner.Empire && c != target && !c.Contested)
                .OrderBy(c => (c.transform.position - target.transform.position).sqrMagnitude).FirstOrDefault() ?? (MapInfo.I ? MapInfo.I.EmpireBase : null);
            if (!from) return;
            int n = Mathf.Min(6 + battle.Settings.Difficulty * 2, battle.EmpireTickets);
            var sq = Make(from, "Ударная группа", n, Role.Rifleman, FormationType.Wedge);
            battle.EmpireTickets -= n;
            sq.Gait = Gait.Run;
            sq.Stance = Stance.Combat;
            sq.Roe = Roe.FireAtWill;
            sq.SetOrder(order, target.transform.position, float.NaN, 0, target.Id);
            responders.Add(sq);
        }

        void UpdatePatrols()
        {
            foreach (var kv in patrolPost.ToList())
            {
                var sq = kv.Key;
                var cp = kv.Value;
                if (sq.Members.Count == 0) { patrolPost.Remove(sq); continue; }
                if (sq.Order != OrderType.Move) continue;
                if ((sq.Anchor - sq.OrderPos).sqrMagnitude < 4f && !sq.Moving)
                {
                    int i = (patrolIndex[sq] + 1) % cp.Patrol.Length;
                    patrolIndex[sq] = i;
                    if (cp.Patrol[i]) sq.SetOrder(OrderType.Move, cp.Patrol[i].position);
                }
            }
            foreach (var kv in roadPatrols.ToList())
            {
                var sq = kv.Key;
                if (sq.Members.Count == 0) { roadPatrols.Remove(sq); continue; }
                if (sq.Order != OrderType.Move || sq.InCombat) continue;
                if ((sq.Anchor - sq.OrderPos).sqrMagnitude < 9f && !sq.Moving)
                {
                    var held = CommandPost.All.Where(c => c.Owner == PostOwner.Empire && !c.IsBase && c != kv.Value).ToList();
                    if (held.Count == 0) { sq.SetOrder(OrderType.Defend, sq.Anchor); roadPatrols.Remove(sq); continue; }
                    var next = held[Random.Range(0, held.Count)];
                    roadPatrols[sq] = next;
                    sq.SetOrder(OrderType.Move, next.transform.position + Random.insideUnitSphere.normalized * 22f);
                }
            }
            // атакующие группы: дошли до цели и пункт свой — переходят в оборону
            foreach (var sq in responders)
            {
                if (sq.Members.Count == 0) continue;
                if (sq.Order == OrderType.Attack && !sq.InCombat && (sq.Anchor - sq.OrderPos).sqrMagnitude < 100f)
                {
                    var cp = CommandPost.All.OrderBy(c => (c.transform.position - sq.OrderPos).sqrMagnitude).FirstOrDefault();
                    if (cp && cp.Owner != PostOwner.Empire) sq.SetOrder(OrderType.Capture, cp.transform.position, float.NaN, 0, cp.Id);
                    else sq.SetOrder(OrderType.Defend, sq.OrderPos);
                    sq.Gait = Gait.Tactical;
                }
                else if (sq.Order == OrderType.Capture && sq.TargetPost >= 0)
                {
                    var cp = CommandPost.Get(sq.TargetPost);
                    if (cp && cp.Owner == PostOwner.Empire && !sq.InCombat) { sq.SetOrder(OrderType.Defend, cp.transform.position); sq.Gait = Gait.Walk; }
                }
            }
        }

        public void OnPostCaptured(CommandPost cp, PostOwner owner)
        {
            if (owner == PostOwner.Republic) lostAt[cp] = Time.time;
            else if (owner == PostOwner.Empire) lostAt.Remove(cp);
        }
    }
}
