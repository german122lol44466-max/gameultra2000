using UnityEngine;
using UnityEngine.AI;

namespace SW.Battle
{
    /// <summary>
    /// ИИ стрелка (штурмовик/клон). Восприятие: видимые враги (луч из глаз), угроза, дистанция.
    /// Поведение: выбор цели → укрытие под огнём или при ранении → выглядывание и стрельба очередями →
    /// перезарядка в укрытии; без укрытия — стрейф с огнём; от джедая/ситха вблизи — отход с огнём.
    /// Точность зависит от дистанции, движения и подавления.
    /// </summary>
    [RequireComponent(typeof(CharacterBody))]
    public class SoldierAI : MonoBehaviour
    {
        public float Damage = 18f;
        public float BoltSpeed = 70f;
        public float RoundsPerMinute = 300f;
        public int Magazine = 25;
        public float Range = 55f;
        public float Accuracy = 1f;            // 1 — обычный солдат, 1.3 — командир
        public float PreferredRange = 22f;
        public bool Heavy;                      // тяжёлый: длинные очереди, болты сильнее
        public Color BoltColor = new Color(1f, 0.12f, 0.08f);
        public Vector3 MuzzleLocal = new Vector3(0, 0.45f, 0.06f);

        CharacterBody body;
        Unit self;
        Unit target;
        int ammo;
        float nextThink, nextShot, burstLeft, burstCooldown, strafeUntil, coverUntil;
        Vector3 strafeDir;
        CoverPoint cover;
        enum Mode { Advance, Fight, Cover, Retreat }
        Mode mode;
        static readonly int VisMask = ~0;

        void Start()
        {
            body = GetComponent<CharacterBody>();
            self = GetComponent<Unit>();
            ammo = Magazine;
            body.WalkSpeed = 1.45f;
            nextThink = Time.time + Random.value * 0.3f;
        }

        void OnDisable() { if (cover) cover.Release(this); }

        bool CanSee(Unit u)
        {
            Vector3 a = self.Eye, b = u.Center;
            Vector3 d = b - a;
            if (Physics.Raycast(a, d.normalized, out var h, d.magnitude, VisMask, QueryTriggerInteraction.Ignore))
            {
                var hb = h.collider.GetComponent<Hitbox>();
                var owner = hb ? hb.Owner : h.collider.GetComponentInParent<Unit>();
                return owner == u;
            }
            return true;
        }

        Unit PickTarget()
        {
            Unit best = null;
            float bs = float.MaxValue;
            foreach (var u in Unit.Enemies(self.Team))
            {
                float d = Vector3.Distance(u.transform.position, transform.position);
                if (d > Range * 1.6f) continue;
                float s = d / Mathf.Max(0.3f, u.Threat);
                if (u == self.LastAttacker) s *= 0.5f;
                if (u == target) s *= 0.7f;          // не прыгаем между целями
                if (s < bs && (d < 8f || CanSee(u))) { bs = s; best = u; }
            }
            return best;
        }

        void Think()
        {
            target = PickTarget();
            if (!target)
            {
                // нет видимых врагов — идти к ближайшему врагу или к центру поля
                Unit any = null;
                float bd = float.MaxValue;
                foreach (var u in Unit.Enemies(self.Team))
                {
                    float d = (u.transform.position - transform.position).sqrMagnitude;
                    if (d < bd) { bd = d; any = u; }
                }
                body.Aiming = false;
                body.FaceTarget = null;
                body.Crouching = false;
                if (any) MoveTo(any.transform.position, run: true);
                mode = Mode.Advance;
                return;
            }
            float dist = Vector3.Distance(target.transform.position, transform.position);
            bool underFire = Time.time < self.SuppressedUntil || Time.time - self.LastHitTime < 2f;
            bool hurt = self.Health < self.MaxHealth * 0.55f;
            bool melee = target.IsDuelist && dist < 9f;
            if (melee)
            {
                mode = Mode.Retreat;
                Vector3 away = (transform.position - target.transform.position).normalized;
                MoveTo(transform.position + away * 6f + Vector3.Cross(away, Vector3.up) * Random.Range(-3f, 3f), run: dist < 4f);
                return;
            }
            if ((underFire || hurt || ammo <= 0) && Time.time > coverUntil)
            {
                var c = CoverPoint.Find(transform.position, target.transform.position, 18f, this);
                if (c)
                {
                    if (cover && cover != c) cover.Release(this);
                    cover = c;
                    mode = Mode.Cover;
                    coverUntil = Time.time + Random.Range(4f, 7f);
                    MoveTo(c.transform.position, run: true);
                    return;
                }
            }
            if (mode == Mode.Cover && cover && Time.time < coverUntil)
            {
                if ((transform.position - cover.transform.position).sqrMagnitude > 1f) MoveTo(cover.transform.position, run: true);
                return;
            }
            if (cover) { cover.Release(this); cover = null; }
            mode = Mode.Fight;
            if (dist > PreferredRange * 1.4f || !CanSee(target))
            {
                MoveTo(target.transform.position, run: dist > PreferredRange * 2f);
            }
            else if (dist < PreferredRange * 0.5f)
            {
                MoveTo(transform.position + (transform.position - target.transform.position).normalized * 4f, run: false);
            }
            else if (Time.time > strafeUntil)
            {
                strafeUntil = Time.time + Random.Range(1.5f, 3.5f);
                if (Random.value < 0.55f)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, (target.transform.position - transform.position).normalized) * (Random.value < 0.5f ? -1 : 1);
                    MoveTo(transform.position + side * Random.Range(2f, 4f), run: false);
                }
                else if (body.Agent.isOnNavMesh) body.Agent.ResetPath();
            }
        }

        void MoveTo(Vector3 p, bool run)
        {
            var ag = body.Agent;
            if (!ag.enabled || !ag.isOnNavMesh) return;
            ag.speed = run ? body.RunSpeed : (body.Aiming ? body.AimWalkSpeed : body.WalkSpeed);
            if (NavMesh.SamplePosition(p, out var h, 4f, NavMesh.AllAreas)) ag.SetDestination(h.position);
        }

        void Update()
        {
            if (self.Dead) return;
            if (Time.time >= nextThink)
            {
                nextThink = Time.time + 0.35f + Random.value * 0.15f;
                Think();
            }
            if (!target || target.Dead)
            {
                body.FaceTarget = null;
                body.Aiming = false;
                return;
            }
            float dist = Vector3.Distance(target.transform.position, transform.position);
            bool inCover = mode == Mode.Cover && cover && (transform.position - cover.transform.position).sqrMagnitude < 1.5f;
            bool running = body.Agent.velocity.magnitude > body.WalkSpeed + 0.5f;
            body.FaceTarget = running ? (Vector3?)null : target.transform.position;
            body.Aiming = !running;
            // в укрытии: присел — перезарядка, затем выглядывает и стреляет
            if (inCover)
            {
                bool peek = (Time.time % 4f) > 1.6f && ammo > 0;
                body.Crouching = !peek || cover.Low;
                if (ammo <= 0 && !body.Busy) { body.DoAction("Reload"); ammo = Magazine; }
                if (!peek) return;
            }
            else body.Crouching = false;
            if (ammo <= 0)
            {
                if (!body.Busy) { body.DoAction("Reload", stop: false); ammo = Magazine; }
                return;
            }
            if (running || body.Busy || dist > Range) return;
            float facing = Vector3.Angle(transform.forward, Flat(target.transform.position - transform.position));
            if (facing > 25f) return;
            if (burstLeft <= 0)
            {
                if (Time.time < burstCooldown) return;
                burstLeft = Heavy ? Random.Range(6, 12) : Random.Range(2, 5);
            }
            if (Time.time < nextShot) return;
            nextShot = Time.time + 60f / RoundsPerMinute;
            burstLeft--;
            if (burstLeft <= 0) burstCooldown = Time.time + Random.Range(0.6f, 1.4f);
            Shoot(dist);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void Shoot(float dist)
        {
            ammo--;
            Vector3 muzzle = body.WeaponBone ? body.WeaponBone.TransformPoint(MuzzleLocal) : self.Eye;
            Vector3 aim = target.Center + Vector3.up * Random.Range(-0.25f, 0.3f);
            // точность: разброс растёт с дистанцией, движением (своим и цели) и под подавлением
            float spread = 1.2f + dist * 0.04f;
            if (body.Agent.velocity.magnitude > 0.3f) spread += 1.5f;
            var tb = target.GetComponent<NavMeshAgent>();
            if (tb && tb.enabled) spread += tb.velocity.magnitude * 0.5f;
            if (Time.time < self.SuppressedUntil) spread += 1.5f;
            spread /= Accuracy;
            Vector3 dir = (aim - muzzle).normalized;
            dir = Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0) * dir;
            BlasterBolt.Fire(self, muzzle, dir, Heavy ? Damage * 1.4f : Damage, BoltSpeed, BoltColor, 0f, Heavy ? 1.3f : 1f);
            if (body.Has("Fire") && !body.Busy && body.Agent.velocity.magnitude < 0.2f && Random.value < 0.35f)
                body.DoAction(Heavy && body.Has("FireAuto") ? "FireAuto" : "Fire", 0.05f, stop: false);
        }
    }
}
