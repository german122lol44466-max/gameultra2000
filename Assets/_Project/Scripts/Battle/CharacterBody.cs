using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SW.Battle
{
    /// <summary>
    /// «Тело» персонажа: NavMeshAgent + Animator. Выбирает клип передвижения по скорости и направлению относительно
    /// взгляда (Walk/Run/WalkAim/Strafe/WalkBack), проигрывает действия, при смерти — клип смерти по направлению урона,
    /// а с последним кадром переходит в рэгдолл.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class CharacterBody : MonoBehaviour
    {
        public string[] Clips = new string[0];
        public float[] Lengths = new float[0];
        public bool Saber;
        public float WalkSpeed = 1.45f, AimWalkSpeed = 1.0f, RunSpeed = 4.4f, StrafeSpeed = 0.8f;

        [System.NonSerialized] public NavMeshAgent Agent;
        [System.NonSerialized] public Animator Anim;
        [System.NonSerialized] public Unit Unit;
        [System.NonSerialized] public Ragdoll Ragdoll;

        public Transform WeaponBone, HandL, Head, Chest;
        public readonly List<Transform> Blades = new List<Transform>();

        public bool Aiming;                 // держит оружие у плеча / в стойке
        public bool Crouching;
        public Vector3? FaceTarget;         // куда смотреть (иначе — по ходу движения)
        string action;                      // текущее действие (атака, выстрел, перезарядка) — блокирует локомоцию
        float actionEnd;
        string current;
        float deathStart = -1f, deathLen;
        string deathClip;
        Dictionary<string, float> len = new Dictionary<string, float>();

        public bool Busy => action != null && Time.time < actionEnd;
        public string Action => Busy ? action : null;
        public float ActionProgress(float dur) => Busy ? 1f - (actionEnd - Time.time) / dur : 1f;

        void Awake()
        {
            Agent = GetComponent<NavMeshAgent>();
            Anim = GetComponentInChildren<Animator>();
            Unit = GetComponent<Unit>();
            Ragdoll = GetComponent<Ragdoll>();
            for (int i = 0; i < Clips.Length && i < Lengths.Length; i++) len[Clips[i]] = Lengths[i];
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "Weapon.R": WeaponBone = t; break;
                    case "Hand.L": HandL = t; break;
                    case "Head": Head = t; break;
                    case "Chest": Chest = t; break;
                    case "Blade.R":
                    case "Blade.R2": Blades.Add(t); break;
                }
            }
            if (Anim)
            {
                Anim.applyRootMotion = false;
                Anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
            if (Unit)
            {
                Unit.Died += Die;
                Unit.Damaged += OnDamaged;
            }
            Agent.updateRotation = false;
            Agent.angularSpeed = 540f;
            Agent.acceleration = 12f;
            Agent.autoBraking = true;
        }

        public bool Has(string clip) => len.ContainsKey(clip);
        public float Length(string clip) => len.TryGetValue(clip, out var l) ? l : 1f;

        void Play(string clip, float fade = 0.15f)
        {
            if (!Anim || current == clip || !Has(clip)) return;
            Anim.CrossFadeInFixedTime(clip, fade, 0, 0f);
            current = clip;
        }

        /// <summary>Действие поверх передвижения (стоит на месте). Возвращает длительность.</summary>
        public float DoAction(string clip, float fade = 0.08f, bool stop = true)
        {
            if (!Has(clip) || Unit.Dead) return 0f;
            if (Anim)
            {
                Anim.CrossFadeInFixedTime(clip, fade, 0, 0f);
            }
            current = clip;
            action = clip;
            actionEnd = Time.time + Length(clip);
            if (stop && Agent.enabled && Agent.isOnNavMesh) Agent.ResetPath();
            return Length(clip);
        }

        public void CancelAction() { action = null; current = null; }

        void Update()
        {
            if (Unit.Dead)
            {
                UpdateDeath();
                return;
            }
            Vector3 v = Agent.enabled ? Agent.velocity : Vector3.zero;
            v.y = 0;
            float speed = v.magnitude;
            // поворот: к цели, иначе по движению
            Vector3 look = FaceTarget.HasValue ? FaceTarget.Value - transform.position : v;
            look.y = 0;
            if (look.sqrMagnitude > 0.01f)
            {
                var want = Quaternion.LookRotation(look.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, (Busy ? 220f : 360f) * Time.deltaTime);
            }
            if (Busy) return;
            if (action != null) { action = null; current = null; }
            string clip;
            Vector3 local = transform.InverseTransformDirection(v);
            if (speed < 0.15f)
                clip = Crouching ? (Aiming && Has("CrouchAim") ? "CrouchAim" : "Crouch") : (Aiming && Has("Aim") ? "Aim" : "Idle");
            else if (speed > (WalkSpeed + RunSpeed) * 0.5f && local.z > 0.3f * speed)
                clip = "Run";
            else if (Aiming || FaceTarget.HasValue)
            {
                if (local.z < -0.5f * speed) clip = "WalkBack";
                else if (Mathf.Abs(local.x) > Mathf.Abs(local.z)) clip = local.x > 0 ? "StrafeR" : "StrafeL";
                else clip = Has("WalkAim") ? "WalkAim" : "Walk";
            }
            else clip = "Walk";
            if (!Has(clip)) clip = Has("Walk") ? "Walk" : "Idle";
            Play(clip);
            // скорость клипа под фактическую скорость
            if (Anim)
            {
                float refSpeed = clip == "Run" ? RunSpeed : clip == "Walk" ? WalkSpeed : clip.StartsWith("Strafe") ? StrafeSpeed : AimWalkSpeed;
                Anim.speed = speed < 0.15f ? 1f : Mathf.Clamp(speed / refSpeed, 0.6f, 1.6f);
            }
        }

        void OnDamaged(DamageInfo d)
        {
            if (Unit.Dead || Busy || Random.value > 0.45f) return;
            Vector3 local = transform.InverseTransformDirection(d.Direction);
            DoAction(local.z > 0.2f ? "HitBack" : "Hit", 0.05f, stop: false);
        }

        /// <summary>Выбор смерти: по виду урона и направлению; взрыв/толчок Силой — сразу в рэгдолл.</summary>
        public void Die(DamageInfo d)
        {
            if (Agent.enabled && Agent.isOnNavMesh) Agent.ResetPath();
            Agent.enabled = false;
            foreach (var c in GetComponents<MonoBehaviour>())
                if (c is SoldierAI || c is DuelistAI) c.enabled = false;
            if (Anim) Anim.speed = 1f;
            foreach (var b in Blades) b.localScale = new Vector3(1, 0.001f, 1);
            Vector3 imp = d.Direction.normalized * d.Impulse;
            if (d.Kind == DamageKind.Explosion || (d.Kind == DamageKind.Force && d.Impulse > 15f))
            {
                Ragdoll?.Enable(imp + Vector3.up * d.Impulse * 0.4f, d.Point);
                return;
            }
            Vector3 local = transform.InverseTransformDirection(d.Direction);
            string clip;
            if (d.Headshot && Random.value < 0.8f) clip = "Death_Headshot";
            else if (local.z > 0.3f) clip = Random.value < 0.6f ? "Death_Forward" : "Death_Knees";   // урон сзади — падает вперёд
            else
            {
                float r = Random.value;
                clip = r < 0.45f ? "Death_Back" : r < 0.7f ? "Death_Spin" : r < 0.85f ? "Death_Knees" : "Death_Back";
            }
            if (!Has(clip)) clip = "Death_Back";
            if (!Has(clip) || !Anim)
            {
                Ragdoll?.Enable(imp, d.Point);
                return;
            }
            Anim.CrossFadeInFixedTime(clip, 0.06f, 0, 0f);
            deathClip = clip;
            deathStart = Time.time;
            deathLen = Length(clip);
        }

        void UpdateDeath()
        {
            if (deathStart < 0 || Ragdoll == null || Ragdoll.Active) return;
            // последний кадр анимации смерти -> физика тела
            if (Time.time - deathStart >= deathLen * 0.97f)
                Ragdoll.Enable(Vector3.zero, transform.position);
        }
    }
}
