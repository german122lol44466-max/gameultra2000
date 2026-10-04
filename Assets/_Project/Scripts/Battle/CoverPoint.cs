using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SW.Battle
{
    /// <summary>
    /// Точка укрытия у препятствия. Генерируются автоматически вокруг коллайдеров-укрытий (Generate).
    /// Low — низкое укрытие (стрелять можно только встав), иначе — высокое (выглядывать сбоку).
    /// </summary>
    public class CoverPoint : MonoBehaviour
    {
        public static readonly List<CoverPoint> All = new List<CoverPoint>();
        public bool Low;
        public Vector3 Normal;          // наружу от препятствия
        Object owner;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public bool Free => owner == null || (owner is Component c && (!c || !c.gameObject.activeInHierarchy));

        public void Release(Object who) { if (owner == who) owner = null; }

        /// <summary>Найти свободное укрытие рядом, которое закрывает от угрозы.</summary>
        public static CoverPoint Find(Vector3 from, Vector3 threat, float radius, Object who)
        {
            CoverPoint best = null;
            float bs = float.MaxValue;
            foreach (var c in All)
            {
                if (!c.Free && c.owner != who) continue;
                Vector3 p = c.transform.position;
                float d = Vector3.Distance(p, from);
                if (d > radius) continue;
                Vector3 toThreat = (threat - p);
                toThreat.y = 0;
                // укрытие должно быть между точкой и угрозой: нормаль смотрит от угрозы
                if (Vector3.Dot(c.Normal, toThreat.normalized) > -0.3f) continue;
                if (toThreat.magnitude < 6f) continue;
                // проверка: на уровне присяда луч к угрозе перекрыт
                Vector3 eye = p + Vector3.up * 0.9f;
                if (!Physics.Raycast(eye, (threat + Vector3.up - eye).normalized, toThreat.magnitude - 1f)) continue;
                float s = d + toThreat.magnitude * 0.15f;
                if (s < bs) { bs = s; best = c; }
            }
            if (best) best.owner = who;
            return best;
        }

        /// <summary>Расставить точки укрытий вокруг всех коллайдеров с тегом-компонентом CoverObject.</summary>
        public static void Generate(Transform root)
        {
            foreach (var obj in Object.FindObjectsByType<CoverObject>(FindObjectsSortMode.None))
            {
                var col = obj.GetComponent<Collider>();
                if (!col) continue;
                var b = col.bounds;
                bool low = b.size.y < 1.3f;
                int n = Mathf.Clamp(Mathf.RoundToInt((b.size.x + b.size.z) / 1.6f), 4, 16);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Vector3 p = b.center + Vector3.Scale(dir, b.extents) + dir * 0.9f;
                    p.y = b.min.y;
                    if (!NavMesh.SamplePosition(p, out var h, 1.2f, NavMesh.AllAreas)) continue;
                    var go = new GameObject("Cover");
                    go.transform.SetParent(root, true);
                    go.transform.position = h.position;
                    var cp = go.AddComponent<CoverPoint>();
                    cp.Low = low;
                    cp.Normal = dir;
                }
            }
        }
    }
}
