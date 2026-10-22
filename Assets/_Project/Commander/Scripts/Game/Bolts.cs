using System.Collections.Generic;
using SW.Battle;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>
    /// Все болты бластеров одним менеджером (сотни выстрелов без сотен MonoBehaviour).
    /// На сервере болт наносит урон (луч между кадрами по хитбоксам), у клиентов — только визуал и искры.
    /// Свечение: эмиссивная капсула + свет у ближайших к камере болтов (bloom в постобработке).
    /// </summary>
    public class Bolts : MonoBehaviour
    {
        struct Bolt
        {
            public Vector3 Pos, Vel;
            public float Life, Damage, Splash;
            public Team Team;
            public Unit Shooter;
            public bool Lethal;
            public BoltKind Kind;
            public Transform View;
            public Light Light;
        }

        static Bolts inst;
        public static Bolts I
        {
            get
            {
                if (!inst) inst = new GameObject("[Bolts]").AddComponent<Bolts>();
                return inst;
            }
        }

        readonly List<Bolt> bolts = new List<Bolt>(512);
        readonly Stack<Transform> pool = new Stack<Transform>();
        readonly RaycastHit[] hits = new RaycastHit[16];
        static Material blue, red, rocket, blueHeavy, redHeavy;
        static Mesh capsule;
        const int MaxLights = 12;

        /// <summary>Сервер: выстрел (вызывается при выстреле — событие уйдёт клиентам через Sync).</summary>
        public static event System.Action<Unit, Vector3, Vector3, BoltKind> Fired;
        /// <summary>Сервер: взрыв.</summary>
        public static event System.Action<Vector3, float> Exploded;

        public static Color ColorOf(BoltKind k) => k switch
        {
            BoltKind.Blue or BoltKind.BlueHeavy or BoltKind.TurretBlue or BoltKind.WalkerBlue => TeamColors.BoltBlue,
            BoltKind.Rocket => new Color(1f, 0.6f, 0.25f),
            _ => TeamColors.BoltRed,
        };

        public static float SpeedOf(BoltKind k) => k switch
        {
            BoltKind.Rocket => 45f,
            BoltKind.BlueHeavy or BoltKind.RedHeavy => 160f,
            BoltKind.WalkerRed or BoltKind.WalkerBlue => 70f,
            _ => 95f,
        };

        static float ScaleOf(BoltKind k) => k switch
        {
            BoltKind.Rocket => 2.2f,
            BoltKind.BlueHeavy or BoltKind.RedHeavy => 1.3f,
            BoltKind.WalkerRed or BoltKind.WalkerBlue => 2.6f,
            BoltKind.TurretBlue or BoltKind.TurretRed => 1.5f,
            _ => 1f,
        };

        static string SoundOf(BoltKind k, string weaponSound) => !string.IsNullOrEmpty(weaponSound) ? weaponSound : k switch
        {
            BoltKind.Rocket => "rocket",
            BoltKind.WalkerRed or BoltKind.WalkerBlue => "cannon",
            BoltKind.BlueHeavy or BoltKind.RedHeavy => "sniper",
            _ => "blaster",
        };

        /// <summary>Выстрел. lethal — наносит урон (только сервер); иначе визуал. Возвращает false, если болт не создан.</summary>
        public void Fire(Unit shooter, Vector3 from, Vector3 dir, BoltKind kind, float damage, float splash, bool lethal, string sound = null,
                         bool flash = true)
        {
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            var b = new Bolt
            {
                Pos = from, Vel = dir * SpeedOf(kind), Life = kind == BoltKind.Rocket ? 4f : 2.2f, Damage = damage, Splash = splash,
                Team = shooter ? shooter.Team : (kind == BoltKind.Blue || kind == BoltKind.BlueHeavy || kind == BoltKind.TurretBlue || kind == BoltKind.WalkerBlue ? Team.Republic : Team.Empire),
                Shooter = shooter, Lethal = lethal, Kind = kind,
            };
            b.View = Take(kind);
            b.View.position = from;
            b.View.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            bolts.Add(b);
            if (flash)
            {
                Fx.Flash(from, ColorOf(kind), kind == BoltKind.Rocket ? 5f : 2.5f, kind == BoltKind.Rocket ? 5f : 3f, 0.05f);
                Audio.Shot(SoundOf(kind, sound), from, kind);
            }
            if (lethal) Fired?.Invoke(shooter, from, dir, kind);
        }

        Transform Take(BoltKind k)
        {
            Transform t = pool.Count > 0 ? pool.Pop() : null;
            if (!t)
            {
                var go = new GameObject("Bolt");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Capsule();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.intensity = 2.2f;
                l.renderMode = LightRenderMode.ForcePixel;
                t = go.transform;
            }
            t.gameObject.SetActive(true);
            float s = ScaleOf(k);
            t.localScale = new Vector3(0.05f * s, k == BoltKind.Rocket ? 0.25f : 0.42f * s, 0.05f * s);
            t.GetComponent<MeshRenderer>().sharedMaterial = MaterialOf(k);
            var light = t.GetComponent<Light>();
            light.color = ColorOf(k);
            light.range = 3f * s;
            light.enabled = false;
            return t;
        }

        void Give(Transform t)
        {
            if (!t) return;
            t.gameObject.SetActive(false);
            pool.Push(t);
        }

        static Mesh Capsule()
        {
            if (capsule) return capsule;
            var g = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule = g.GetComponent<MeshFilter>().sharedMesh;
            Destroy(g);
            return capsule;
        }

        static Material MaterialOf(BoltKind k)
        {
            Material Make(Color c, float mul)
            {
                var m = new Material(Shader.Find("Standard"));
                m.color = Color.white;
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * mul);
                return m;
            }
            switch (k)
            {
                case BoltKind.Rocket: return rocket ? rocket : rocket = Make(new Color(1f, 0.55f, 0.2f), 5f);
                case BoltKind.BlueHeavy: return blueHeavy ? blueHeavy : blueHeavy = Make(TeamColors.BoltBlue, 9f);
                case BoltKind.RedHeavy: return redHeavy ? redHeavy : redHeavy = Make(TeamColors.BoltRed, 9f);
                case BoltKind.Blue:
                case BoltKind.TurretBlue:
                case BoltKind.WalkerBlue: return blue ? blue : blue = Make(TeamColors.BoltBlue, 6f);
                default: return red ? red : red = Make(TeamColors.BoltRed, 6f);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var cam = Camera.main;
            Vector3 camPos = cam ? cam.transform.position : Vector3.zero;
            // свет — только у ближайших болтов
            float lightDist = 40f * 40f;
            int lights = 0;
            for (int i = bolts.Count - 1; i >= 0; i--)
            {
                var b = bolts[i];
                Vector3 step = b.Vel * dt;
                float len = step.magnitude;
                bool done = false;
                int n = Physics.RaycastNonAlloc(b.Pos, b.Vel / Mathf.Max(0.001f, b.Vel.magnitude), hits, len + 0.05f, ~0, QueryTriggerInteraction.Ignore);
                int bi = -1;
                float bd = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    var c = hits[k].collider;
                    var hb = c.GetComponent<Hitbox>();
                    Unit owner = hb ? hb.Owner : c.GetComponentInParent<Unit>();
                    if (owner && (owner == b.Shooter || (owner.Team == b.Team && !owner.Dead))) continue;
                    if (owner && owner.Dead && hb) continue;                 // по трупам — насквозь
                    if (hits[k].distance < bd) { bd = hits[k].distance; bi = k; }
                }
                if (b.Lethal) Suppress(b);
                if (bi >= 0)
                {
                    Hit(b, hits[bi]);
                    done = true;
                }
                else
                {
                    b.Pos += step;
                    b.Life -= dt;
                    if (b.Life <= 0f)
                    {
                        if (b.Kind == BoltKind.Rocket && b.Lethal) Explode(b, b.Pos);
                        done = true;
                    }
                }
                if (done)
                {
                    Give(b.View);
                    bolts.RemoveAt(i);
                    continue;
                }
                if (b.View)
                {
                    b.View.position = b.Pos;
                    var light = b.View.GetComponent<Light>();
                    bool on = lights < MaxLights && (b.Pos - camPos).sqrMagnitude < lightDist;
                    if (on) lights++;
                    if (light.enabled != on) light.enabled = on;
                }
                bolts[i] = b;
            }
        }

        static void Suppress(Bolt b)
        {
            foreach (var u in Unit.All)
            {
                if (!u || u.Team == b.Team) continue;
                if ((u.Center - b.Pos).sqrMagnitude < 9f) u.SuppressedUntil = Time.time + 2f;
            }
        }

        void Hit(Bolt b, RaycastHit h)
        {
            var hb = h.collider.GetComponent<Hitbox>();
            Unit target = hb ? hb.Owner : h.collider.GetComponentInParent<Unit>();
            var color = ColorOf(b.Kind);
            if (b.Splash > 0f)
            {
                if (b.Lethal) Explode(b, h.point);
                else Fx.Explosion(h.point, b.Splash);
                return;
            }
            if (target && target.Team != b.Team && BattleNet.I && b.Shooter && b.Shooter == BattleNet.I.LocalCommander) UI.BattleHud.I?.HitConfirmed();
            if (b.Lethal)
            {
                if (target && target.Team != b.Team)
                {
                    float mult = hb ? hb.Multiplier : 1f;
                    target.Damage(new DamageInfo
                    {
                        Amount = b.Damage * mult, Point = h.point, Direction = b.Vel.normalized, Kind = DamageKind.Blaster,
                        Attacker = b.Shooter, Headshot = hb && hb.IsHead, Impulse = 6f,
                    });
                }
                else
                {
                    var d = h.collider.GetComponentInParent<Destructible>();
                    if (d && d.Team != b.Team) d.Damage(b.Damage, DamageKind.Blaster, h.point, b.Shooter);
                }
            }
            Fx.Sparks(h.point, h.normal, color);
            Audio.At("impact", h.point, 0.5f, 30f);
            if (!target) Scorch(h);
        }

        void Explode(Bolt b, Vector3 at) => Explosion(at, b.Splash > 0 ? b.Splash : 4f, b.Damage, b.Shooter, b.Team, true);

        /// <summary>Взрыв: урон по всем юнитам и постройкам в радиусе (lethal — только сервер).</summary>
        public static void Explosion(Vector3 at, float radius, float damage, Unit shooter, Team team, bool lethal)
        {
            Fx.Explosion(at, radius);
            if (!lethal) return;
            Exploded?.Invoke(at, radius);
            foreach (var u in Unit.All.ToArray())
            {
                if (!u || u.Dead) continue;
                float d = Vector3.Distance(u.Center, at);
                if (d > radius) continue;
                float k = 1f - d / radius;
                float friendly = u.Team == team ? 0.35f : 1f;
                u.Damage(new DamageInfo
                {
                    Amount = damage * (0.3f + 0.7f * k) * friendly, Point = at, Direction = (u.Center - at).normalized + Vector3.up * 0.6f,
                    Kind = DamageKind.Explosion, Attacker = shooter, Impulse = 40f * k + 10f,
                });
            }
            foreach (var d in Destructible.All.ToArray())
            {
                if (!d || d.Dead || d.Team == team) continue;
                float dist = d.DistanceTo(at);
                if (dist > radius) continue;
                d.Damage(damage * (0.4f + 0.6f * (1f - dist / radius)), DamageKind.Explosion, at, shooter);
            }
        }

        static readonly Queue<GameObject> scorches = new Queue<GameObject>();
        static Material scorchMat;

        static void Scorch(RaycastHit h)
        {
            if (!scorchMat)
            {
                scorchMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.05f, 0.04f, 0.035f, 0.55f) };
            }
            GameObject g;
            if (scorches.Count >= 120) g = scorches.Dequeue();
            else
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(g.GetComponent<Collider>());
                g.GetComponent<Renderer>().sharedMaterial = scorchMat;
                g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                g.name = "Scorch";
            }
            g.transform.position = h.point + h.normal * 0.02f;
            g.transform.rotation = Quaternion.LookRotation(-h.normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            g.transform.localScale = Vector3.one * Random.Range(0.15f, 0.3f);
            scorches.Enqueue(g);
        }

        public void Clear()
        {
            foreach (var b in bolts) Give(b.View);
            bolts.Clear();
        }
    }
}
