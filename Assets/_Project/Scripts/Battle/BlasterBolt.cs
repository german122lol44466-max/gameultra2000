using UnityEngine;

namespace SW.Battle
{
    /// <summary>
    /// Болт бластера: летит с проверкой лучом между кадрами, наносит урон по хитбоксам (голова x2.5),
    /// может быть отражён световым мечом, тяжёлые болты техники взрываются.
    /// </summary>
    public class BlasterBolt : MonoBehaviour
    {
        public Unit Shooter;
        public Team Team;
        public float Damage = 20f;
        public float Speed = 60f;
        public float Life = 2.5f;
        public float SplashRadius;           // > 0 — взрывной болт
        public Color Color = new Color(1f, 0.15f, 0.1f);
        public bool Reflected;

        Vector3 velocity;
        static readonly RaycastHit[] hits = new RaycastHit[16];

        public static BlasterBolt Fire(Unit shooter, Vector3 from, Vector3 dir, float damage, float speed, Color color,
                                       float splash = 0f, float thickness = 1f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(go.GetComponent<Collider>());
            go.name = "Bolt";
            go.transform.position = from;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            go.transform.localScale = new Vector3(0.04f, 0.32f, 0.04f) * thickness;
            go.GetComponent<Renderer>().sharedMaterial = BoltMaterial(color);
            var l = go.AddComponent<Light>();
            l.color = color;
            l.range = 3f * thickness;
            l.intensity = 2.5f;
            l.shadows = LightShadows.None;
            var b = go.AddComponent<BlasterBolt>();
            b.Shooter = shooter;
            b.Team = shooter ? shooter.Team : Team.Empire;
            b.Damage = damage;
            b.Speed = speed;
            b.Color = color;
            b.SplashRadius = splash;
            b.velocity = dir.normalized * speed;
            Fx.Flash(from, color, 3f, 3f, 0.05f);
            return b;
        }

        static Material redMat, blueMat;

        static Material BoltMaterial(Color c)
        {
            bool red = c.r > c.b;
            ref Material m = ref red ? ref redMat : ref blueMat;
            if (!m)
            {
                m = new Material(Shader.Find("Standard"));
                m.color = Color.white;
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * 6f);
            }
            return m;
        }

        public void Reflect(Vector3 newDir, Unit by)
        {
            Reflected = true;
            Team = by.Team;
            Shooter = by;
            velocity = newDir.normalized * Speed;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, newDir);
            Life = 2f;
            Fx.Sparks(transform.position, -newDir, new Color(1f, 0.85f, 0.5f));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 step = velocity * dt;
            Vector3 p = transform.position;
            // «подавление»: болт пролетает рядом с врагом
            int n = Physics.RaycastNonAlloc(p, velocity.normalized, hits, step.magnitude + 0.05f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bi = -1;
            for (int i = 0; i < n; i++)
            {
                var hb = hits[i].collider.GetComponent<Hitbox>();
                if (hb && hb.Owner && (hb.Owner == Shooter || hb.Owner.Team == Team)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; bi = i; }
            }
            // световой меч рядом — дуэлянт может отразить
            foreach (var u in Unit.All)
            {
                if (!u || u.Dead || !u.IsDuelist || u.Team == Team) continue;
                Vector3 to = u.Center - p;
                if (to.sqrMagnitude < 4f && Vector3.Dot(to, velocity) > 0f)
                {
                    var d = u.GetComponent<DuelistAI>();
                    if (d && d.TryDeflect(this)) return;
                }
                if (to.sqrMagnitude < 9f) u.SuppressedUntil = Time.time + 1.5f;
            }
            foreach (var u in Unit.All)
            {
                if (u && u.Team != Team && (u.Center - p).sqrMagnitude < 6f) u.SuppressedUntil = Time.time + 2f;
            }
            if (bi >= 0)
            {
                Hit(hits[bi]);
                return;
            }
            transform.position = p + step;
            Life -= dt;
            if (Life <= 0f) Destroy(gameObject);
        }

        void Hit(RaycastHit h)
        {
            var hb = h.collider.GetComponent<Hitbox>();
            Unit target = hb ? hb.Owner : h.collider.GetComponentInParent<Unit>();
            if (SplashRadius > 0f)
            {
                Explode(h.point);
            }
            else if (target && target.Team != Team)
            {
                float mult = hb ? hb.Multiplier : 1f;
                target.Damage(new DamageInfo
                {
                    Amount = Damage * mult, Point = h.point, Direction = velocity.normalized, Kind = DamageKind.Blaster,
                    Attacker = Shooter, Headshot = hb && hb.IsHead, Impulse = 6f
                });
            }
            Fx.Sparks(h.point, h.normal, Color);
            Destroy(gameObject);
        }

        void Explode(Vector3 at)
        {
            Fx.Explosion(at, SplashRadius);
            foreach (var u in Unit.All.ToArray())
            {
                if (!u || u.Dead) continue;
                float d = Vector3.Distance(u.Center, at);
                if (d > SplashRadius) continue;
                float k = 1f - d / SplashRadius;
                u.Damage(new DamageInfo
                {
                    Amount = Damage * (0.35f + 0.65f * k), Point = at, Direction = (u.Center - at).normalized + Vector3.up * 0.6f,
                    Kind = DamageKind.Explosion, Attacker = Shooter, Impulse = 40f * k + 10f
                });
            }
        }
    }
}
