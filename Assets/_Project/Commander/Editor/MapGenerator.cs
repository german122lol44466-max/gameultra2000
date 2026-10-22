using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SW.Battle;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace SW.Commander.EditorTools
{
    /// <summary>
    /// Генератор карты «Феллусия» (1.6 x 1.6 км): рельеф с горами по краям и оврагом, дороги между пунктами,
    /// слои текстур по уклону/дорогам, леса и трава, скалы-укрытия; база Республики на юге, крепость Империи
    /// на севере (стены, ворота, башни, командный центр под щитом двух генераторов), 7 аванпостов с турелями.
    /// В конце — NavMesh (ассет) и снимок карты сверху для тактического вида.
    /// </summary>
    public class MapGenerator
    {
        public const float Size = 1600f, Height = 160f, BaseLevel = 30f;
        const string Gen = WorkshopImport.GenDir;
        readonly Random rnd = new Random(1977);
        readonly Dictionary<string, GameObject> props;
        readonly Dictionary<string, Material> mats;
        readonly Catalog catalog;
        Terrain terrain;
        TerrainData td;
        Transform root;
        MapInfo map;
        byte nextPostId, nextStructId = 1, nextTurretId = 1;
        readonly List<Turret> turrets = new List<Turret>();
        readonly List<CommandPost> posts = new List<CommandPost>();

        public class Site
        {
            public string Name, Letter;
            public Vector2 Pos;
            public float Radius;
            public int Kind;           // 0 аванпост, 1 база Республики, 2 крепость Империи
            public int MinSize;
            public float Level;        // высота площадки
        }

        public readonly List<Site> Sites = new List<Site>
        {
            new Site { Name = "База Республики", Letter = "Р", Pos = new Vector2(0, -600), Radius = 95, Kind = 1 },
            new Site { Name = "Аврора", Letter = "A", Pos = new Vector2(-250, -370), Radius = 55, MinSize = 1 },
            new Site { Name = "Бастион", Letter = "B", Pos = new Vector2(240, -340), Radius = 55, MinSize = 2 },
            new Site { Name = "Каньон", Letter = "C", Pos = new Vector2(-330, -40), Radius = 55, MinSize = 0 },
            new Site { Name = "Перекрёсток", Letter = "D", Pos = new Vector2(40, -60), Radius = 55, MinSize = 0 },
            new Site { Name = "Утёс", Letter = "E", Pos = new Vector2(330, 80), Radius = 55, MinSize = 0 },
            new Site { Name = "Северный", Letter = "F", Pos = new Vector2(-200, 320), Radius = 55, MinSize = 2 },
            new Site { Name = "Застава", Letter = "G", Pos = new Vector2(200, 360), Radius = 55, MinSize = 1 },
            new Site { Name = "Крепость Империи", Letter = "И", Pos = new Vector2(0, 600), Radius = 120, Kind = 2 },
        };

        // дороги: пары индексов площадок
        readonly int[][] roads = { new[] { 0, 1 }, new[] { 0, 2 }, new[] { 1, 3 }, new[] { 1, 4 }, new[] { 2, 4 }, new[] { 2, 5 }, new[] { 3, 6 }, new[] { 4, 6 },
                                   new[] { 4, 7 }, new[] { 5, 7 }, new[] { 6, 8 }, new[] { 7, 8 } };

        public MapGenerator(Dictionary<string, GameObject> props, Dictionary<string, Material> mats, Catalog catalog)
        {
            this.props = props;
            this.mats = mats;
            this.catalog = catalog;
        }

        // ================================================================== рельеф
        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        static float Fbm(float x, float z, int oct, float freq, float seed)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < oct; i++)
            {
                sum += (Mathf.PerlinNoise(x * freq + seed + i * 17.3f, z * freq - seed + i * 31.7f) - 0.5f) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return sum / norm;
        }

        float CreekZ(float x) => 175f + 45f * Mathf.Sin(x / 130f) + 20f * Mathf.Sin(x / 47f);

        float RawHeight(float x, float z)
        {
            float h = BaseLevel;
            h += Fbm(x, z, 5, 0.0025f, 3.1f) * 34f;
            h += Mathf.Abs(Fbm(x, z, 3, 0.008f, 9.7f)) * 10f;
            // горы по краям
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            float m = Smooth(560f, 780f, edge);
            h += m * (55f + Fbm(x, z, 4, 0.006f, 21f) * 70f + 40f);
            // овраг с ручьём
            float d = Mathf.Abs(z - CreekZ(x));
            h -= 7f * Mathf.Exp(-(d / 16f) * (d / 16f));
            return h;
        }

        float DistToRoads(float x, float z, out float along)
        {
            float best = 1e9f;
            along = 0;
            var p = new Vector2(x, z);
            foreach (var r in roads)
            {
                var a = Sites[r[0]].Pos;
                var b = Sites[r[1]].Pos;
                // лёгкий изгиб дороги
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                var q = a + ab * t + new Vector2(-ab.y, ab.x).normalized * Mathf.Sin(t * Mathf.PI) * 30f * (r[0] % 2 == 0 ? 1 : -1);
                float d = Vector2.Distance(p, q);
                if (d < best) { best = d; along = t; }
            }
            return best;
        }

        float FinalHeight(float x, float z)
        {
            float h = RawHeight(x, z);
            foreach (var s in Sites)
            {
                float d = Vector2.Distance(new Vector2(x, z), s.Pos);
                float w = Smooth(s.Radius * 1.5f, s.Radius * 0.85f, d);
                if (w > 0) h = Mathf.Lerp(h, s.Level, w);
            }
            float rd = DistToRoads(x, z, out _);
            if (rd < 14f)
            {
                // дорога чуть утоплена и сглажена
                float smooth = (RawHeight(x - 6, z) + RawHeight(x + 6, z) + RawHeight(x, z - 6) + RawHeight(x, z + 6)) * 0.25f;
                h = Mathf.Lerp(h, Mathf.Min(h, smooth) - 0.3f, Smooth(14f, 5f, rd));
            }
            return h;
        }

        public float GroundAt(Vector3 p) => terrain ? terrain.SampleHeight(p) + terrain.transform.position.y : 0f;

        // ================================================================== сборка
        public Terrain BuildTerrain(Transform parent)
        {
            root = parent;
            foreach (var s in Sites) s.Level = Mathf.Max(BaseLevel - 4f, RawHeight(s.Pos.x, s.Pos.y)) + (s.Kind == 2 ? 2f : 0f);
            td = new TerrainData { heightmapResolution = 1025 };
            td.size = new Vector3(Size, Height, Size);
            int res = td.heightmapResolution;
            var h = new float[res, res];
            for (int y = 0; y < res; y++)
            {
                if (y % 64 == 0) EditorUtility.DisplayProgressBar("Командир клонов: карта", "Рельеф", 0.5f + 0.1f * y / res);
                float z = -Size / 2 + y / (float)(res - 1) * Size;
                for (int x = 0; x < res; x++)
                {
                    float wx = -Size / 2 + x / (float)(res - 1) * Size;
                    h[y, x] = Mathf.Clamp01(FinalHeight(wx, z) / Height);
                }
            }
            td.SetHeights(0, 0, h);
            Directory.CreateDirectory(Gen);
            AssetDatabase.DeleteAsset(Gen + "/Terrain.asset");
            AssetDatabase.CreateAsset(td, Gen + "/Terrain.asset");
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(-Size / 2, 0, -Size / 2);
            terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 4;
            terrain.basemapDistance = 400;
            terrain.treeDistance = 900;
            terrain.treeBillboardDistance = 300;
            terrain.detailObjectDistance = 90;
            terrain.detailObjectDensity = 0.8f;
            terrain.drawInstanced = true;
            go.isStatic = true;
            Paint();
            return terrain;
        }

        TerrainLayer Layer(string name, float tile)
        {
            string path = $"{Gen}/Layer_{name}.terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (!l) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, path); }
            l.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{WorkshopImport.EnvDir}Textures/{name}.png");
            l.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{WorkshopImport.EnvDir}Textures/{name}_n.png");
            l.tileSize = new Vector2(tile, tile);
            l.smoothness = name == "ground_rock" ? 0.2f : 0.05f;
            EditorUtility.SetDirty(l);
            return l;
        }

        void Paint()
        {
            td.terrainLayers = new[] { Layer("ground_grass", 9f), Layer("ground_dirt", 7f), Layer("ground_rock", 14f), Layer("ground_road", 6f) };
            td.alphamapResolution = 1024;
            int ar = td.alphamapResolution;
            var a = new float[ar, ar, 4];
            for (int y = 0; y < ar; y++)
            {
                if (y % 64 == 0) EditorUtility.DisplayProgressBar("Командир клонов: карта", "Текстуры земли", 0.6f + 0.1f * y / ar);
                float z = -Size / 2 + (y + 0.5f) / ar * Size;
                for (int x = 0; x < ar; x++)
                {
                    float wx = -Size / 2 + (x + 0.5f) / ar * Size;
                    float steep = td.GetSteepness((x + 0.5f) / ar, (y + 0.5f) / ar);
                    float rock = Smooth(26f, 38f, steep);
                    float road = Smooth(7.5f, 3.5f, DistToRoads(wx, z, out _));
                    float dirt = Mathf.Clamp01((Fbm(wx, z, 3, 0.01f, 5f) + 0.12f) * 3f) * 0.6f;
                    foreach (var s in Sites)
                    {
                        float d = Vector2.Distance(new Vector2(wx, z), s.Pos);
                        dirt = Mathf.Max(dirt, Smooth(s.Radius * 1.1f, s.Radius * 0.5f, d));
                    }
                    float creek = Mathf.Exp(-Mathf.Pow(Mathf.Abs(z - CreekZ(wx)) / 10f, 2));
                    dirt = Mathf.Max(dirt, creek * 0.8f);
                    float grass = 1f;
                    float w3 = road;
                    float w2 = rock * (1 - w3);
                    float w1 = dirt * (1 - w2 - w3);
                    float w0 = Mathf.Max(0, grass - w1 - w2 - w3);
                    float sum = w0 + w1 + w2 + w3 + 1e-5f;
                    a[y, x, 0] = w0 / sum; a[y, x, 1] = w1 / sum; a[y, x, 2] = w2 / sum; a[y, x, 3] = w3 / sum;
                }
            }
            td.SetAlphamaps(0, 0, a);
        }

        public void Vegetation()
        {
            // деревья
            var protoNames = new[] { "Pine0", "Pine1", "Pine2", "Tree0", "Tree1", "Tree2" }.Where(n => props.ContainsKey(n)).ToArray();
            td.treePrototypes = protoNames.Select(n => new TreePrototype { prefab = props[n], bendFactor = 0.2f }).ToArray();
            var trees = new List<TreeInstance>();
            treeSpots.Clear();
            for (float z = -Size / 2 + 20; z < Size / 2 - 20; z += 8f)
            for (float x = -Size / 2 + 20; x < Size / 2 - 20; x += 8f)
            {
                float jx = x + (float)rnd.NextDouble() * 7f, jz = z + (float)rnd.NextDouble() * 7f;
                float forest = Fbm(jx, jz, 3, 0.006f, 42f) + 0.5f;
                float edge = Smooth(450f, 700f, Mathf.Max(Mathf.Abs(jx), Mathf.Abs(jz)));
                float chance = Smooth(0.52f, 0.68f, forest) * 0.75f + edge * 0.35f;
                if (rnd.NextDouble() > chance) continue;
                if (DistToRoads(jx, jz, out _) < 12f) continue;
                if (Sites.Any(s => Vector2.Distance(s.Pos, new Vector2(jx, jz)) < s.Radius + 25f)) continue;
                float nx = (jx + Size / 2) / Size, nz = (jz + Size / 2) / Size;
                if (td.GetSteepness(nx, nz) > 33f) continue;
                float hy = td.GetInterpolatedHeight(nx, nz) / Height;
                int proto = edge > 0.3f || forest > 0.62f ? rnd.Next(0, 3) : rnd.Next(0, protoNames.Length);
                proto = Mathf.Min(proto, protoNames.Length - 1);
                float s = 0.75f + (float)rnd.NextDouble() * 0.6f;
                trees.Add(new TreeInstance
                {
                    position = new Vector3(nx, hy, nz), prototypeIndex = proto, widthScale = s, heightScale = s * (0.9f + (float)rnd.NextDouble() * 0.25f),
                    color = Color.Lerp(Color.white, new Color(0.85f, 0.9f, 0.8f), (float)rnd.NextDouble()), lightmapColor = Color.white,
                    rotation = (float)rnd.NextDouble() * Mathf.PI * 2f,
                });
                treeSpots.Add(new Vector3(jx, 0, jz));
            }
            td.SetTreeInstances(trees.ToArray(), true);
            // трава
            var grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{WorkshopImport.EnvDir}Textures/grass_blades.png");
            if (grassTex)
            {
                td.SetDetailResolution(1024, 32);
                td.detailPrototypes = new[]
                {
                    new DetailPrototype
                    {
                        prototypeTexture = grassTex, renderMode = DetailRenderMode.GrassBillboard, minWidth = 0.6f, maxWidth = 1.1f, minHeight = 0.35f, maxHeight = 0.8f,
                        healthyColor = new Color(0.7f, 0.82f, 0.5f), dryColor = new Color(0.82f, 0.76f, 0.5f), noiseSpread = 0.3f,
                    },
                };
                int dr = td.detailResolution;
                var map = new int[dr, dr];
                var alpha = td.GetAlphamaps(0, 0, td.alphamapResolution, td.alphamapResolution);
                int ar = td.alphamapResolution;
                for (int y = 0; y < dr; y++)
                for (int x = 0; x < dr; x++)
                {
                    float g = alpha[y * ar / dr, x * ar / dr, 0];
                    float n = Fbm(x * 1.56f, y * 1.56f, 2, 0.03f, 77f) + 0.5f;
                    map[y, x] = g > 0.6f ? Mathf.RoundToInt(n * 7f * g) : 0;
                }
                td.SetDetailLayer(0, 0, 0, map);
            }
        }

        readonly List<Vector3> treeSpots = new List<Vector3>();

        public void Rocks()
        {
            var parent = new GameObject("Rocks").transform;
            parent.SetParent(root, false);
            var names = new[] { "Rock0", "Rock1", "Rock2", "Rock3" }.Where(props.ContainsKey).ToArray();
            if (names.Length == 0) return;
            int placed = 0;
            for (int i = 0; i < 2600 && placed < 520; i++)
            {
                float x = ((float)rnd.NextDouble() - 0.5f) * (Size - 120), z = ((float)rnd.NextDouble() - 0.5f) * (Size - 120);
                if (DistToRoads(x, z, out _) < 9f) continue;
                bool nearSite = false;
                foreach (var s in Sites)
                {
                    float d = Vector2.Distance(s.Pos, new Vector2(x, z));
                    if (d < s.Radius * 0.8f) { nearSite = true; break; }
                }
                if (nearSite) continue;
                float big = (float)rnd.NextDouble();
                float scale = big > 0.93f ? 4f + big * 5f : 0.7f + (float)rnd.NextDouble() * 1.8f;
                var g = Place(names[rnd.Next(names.Length)], new Vector3(x, 0, z), (float)rnd.NextDouble() * 360f, parent, scale, -0.25f * scale);
                if (!g) continue;
                g.transform.rotation *= Quaternion.Euler((float)rnd.NextDouble() * 20 - 10, 0, (float)rnd.NextDouble() * 20 - 10);
                placed++;
            }
        }

        GameObject Place(string prop, Vector3 pos, float yaw, Transform parent, float scale = 1f, float sink = 0f)
        {
            if (!props.TryGetValue(prop, out var pf) || !pf) return null;
            var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            pos.y = GroundAt(pos) + sink;
            g.transform.position = pos;
            g.transform.rotation = Quaternion.Euler(0, yaw, 0);
            g.transform.localScale = Vector3.one * scale;
            g.isStatic = true;
            foreach (Transform t in g.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return g;
        }

        Vector3 W(Site s, float lx, float lz, float yawSite)
        {
            var off = Quaternion.Euler(0, yawSite, 0) * new Vector3(lx, 0, lz);
            return new Vector3(s.Pos.x, 0, s.Pos.y) + off;
        }

        static float YawTo(Vector2 from, Vector2 to) => Mathf.Atan2(to.x - from.x, to.y - from.y) * Mathf.Rad2Deg;

        // ================================================================== постройки
        public void Structures()
        {
            map = new GameObject("MapInfo").AddComponent<MapInfo>();
            map.transform.SetParent(root, false);
            map.Size = new Vector2(1500, 1500);
            for (int i = 0; i < Sites.Count; i++)
            {
                var s = Sites[i];
                EditorUtility.DisplayProgressBar("Командир клонов: карта", "Постройки: " + s.Name, 0.75f + 0.1f * i / Sites.Count);
                var parent = new GameObject(s.Name).transform;
                parent.SetParent(root, false);
                if (s.Kind == 1) RepublicBase(s, parent);
                else if (s.Kind == 2) EmpireFortress(s, parent);
                else Outpost(s, parent);
            }
            map.Posts = posts.ToArray();
            map.Turrets = turrets.ToArray();
            // руины и укрытия вдоль дорог
            var deco = new GameObject("Roadside").transform;
            deco.SetParent(root, false);
            for (int i = 0; i < 90; i++)
            {
                var r = roads[rnd.Next(roads.Length)];
                var a = Sites[r[0]].Pos;
                var b = Sites[r[1]].Pos;
                float t = 0.2f + (float)rnd.NextDouble() * 0.6f;
                var p = Vector2.Lerp(a, b, t) + new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 50f;
                if (DistToRoads(p.x, p.y, out _) < 9f) continue;
                string what = new[] { "ImpBarrier", "Crate", "Ruin", "Container", "ImpBarrier", "RepBarricade" }[rnd.Next(6)];
                var g = Place(what, new Vector3(p.x, 0, p.y), (float)rnd.NextDouble() * 360f, deco);
                if (g && what != "Ruin" && !g.GetComponent<CoverObject>()) g.AddComponent<CoverObject>();
            }
            // разбитая техника на поле
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector2(((float)rnd.NextDouble() - 0.5f) * 700f, ((float)rnd.NextDouble() - 0.5f) * 800f);
                if (Sites.Any(s => Vector2.Distance(s.Pos, p) < s.Radius + 30)) continue;
                var g = Place("ATST", new Vector3(p.x, 0, p.y), (float)rnd.NextDouble() * 360f, deco, 1f, -0.8f);
                if (g) g.transform.rotation *= Quaternion.Euler(0, 0, 70f + (float)rnd.NextDouble() * 30f);
            }
        }

        CommandPost MakePost(Site s, Transform parent, Vector3 pos, PostOwner owner, bool isBase)
        {
            var g = Place(owner == PostOwner.Republic ? "RepPost" : "ImpPost", pos, 0f, parent) ?? new GameObject("Post");
            if (!g.transform.parent) g.transform.SetParent(parent, false);
            g.name = "CommandPost_" + s.Letter;
            var cp = g.AddComponent<CommandPost>();
            cp.Id = nextPostId++;
            cp.Title = s.Name;
            cp.Letter = s.Letter;
            cp.Owner = owner;
            cp.IsBase = isBase;
            cp.Radius = isBase ? 22f : 15f;
            cp.MinSize = s.MinSize;
            // голограмма над столом — меняет цвет по владельцу
            var holo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(holo.GetComponent<Collider>());
            holo.name = "Holo";
            holo.transform.SetParent(g.transform, false);
            holo.transform.localPosition = new Vector3(0, 2.1f, 0);
            holo.transform.localScale = new Vector3(1.2f, 0.5f, 1.2f);
            holo.GetComponent<Renderer>().sharedMaterial = mats["glow_white"];
            holo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(beam.GetComponent<Collider>());
            beam.name = "Beam";
            beam.transform.SetParent(g.transform, false);
            beam.transform.localPosition = new Vector3(0, 30f, 0);
            beam.transform.localScale = new Vector3(0.25f, 28f, 0.25f);
            beam.GetComponent<Renderer>().sharedMaterial = mats["glow_white"];
            beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cp.Lights = new[] { holo.GetComponent<Renderer>(), beam.GetComponent<Renderer>() };
            var l = new GameObject("Light").AddComponent<Light>();
            l.transform.SetParent(g.transform, false);
            l.transform.localPosition = new Vector3(0, 3f, 0);
            l.type = LightType.Point;
            l.range = 18f;
            l.intensity = 2.5f;
            cp.Flag = holo.transform;
            posts.Add(cp);
            return cp;
        }

        Transform[] Points(Transform parent, string name, IEnumerable<Vector3> pts, float yaw = 0f)
        {
            var list = new List<Transform>();
            int i = 0;
            foreach (var p in pts)
            {
                var t = new GameObject($"{name}{i++}").transform;
                t.SetParent(parent, false);
                t.position = new Vector3(p.x, GroundAt(p) + 0.05f, p.z);
                t.rotation = Quaternion.Euler(0, yaw, 0);
                list.Add(t);
            }
            return list.ToArray();
        }

        Turret MakeTurret(Vector3 pos, float yaw, Transform parent, Team team)
        {
            Place("TurretPlatform", pos, yaw, parent);
            string model = team == Team.Empire ? "EmpSentryGun" : "RepSentryGun";
            GameObject g;
            if (props.TryGetValue(model, out var pf) && pf)
            {
                g = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
                g.transform.localScale = Vector3.one * 2.4f;
            }
            else
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                g.transform.SetParent(parent, false);
                g.transform.localScale = new Vector3(0.9f, 0.7f, 0.9f);
                g.GetComponent<Renderer>().sharedMaterial = mats["imperial_panel"];
            }
            g.name = "Turret";
            g.transform.position = new Vector3(pos.x, GroundAt(pos) + 0.8f, pos.z);
            g.transform.rotation = Quaternion.Euler(0, yaw, 0);
            foreach (var c in g.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var bc = g.AddComponent<BoxCollider>();
            var b = new Bounds(g.transform.position, Vector3.one);
            foreach (var r in g.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            bc.center = g.transform.InverseTransformPoint(b.center);
            bc.size = new Vector3(b.size.x / g.transform.lossyScale.x, b.size.y / g.transform.lossyScale.y, b.size.z / g.transform.lossyScale.z);
            var t = g.AddComponent<Turret>();
            t.Id = nextTurretId++;
            t.Team = team;
            t.Head = g.transform;
            t.MaxHealth = t.Health = 420f;
            t.Title = "Турель";
            turrets.Add(t);
            return t;
        }

        void Wall(Site s, Transform parent, float radius, float yawSite, float gapAt, float gapWidth, float breachAt = -999f)
        {
            float circ = 2f * Mathf.PI * radius;
            int n = Mathf.Max(8, Mathf.RoundToInt(circ / 10f));
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * 360f;
                if (Mathf.Abs(Mathf.DeltaAngle(a, gapAt)) < gapWidth) continue;
                if (breachAt > -900f && Mathf.Abs(Mathf.DeltaAngle(a, breachAt)) < 9f) continue;
                var p = W(s, Mathf.Sin(a * Mathf.Deg2Rad) * radius, Mathf.Cos(a * Mathf.Deg2Rad) * radius, yawSite);
                var g = Place("ImpWall", p, a + yawSite + 90f, parent, 1f, -0.3f);
                if (g) g.transform.localScale = new Vector3(circ / n / 10f + 0.05f, 1, 1);
            }
        }

        void Outpost(Site s, Transform parent)
        {
            // ворота смотрят на ближайшую к Республике сторону
            float toRep = YawTo(s.Pos, Sites[0].Pos);
            float yaw = toRep;
            float r = 28f;
            Wall(s, parent, r, yaw, 0f, 14f, breachAt: 120f + rnd.Next(0, 120));
            Place("ImpGate", W(s, 0, r, yaw), yaw, parent, 1f, -0.2f);
            Place("ImpTower", W(s, -r * 0.72f, r * 0.72f, yaw), yaw, parent);
            Place("ImpTower", W(s, r * 0.72f, -r * 0.72f, yaw), yaw + 180, parent);
            Place("ImpBunker", W(s, -9, -12, yaw), yaw + 180f, parent);
            Place("Container", W(s, 12, -6, yaw), yaw + 80f, parent);
            Place("Container", W(s, 14, -1, yaw), yaw + 85f, parent);
            for (int i = 0; i < 6; i++) Place("Crate", W(s, -14 + i * 1.6f, 6 + (i % 2) * 1.5f, yaw), yaw + i * 13f, parent);
            // передний рубеж из барьеров
            for (int i = -2; i <= 2; i++) Place("ImpBarrier", W(s, i * 6f, r + 14f + Mathf.Abs(i) * -2f, yaw), yaw, parent);
            for (int i = -1; i <= 1; i++) Place("ImpBarrier", W(s, i * 7f + 3, 9, yaw), yaw, parent);
            MakeTurret(W(s, -7, r - 5f, yaw), yaw, parent, Team.Empire);
            MakeTurret(W(s, 8, r - 5f, yaw), yaw, parent, Team.Empire);
            var cp = MakePost(s, parent, W(s, 0, 0, yaw), PostOwner.Empire, false);
            cp.Spawns = Points(cp.transform, "Spawn", new[] { W(s, -6, -4, yaw), W(s, 6, -4, yaw), W(s, -4, -16, yaw), W(s, 5, -16, yaw) }, yaw);
            cp.Guards = Points(cp.transform, "Guard", new[] { W(s, -4.5f, r + 2.5f, yaw), W(s, 4.5f, r + 2.5f, yaw) }, yaw);
            cp.Patrol = Points(cp.transform, "Patrol", Enumerable.Range(0, 6).Select(i => W(s, Mathf.Sin(i * Mathf.PI / 3) * (r + 18), Mathf.Cos(i * Mathf.PI / 3) * (r + 18), yaw)));
        }

        void RepublicBase(Site s, Transform parent)
        {
            float yaw = 0f;     // фронт — на север
            Place("LandingPad", W(s, -30, -25, yaw), 0, parent, 1f, 0.05f);
            Place("LandingPad", W(s, 30, -25, yaw), 0, parent, 1f, 0.05f);
            Place("RepBunker", W(s, -18, 12, yaw), 0, parent);
            Place("RepBunker", W(s, 18, 12, yaw), 0, parent);
            Place("RepBunker", W(s, 0, -45, yaw), 0, parent);
            for (int i = 0; i < 7; i++)
            {
                float a = -60 + i * 20;
                Place("RepBarricade", W(s, Mathf.Sin(a * Mathf.Deg2Rad) * 60, Mathf.Cos(a * Mathf.Deg2Rad) * 60 - 4, yaw), a, parent);
            }
            var atte = Place("ATTE", W(s, 45, 25, yaw), 90f, parent);
            for (int i = 0; i < 10; i++) Place("Crate", W(s, -40 + (i % 5) * 1.6f, 20 + (i / 5) * 1.6f, yaw), i * 7f, parent);
            MakeTurret(W(s, -12, 42, yaw), 0, parent, Team.Republic);
            MakeTurret(W(s, 12, 42, yaw), 0, parent, Team.Republic);
            var cp = MakePost(s, parent, W(s, 0, 20, yaw), PostOwner.Republic, true);
            cp.Spawns = Points(cp.transform, "Spawn", new[] { W(s, -30, -25, yaw), W(s, 30, -25, yaw), W(s, -10, -5, yaw), W(s, 10, -5, yaw), W(s, 0, -20, yaw), W(s, -20, -35, yaw), W(s, 20, -35, yaw) });
            map.RepublicBase = cp;
        }

        void EmpireFortress(Site s, Transform parent)
        {
            float yaw = 180f;   // ворота на юг
            float r = 75f;
            Wall(s, parent, r, yaw, 0f, 7f);
            Place("ImpGate", W(s, 0, r, yaw), yaw, parent, 1.3f, -0.2f);
            for (int i = 0; i < 4; i++)
            {
                float a = 45 + i * 90;
                Place("ImpTower", W(s, Mathf.Sin(a * Mathf.Deg2Rad) * r, Mathf.Cos(a * Mathf.Deg2Rad) * r, yaw), a + yaw, parent, 1.2f);
            }
            // командный центр (цель) и генераторы щита
            var cc = Place("CommandCenter", W(s, 0, -28, yaw), yaw, parent);
            var gens = new List<Destructible>();
            foreach (float side in new[] { -1f, 1f })
            {
                var gg = Place("ShieldGenerator", W(s, side * 42, 5, yaw), yaw, parent);
                if (!gg) continue;
                var d = MakeDestructible(gg, "Генератор щита", 1600f, 0.06f, 8f);
                gens.Add(d);
            }
            if (cc)
            {
                var d = MakeDestructible(cc, "Командный центр", 6000f, 0.03f, 18f);
                d.Invulnerable = true;
                map.CommandCenter = d;
            }
            map.ShieldGenerators = gens.ToArray();
            // купол щита (визуал, гаснет, когда генераторы уничтожены)
            if (cc)
            {
                var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(dome.GetComponent<Collider>());
                dome.name = "ShieldDome";
                dome.transform.SetParent(cc.transform, false);
                dome.transform.localPosition = Vector3.zero;
                dome.transform.localScale = new Vector3(46f, 36f, 40f);
                dome.GetComponent<Renderer>().sharedMaterial = mats["shield"];
                dome.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cc.AddComponent<ShieldDome>().Dome = dome;
            }
            Place("ATAT", W(s, 45, -48, yaw), yaw + 30f, parent);
            Place("ImpBunker", W(s, -30, 40, yaw), yaw, parent);
            Place("ImpBunker", W(s, 30, 40, yaw), yaw, parent);
            for (int i = 0; i < 6; i++) Place("Container", W(s, -55 + i * 3f, -20, yaw), yaw + 90f, parent);
            for (int i = -3; i <= 3; i++) Place("ImpBarrier", W(s, i * 7f, 48 + Mathf.Abs(i) * 2f, yaw), yaw, parent);
            for (int i = -2; i <= 2; i++) Place("ImpBarrier", W(s, i * 8f, r + 20f, yaw), yaw, parent);
            MakeTurret(W(s, -14, r - 6f, yaw), yaw, parent, Team.Empire);
            MakeTurret(W(s, 14, r - 6f, yaw), yaw, parent, Team.Empire);
            MakeTurret(W(s, -40, 30, yaw), yaw, parent, Team.Empire);
            MakeTurret(W(s, 40, 30, yaw), yaw, parent, Team.Empire);
            var cp = MakePost(s, parent, W(s, 0, 20, yaw), PostOwner.Empire, true);
            cp.Locked = true;
            cp.Spawns = Points(cp.transform, "Spawn", new[] { W(s, -20, -10, yaw), W(s, 20, -10, yaw), W(s, -10, 0, yaw), W(s, 10, 0, yaw), W(s, 0, -45, yaw), W(s, -50, 10, yaw), W(s, 50, 10, yaw) }, yaw);
            cp.Guards = Points(cp.transform, "Guard", new[] { W(s, -6f, r + 3f, yaw), W(s, 6f, r + 3f, yaw), W(s, -3f, 2f, yaw), W(s, 3f, 2f, yaw) }, yaw);
            cp.Patrol = Points(cp.transform, "Patrol", Enumerable.Range(0, 8).Select(i => W(s, Mathf.Sin(i * Mathf.PI / 4) * (r - 12), Mathf.Cos(i * Mathf.PI / 4) * (r - 12), yaw)));
            map.EmpireBase = cp;
        }

        Destructible MakeDestructible(GameObject g, string title, float hp, float blaster, float radius)
        {
            var d = g.AddComponent<Destructible>();
            d.Id = nextStructId++;
            d.Title = title;
            d.Team = Team.Empire;
            d.MaxHealth = d.Health = hp;
            d.BlasterFactor = blaster;
            d.Objective = true;
            d.ExplosionRadius = radius;
            return d;
        }

        // ================================================================== NavMesh и снимок карты
        public NavMeshData BakeNavMesh()
        {
            EditorUtility.DisplayProgressBar("Командир клонов: карта", "NavMesh (может занять пару минут)...", 0.9f);
            var bounds = new Bounds(new Vector3(0, Height / 2, 0), new Vector3(Size - 60, Height + 20, Size - 60));
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            foreach (var t in treeSpots)
            {
                var p = new Vector3(t.x, GroundAt(t), t.z);
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox, size = new Vector3(1.4f, 6f, 1.4f), transform = Matrix4x4.Translate(p + Vector3.up * 2f), area = 1 });
            }
            var st = NavMesh.GetSettingsByID(0);
            st.agentRadius = 0.4f;
            st.agentHeight = 1.8f;
            st.agentSlope = 40f;
            st.agentClimb = 0.6f;
            st.overrideVoxelSize = true;
            st.voxelSize = 0.22f;
            st.overrideTileSize = true;
            st.tileSize = 256;
            st.minRegionArea = 6f;
            var data = NavMeshBuilder.BuildNavMeshData(st, sources, bounds, Vector3.zero, Quaternion.identity);
            string path = Gen + "/BattleNavMesh.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(data, path);
            return data;
        }

        public Texture2D RenderMapImage()
        {
            var cam = new GameObject("MapCam").AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = map.Size.y / 2f;
            cam.transform.position = new Vector3(0, 600, 0);
            cam.transform.rotation = Quaternion.Euler(90, 0, 0);
            cam.farClipPlane = 1200;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            var rt = new RenderTexture(1024, 1024, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
            tex.Apply();
            // тактическая карта: приглушить и подсинить, как голограмма
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float l = c.r * 0.3f + c.g * 0.55f + c.b * 0.15f;
                px[i] = Color.Lerp(c, new Color(l * 0.6f, l * 0.85f, l * 1.1f), 0.55f);
            }
            tex.SetPixels(px);
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(cam.gameObject);
            RenderSettings.fog = fog;
            string path = Gen + "/MapImage.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var img = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            map.MapImage = img;
            return img;
        }

        public MapInfo Map => map;
    }
}
