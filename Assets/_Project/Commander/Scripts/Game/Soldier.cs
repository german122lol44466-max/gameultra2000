using System;
using System.Collections.Generic;
using SW.Battle;
using SW.Commander.Net;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace SW.Commander
{
    public enum Authority : byte { Server, Owner, Puppet }

    /// <summary>
    /// Боец (клон, штурмовик, командир игрока). Один префаб на всех машинах:
    ///  Server — симулирует хост (NavMeshAgent + SoldierBrain);
    ///  Owner  — командир, которым управляет локальный игрок;
    ///  Puppet — копия у клиентов (позиция и клип из снимков, плавная интерполяция).
    /// Анимации — клипы по имени (CrossFade), с заменой отсутствующих (Clips.Fallback).
    /// Смерть: клип смерти, на последнем кадре — рэгдолл.
    /// </summary>
    public class Soldier : Unit
    {
        public static readonly Dictionary<ushort, Soldier> ById = new Dictionary<ushort, Soldier>();
        public static readonly List<Soldier> Living = new List<Soldier>();
        public static event Action<Soldier, DamageInfo, string> Killed;     // сервер: смерть (клип смерти)

        [NonSerialized] public ushort NetId;
        [NonSerialized] public int TypeIndex;
        [NonSerialized] public UnitType Type;
        [NonSerialized] public byte Owner = 255;              // игрок-владелец (255 — ИИ)
        [NonSerialized] public Squad Squad;
        [NonSerialized] public int Slot = -1;
        [NonSerialized] public Authority Auth;
        [NonSerialized] public bool IsCommander;

        // оружие
        [NonSerialized] public int Ammo;
        [NonSerialized] public float NextShot, ReloadEnd;
        public bool Reloading => Time.time < ReloadEnd;

        // анимация
        Animator anim;
        readonly Dictionary<string, float> clipLen = new Dictionary<string, float>();
        string current, action;
        float actionEnd;
        [NonSerialized] public byte NetClip;                  // клип для снимка (сервер) / из снимка (клиент)
        [NonSerialized] public string IdleClip = "Idle";       // что делать на месте
        [NonSerialized] public string MoveClip = "Walk";       // как идти
        [NonSerialized] public bool AimMove;                   // на ходу с оружием у плеча (стрейф/назад)
        [NonSerialized] public Vector3? Face;                  // куда смотреть (иначе — по движению)
        [NonSerialized] public float SyncPhase = -1f;           // фаза строевого шага (в ногу)
        public Transform WeaponBone, HeadBone, ChestBone;

        // движение
        [NonSerialized] public NavMeshAgent Agent;
        [NonSerialized] public Ragdoll Body;
        [NonSerialized] public Vector3 Velocity;               // для пупетов — по интерполяции
        Vector3 lastPos;

        // интерполяция
        struct Snap { public float T; public Vector3 Pos; public float Yaw; }
        readonly Snap[] snaps = new Snap[16];
        int snapCount;
        public const float InterpDelay = 0.2f;

        // смерть
        float deathStart = -1f, deathLen;
        [NonSerialized] public float DiedAt;

        // ------------------------------------------------------------------ создание
        public void Init(ushort id, int typeIndex, Team team, Authority auth)
        {
            NetId = id;
            TypeIndex = typeIndex;
            Type = Catalog.I.Get(typeIndex);
            Team = team;
            Auth = auth;
            Title = Type?.Title ?? name;
            MaxHealth = Health = Type?.Health ?? 100f;
            IsCommander = Type != null && Type.Role == Role.Commander;
            Threat = IsCommander ? 2f : Type != null && Type.Role == Role.Heavy ? 1.4f : 1f;
            Ammo = Type?.Weapon.Magazine ?? 30;
            ById[id] = this;
            if (!Living.Contains(this)) Living.Add(this);

            anim = GetComponentInChildren<Animator>();
            if (anim)
            {
                anim.applyRootMotion = false;
                anim.cullingMode = auth == Authority.Server ? AnimatorCullingMode.CullUpdateTransforms : AnimatorCullingMode.CullUpdateTransforms;
            }
            if (Type != null)
                for (int i = 0; i < Type.ClipNames.Length && i < Type.ClipLengths.Length; i++)
                {
                    // только клипы, которые реально есть в контроллере (иначе CrossFade молча не играет)
                    if (anim && anim.runtimeAnimatorController && !anim.HasState(0, Animator.StringToHash(Type.ClipNames[i]))) continue;
                    clipLen[Type.ClipNames[i]] = Type.ClipLengths[i];
                }
            if (anim && !anim.runtimeAnimatorController) Debug.LogError($"[Командир] у {name} нет контроллера анимаций — пересоберите: Star Wars → Командир клонов → Собрать игру");
            else if (clipLen.Count == 0 && !warnedClips)
            {
                warnedClips = true;
                Debug.LogError($"[Командир] у {name} в контроллере нет клипов — модель импортировалась без анимаций. Пересоберите: Star Wars → Командир клонов → Собрать игру");
            }
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "Weapon.R": WeaponBone = t; break;
                    case "Head": HeadBone = t; break;
                    case "Chest": ChestBone = t; break;
                }
            }
            AimPoint = ChestBone;
            HeadPoint = HeadBone;
            Agent = GetComponent<NavMeshAgent>();
            if (Agent)
            {
                Agent.enabled = auth == Authority.Server;
                if (Agent.enabled)
                {
                    Agent.updateRotation = false;
                    Agent.angularSpeed = 540f;
                    Agent.acceleration = 10f;
                    Agent.autoBraking = true;
                    Agent.speed = Gaits.Speed(Gait.Walk) * (Type?.SpeedMul ?? 1f);
                    Agent.avoidancePriority = Random.Range(30, 70);
                    SnapToNavMesh();
                }
            }
            Body = GetComponent<Ragdoll>();
            if (Body) Body.Build(this);
            Died += OnDied;
            lastPos = transform.position;
            Play(Resolve("Idle"), 0f);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Living.Remove(this);
            if (ById.TryGetValue(NetId, out var s) && s == this) ById.Remove(NetId);
        }

        static bool warnedClips;

        /// <summary>Поставить агента на NavMesh (если спавн чуть мимо сетки). false — сетки рядом нет.</summary>
        public bool SnapToNavMesh()
        {
            if (!Agent || !Agent.enabled) return false;
            if (Agent.isOnNavMesh) return true;
            if (NavMesh.SamplePosition(transform.position, out var h, 25f, NavMesh.AllAreas)) return Agent.Warp(h.position);
            return false;
        }

        // ------------------------------------------------------------------ анимации
        public bool Has(string clip) => clipLen.ContainsKey(clip);
        public float Length(string clip) => clipLen.TryGetValue(clip, out var l) ? l : 1f;

        public string Resolve(string clip)
        {
            int guard = 0;
            while (!Has(clip) && Clips.Fallback.TryGetValue(clip, out var f) && guard++ < 6) clip = f;
            return Has(clip) ? clip : (Has("Idle") ? "Idle" : clip);
        }

        void Play(string clip, float fade)
        {
            if (!anim || current == clip) return;
            anim.CrossFadeInFixedTime(clip, fade, 0, 0f);
            current = clip;
            NetClip = Clips.Id(clip);
        }

        /// <summary>Действие поверх движения (выстрел, перезарядка, жест). Возвращает длительность.</summary>
        public float DoAction(string clip, float fade = 0.08f)
        {
            if (Dead) return 0f;
            clip = Resolve(clip);
            if (!Has(clip)) return 0f;
            if (anim) anim.CrossFadeInFixedTime(clip, fade, 0, 0f);
            current = clip;
            action = clip;
            actionEnd = Time.time + Length(clip);
            NetClip = Clips.Id(clip);
            return Length(clip);
        }

        public bool Busy => action != null && Time.time < actionEnd;
        public string CurrentClip => current;

        /// <summary>Пупет: клип из снимка сервера.</summary>
        public void ApplyNetClip(byte id)
        {
            if (Dead) return;
            var name = Resolve(Clips.Name(id));
            // короткие действия (выстрел) играем локально по событиям — не перебиваем их снимком
            if (Busy && (name == "Idle" || name == "Aim" || name == "CrouchAim" || name == "ProneAim")) return;
            if (name == "Fire" || name == "FireAuto" || name == "Reload" || name == "Hit" || name == "HitBack" || name == "Point" ||
                name == "Halt" || name == "Salute" || name == "GoProne" || name == "StandUp" || name == "ProneFire")
            {
                if (current != name) DoAction(name, 0.08f);
                return;
            }
            if (!Busy) Play(name, 0.18f);
        }

        void LateUpdateAnimation(float speed)
        {
            if (Busy || !anim) return;
            if (action != null) { action = null; current = null; }
            string clip;
            if (speed < 0.2f) clip = Resolve(IdleClip);
            else
            {
                clip = MoveClip;
                if (AimMove && Face.HasValue && clip != "Run")
                {
                    Vector3 local = transform.InverseTransformDirection(Velocity);
                    if (local.z < -0.5f * speed) clip = "WalkBack";
                    else if (Mathf.Abs(local.x) > Mathf.Abs(local.z) * 1.2f) clip = local.x > 0 ? "StrafeR" : "StrafeL";
                    else if (clip != "CrouchWalk") clip = "WalkAim";
                }
                clip = Resolve(clip);
            }
            bool changed = current != clip;
            Play(clip, 0.2f);
            if (changed && clip == "March" && SyncPhase >= 0f)
                anim.Play(clip, 0, SyncPhase);                     // строевой — в ногу со всем отрядом
            float rate = speed < 0.2f ? 1f : Mathf.Clamp(speed / Gaits.ClipSpeed(clip), 0.6f, 1.7f);
            anim.speed = rate;
        }

        // ------------------------------------------------------------------ кадр
        void Update()
        {
            if (Dead) { UpdateDeath(); return; }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (Auth == Authority.Puppet) Interpolate();
            UpdateCulling();
            Vector3 p = transform.position;
            Vector3 v = Auth == Authority.Server && Agent && Agent.enabled && Agent.isOnNavMesh ? Agent.velocity : (p - lastPos) / dt;
            v.y = 0;
            Velocity = Vector3.Lerp(Velocity, v, 1f - Mathf.Exp(-dt * 12f));
            lastPos = p;
            if (Auth == Authority.Server)
            {
                // поворот: к цели или по движению
                Vector3 look = Face.HasValue ? Face.Value - p : Velocity;
                look.y = 0;
                if (look.sqrMagnitude > 0.01f)
                {
                    var want = Quaternion.LookRotation(look.normalized);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 360f * dt);
                }
            }
            if (Auth != Authority.Puppet) LateUpdateAnimation(Velocity.magnitude);
            else if (anim)
            {
                float speed = Velocity.magnitude;
                string clip = current ?? "Idle";
                anim.speed = speed < 0.2f || Busy ? 1f : Mathf.Clamp(speed / Gaits.ClipSpeed(clip), 0.6f, 1.7f);
            }
        }

        float cullAt;

        /// <summary>Вблизи камеры — анимация всегда (неточные границы меша не «замораживают» бойца), вдали — экономим.</summary>
        void UpdateCulling()
        {
            if (!anim || Time.time < cullAt) return;
            cullAt = Time.time + 0.5f + Random.value * 0.2f;
            var cam = Camera.main;
            bool near = cam && (cam.transform.position - transform.position).sqrMagnitude < 150f * 150f;
            var mode = near || Auth == Authority.Owner ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;
            if (anim.cullingMode != mode) anim.cullingMode = mode;
        }

        // ------------------------------------------------------------------ сеть: интерполяция
        public void PushSnapshot(Vector3 pos, float yaw)
        {
            float t = Time.time;
            if (snapCount == snaps.Length)
            {
                Array.Copy(snaps, 1, snaps, 0, snaps.Length - 1);
                snapCount--;
            }
            snaps[snapCount++] = new Snap { T = t, Pos = pos, Yaw = yaw };
            if (snapCount == 1) { transform.position = pos; transform.rotation = Quaternion.Euler(0, yaw, 0); }
        }

        public void Teleport(Vector3 pos, float yaw)
        {
            snapCount = 0;
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            lastPos = pos;
            if (Agent && Agent.enabled) Agent.Warp(pos);
        }

        void Interpolate()
        {
            if (snapCount == 0) return;
            float t = Time.time - InterpDelay;
            int i = 0;
            while (i < snapCount - 1 && snaps[i + 1].T <= t) i++;
            Snap a = snaps[i];
            if (i == snapCount - 1)
            {
                // нет более нового снимка — немного экстраполируем и стоим
                transform.position = Vector3.Lerp(transform.position, a.Pos, 0.25f);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, a.Yaw, 0), 0.25f);
                return;
            }
            Snap b = snaps[i + 1];
            float k = Mathf.Clamp01((t - a.T) / Mathf.Max(0.001f, b.T - a.T));
            transform.position = Vector3.Lerp(a.Pos, b.Pos, k);
            transform.rotation = Quaternion.Slerp(Quaternion.Euler(0, a.Yaw, 0), Quaternion.Euler(0, b.Yaw, 0), k);
            if (i > 0)
            {
                Array.Copy(snaps, i, snaps, 0, snapCount - i);
                snapCount -= i;
            }
        }

        // ------------------------------------------------------------------ оружие
        public Vector3 Muzzle => WeaponBone && Type != null ? WeaponBone.TransformPoint(Type.Muzzle) : Eye + transform.forward * 0.5f;

        /// <summary>Сервер/владелец: выстрел в направлении dir с разбросом spreadDeg. lethal — наносит урон.</summary>
        public bool Fire(Vector3 dir, float spreadDeg, bool lethal, bool animate = true)
        {
            if (Dead || Type == null || Reloading) return false;
            if (Time.time < NextShot) return false;
            if (Ammo <= 0) { StartReload(); return false; }
            var w = Type.Weapon;
            NextShot = Time.time + w.Interval;
            Ammo--;
            dir = Quaternion.AngleAxis(Random.Range(-spreadDeg, spreadDeg), Vector3.up) *
                  Quaternion.AngleAxis(Random.Range(-spreadDeg, spreadDeg) * 0.6f, transform.right) * dir;
            Bolts.I.Fire(this, Muzzle, dir, w.Bolt, w.Damage, w.Splash, lethal, w.Sound);
            if (animate) FireAnim();
            return true;
        }

        public void FireAnim()
        {
            if (Busy) return;
            if (Velocity.magnitude > 0.4f) return;
            string idle = Resolve(IdleClip);
            if (idle == "ProneAim" || idle == "Prone") DoAction("ProneFire", 0.04f);
            else if (idle == "CrouchAim" || idle == "CrouchIdle" || idle == "Crouch") { /* стреляет с колена без отдельного клипа */ }
            else DoAction(Type != null && Type.Role == Role.Heavy && Has("FireAuto") ? "FireAuto" : "Fire", 0.04f);
        }

        public void StartReload()
        {
            if (Reloading || Type == null) return;
            ReloadEnd = Time.time + Type.Weapon.ReloadTime;
            Ammo = Type.Weapon.Magazine;
            string idle = Resolve(IdleClip);
            if (idle != "ProneAim" && idle != "Prone") DoAction("Reload", 0.1f);
            Audio.Reload(Type.Weapon.Sound, transform.position + Vector3.up);
        }

        // ------------------------------------------------------------------ урон и смерть
        public override void Damage(DamageInfo d)
        {
            if (Dead) return;
            if (Auth == Authority.Puppet && !(NetSession.Instance && NetSession.Instance.IsServer)) return;   // урон считает сервер
            base.Damage(d);
            if (!Dead && !Busy && Random.value < 0.3f && Velocity.magnitude < 0.5f)
            {
                Vector3 local = transform.InverseTransformDirection(d.Direction);
                DoAction(local.z > 0.2f ? "HitBack" : "Hit", 0.05f);
            }
        }

        bool netDeath;

        void OnDied(DamageInfo d)
        {
            if (netDeath) return;
            string clip = ChooseDeath(d);
            Killed?.Invoke(this, d, clip);
            PlayDeath(clip, d.Direction * d.Impulse, d.Point, d.Kind == DamageKind.Explosion);
        }

        string ChooseDeath(DamageInfo d)
        {
            if (d.Kind == DamageKind.Explosion) return "";
            Vector3 local = transform.InverseTransformDirection(d.Direction);
            string clip;
            if (d.Headshot && Random.value < 0.8f) clip = "Death_Headshot";
            else if (local.z > 0.3f) clip = Random.value < 0.6f ? "Death_Forward" : "Death_Knees";
            else
            {
                float r = Random.value;
                clip = r < 0.45f ? "Death_Back" : r < 0.7f ? "Death_Spin" : r < 0.85f ? "Death_Knees" : "Death_Back";
            }
            if (Resolve(IdleClip).StartsWith("Prone")) clip = "";      // лежачий — сразу рэгдолл
            return Has(clip) ? clip : "";
        }

        /// <summary>Визуал смерти (сервер — сразу, клиенты — по UnitDeath). clip пустой — сразу рэгдолл.</summary>
        public void PlayDeath(string clip, Vector3 impulse, Vector3 point, bool blast)
        {
            if (!Dead) ForceDead();
            DiedAt = Time.time;
            if (Agent && Agent.enabled)
            {
                if (Agent.isOnNavMesh) Agent.ResetPath();
                Agent.enabled = false;
            }
            var brain = GetComponent<SoldierBrain>();
            if (brain) brain.enabled = false;
            Living.Remove(this);
            if (anim) anim.speed = 1f;
            if (string.IsNullOrEmpty(clip) || !anim)
            {
                Body?.Enable(impulse + (blast ? Vector3.up * impulse.magnitude * 0.4f : Vector3.zero), point);
                return;
            }
            anim.CrossFadeInFixedTime(clip, 0.06f, 0, 0f);
            current = clip;
            deathStart = Time.time;
            deathLen = Length(clip);
        }

        void ForceDead()
        {
            // смерть пришла по сети: отметить мёртвым через базовый урон
            netDeath = true;
            Health = 0.01f;
            base.Damage(new DamageInfo { Amount = 1f, Kind = DamageKind.Blaster });
            netDeath = false;
        }

        void UpdateDeath()
        {
            if (deathStart < 0 || Body == null || Body.Active) return;
            if (Time.time - deathStart >= deathLen * 0.97f) Body.Enable(Vector3.zero, transform.position);
        }
    }
}
