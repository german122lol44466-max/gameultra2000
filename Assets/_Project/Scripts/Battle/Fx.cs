using System.Collections;
using UnityEngine;

namespace SW.Battle
{
    /// <summary>Эффекты без ассетов: вспышки, искры, взрывы, дым, молнии — всё создаётся кодом.</summary>
    public class Fx : MonoBehaviour
    {
        static Fx inst;
        static Material additive, unlit;

        public static Fx I
        {
            get
            {
                if (!inst)
                {
                    inst = new GameObject("Fx").AddComponent<Fx>();
                }
                return inst;
            }
        }

        public static Material Additive
        {
            get
            {
                if (!additive)
                {
                    Shader sh = null;
                    foreach (var n in new[] { "Legacy Shaders/Particles/Additive", "Particles/Standard Unlit", "Sprites/Default" })
                        if (sh == null) sh = Shader.Find(n);
                    additive = new Material(sh);
                }
                return additive;
            }
        }

        public static Material Unlit(Color c)
        {
            var m = new Material(Shader.Find("Unlit/Color")) { color = c };
            return m;
        }

        /// <summary>Короткая вспышка света.</summary>
        public static void Flash(Vector3 pos, Color c, float intensity = 4f, float range = 4f, float time = 0.06f)
        {
            var go = new GameObject("Flash");
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            Destroy(go, time);
        }

        static ParticleSystem Burst(Vector3 pos, Vector3 normal, Color c, int count, float speed, float life, float size, float gravity, string name)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(normal.sqrMagnitude > 0 ? normal : Vector3.up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.5f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = c;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 35f;
            sh.radius = 0.02f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c * 0.5f, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Additive;
            ps.Play();
            Destroy(go, life + 0.5f);
            return ps;
        }

        public static void Sparks(Vector3 pos, Vector3 normal, Color c)
        {
            Burst(pos, normal, c, 18, 6f, 0.4f, 0.04f, 1.5f, "Sparks");
            Flash(pos, c, 2.5f, 3f, 0.05f);
        }

        public static void SaberClash(Vector3 pos)
        {
            Burst(pos, Vector3.up, new Color(1f, 0.9f, 0.6f), 30, 7f, 0.5f, 0.05f, 1f, "Clash");
            Flash(pos, new Color(1f, 0.9f, 0.7f), 6f, 5f, 0.08f);
        }

        public static void Explosion(Vector3 pos, float radius)
        {
            Burst(pos, Vector3.up, new Color(1f, 0.6f, 0.2f), 60, 9f * radius / 3f, 0.8f, 0.6f * radius / 3f, -0.1f, "Fire");
            Burst(pos, Vector3.up, new Color(0.25f, 0.23f, 0.22f), 40, 3f, 2.5f, 1.2f * radius / 3f, -0.05f, "Smoke");
            Flash(pos + Vector3.up, new Color(1f, 0.6f, 0.3f), 12f, radius * 4f, 0.25f);
        }

        public static void Smoke(Transform parent, float scale = 1f)
        {
            var go = new GameObject("WreckSmoke");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.up * 1.5f * scale;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 4f;
            main.startSpeed = 1.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f * scale, 2f * scale);
            main.startColor = new Color(0.15f, 0.14f, 0.13f, 0.6f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 6f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 10f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        /// <summary>Молния Силы между точками на время t.</summary>
        public static void Lightning(Vector3 a, Vector3 b, float time)
        {
            I.StartCoroutine(I.LightningCo(a, b, time));
        }

        IEnumerator LightningCo(Vector3 a, Vector3 b, float time)
        {
            var go = new GameObject("ForceLightning");
            var lines = new LineRenderer[3];
            for (int k = 0; k < lines.Length; k++)
            {
                var c = new GameObject("bolt");
                c.transform.SetParent(go.transform, false);
                var lr = c.AddComponent<LineRenderer>();
                lr.sharedMaterial = Additive;
                lr.startColor = new Color(0.75f, 0.85f, 1f);
                lr.endColor = new Color(0.45f, 0.6f, 1f, 0.6f);
                lr.widthMultiplier = 0.035f;
                lr.positionCount = 18;
                lines[k] = lr;
            }
            var l = go.AddComponent<Light>();
            l.color = new Color(0.55f, 0.7f, 1f);
            l.range = 8f;
            l.intensity = 5f;
            float t = 0;
            while (t < time)
            {
                go.transform.position = Vector3.Lerp(a, b, 0.4f);
                foreach (var lr in lines)
                {
                    for (int i = 0; i < lr.positionCount; i++)
                    {
                        float u = i / (lr.positionCount - 1f);
                        Vector3 p = Vector3.Lerp(a, b, u);
                        if (i > 0 && i < lr.positionCount - 1) p += Random.insideUnitSphere * 0.25f * Mathf.Sin(u * Mathf.PI);
                        lr.SetPosition(i, p);
                    }
                }
                t += Time.deltaTime;
                yield return null;
            }
            Destroy(go);
        }
    }
}
