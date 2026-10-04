using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SW.Battle
{
    /// <summary>
    /// ИИ техники. Шагоходы (AT-ST, AT-RT) держат дистанцию, поворачивают рубку к цели и бьют тяжёлыми
    /// болтами (у AT-ST — взрывными). Спидеры носятся «заходами»: разгон мимо врага с огнём, разворот, новый заход.
    /// При уничтожении: клип Death, взрыв, дым, экипаж погибает.
    /// </summary>
    public class VehicleAI : MonoBehaviour
    {
        public bool Walker = true;
        public float Speed = 2.2f;
        public float Range = 60f;
        public float FireInterval = 1.1f;
        public float BoltDamage = 60f;
        public float Splash;               // радиус взрыва болта
        public float BoltSize = 2f;
        public Color BoltColor = new Color(1f, 0.15f, 0.1f);
        public float WalkClipSpeed = 2.2f; // скорость, под которую сделан клип ходьбы
        public string MoveClip = "Walk";
        public float StepPeriod = 2.4f;    // длительность цикла ходьбы (с)

        Unit self;
        NavMeshAgent agent;
        Animator anim;
        Transform head;
        readonly List<Transform> muzzles = new List<Transform>();
        Unit target;
        float nextThink, nextShot;
        int muzzleIdx;
        string current;
        Vector3 runPoint;
        bool dead;
        public GameObject Crew;
        AudioSource engine;
        float stepTimer;

        void Start()
        {
            self = GetComponent<Unit>();
            agent = GetComponent<NavMeshAgent>();
            anim = GetComponentInChildren<Animator>();
            if (anim) anim.applyRootMotion = false;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Head") head = t;
                if (t.name.StartsWith("Muzzle")) muzzles.Add(t);
            }
            agent.speed = Speed;
            agent.updateRotation = true;
            agent.angularSpeed = Walker ? 60f : 160f;
            agent.acceleration = Walker ? 2f : 10f;
            self.Died += OnDied;
            if (!Walker) engine = Sfx.Loop("speeder", transform, 0.6f, 4f, 80f);
        }

        void Play(string clip, float fade = 0.3f)
        {
            if (!anim || current == clip) return;
            anim.CrossFadeInFixedTime(clip, fade, 0, 0f);
            current = clip;
        }

        Unit Pick()
        {
            Unit best = null;
            float bs = float.MaxValue;
            foreach (var u in Unit.Enemies(self.Team))
            {
                float d = Vector3.Distance(u.transform.position, transform.position);
                float s = d / Mathf.Max(0.3f, u.Threat) * (u.IsVehicle ? 0.6f : 1f);
                if (s < bs) { bs = s; best = u; }
            }
            return best;
        }

        void Think()
        {
            target = Pick();
            if (!target || !agent.isOnNavMesh) return;
            float d = Vector3.Distance(target.transform.position, transform.position);
            if (Walker)
            {
                if (d > Range * 0.6f) agent.SetDestination(target.transform.position);
                else if (d < Range * 0.25f) agent.SetDestination(transform.position + (transform.position - target.transform.position).normalized * 10f);
                else agent.ResetPath();
            }
            else
            {
                // заход на цель: точка за целью, потом разворот
                if (!agent.hasPath || agent.remainingDistance < 4f)
                {
                    Vector3 through = (target.transform.position - transform.position).normalized;
                    runPoint = target.transform.position + through * Random.Range(18f, 30f) + Vector3.Cross(Vector3.up, through) * Random.Range(-10f, 10f);
                    if (NavMesh.SamplePosition(runPoint, out var h, 8f, NavMesh.AllAreas)) agent.SetDestination(h.position);
                }
            }
        }

        void Update()
        {
            if (dead) return;
            if (Time.time > nextThink) { nextThink = Time.time + 0.5f; Think(); }
            float v = agent.velocity.magnitude;
            Play(v > 0.3f ? MoveClip : "Idle");
            if (anim) anim.speed = v > 0.3f && Walker ? Mathf.Clamp(v / WalkClipSpeed, 0.5f, 1.5f) : 1f;
            if (engine) engine.pitch = 0.8f + Mathf.Clamp01(v / Mathf.Max(1f, Speed)) * 0.7f;
            if (Walker && v > 0.3f)
            {
                // шаг: дважды за цикл ходьбы
                stepTimer -= Time.deltaTime * Mathf.Clamp(v / WalkClipSpeed, 0.5f, 1.5f);
                if (stepTimer <= 0f)
                {
                    stepTimer = StepPeriod * 0.5f;
                    Sfx.Play("walker_step", transform.position, self.MaxHealth > 1000 ? 1f : 0.7f, self.MaxHealth > 1000 ? 0.8f : 1.1f, 6f, 120f);
                }
            }
            if (!target || target.Dead) return;
            Vector3 to = target.Center - (head ? head.position : transform.position);
            float dist = to.magnitude;
            bool facing = Vector3.Angle(Flat(transform.forward), Flat(to)) < (Walker ? 50f : 15f);
            if (dist < Range && facing && Time.time > nextShot && muzzles.Count > 0)
            {
                nextShot = Time.time + FireInterval * Random.Range(0.8f, 1.2f);
                var m = muzzles[muzzleIdx++ % muzzles.Count];
                Vector3 dir = (target.Center + Random.insideUnitSphere * (0.4f + dist * 0.02f) - m.position).normalized;
                BlasterBolt.Fire(self, m.position, dir, BoltDamage, 80f, BoltColor, Splash, BoltSize);
                if (anim && Walker && Random.value < 0.5f) { anim.CrossFadeInFixedTime("Fire", 0.05f, 0, 0f); current = "Fire"; }
            }
        }

        void LateUpdate()
        {
            // рубка шагохода смотрит на цель (поверх анимации)
            if (dead || !Walker || !head || !target || target.Dead) return;
            Vector3 to = target.Center - head.position;
            Vector3 local = transform.InverseTransformDirection(to);
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -70f, 70f);
            head.rotation = Quaternion.AngleAxis(yaw, transform.up) * head.rotation;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void OnDied(DamageInfo d)
        {
            dead = true;
            if (engine) Destroy(engine.gameObject);
            if (agent && agent.enabled) { if (agent.isOnNavMesh) agent.ResetPath(); agent.enabled = false; }
            if (anim) { anim.speed = 1f; anim.CrossFadeInFixedTime("Death", 0.1f, 0, 0f); }
            Fx.Explosion(transform.position + Vector3.up * (Walker ? 4f : 1f), Walker ? 6f : 4f);
            Fx.Smoke(transform, Walker ? 3f : 1f);
            if (Crew)
            {
                var cu = Crew.GetComponent<Unit>();
                Crew.transform.SetParent(null, true);
                if (cu && !cu.Dead)
                    cu.Damage(new DamageInfo { Amount = 9999f, Point = Crew.transform.position, Direction = Vector3.up + Random.insideUnitSphere,
                        Kind = DamageKind.Explosion, Impulse = 30f });
            }
            foreach (var c in GetComponentsInChildren<Collider>()) if (!(c is CharacterController)) c.enabled = true;
        }
    }
}
