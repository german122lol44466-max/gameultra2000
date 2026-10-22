using SW.Battle;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>
    /// Стационарная турель (сентри-пушка) на аванпосте: ищет цели в радиусе по линии видимости, поворачивается
    /// и бьёт очередями тяжёлых болтов. Это Unit — бойцы целятся в неё; уничтожается бластерами и взрывами.
    /// Сервер стреляет и считает здоровье; клиенты получают поворот/здоровье в StructureState, выстрелы — в Shots.
    /// </summary>
    public class Turret : Unit
    {
        public byte Id;
        public Transform Head;                  // поворотная часть (иначе — весь объект)
        public float Range = 70f;
        public float Damage = 26f;
        public float Interval = 0.22f;
        public int Burst = 5;
        public float TurnSpeed = 90f;
        [System.NonSerialized] public float NetYaw;
        Unit target;
        float nextThink, nextShot, cooldown;
        int shots;

        void Start()
        {
            if (!Head) Head = transform;
            MaxHealth = Health = Mathf.Max(MaxHealth, 350f);
            Threat = 1.8f;
            if (!AimPoint)
            {
                var a = new GameObject("Aim").transform;
                a.SetParent(transform, false);
                a.localPosition = Vector3.up * 0.8f;
                AimPoint = a;
            }
            if (!GetComponentInChildren<Collider>())
            {
                var c = gameObject.AddComponent<BoxCollider>();
                c.center = Vector3.up * 0.6f;
                c.size = new Vector3(1.4f, 1.4f, 1.4f);
            }
            foreach (var c in GetComponentsInChildren<Collider>())
            {
                var hb = c.gameObject.GetComponent<Hitbox>();
                if (!hb) hb = c.gameObject.AddComponent<Hitbox>();
                hb.Owner = this;
            }
            Died += _ =>
            {
                Fx.Explosion(Center, 3.5f);
                Fx.Smoke(transform, 0.8f);
                if (Head) Head.localRotation *= Quaternion.Euler(25f, 0, 10f);
            };
            NetYaw = Head.eulerAngles.y;
        }

        bool Server => Net.NetSession.Instance == null || Net.NetSession.Instance.IsServer;

        void Update()
        {
            if (Dead) return;
            if (!Server)
            {
                var want = Quaternion.Euler(0, NetYaw, 0);
                Head.rotation = Quaternion.RotateTowards(Head.rotation, want, TurnSpeed * 2f * Time.deltaTime);
                return;
            }
            if (Time.time > nextThink)
            {
                nextThink = Time.time + 0.5f;
                target = null;
                float best = Range;
                foreach (var u in Unit.All)
                {
                    if (!u || u.Dead || u.Team == Team || u is Turret) continue;
                    float d = Vector3.Distance(u.Center, Center);
                    if (d > best) continue;
                    Vector3 dir = (u.Center - Center) / d;
                    if (Physics.Raycast(Center + dir * 1.2f, dir, out var h, d - 1.2f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        var hb = h.collider.GetComponent<Hitbox>();
                        if ((hb ? hb.Owner : h.collider.GetComponentInParent<Unit>()) != u) continue;
                    }
                    best = d;
                    target = u;
                }
            }
            if (!target || target.Dead) return;
            Vector3 to = target.Center - Head.position;
            to.y = 0;
            var rot = Quaternion.LookRotation(to);
            Head.rotation = Quaternion.RotateTowards(Head.rotation, rot, TurnSpeed * Time.deltaTime);
            NetYaw = Head.eulerAngles.y;
            if (Quaternion.Angle(Head.rotation, rot) > 6f || Time.time < cooldown || Time.time < nextShot) return;
            nextShot = Time.time + Interval;
            Vector3 muzzle = Head.position + Head.forward * 0.9f + Vector3.up * 0.55f;
            Vector3 aim = target.Center + Random.insideUnitSphere * (0.4f + Vector3.Distance(muzzle, target.Center) * 0.012f) - muzzle;
            Bolts.I.Fire(this, muzzle, aim, Team == Team.Empire ? BoltKind.TurretRed : BoltKind.TurretBlue, Damage, 0f, true, "dlt19");
            if (++shots >= Burst) { shots = 0; cooldown = Time.time + Random.Range(0.8f, 1.6f); }
        }
    }
}
