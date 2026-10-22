using SW.Battle;
using SW.Commander.Net;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>
    /// Управление своим командиром (вид от третьего лица, как в Battlefront): WASD, Shift — бег, C — присесть,
    /// Z — лечь, ЛКМ — огонь, ПКМ — прицел, R — перезарядка, G — термальный детонатор.
    /// Позиция у владельца, по сети уходит AvatarInput; урон и смерть решает сервер.
    /// </summary>
    [RequireComponent(typeof(Soldier))]
    public class CommanderController : MonoBehaviour
    {
        [System.NonSerialized] public BattleNet Battle;
        public Soldier Self { get; private set; }
        public bool Aiming { get; private set; }
        public bool Crouched { get; private set; }
        public bool Prone { get; private set; }
        public bool Sprinting { get; private set; }
        public int Grenades { get; private set; } = 3;
        public float HurtFlash { get; private set; }
        public Vector3 AimPoint { get; private set; }
        public float Spread { get; private set; }
        /// <summary>Управление отдано интерфейсу (казарма, тактическая карта, пауза).</summary>
        public static bool Blocked;

        CharacterController cc;
        CommanderCamera cam;
        float vy, sendAt, grenadeAt, proneUntil, lastFire, bloom;
        readonly NetWriter w = new NetWriter(64);
        static readonly RaycastHit[] hits = new RaycastHit[24];

        void Start()
        {
            Self = GetComponent<Soldier>();
            if (Self.Agent) Self.Agent.enabled = false;
            cc = gameObject.AddComponent<CharacterController>();
            cc.radius = 0.35f;
            cc.height = 1.8f;
            cc.center = new Vector3(0, 0.92f, 0);
            cc.stepOffset = 0.45f;
            cc.slopeLimit = 50f;
            foreach (var c in GetComponentsInChildren<Collider>())
                if (c != cc) Physics.IgnoreCollision(cc, c, true);
            cam = CommanderCamera.Ensure();
            cam.Follow(this);
            Self.Died += _ => { if (cc) cc.enabled = false; };
        }

        public void OnHurt(Vector3 dir)
        {
            HurtFlash = 1f;
        }

        void Update()
        {
            HurtFlash = Mathf.MoveTowards(HurtFlash, 0f, Time.deltaTime * 1.5f);
            if (Self.Dead) return;
            float dt = Time.deltaTime;
            bool control = !Blocked;
            // --- стойка
            if (control && (Keys.Down(KeyCode.C) || Keys.Down(KeyCode.LeftControl)))
            {
                if (Prone) { Prone = false; Self.DoAction("StandUp"); proneUntil = Time.time + 0.6f; }
                else Crouched = !Crouched;
            }
            if (control && Keys.Down(KeyCode.Z))
            {
                Prone = !Prone;
                Crouched = false;
                Self.DoAction(Prone ? "GoProne" : "StandUp");
                proneUntil = Time.time + 0.7f;
            }
            Aiming = control && Keys.Mouse(1) && !Sprinting;
            Vector2 mv = control ? Keys.Move : Vector2.zero;
            Sprinting = control && Keys.Shift && mv.y > 0.1f && !Aiming && !Prone;
            if (Sprinting) Crouched = false;
            if (Prone && mv.sqrMagnitude > 0.01f && Time.time > proneUntil && Keys.Shift) { Prone = false; Self.DoAction("StandUp"); proneUntil = Time.time + 0.5f; }

            // --- движение относительно камеры
            float camYaw = cam ? cam.Yaw : transform.eulerAngles.y;
            var rot = Quaternion.Euler(0, camYaw, 0);
            Vector3 dir = rot * new Vector3(mv.x, 0, mv.y);
            float speed = Prone || Time.time < proneUntil ? 0f : Sprinting ? 5.6f : Crouched ? 1.3f : Aiming ? 1.7f : 2.4f;
            Vector3 vel = dir * speed;
            if (cc.isGrounded) vy = -1f;
            else vy -= 20f * dt;
            if (cc.enabled) cc.Move((vel + Vector3.up * vy) * dt);

            // --- поворот: при прицеливании/стрельбе — по камере, иначе по движению
            bool combatFacing = Aiming || Time.time - lastFire < 1.2f || Prone;
            if (combatFacing) transform.rotation = Quaternion.RotateTowards(transform.rotation, rot, 720f * dt);
            else if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 600f * dt);

            // --- анимация
            Self.AimMove = combatFacing && !Sprinting;
            Self.Face = combatFacing ? transform.position + rot * Vector3.forward * 10f : (Vector3?)null;
            if (Prone) { Self.IdleClip = Aiming || Time.time - lastFire < 1.2f ? "ProneAim" : "Prone"; Self.MoveClip = "Prone"; }
            else if (Crouched) { Self.IdleClip = combatFacing ? "CrouchAim" : "CrouchIdle"; Self.MoveClip = "CrouchWalk"; }
            else
            {
                Self.IdleClip = combatFacing ? "Aim" : "Idle";
                Self.MoveClip = Sprinting ? "Run" : combatFacing ? "WalkAim" : "Walk";
            }

            // --- прицел и огонь
            UpdateAim();
            bloom = Mathf.MoveTowards(bloom, 0f, dt * 4f);
            float moveSpread = vel.magnitude > 0.3f ? 1.2f : 0f;
            Spread = (Self.Type?.Weapon.Spread ?? 1f) * (Aiming ? 0.35f : 1f) * (Prone ? 0.5f : Crouched ? 0.75f : 1f) + moveSpread + bloom;
            if (control && Keys.Mouse(0) && !Sprinting && !Self.Reloading && Time.time > proneUntil) TryFire();
            if (control && Keys.Down(KeyCode.R) && Self.Type != null && Self.Ammo < Self.Type.Weapon.Magazine) Self.StartReload();
            if (control && Keys.Down(KeyCode.G)) ThrowGrenade();

            // --- сеть
            if (!Battle || !Battle.IsServer)
            {
                if (Time.time > sendAt)
                {
                    sendAt = Time.time + 0.05f;
                    w.Reset(Msg.AvatarInput).UShort(Self.NetId).Pos(transform.position).Angle16(transform.eulerAngles.y).Byte(Self.NetClip);
                    NetSession.Instance?.SendToServer(w, false);
                }
            }
        }

        void UpdateAim()
        {
            if (!cam) return;
            var c = cam.Cam;
            var ray = new Ray(c.transform.position, c.transform.forward);
            int n = Physics.RaycastNonAlloc(ray, hits, 600f, ~0, QueryTriggerInteraction.Ignore);
            float best = 600f;
            Vector3 p = ray.origin + ray.direction * 600f;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].distance >= best) continue;
                if (hits[i].collider.transform.IsChildOf(transform)) continue;
                best = hits[i].distance;
                p = hits[i].point;
            }
            AimPoint = p;
        }

        void TryFire()
        {
            if (Self.Type == null || Time.time < Self.NextShot) return;
            if (Self.Ammo <= 0) { Self.StartReload(); return; }
            Vector3 muzzle = Self.Muzzle;
            Vector3 d = (AimPoint - muzzle).normalized;
            d = Quaternion.AngleAxis(Random.Range(-Spread, Spread), Vector3.up) * Quaternion.AngleAxis(Random.Range(-Spread, Spread) * 0.6f, transform.right) * d;
            bool server = Battle && Battle.IsServer;
            if (!Self.Fire(d, 0f, server, animate: !Aiming)) return;
            lastFire = Time.time;
            bloom = Mathf.Min(bloom + 0.35f, 2.5f);
            if (cam) cam.Kick(Self.Type.Weapon.Damage > 60 ? 2.5f : 0.6f);
            if (!server)
            {
                w.Reset(Msg.CommanderFire).UShort(Self.NetId).Pos(muzzle).Dir(d);
                NetSession.Instance?.SendToServer(w, true);
            }
        }

        void ThrowGrenade()
        {
            if (Grenades <= 0 || Time.time < grenadeAt) return;
            grenadeAt = Time.time + 1.2f;
            Grenades--;
            Vector3 from = Self.Eye + transform.right * 0.2f + transform.forward * 0.4f;
            Vector3 aim = cam ? cam.Cam.transform.forward : transform.forward;
            Vector3 vel = (aim + Vector3.up * 0.25f).normalized * 17f;
            bool server = Battle && Battle.IsServer;
            Grenade.Throw(Self, from, vel, server);
            Self.DoAction("Point", 0.1f);
            if (!server)
            {
                w.Reset(Msg.CommanderGrenade).UShort(Self.NetId).Pos(from).Vector3(vel);
                NetSession.Instance?.SendToServer(w, true);
            }
        }

        public void Refill()
        {
            Grenades = 3;
            if (Self && Self.Type != null) Self.Ammo = Self.Type.Weapon.Magazine;
        }
    }
}
