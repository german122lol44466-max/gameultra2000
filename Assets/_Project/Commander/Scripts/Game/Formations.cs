using System.Collections.Generic;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>
    /// Геометрия строя: места бойцов относительно «якоря» отряда (x — вправо, z — вперёд по направлению строя)
    /// и куда каждый смотрит (градусы относительно направления строя; в каре и круговой обороне — наружу).
    /// </summary>
    public static class Formations
    {
        public struct Slot
        {
            public Vector3 Offset;
            public float Facing;
        }

        public static float DefaultSpacing(FormationType f) => f switch
        {
            FormationType.Skirmish => 4.5f,
            FormationType.Column => 1.8f,
            FormationType.DoubleColumn => 1.8f,
            FormationType.Circle => 2.4f,
            _ => 2.0f,
        };

        public static List<Slot> Slots(FormationType f, int n, float s, List<Slot> res = null)
        {
            res ??= new List<Slot>(n);
            res.Clear();
            if (n <= 0) return res;
            switch (f)
            {
                case FormationType.Line:
                    for (int i = 0; i < n; i++) Add(res, (i - (n - 1) * 0.5f) * s, 0f);
                    break;
                case FormationType.DoubleLine:
                {
                    int cols = Mathf.CeilToInt(n / 2f);
                    for (int i = 0; i < n; i++)
                    {
                        int row = i / cols, col = i % cols;
                        int inRow = row == 0 ? cols : n - cols;
                        Add(res, (col - (inRow - 1) * 0.5f) * s + row * s * 0.5f, -row * s * 1.1f);
                    }
                    break;
                }
                case FormationType.Column:
                    for (int i = 0; i < n; i++) Add(res, 0f, -i * s);
                    break;
                case FormationType.DoubleColumn:
                    for (int i = 0; i < n; i++) Add(res, (i % 2 == 0 ? -0.5f : 0.5f) * s * 0.9f, -(i / 2) * s);
                    break;
                case FormationType.Wedge:
                    Add(res, 0, 0);
                    for (int i = 1; i < n; i++)
                    {
                        int k = (i + 1) / 2;
                        float side = i % 2 == 1 ? -1f : 1f;
                        Add(res, side * k * s * 0.85f, -k * s * 0.85f);
                    }
                    break;
                case FormationType.Vee:
                    Add(res, 0, 0);
                    for (int i = 1; i < n; i++)
                    {
                        int k = (i + 1) / 2;
                        float side = i % 2 == 1 ? -1f : 1f;
                        Add(res, side * k * s * 0.85f, k * s * 0.85f);
                    }
                    break;
                case FormationType.Diamond:
                    Perimeter(res, n, s, diamond: true);
                    break;
                case FormationType.Square:
                    Perimeter(res, n, s, diamond: false);
                    break;
                case FormationType.Circle:
                {
                    float r = Mathf.Max(2f, n * s / (2f * Mathf.PI));
                    if (n == 1) { Add(res, 0, 0); break; }
                    for (int i = 0; i < n; i++)
                    {
                        float a = i / (float)n * Mathf.PI * 2f;
                        res.Add(new Slot { Offset = new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r), Facing = a * Mathf.Rad2Deg });
                    }
                    break;
                }
                case FormationType.EchelonLeft:
                    for (int i = 0; i < n; i++) Add(res, -i * s * 0.75f, -i * s * 0.75f);
                    break;
                case FormationType.EchelonRight:
                    for (int i = 0; i < n; i++) Add(res, i * s * 0.75f, -i * s * 0.75f);
                    break;
                case FormationType.Skirmish:
                {
                    int cols = Mathf.CeilToInt(n / 2f);
                    for (int i = 0; i < n; i++)
                    {
                        int row = i / cols, col = i % cols;
                        int inRow = row == 0 ? cols : n - cols;
                        float jx = (Hash(i * 7 + 1) - 0.5f) * s * 0.5f, jz = (Hash(i * 13 + 5) - 0.5f) * s * 0.6f;
                        Add(res, (col - (inRow - 1) * 0.5f) * s + row * s * 0.5f + jx, -row * s * 0.9f + jz);
                    }
                    break;
                }
                default:
                    for (int i = 0; i < n; i++) Add(res, (i - (n - 1) * 0.5f) * s, 0f);
                    break;
            }
            return res;
        }

        static void Add(List<Slot> res, float x, float z) => res.Add(new Slot { Offset = new Vector3(x, 0, z), Facing = 0f });

        static float Hash(int i)
        {
            uint h = (uint)i * 2654435761u;
            h ^= h >> 15;
            return (h & 0xffff) / 65535f;
        }

        /// <summary>Точки по контуру ромба/квадрата; первая — впереди по центру; бойцы смотрят наружу.</summary>
        static void Perimeter(List<Slot> res, int n, float s, bool diamond)
        {
            if (n == 1) { Add(res, 0, 0); return; }
            float per = n * s;
            float half = Mathf.Max(s, per / 8f);           // полусторона квадрата
            Vector3[] corners = diamond
                ? new[] { new Vector3(0, 0, half * 1.41f), new Vector3(half * 1.41f, 0, 0), new Vector3(0, 0, -half * 1.41f), new Vector3(-half * 1.41f, 0, 0) }
                : new[] { new Vector3(half, 0, half), new Vector3(half, 0, -half), new Vector3(-half, 0, -half), new Vector3(-half, 0, half) };
            // контур начинается с середины передней стороны (у квадрата) или с вершины (у ромба)
            var pts = new List<Vector3>();
            if (!diamond) pts.Add(new Vector3(0, 0, half));
            for (int i = 0; i < 4; i++) pts.Add(corners[i]);
            if (!diamond) pts.Add(new Vector3(0, 0, half)); else pts.Add(corners[0]);
            float total = 0;
            for (int i = 0; i < pts.Count - 1; i++) total += Vector3.Distance(pts[i], pts[i + 1]);
            for (int k = 0; k < n; k++)
            {
                float d = k / (float)n * total;
                int seg = 0;
                while (seg < pts.Count - 2 && d > Vector3.Distance(pts[seg], pts[seg + 1]))
                {
                    d -= Vector3.Distance(pts[seg], pts[seg + 1]);
                    seg++;
                }
                float len = Vector3.Distance(pts[seg], pts[seg + 1]);
                var p = Vector3.Lerp(pts[seg], pts[seg + 1], len > 0 ? d / len : 0);
                float facing = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
                res.Add(new Slot { Offset = p, Facing = facing });
            }
        }

        /// <summary>
        /// Назначить бойцам места: жадно, ближайшие пары «боец — место» (без пересечений на коротких перестроениях).
        /// positions — текущие позиции бойцов, targets — мировые позиции мест. Возвращает индекс места для каждого бойца.
        /// </summary>
        public static int[] Assign(IList<Vector3> positions, IList<Vector3> targets)
        {
            int n = positions.Count, m = targets.Count;
            var res = new int[n];
            for (int i = 0; i < n; i++) res[i] = -1;
            var pairs = new List<(float d, int i, int j)>(n * m);
            for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                pairs.Add(((positions[i] - targets[j]).sqrMagnitude, i, j));
            pairs.Sort((a, b) => a.d.CompareTo(b.d));
            var usedSlot = new bool[m];
            int left = Mathf.Min(n, m);
            foreach (var (d, i, j) in pairs)
            {
                if (left == 0) break;
                if (res[i] >= 0 || usedSlot[j]) continue;
                res[i] = j;
                usedSlot[j] = true;
                left--;
            }
            return res;
        }
    }
}
