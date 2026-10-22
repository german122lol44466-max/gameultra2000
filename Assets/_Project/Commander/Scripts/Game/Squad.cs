using System.Collections.Generic;
using System.Linq;
using SW.Battle;
using SW.Commander.Net;
using UnityEngine;
using UnityEngine.AI;

namespace SW.Commander
{
    /// <summary>
    /// Отряд: бойцы, строй, стойка, походка, правила огня и приказ. На сервере ведёт «якорь» строя по пути NavMesh
    /// (скорость — по походке, ждёт отстающих), раздаёт бойцам места и направление взгляда.
    /// У клиентов — зеркальные данные (для интерфейса и меток).
    /// </summary>
    public class Squad
    {
        public ushort Id;
        public byte Owner = 255;                       // игрок (255 — ИИ Империи)
        public Team Team;
        public string Name = "Отряд";
        public byte Color;
        public readonly List<Soldier> Members = new List<Soldier>();
        public readonly List<ushort> MemberIds = new List<ushort>();   // клиент: id бойцов из SquadState

        public FormationType Formation = FormationType.Wedge;
        public float Spacing = 2f;
        public Stance Stance = Stance.Ready;
        public Gait Gait = Gait.Walk;
        public Roe Roe = Roe.FireAtWill;

        public OrderType Order = OrderType.Hold;
        public Vector3 OrderPos;
        public float OrderYaw = float.NaN;             // направление строя по прибытии (NaN — по движению)
        public ushort FollowId;                        // за кем идти (командир)
        public int TargetPost = -1;                    // командный пункт для захвата

        // --- сервер: якорь
        public Vector3 Anchor;
        public float Yaw;
        public bool Moving;
        public float LastAttacked = -99f;
        public float LastContact = -99f;               // видел врага
        public bool Dirty = true;                      // изменился — разослать SquadState
        readonly NavMeshPath path = new NavMeshPath();
        readonly List<Vector3> corners = new List<Vector3>();
        int corner;
        float repathAt;
        Vector3 pathGoal;
        readonly List<Formations.Slot> slots = new List<Formations.Slot>();
        readonly List<Vector3> slotWorld = new List<Vector3>();
        readonly List<float> slotYaw = new List<float>();
        bool needAssign = true;
        Vector3 lastSlotAnchor;
        float lastSlotYaw = float.NaN;
        int lastCount = -1;
        FormationType lastFormation;
        float lastSpacing;

        public bool IsAI => Owner == 255;
        public int Alive => Members.Count(m => m && !m.Dead);
        public bool InCombat => Time.time - LastContact < 8f || Time.time - LastAttacked < 8f;

        public Vector3 Center
        {
            get
            {
                Vector3 c = Vector3.zero;
                int n = 0;
                foreach (var m in Members) if (m && !m.Dead) { c += m.transform.position; n++; }
                return n > 0 ? c / n : Anchor;
            }
        }

        /// <summary>Фаза строевого шага (одна на отряд — шаг в ногу).</summary>
        public float MarchPhase(Soldier s)
        {
            float len = s.Length("March");
            return len > 0.01f ? Mathf.Repeat(Time.time / len, 1f) : 0f;
        }

        // ------------------------------------------------------------------ состав
        public void Add(Soldier s)
        {
            if (Members.Contains(s)) return;
            Members.Add(s);
            s.Squad = this;
            needAssign = true;
            Dirty = true;
        }

        public void Remove(Soldier s)
        {
            if (Members.Remove(s))
            {
                if (s.Squad == this) s.Squad = null;
                needAssign = true;
                Dirty = true;
            }
        }

        // ------------------------------------------------------------------ приказы (сервер)
        public void SetOrder(OrderType o, Vector3 pos, float yaw = float.NaN, ushort follow = 0, int post = -1)
        {
            Order = o;
            OrderPos = pos;
            OrderYaw = yaw;
            FollowId = follow;
            TargetPost = post;
            repathAt = 0f;
            needAssign = true;
            Dirty = true;
        }

        public void SetFormation(FormationType f, float spacing = -1f)
        {
            Formation = f;
            Spacing = spacing > 0 ? spacing : Formations.DefaultSpacing(f);
            needAssign = true;
            Dirty = true;
        }

        /// <summary>Поставить якорь в центр бойцов (при создании и телепортах).</summary>
        public void ResetAnchor()
        {
            Anchor = Center;
            var f = Members.FirstOrDefault(m => m && !m.Dead);
            Yaw = f ? f.transform.eulerAngles.y : 0f;
            needAssign = true;
        }

        // ------------------------------------------------------------------ сервер: шаг
        public void Tick(float dt)
        {
            for (int i = Members.Count - 1; i >= 0; i--)
                if (!Members[i] || Members[i].Dead) { Members.RemoveAt(i); needAssign = true; Dirty = true; }
            if (Members.Count == 0) return;

            Vector3 goal = Anchor;
            float goalYaw = OrderYaw;
            switch (Order)
            {
                case OrderType.Hold:
                    goal = Anchor;
                    break;
                case OrderType.Follow:
                    if (Soldier.ById.TryGetValue(FollowId, out var leader) && leader && !leader.Dead)
                    {
                        var fwd = leader.transform.forward;
                        goal = leader.transform.position - fwd * (4f + Spacing);
                        if ((goal - Anchor).sqrMagnitude < 9f) goal = Anchor;          // не дёргаемся за каждым шагом
                        goalYaw = leader.transform.eulerAngles.y;
                    }
                    break;
                default:
                    goal = OrderPos;
                    break;
            }

            // путь якоря
            if (Time.time > repathAt || (goal - pathGoal).sqrMagnitude > 4f)
            {
                repathAt = Time.time + 2f;
                pathGoal = goal;
                corners.Clear();
                corner = 0;
                if ((goal - Anchor).sqrMagnitude > 1f)
                {
                    if (NavMesh.SamplePosition(Anchor, out var a, 6f, NavMesh.AllAreas) && NavMesh.SamplePosition(goal, out var b, 10f, NavMesh.AllAreas)
                        && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status != NavMeshPathStatus.PathInvalid)
                        corners.AddRange(path.corners);
                    else corners.Add(goal);
                }
            }

            // скорость якоря: по походке и самому медленному бойцу; ждём отстающих
            float speed = Gaits.Speed(EffectiveGait);
            float minMul = 1f;
            foreach (var m in Members) if (m.Type != null) minMul = Mathf.Min(minMul, m.Type.SpeedMul);
            speed *= minMul * 0.92f;
            if (Order == OrderType.Follow && (goal - Anchor).sqrMagnitude > 15f * 15f) speed = Mathf.Max(speed, Gaits.Speed(Gait.Run) * 0.9f);
            float lag = AverageLag();
            if (lag > 8f) speed = 0f;
            else if (lag > 4f) speed *= 0.4f;

            Moving = false;
            if (corner < corners.Count)
            {
                Vector3 target = corners[corner];
                Vector3 d = target - Anchor;
                d.y = 0;
                float dist = d.magnitude;
                if (dist < 0.5f) corner++;
                else
                {
                    Moving = speed > 0.01f;
                    Anchor += d / dist * Mathf.Min(dist, speed * dt);
                    if (NavMesh.SamplePosition(Anchor, out var h, 3f, NavMesh.AllAreas)) Anchor.y = h.position.y;
                    float want = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    Yaw = Mathf.MoveTowardsAngle(Yaw, want, 90f * dt);
                }
            }
            else if (!float.IsNaN(goalYaw))
                Yaw = Mathf.MoveTowardsAngle(Yaw, goalYaw, 120f * dt);

            UpdateSlots();
        }

        public Gait EffectiveGait
        {
            get
            {
                if (InCombat && Gait == Gait.March) return Gait.Tactical;
                return Gait;
            }
        }

        float AverageLag()
        {
            if (slotWorld.Count == 0) return 0f;
            float sum = 0f;
            int n = 0;
            foreach (var m in Members)
            {
                if (m.Slot < 0 || m.Slot >= slotWorld.Count) continue;
                var d = m.transform.position - slotWorld[m.Slot];
                d.y = 0;
                sum += d.magnitude;
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        void UpdateSlots()
        {
            int n = Members.Count;
            bool shape = n != lastCount || Formation != lastFormation || !Mathf.Approximately(Spacing, lastSpacing);
            bool moved = (Anchor - lastSlotAnchor).sqrMagnitude > 0.09f || float.IsNaN(lastSlotYaw) || Mathf.Abs(Mathf.DeltaAngle(Yaw, lastSlotYaw)) > 2f;
            if (!shape && !moved && !needAssign) return;
            if (shape)
            {
                Formations.Slots(Formation, n, Spacing, slots);
                lastCount = n;
                lastFormation = Formation;
                lastSpacing = Spacing;
                needAssign = true;
            }
            lastSlotAnchor = Anchor;
            lastSlotYaw = Yaw;
            var rot = Quaternion.Euler(0, Yaw, 0);
            slotWorld.Clear();
            slotYaw.Clear();
            for (int i = 0; i < slots.Count; i++)
            {
                Vector3 p = Anchor + rot * slots[i].Offset;
                if (NavMesh.SamplePosition(p, out var h, 2.5f, NavMesh.AllAreas)) p = h.position;
                slotWorld.Add(p);
                slotYaw.Add(Yaw + slots[i].Facing);
            }
            if (needAssign)
            {
                needAssign = false;
                var pos = Members.Select(m => m.transform.position).ToList();
                var a = Formations.Assign(pos, slotWorld);
                for (int i = 0; i < Members.Count; i++) Members[i].Slot = a[i];
            }
        }

        public bool SlotOf(Soldier s, out Vector3 pos, out float yaw)
        {
            if (s.Slot >= 0 && s.Slot < slotWorld.Count)
            {
                pos = slotWorld[s.Slot];
                yaw = slotYaw[s.Slot];
                return true;
            }
            pos = Anchor;
            yaw = Yaw;
            return false;
        }

        /// <summary>Конечная точка приказа (для меток на карте).</summary>
        public Vector3 Destination => Order == OrderType.Hold || Order == OrderType.Follow ? Anchor : OrderPos;

        // ------------------------------------------------------------------ сеть
        public void Write(NetWriter w)
        {
            w.UShort(Id); w.Byte(Owner); w.Byte((byte)Team); w.String(Name); w.Byte(Color);
            w.Byte((byte)Formation); w.Byte((byte)Mathf.Clamp(Mathf.RoundToInt(Spacing * 10f), 5, 255)); w.Byte((byte)Stance); w.Byte((byte)Gait); w.Byte((byte)Roe);
            w.Byte((byte)Order); w.Pos(Destination); w.Pos(Anchor); w.Angle(Yaw); w.Bool(InCombat); w.Short((short)TargetPost);
            w.Byte((byte)Members.Count);
            foreach (var m in Members) w.UShort(m.NetId);
        }

        public void Read(NetReader r)
        {
            Owner = r.Byte(); Team = (Team)r.Byte(); Name = r.String(); Color = r.Byte();
            Formation = (FormationType)r.Byte(); Spacing = r.Byte() / 10f; Stance = (Stance)r.Byte(); Gait = (Gait)r.Byte(); Roe = (Roe)r.Byte();
            Order = (OrderType)r.Byte(); OrderPos = r.Pos(); Anchor = r.Pos(); Yaw = r.Angle(); ClientInCombat = r.Bool(); TargetPost = r.Short();
            int n = r.Byte();
            MemberIds.Clear();
            Members.Clear();
            for (int i = 0; i < n; i++)
            {
                ushort id = r.UShort();
                MemberIds.Add(id);
                if (Soldier.ById.TryGetValue(id, out var s) && s) { Members.Add(s); s.Squad = this; }
            }
        }

        public bool ClientInCombat;
    }
}
