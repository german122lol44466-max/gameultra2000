using SW.Battle;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>Термальный детонатор: летит по баллистике, мигает, через 2.6 с взрывается (урон — только на сервере).</summary>
    public class Grenade : MonoBehaviour
    {
        public const float Fuse = 2.6f;
        public const float Radius = 6.5f;
        public const float DamageAmount = 180f;
        Unit thrower;
        Team team;
        bool lethal;
        float boom;
        Light blink;

        public static void Throw(Unit thrower, Vector3 origin, Vector3 velocity, bool lethal)
        {
            var prefab = Catalog.I.ThermalDetonator;
            GameObject go;
            if (prefab) go = Instantiate(prefab, origin, Random.rotation);
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.position = origin;
                go.transform.localScale = Vector3.one * 0.12f;
                go.GetComponent<Renderer>().material.color = new Color(0.4f, 0.4f, 0.42f);
            }
            go.name = "ThermalDetonator";
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            var sc = go.AddComponent<SphereCollider>();
            sc.radius = 0.06f / Mathf.Max(0.01f, go.transform.lossyScale.x);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.6f;
            Compat.SetVelocity(rb, velocity);
            rb.angularVelocity = Random.insideUnitSphere * 8f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Compat.SetDamping(rb, 0.1f, 1.5f);
            if (thrower)
                foreach (var tc in thrower.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(sc, tc, true);
            var g = go.AddComponent<Grenade>();
            g.thrower = thrower;
            g.team = thrower ? thrower.Team : Team.Republic;
            g.lethal = lethal;
            g.boom = Time.time + Fuse;
            var l = new GameObject("Blink").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.color = new Color(1f, 0.25f, 0.15f);
            l.range = 2f;
            l.intensity = 0f;
            g.blink = l;
            Audio.At("w_grenade_throw", origin, 0.8f, 30f);
            if (thrower is Soldier s) Audio.Voice(s.Team, "grenadespotted", s.Eye, 4f);
        }

        void Update()
        {
            float left = boom - Time.time;
            if (blink)
            {
                float rate = left < 1f ? 12f : 5f;
                blink.intensity = Mathf.Repeat(Time.time * rate, 1f) < 0.5f ? 3f : 0f;
            }
            if (left > 0f) return;
            if (lethal) Bolts.Explosion(transform.position + Vector3.up * 0.2f, Radius, DamageAmount, thrower, team, true);
            Destroy(gameObject);
        }
    }
}
