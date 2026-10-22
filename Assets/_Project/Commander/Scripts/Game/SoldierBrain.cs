using SW.Battle;
using UnityEngine;
using UnityEngine.AI;

namespace SW.Commander
{
    /// <summary>
    /// ИИ бойца (только на сервере). Держит своё место в строю и стойку отряда; при контакте с противником —
    /// боевой режим: цель по линии видимости, огонь очередями по правилам огня отряда, укрытия при обороне и
    /// под подавлением, перезарядка. Строевые стойки («смирно», «вольно») в бою сменяются на боевую.
    /// </summary>
    [RequireComponent(typeof(Soldier))]
    public class SoldierBrain : MonoBehaviour
    {
        Soldier self;
        Unit target;
        float nextThink, burstCooldown, coverUntil, alertedAt = -99f, lastSeen = -99f;
        int burstLeft;
        CoverPoint cover;
        Vector3 moveGoal;
        bool hasGoal;
        public static float EmpireAccuracy = 1f;       // сложность: множитель точности ИИ Империи
        public static float RepublicAccuracy = 1.15f;

        void Awake() => self = GetComponent<Soldier>();

        void OnDisable()
        {
            if (cover) cover.Release(this);
            cover = null;
        }

        void Start() => nextThink = Time.time + Random.value * 0.4f;

        float Range => self.Type?.Weapon.Range ?? 60f;

        static readonly RaycastHit[] seeHits = new RaycastHit[16];

        bool CanSee(Unit u)
        {
            Vector3 a = self.Eye, b = u.Center;
            Vector3 d = b - a;
            float dist = d.magnitude;
            int n = Physics.RaycastNonAlloc(a, d / dist, seeHits, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Unit first = null;
            bool blocked = false;
            for (int i = 0; i < n; i++)
            {
                var c = seeHits[i].collider;
                var hb = c.GetComponent<Hitbox>();
                var owner = hb ? hb.Owner : c.GetComponentInParent<Unit>();
                if (owner == self) continue;                       // свои руки и оружие перед лицом
                if (seeHits[i].distance >= best) continue;
                best = seeHits[i].distance;
                first = owner;
                blocked = owner == null;
            }
            if (best == float.MaxValue) return true;
            return !blocked && (first == u || (first && first.Team == u.Team));   // юнит рядом с целью — тоже видно
        }

        Unit PickTarget()
        {
            Unit best = null;
            float bs = float.MaxValue;
            float maxR = Range * 1.3f;
            Vector3 p = transform.position;
            foreach (var u in Unit.All)
            {
                if (!u || u.Dead || u.Team == self.Team) continue;
                float d = (u.transform.position - p).sqrMagnitude;
                if (d > maxR * maxR) continue;
                d = Mathf.Sqrt(d);
                float s = d / Mathf.Max(0.3f, u.Threat);
                if (u == self.LastAttacker) s *= 0.5f;
                if (u == target) s *= 0.7f;
                if (s >= bs) continue;
                if (d < 6f || CanSee(u)) { bs = s; best = u; }
            }
            return best;
        }

        bool MayShoot(Squad sq)
        {
            if (sq == null) return true;
            switch (sq.Roe)
            {
                case Roe.HoldFire: return false;
                case Roe.ReturnFire: return Time.time - sq.LastAttacked < 12f || Time.time - self.LastHitTime < 12f;
                default: return true;
            }
        }

        void Think()
        {
            var sq = self.Squad;
            var prev = target;
            target = PickTarget();
            if (target)
            {
                lastSeen = Time.time;
                if (sq != null) sq.LastContact = Time.time;
                if (!prev && Time.time - alertedAt > 20f)
                {
                    alertedAt = Time.time;
                    BattleNet.Voice(self, "alert");
                }
            }
            if (Time.time - self.LastHitTime < 0.5f && sq != null) sq.LastAttacked = Time.time;

            bool combat = (target && MayShoot(sq)) || Time.time - self.LastHitTime < 6f;
            bool underFire = Time.time < self.SuppressedUntil || Time.time - self.LastHitTime < 2f;

            // --- место в строю
            Vector3 slot = transform.position;
            float slotYaw = transform.eulerAngles.y;
            bool haveSlot = sq != null && sq.SlotOf(self, out slot, out slotYaw);
            var order = sq?.Order ?? OrderType.Hold;
            Gait gait = sq?.EffectiveGait ?? Gait.Walk;
            Stance stance = sq?.Stance ?? Stance.Combat;

            // атака: свободный бой в районе приказа
            if (order == OrderType.Attack && sq != null && target && (transform.position - sq.OrderPos).sqrMagnitude < 40f * 40f)
            {
                float dist = Vector3.Distance(target.transform.position, transform.position);
                float pref = Range * 0.45f;
                if (dist > pref * 1.3f || !CanSee(target)) Goal(target.transform.position + (transform.position - target.transform.position).normalized * pref);
                else if (underFire && Time.time > coverUntil) TryCover(sq);
                else Goal(transform.position);
            }
            else if ((order == OrderType.Defend || (underFire && order == OrderType.Hold && stance != Stance.Prone)) && combat && target && Time.time > coverUntil)
            {
                if (!TryCover(sq) && haveSlot) Goal(slot);
            }
            else if (cover && Time.time < coverUntil && combat)
            {
                Goal(cover.transform.position);
            }
            else
            {
                if (cover) { cover.Release(this); cover = null; }
                if (haveSlot) Goal(slot);
            }

            // --- анимация: походка и стойка
            bool moving = hasGoal && (moveGoal - transform.position).sqrMagnitude > 0.5f;
            if (combat && gait != Gait.Run)
            {
                self.MoveClip = gait == Gait.CrouchWalk ? "CrouchWalk" : "WalkAim";
                self.AimMove = true;
            }
            else
            {
                self.MoveClip = Gaits.Clip(gait);
                self.AimMove = false;
            }
            Stance idle = stance;
            if (combat && (stance == Stance.AtEase || stance == Stance.Attention || stance == Stance.Ready)) idle = Stance.Combat;
            if (cover && Time.time < coverUntil) idle = cover.Low ? Stance.Crouch : Stance.Combat;
            self.IdleClip = Gaits.StanceClip(idle, combat);
            if (gait == Gait.March && sq != null) self.SyncPhase = sq.MarchPhase(self);
            else self.SyncPhase = -1f;

            // --- скорость агента
            var ag = self.Agent;
            if (ag && ag.enabled)
            {
                float sp = Gaits.Speed(combat && gait != Gait.Run ? (gait == Gait.CrouchWalk ? Gait.CrouchWalk : Gait.Tactical) : gait);
                // догоняем строй бегом, если сильно отстали
                if (haveSlot && (slot - transform.position).sqrMagnitude > 64f && !combat) sp = Gaits.Speed(Gait.Run);
                else if (haveSlot && (slot - transform.position).sqrMagnitude > 9f) sp *= 1.35f;
                ag.speed = sp * (self.Type?.SpeedMul ?? 1f);
            }

            // --- взгляд
            if (target && combat) self.Face = target.transform.position;
            else if (!moving && haveSlot)
            {
                var r = Quaternion.Euler(0, slotYaw, 0);
                self.Face = transform.position + r * Vector3.forward * 10f;
            }
            else self.Face = null;
        }

        bool TryCover(Squad sq)
        {
            if (!target) return false;
            Vector3 around = transform.position;
            if (sq != null && sq.SlotOf(self, out var sp, out _)) around = sp;
            var c = CoverPoint.Find(around, target.transform.position, 10f, this);
            if (!c) return false;
            if (cover && cover != c) cover.Release(this);
            cover = c;
            coverUntil = Time.time + Random.Range(5f, 9f);
            Goal(c.transform.position);
            return true;
        }

        void Goal(Vector3 p)
        {
            var ag = self.Agent;
            if (!ag || !ag.enabled) return;
            if (!ag.isOnNavMesh && !self.SnapToNavMesh())
            {
                // NavMesh нет — идём напрямую (запасной вариант, чтобы армия не стояла)
                moveGoal = p;
                hasGoal = true;
                direct = true;
                return;
            }
            direct = false;
            if (hasGoal && (p - moveGoal).sqrMagnitude < 0.25f) return;
            moveGoal = p;
            hasGoal = true;
            if ((p - transform.position).sqrMagnitude < 0.36f) { ag.ResetPath(); return; }
            if (NavMesh.SamplePosition(p, out var h, 3f, NavMesh.AllAreas)) ag.SetDestination(h.position);
        }

        bool direct;

        void DirectMove()
        {
            if (!direct || !hasGoal) return;
            Vector3 d = moveGoal - transform.position;
            d.y = 0;
            float dist = d.magnitude;
            if (dist < 0.4f) return;
            float sp = self.Agent ? self.Agent.speed : 1.5f;
            Vector3 p = transform.position + d / dist * Mathf.Min(dist, sp * Time.deltaTime);
            p.y = MapInfo.GroundHeight(p);
            transform.position = p;
        }

        void Update()
        {
            if (self.Dead) return;
            DirectMove();
            if (Time.time >= nextThink)
            {
                nextThink = Time.time + 0.3f + Random.value * 0.15f;
                Think();
            }
            Shoot();
        }

        void Shoot()
        {
            if (!target || target.Dead) return;
            var sq = self.Squad;
            if (!MayShoot(sq) && Time.time - self.LastHitTime > 6f) return;
            if (self.Reloading) return;
            if (self.Ammo <= 0)
            {
                self.StartReload();
                return;
            }
            var w = self.Type?.Weapon;
            if (w == null) return;
            float dist = Vector3.Distance(target.transform.position, transform.position);
            if (dist > w.Range) return;
            bool running = self.MoveClip == "Run" && self.Velocity.magnitude > 2.5f;
            if (running || (self.Busy && self.CurrentClip != "Fire" && self.CurrentClip != "FireAuto" && self.CurrentClip != "ProneFire")) return;
            Vector3 to = target.Center - self.Muzzle;
            Vector3 flat = new Vector3(to.x, 0, to.z);
            if (Vector3.Angle(transform.forward, flat) > 25f) return;
            if (burstLeft <= 0)
            {
                if (Time.time < burstCooldown) return;
                burstLeft = self.Type.Role == Role.Heavy ? Random.Range(8, 16) : self.Type.Role == Role.Sniper || self.Type.Role == Role.Rocket ? 1 : Random.Range(2, 5);
            }
            if (Time.time < self.NextShot) return;
            // точность: дистанция, движение (своё и цели), подавление, стойка
            float spread = w.Spread + dist * 0.03f;
            if (self.Velocity.magnitude > 0.4f) spread += 1.4f;
            var tAgent = target.GetComponent<NavMeshAgent>();
            if (tAgent && tAgent.enabled) spread += tAgent.velocity.magnitude * 0.4f;
            if (Time.time < self.SuppressedUntil) spread += 1.5f;
            string idle = self.CurrentClip ?? "";
            if (idle.StartsWith("Prone")) spread *= 0.6f;
            else if (idle.StartsWith("Crouch")) spread *= 0.8f;
            float acc = (self.Type?.Accuracy ?? 1f) * (self.Team == Team.Empire ? EmpireAccuracy : RepublicAccuracy);
            spread /= Mathf.Max(0.2f, acc);
            Vector3 aim = target.Center + Vector3.up * Random.Range(-0.2f, 0.25f) - self.Muzzle;
            if (self.Fire(aim, spread, true))
            {
                burstLeft--;
                if (burstLeft <= 0) burstCooldown = Time.time + Random.Range(0.5f, 1.3f) * (self.Type.Role == Role.Sniper ? 2.5f : 1f);
                if (Random.value < 0.01f) BattleNet.Voice(self, "suppressing");
            }
        }
    }
}
