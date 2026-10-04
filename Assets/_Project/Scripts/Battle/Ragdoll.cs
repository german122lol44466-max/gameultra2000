using System.Collections.Generic;
using UnityEngine;

namespace SW.Battle
{
    /// <summary>
    /// Рэгдолл на костях скелета: капсулы/сферы + CharacterJoint с ограничениями (голова и шея — жёсткие,
    /// чтобы голова не вертелась). Пока персонаж жив, тела кинематические и служат хитбоксами.
    /// Enable() — включить физику с текущей позы (после последнего кадра анимации смерти).
    /// </summary>
    public class Ragdoll : MonoBehaviour
    {
        public readonly List<Rigidbody> Bodies = new List<Rigidbody>();
        public Rigidbody Pelvis;
        public bool Active { get; private set; }
        Animator animator;
        // страховка от «растягивания»: тело, родительское тело и исходное смещение в его системе
        readonly List<(Rigidbody body, Rigidbody parent)> links = new List<(Rigidbody, Rigidbody)>();
        Vector3[] linkLocal = new Vector3[0];
        float guardUntil;

        enum Kind { Ball, Knee, Elbow, Neck }

        struct Part
        {
            public string Bone, Child, Parent;
            public float Radius, Mass, Mult;
            public bool Box, Head;
            public Kind Joint;
            public float Twist, Swing1, Swing2;     // для Ball/Neck: скручивание ±, сгиб, наклон; для шарниров: Swing1 = макс. сгиб
        }

        static readonly Part[] Parts =
        {
            new Part { Bone = "Hips", Child = "Spine", Radius = 0.14f, Mass = 12f, Mult = 1f, Box = true },
            new Part { Bone = "Spine", Child = "Chest", Parent = "Hips", Radius = 0.13f, Mass = 8f, Mult = 1f, Box = true, Joint = Kind.Ball, Twist = 12, Swing1 = 20, Swing2 = 10 },
            new Part { Bone = "Chest", Child = "Neck", Parent = "Spine", Radius = 0.16f, Mass = 14f, Mult = 1.2f, Box = true, Joint = Kind.Ball, Twist = 12, Swing1 = 20, Swing2 = 10 },
            // голова: малые пределы и пружина — не крутится «на 360»
            new Part { Bone = "Head", Child = null, Parent = "Chest", Radius = 0.11f, Mass = 5f, Mult = 2.5f, Head = true, Joint = Kind.Neck, Twist = 25, Swing1 = 30, Swing2 = 15 },
            new Part { Bone = "UpperArm.L", Child = "LowerArm.L", Parent = "Chest", Radius = 0.055f, Mass = 2.5f, Mult = 0.8f, Joint = Kind.Ball, Twist = 40, Swing1 = 70, Swing2 = 50 },
            new Part { Bone = "LowerArm.L", Child = "Hand.L", Parent = "UpperArm.L", Radius = 0.045f, Mass = 1.8f, Mult = 0.7f, Joint = Kind.Elbow, Swing1 = 135 },
            new Part { Bone = "UpperArm.R", Child = "LowerArm.R", Parent = "Chest", Radius = 0.055f, Mass = 2.5f, Mult = 0.8f, Joint = Kind.Ball, Twist = 40, Swing1 = 70, Swing2 = 50 },
            new Part { Bone = "LowerArm.R", Child = "Hand.R", Parent = "UpperArm.R", Radius = 0.045f, Mass = 1.8f, Mult = 0.7f, Joint = Kind.Elbow, Swing1 = 135 },
            new Part { Bone = "UpperLeg.L", Child = "LowerLeg.L", Parent = "Hips", Radius = 0.08f, Mass = 7f, Mult = 0.8f, Joint = Kind.Ball, Twist = 20, Swing1 = 60, Swing2 = 30 },
            new Part { Bone = "LowerLeg.L", Child = "Foot.L", Parent = "UpperLeg.L", Radius = 0.06f, Mass = 4f, Mult = 0.7f, Joint = Kind.Knee, Swing1 = 130 },
            new Part { Bone = "UpperLeg.R", Child = "LowerLeg.R", Parent = "Hips", Radius = 0.08f, Mass = 7f, Mult = 0.8f, Joint = Kind.Ball, Twist = 20, Swing1 = 60, Swing2 = 30 },
            new Part { Bone = "LowerLeg.R", Child = "Foot.R", Parent = "UpperLeg.R", Radius = 0.06f, Mass = 4f, Mult = 0.7f, Joint = Kind.Knee, Swing1 = 130 },
        };

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var r = Find(c, name);
                if (r) return r;
            }
            return null;
        }

        /// <summary>Создать тела и суставы (вызывается при спавне).</summary>
        public void Build(Unit owner)
        {
            animator = GetComponentInChildren<Animator>();
            var map = new Dictionary<string, Rigidbody>();
            float scale = transform.lossyScale.y;
            foreach (var p in Parts)
            {
                var b = Find(transform, p.Bone);
                if (!b) continue;
                var rb = b.gameObject.AddComponent<Rigidbody>();
                rb.mass = p.Mass;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;      // пока жив, позой управляет аниматор
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 20;
                rb.solverVelocityIterations = 10;
                rb.maxDepenetrationVelocity = 2f;
                rb.maxAngularVelocity = 20f;
                Compat.SetDamping(rb, 0.05f, 0.6f);
                var child = p.Child != null ? Find(transform, p.Child) : null;
                float lossy = b.lossyScale.y;
                if (p.Head)
                {
                    var sc = b.gameObject.AddComponent<SphereCollider>();
                    sc.radius = p.Radius / lossy;
                    sc.center = new Vector3(0, 0.07f / lossy, 0.02f / lossy);
                }
                else if (child)
                {
                    float len = Vector3.Distance(b.position, child.position) / lossy;
                    if (p.Box)
                    {
                        var bc = b.gameObject.AddComponent<BoxCollider>();
                        bc.size = new Vector3(p.Radius * 2.4f / lossy, len, p.Radius * 1.5f / lossy);
                        bc.center = new Vector3(0, len * 0.5f, 0);
                    }
                    else
                    {
                        var cc = b.gameObject.AddComponent<CapsuleCollider>();
                        cc.direction = 1;
                        cc.radius = p.Radius / lossy;
                        cc.height = len + p.Radius / lossy;
                        cc.center = new Vector3(0, len * 0.5f, 0);
                    }
                }
                var hb = b.gameObject.AddComponent<Hitbox>();
                hb.Owner = owner;
                hb.Multiplier = p.Mult;
                hb.IsHead = p.Head;
                map[p.Bone] = rb;
                Bodies.Add(rb);
                if (p.Bone == "Hips") Pelvis = rb;
            }
            // части одного тела не сталкиваются между собой (иначе при включении физики их «разрывает»)
            var cols = new List<Collider>();
            foreach (var rb in Bodies) cols.AddRange(rb.GetComponents<Collider>());
            for (int a = 0; a < cols.Count; a++)
                for (int b2 = a + 1; b2 < cols.Count; b2++)
                    Physics.IgnoreCollision(cols[a], cols[b2], true);
            Vector3 fwd = transform.forward, right = transform.right;
            foreach (var p in Parts)
            {
                if (p.Parent == null || !map.ContainsKey(p.Bone) || !map.ContainsKey(p.Parent)) continue;
                var bone = map[p.Bone].transform;
                var child = p.Child != null ? Find(transform, p.Child) : null;
                Vector3 d = child ? (child.position - bone.position).normalized : transform.up;
                var j = bone.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = map[p.Parent];
                j.enablePreprocessing = false;
                links.Add((map[p.Bone], map[p.Parent]));
                if (p.Joint == Kind.Knee || p.Joint == Kind.Elbow)
                {
                    // шарнир: ось — поперёк конечности; знак выбираем так, чтобы +угол сгибал в нужную сторону
                    Vector3 want = p.Joint == Kind.Knee ? -fwd : fwd;
                    Vector3 lat = Vector3.Cross(d, want).sqrMagnitude > 1e-4f ? Vector3.Cross(d, want).normalized : right;
                    if (Vector3.Dot(Vector3.Cross(lat, d), want) < 0) lat = -lat;
                    j.axis = bone.InverseTransformDirection(lat);
                    j.swingAxis = bone.InverseTransformDirection(d);
                    j.lowTwistLimit = new SoftJointLimit { limit = -3f };
                    j.highTwistLimit = new SoftJointLimit { limit = p.Swing1 };
                    j.swing1Limit = new SoftJointLimit { limit = 4f };
                    j.swing2Limit = new SoftJointLimit { limit = 4f };
                }
                else
                {
                    // шаровой: скручивание вокруг кости, сгиб вперёд-назад и наклон вбок
                    j.axis = bone.InverseTransformDirection(d);
                    j.swingAxis = bone.InverseTransformDirection(right);
                    j.lowTwistLimit = new SoftJointLimit { limit = -p.Twist };
                    j.highTwistLimit = new SoftJointLimit { limit = p.Twist };
                    j.swing1Limit = new SoftJointLimit { limit = p.Swing1 };
                    j.swing2Limit = new SoftJointLimit { limit = p.Swing2 };
                }
                j.enableProjection = true;
                j.projectionDistance = 0.02f;
                j.projectionAngle = 5f;
                float k = p.Joint == Kind.Neck ? 120f : 40f;          // шея жёстче
                j.twistLimitSpring = new SoftJointLimitSpring { spring = k, damper = k * 0.15f };
                j.swingLimitSpring = new SoftJointLimitSpring { spring = k, damper = k * 0.15f };
            }
        }

        /// <summary>Включить физику с текущей позы. impulse — толчок (в мире): переводится в скорость всего тела с ограничением.</summary>
        public void Enable(Vector3 impulse, Vector3 point)
        {
            if (Active) return;
            Active = true;
            if (animator) animator.enabled = false;
            var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent) agent.enabled = false;
            Physics.SyncTransforms();          // тела стартуют ровно с последнего кадра анимации
            linkLocal = new Vector3[links.Count];
            for (int i = 0; i < links.Count; i++)
                linkLocal[i] = links[i].parent.transform.InverseTransformPoint(links[i].body.transform.position);
            float total = 0f;
            foreach (var rb in Bodies) total += rb.mass;
            Vector3 v = total > 0 ? impulse / total * 1.6f : Vector3.zero;
            if (v.magnitude > 7f) v = v.normalized * 7f;                 // никаких «ракет» и разрывов
            Rigidbody best = null;
            float bd = float.MaxValue;
            foreach (var rb in Bodies)
            {
                float d = (rb.worldCenterOfMass - point).sqrMagnitude;
                if (d < bd) { bd = d; best = rb; }
            }
            foreach (var rb in Bodies)
            {
                rb.isKinematic = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                Compat.SetVelocity(rb, rb == best ? v * 1.3f : v);
                rb.angularVelocity = Vector3.zero;
            }
            guardUntil = Time.time + 8f;
            Sfx.Play("body_fall", Pelvis ? Pelvis.position : transform.position, 0.8f, 1f, 2f, 40f);
        }

        void FixedUpdate()
        {
            if (!Active || Time.time > guardUntil) return;
            // если сустав всё же растянуло — возвращаем тело к родителю (кости не превращаются в «макароны»)
            for (int i = 0; i < links.Count && i < linkLocal.Length; i++)
            {
                var (body, parent) = links[i];
                if (!body || !parent) continue;
                Vector3 want = parent.transform.TransformPoint(linkLocal[i]);
                if ((body.position - want).sqrMagnitude > 0.0025f)
                {
                    body.position = want;
                    Compat.SetVelocity(body, Compat.GetVelocity(parent));
                }
            }
        }

        /// <summary>Через какое-то время тела засыпают: убираем физику, оставляем позу (экономия).</summary>
        void Update()
        {
            if (!Active || Time.time < guardUntil + 4f) return;
            foreach (var rb in Bodies)
                if (rb && !rb.isKinematic && rb.IsSleeping()) rb.isKinematic = true;
        }
    }
}
