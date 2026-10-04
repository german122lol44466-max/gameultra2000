using UnityEngine;
using UnityEngine.AI;

namespace SW.Battle
{
    /// <summary>
    /// ИИ джедая/ситха. Сближается с целью (предпочитает дуэль с другим владельцем меча), кружит, бьёт комбо
    /// из 3–4 ударов, блокирует удары меча, отражает болты бластеров обратно в стрелков, применяет Силу:
    /// толчок (раскидывает солдат), удушение (Вейдер), молнии (Сидиус).
    /// </summary>
    [RequireComponent(typeof(CharacterBody))]
    public class DuelistAI : MonoBehaviour
    {
        public string Style = "vader";
        public float SaberDamage = 55f;
        public float DeflectChance = 0.8f;
        public float BlockChance = 0.55f;
        public float BladeLength = 1.0f;
        public bool HasChoke, HasLightning;
        public Color BladeColor = new Color(1f, 0.1f, 0.06f);

        CharacterBody body;
        Unit self;
        Unit target;
        float nextThink, nextAttack, nextForce, circleSign = 1f, lastHitCheck;
        int combo;
        bool hitThisSwing;
        static readonly string[] Attacks = { "Attack1", "Attack2", "Attack3", "Attack4" };

        void Start()
        {
            body = GetComponent<CharacterBody>();
            self = GetComponent<Unit>();
            body.Aiming = true;
            nextForce = Time.time + Random.Range(3f, 8f);
            foreach (var bl in body.Blades)
            {
                var l = new GameObject("BladeLight").AddComponent<Light>();
                l.transform.SetParent(bl, false);
                l.transform.localPosition = new Vector3(0, 0.5f, 0);
                l.type = LightType.Point;
                l.color = BladeColor;
                l.range = 4f;
                l.intensity = 2.5f;
                l.shadows = LightShadows.None;
            }
            if (body.Has("Ignite")) body.DoAction("Ignite");
        }

        Unit PickTarget()
        {
            Unit best = null;
            float bs = float.MaxValue;
            foreach (var u in Unit.Enemies(self.Team))
            {
                float d = Vector3.Distance(u.transform.position, transform.position);
                float s = d;
                if (u.IsDuelist) s *= 0.5f;                     // дуэли в приоритете
                if (u.IsVehicle) s *= 1.6f;
                if (u == target) s *= 0.6f;
                if (s < bs) { bs = s; best = u; }
            }
            return best;
        }

        /// <summary>Попытка отразить летящий болт (вызывает болт). Отражённый болт летит в стрелка.</summary>
        public bool TryDeflect(BlasterBolt bolt)
        {
            if (self.Dead || body.Busy && !(body.Action ?? "").StartsWith("Deflect") && !(body.Action ?? "").StartsWith("Block")) return false;
            if (Random.value > DeflectChance) return false;
            Vector3 dir;
            if (bolt.Shooter && !bolt.Shooter.Dead && Random.value < 0.5f)
                dir = (bolt.Shooter.Center - bolt.transform.position).normalized;
            else
                dir = Quaternion.Euler(Random.Range(-40, 40), Random.Range(-60, 60), 0) * -bolt.transform.up;
            bolt.Reflect(dir, self);
            if (!body.Busy) body.DoAction(Random.value < 0.5f ? "Deflect1" : "Deflect2", 0.05f, stop: false);
            body.FaceTarget = bolt.transform.position - bolt.transform.up * 5f;
            return true;
        }

        /// <summary>Попытка заблокировать удар меча.</summary>
        public bool TryBlock(Unit attacker)
        {
            if (self.Dead || body.Busy || Random.value > BlockChance) return false;
            body.FaceTarget = attacker.transform.position;
            body.DoAction("Block", 0.05f);
            return true;
        }

        void Think()
        {
            target = PickTarget();
            if (!target) { body.FaceTarget = null; return; }
            float dist = Vector3.Distance(target.transform.position, transform.position);
            var ag = body.Agent;
            if (!ag.enabled || !ag.isOnNavMesh) return;
            float reach = target.IsVehicle ? 3.5f : 2.1f;
            if (dist > reach + 0.6f)
            {
                ag.speed = dist > 6f ? body.RunSpeed : body.WalkSpeed;
                ag.stoppingDistance = reach * 0.85f;
                ag.SetDestination(target.transform.position);
            }
            else
            {
                // кружим вокруг противника
                if (Random.value < 0.15f) circleSign = -circleSign;
                Vector3 to = (transform.position - target.transform.position).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, to) * circleSign;
                ag.speed = body.StrafeSpeed * 1.3f;
                ag.stoppingDistance = 0f;
                Vector3 p = target.transform.position + (to * reach + side * 0.8f);
                if (NavMesh.SamplePosition(p, out var h, 2f, NavMesh.AllAreas)) ag.SetDestination(h.position);
            }
        }

        void Update()
        {
            if (self.Dead) return;
            if (Time.time >= nextThink)
            {
                nextThink = Time.time + 0.3f + Random.value * 0.1f;
                Think();
            }
            if (!target || target.Dead) return;
            float dist = Vector3.Distance(target.transform.position, transform.position);
            body.FaceTarget = target.transform.position;
            // Сила
            if (Time.time > nextForce && !body.Busy && dist < 9f && dist > 2.5f)
            {
                nextForce = Time.time + Random.Range(7f, 12f);
                UseForce(dist);
                return;
            }
            // атака
            float reach = target.IsVehicle ? 3.5f : 2.3f;
            if (dist < reach && !body.Busy && Time.time > nextAttack)
            {
                string a = Attacks[combo % Attacks.Length];
                combo = Random.value < 0.75f ? combo + 1 : 0;
                float len = body.DoAction(a, 0.06f);
                nextAttack = Time.time + len * 0.85f + (combo == 0 ? Random.Range(0.3f, 0.9f) : 0f);
                hitThisSwing = false;
                // противник-дуэлянт может заблокировать
                var other = target.GetComponent<DuelistAI>();
                if (other && other.TryBlock(self))
                {
                    hitThisSwing = true;
                    Invoke(nameof(Clash), len * 0.42f);
                }
            }
            // окно удара — проверка клинка
            if (body.Busy && !hitThisSwing && (body.Action ?? "").StartsWith("Attack"))
            {
                float pr = body.ActionProgress(body.Length(body.Action));
                if (pr > 0.3f && pr < 0.65f) SaberSweep();
            }
        }

        void Clash()
        {
            if (body.Blades.Count > 0) Fx.SaberClash(body.Blades[0].position + body.Blades[0].up * 0.6f);
        }

        void SaberSweep()
        {
            foreach (var bl in body.Blades)
            {
                Vector3 a = bl.position, b = bl.position + bl.up * BladeLength * bl.lossyScale.y;
                foreach (var c in Physics.OverlapCapsule(a, b, 0.12f))
                {
                    var hb = c.GetComponent<Hitbox>();
                    var u = hb ? hb.Owner : c.GetComponentInParent<Unit>();
                    if (!u || u.Team == self.Team || u.Dead) continue;
                    hitThisSwing = true;
                    u.Damage(new DamageInfo
                    {
                        Amount = SaberDamage * (hb ? Mathf.Max(1f, hb.Multiplier) : 1f) * (u.IsVehicle ? 0.6f : 1f),
                        Point = c.ClosestPoint(b), Direction = (u.Center - self.Center).normalized, Kind = DamageKind.Saber,
                        Attacker = self, Impulse = 8f, Headshot = hb && hb.IsHead
                    });
                    Fx.Sparks(c.ClosestPoint(b), -transform.forward, new Color(1f, 0.6f, 0.3f));
                    return;
                }
            }
        }

        void UseForce(float dist)
        {
            if (HasLightning && body.Has("ForceLightning"))
            {
                float len = body.DoAction("ForceLightning", 0.1f);
                StartCoroutine(LightningCo(target, len));
                return;
            }
            if (HasChoke && body.Has("ForceChoke") && !target.IsVehicle && !target.IsDuelist)
            {
                float len = body.DoAction("ForceChoke", 0.1f);
                StartCoroutine(ChokeCo(target, len));
                return;
            }
            if (body.Has("ForcePush"))
            {
                body.DoAction("ForcePush", 0.1f);
                Invoke(nameof(PushNow), 0.55f);
            }
        }

        void PushNow()
        {
            Vector3 fwd = transform.forward;
            foreach (var u in Unit.All.ToArray())
            {
                if (!u || u.Dead || u.Team == self.Team || u.IsVehicle) continue;
                Vector3 to = u.transform.position - transform.position;
                if (to.magnitude > 9f || Vector3.Angle(fwd, to) > 35f) continue;
                float k = 1f - to.magnitude / 9f;
                if (u.IsDuelist) { u.GetComponent<CharacterBody>()?.DoAction("HitBack"); continue; }
                u.Damage(new DamageInfo { Amount = 35f + 50f * k, Point = u.Center, Direction = (to.normalized + Vector3.up * 0.35f),
                    Kind = DamageKind.Force, Attacker = self, Impulse = 35f + 40f * k });
                var nb = u.GetComponent<NavMeshAgent>();
                if (!u.Dead && nb && nb.enabled && nb.isOnNavMesh) nb.Move(to.normalized * 2f * k);
            }
            Fx.Flash(transform.position + fwd * 2f + Vector3.up, new Color(0.7f, 0.8f, 1f), 3f, 6f, 0.2f);
        }

        System.Collections.IEnumerator ChokeCo(Unit t, float len)
        {
            var tb = t.GetComponent<CharacterBody>();
            var ag = t.GetComponent<NavMeshAgent>();
            float start = Time.time + 0.4f;
            while (Time.time < start + len * 0.7f && t && !t.Dead && !self.Dead)
            {
                if (ag && ag.enabled) ag.ResetPath();
                if (tb && !tb.Busy) tb.DoAction("Hit", 0.1f);
                t.Damage(new DamageInfo { Amount = 30f * Time.deltaTime, Point = t.Center, Direction = Vector3.up, Kind = DamageKind.Force, Attacker = self });
                yield return null;
            }
        }

        System.Collections.IEnumerator LightningCo(Unit t, float len)
        {
            yield return new WaitForSeconds(0.4f);
            float end = Time.time + len * 0.6f;
            Fx.Lightning(body.HandL ? body.HandL.position : self.Eye, t ? t.Center : transform.position + transform.forward * 4f, len * 0.6f);
            while (Time.time < end && t && !t.Dead && !self.Dead)
            {
                t.Damage(new DamageInfo { Amount = 45f * Time.deltaTime, Point = t.Center, Direction = (t.Center - self.Center).normalized,
                    Kind = DamageKind.Force, Attacker = self, Impulse = 25f });
                // молния бьёт и стоящих рядом
                foreach (var u in Unit.All.ToArray())
                    if (u && u != t && !u.Dead && u.Team != self.Team && (u.Center - t.Center).sqrMagnitude < 4f)
                        u.Damage(new DamageInfo { Amount = 20f * Time.deltaTime, Point = u.Center, Direction = Vector3.up, Kind = DamageKind.Force, Attacker = self, Impulse = 15f });
                yield return null;
            }
        }
    }
}
