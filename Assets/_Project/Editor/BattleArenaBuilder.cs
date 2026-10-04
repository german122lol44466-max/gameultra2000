using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SW.Battle;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace SW.EditorTools
{
    /// <summary>
    /// Меню «Star Wars → Собрать полигон (ситхи против джедаев)»: импорт персонажей v2 и техники, материалы,
    /// контроллеры анимаций, префабы с ИИ/рэгдоллом/хитбоксами и сцена-полигон с укрытиями и базами сторон.
    /// </summary>
    public static class BattleArenaBuilder
    {
        const string Root = "Assets/_Project";
        const string CharDir = Root + "/Art/CharactersV2/";
        const string VehDir = Root + "/Art/VehiclesV2/";
        const string MatDir = Root + "/MaterialsV2";
        const string AnimDir = Root + "/AnimatorsV2";
        const string PrefabDir = Root + "/PrefabsV2";
        const string ScenePath = Root + "/Scenes/BattleArena.unity";

        [Serializable] class Clip { public string name; public int frames; public bool loop; }
        [Serializable] class Mat { public string name; public float[] color; public float smoothness, metallic, alpha; public float[] emission; }
        [Serializable] class Char { public string name, title, side, role, style, weapon; public float height; public Clip[] clips; public Mat[] materials; }
        [Serializable] class CharManifest { public Char[] characters; }
        [Serializable] class Veh { public string name, title, side, kind, crew, crew_clip; public float health, speed, stride, period; public bool hidden_crew; public Clip[] clips; public Mat[] materials; public string[] muzzles; }
        [Serializable] class VehManifest { public Veh[] vehicles; }

        // параметры оружия солдат
        static readonly Dictionary<string, (float dmg, float rpm, int mag, float muzzle, float range)> Guns = new Dictionary<string, (float, float, int, float, float)>
        {
            { "E11", (16f, 320f, 30, 0.39f, 50f) },
            { "DC15A", (20f, 260f, 25, 0.70f, 65f) },
            { "DLT19", (22f, 520f, 60, 0.78f, 60f) },
        };

        [MenuItem("Star Wars/Собрать полигон (ситхи против джедаев)", priority = 10)]
        public static void BuildAll()
        {
            var cm = Load<CharManifest>(CharDir + "manifest_v2.json");
            var vm = Load<VehManifest>(VehDir + "vehicles_v2.json");
            if (cm == null || vm == null) return;
            foreach (var d in new[] { MatDir, AnimDir, PrefabDir, Root + "/Scenes" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
            var mats = new Dictionary<string, Material>();
            foreach (var c in cm.characters) MakeMaterials(c.materials, mats);
            foreach (var v in vm.vehicles) MakeMaterials(v.materials, mats);
            var prefabs = new Dictionary<string, GameObject>();
            foreach (var c in cm.characters)
            {
                string fbx = CharDir + c.name + ".fbx";
                if (AssetImporter.GetAtPath(fbx) == null) { Debug.LogWarning("Нет " + fbx); continue; }
                Remap(fbx, mats);
                prefabs[c.name] = CharacterPrefab(c, fbx, Controller(c.name, fbx, c.clips));
            }
            var catalog = new List<UnitEntry>();
            foreach (var c in cm.characters)
                if (prefabs.TryGetValue(c.name, out var p))
                    catalog.Add(new UnitEntry { Name = c.name, Title = c.title, Team = c.side == "Empire" ? Team.Empire : Team.Republic, Prefab = p });
            foreach (var v in vm.vehicles)
            {
                string fbx = VehDir + v.name + ".fbx";
                if (AssetImporter.GetAtPath(fbx) == null) continue;
                Remap(fbx, mats);
                var vp = VehiclePrefab(v, fbx, Controller(v.name, fbx, v.clips));
                prefabs.TryGetValue(v.crew ?? "", out var crew);
                catalog.Add(new UnitEntry { Name = v.name, Title = v.title, Team = v.side == "Empire" ? Team.Empire : Team.Republic, Prefab = vp,
                                            Vehicle = true, Crew = v.hidden_crew ? null : crew, CrewClip = v.crew_clip ?? "Ride" });
            }
            BuildScene(catalog);
            Debug.Log("Полигон собран: " + ScenePath);
        }

        static T Load<T>(string path) where T : class
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (ta == null)
            {
                EditorUtility.DisplayDialog("Star Wars", "Не найден " + path + "\nСоберите модели: Tools/Blender/build_v2.py", "OK");
                return null;
            }
            return JsonUtility.FromJson<T>(ta.text);
        }

        // ------------------------------------------------------------------ материалы
        static void MakeMaterials(Mat[] list, Dictionary<string, Material> res)
        {
            if (list == null) return;
            var sh = Shader.Find("Standard");
            foreach (var mi in list)
            {
                if (res.ContainsKey(mi.name)) continue;
                string path = $"{MatDir}/{mi.name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
                m.shader = sh;
                m.SetColor("_Color", new Color(Mathf.LinearToGammaSpace(mi.color[0]), Mathf.LinearToGammaSpace(mi.color[1]), Mathf.LinearToGammaSpace(mi.color[2])));
                m.SetFloat("_Metallic", mi.metallic);
                m.SetFloat("_Glossiness", mi.smoothness);
                if (mi.emission != null && mi.emission.Sum() > 0.01f)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", new Color(mi.emission[0], mi.emission[1], mi.emission[2]));
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                EditorUtility.SetDirty(m);
                res[mi.name] = m;
            }
            AssetDatabase.SaveAssets();
        }

        static void Remap(string fbx, Dictionary<string, Material> mats)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            var used = new HashSet<string>(AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().Select(m => m.name));
            foreach (var id in mi.GetExternalObjectMap().Keys) used.Add(id.name);
            bool changed = false;
            foreach (var kv in mats)
            {
                if (!used.Contains(kv.Key)) continue;
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
                changed = true;
            }
            if (changed) mi.SaveAndReimport();
        }

        static AnimatorController Controller(string name, string fbx, Clip[] clips)
        {
            string path = $"{AnimDir}/{name}.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            var all = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(a => !a.name.StartsWith("__preview__"))
                .GroupBy(a => a.name).ToDictionary(g => g.Key, g => g.First());
            int i = 0;
            foreach (var c in clips)
            {
                if (!all.TryGetValue(c.name, out var clip)) continue;
                var st = sm.AddState(c.name, new Vector3(300, 50 * i++, 0));
                st.motion = clip;
                if (c.name == "Idle") sm.defaultState = st;
            }
            return ctrl;
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        // ------------------------------------------------------------------ префабы
        static GameObject CharacterPrefab(Char c, string fbx, AnimatorController ctrl)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = c.name;
            var an = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            an.runtimeAnimatorController = ctrl;
            an.applyRootMotion = false;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
            bool saber = c.role == "sith" || c.role == "jedi";
            var ag = go.AddComponent<NavMeshAgent>();
            ag.radius = 0.35f;
            ag.height = 1.85f * c.height / 1.83f;
            ag.speed = 3.5f;
            ag.stoppingDistance = 0.3f;
            ag.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            var u = go.AddComponent<Unit>();
            u.Team = c.side == "Empire" ? Team.Empire : Team.Republic;
            u.Title = c.title;
            u.IsDuelist = saber;
            u.MaxHealth = u.Health = saber ? (c.name == "DarthVader" ? 650f : 450f) : c.role == "heavy" ? 150f : c.role == "officer" ? 120f : 100f;
            u.Threat = saber ? 2.5f : c.role == "heavy" ? 1.5f : 1f;
            u.AimPoint = FindBone(go.transform, "Chest");
            u.HeadPoint = FindBone(go.transform, "Head");
            var body = go.AddComponent<CharacterBody>();
            body.Clips = c.clips.Select(x => x.name).ToArray();
            body.Lengths = c.clips.Select(x => x.frames / 30f).ToArray();
            body.Saber = saber;
            if (saber)
            {
                body.WalkSpeed = 1.4f; body.AimWalkSpeed = 1.4f; body.RunSpeed = 4.5f; body.StrafeSpeed = 0.8f;
            }
            go.AddComponent<Ragdoll>();
            if (saber)
            {
                var d = go.AddComponent<DuelistAI>();
                d.Style = c.style;
                d.HasChoke = c.name == "DarthVader";
                d.HasLightning = c.name == "DarthSidious";
                d.SaberDamage = c.name == "DarthVader" ? 70f : 55f;
                d.DeflectChance = c.side == "Republic" ? 0.85f : 0.75f;
                d.BlockChance = c.name == "CountDooku" || c.name == "ObiWan" ? 0.7f : 0.55f;
                d.BladeColor = c.name == "MaceWindu" ? new Color(0.6f, 0.1f, 1f) : c.side == "Republic" ? new Color(0.15f, 0.4f, 1f) : new Color(1f, 0.08f, 0.05f);
            }
            else
            {
                var s = go.AddComponent<SoldierAI>();
                var g = Guns.TryGetValue(c.weapon, out var gg) ? gg : Guns["E11"];
                s.Damage = g.dmg; s.RoundsPerMinute = g.rpm; s.Magazine = g.mag; s.Range = g.range;
                s.MuzzleLocal = new Vector3(0, g.muzzle, 0.06f);
                s.Heavy = c.role == "heavy";
                s.Accuracy = c.role == "officer" ? 1.3f : c.side == "Republic" ? 1.15f : 1f;   // клоны точнее :)
                s.BoltColor = c.side == "Empire" ? new Color(1f, 0.12f, 0.08f) : new Color(0.2f, 0.45f, 1f);
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{c.name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject VehiclePrefab(Veh v, string fbx, AnimatorController ctrl)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = v.name;
            var an = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            an.runtimeAnimatorController = ctrl;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
            bool walker = v.kind == "walker";
            var b = new Bounds(go.transform.position, Vector3.one);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            var ag = go.AddComponent<NavMeshAgent>();
            ag.radius = walker ? Mathf.Max(1.2f, b.extents.x * 0.6f) : 1.0f;
            ag.height = b.size.y;
            ag.speed = v.speed;
            ag.stoppingDistance = 2f;
            var u = go.AddComponent<Unit>();
            u.Team = v.side == "Empire" ? Team.Empire : Team.Republic;
            u.Title = v.title;
            u.IsVehicle = true;
            u.MaxHealth = u.Health = v.health;
            u.Threat = walker ? 3f : 2f;
            var aim = new GameObject("AimPoint").transform;
            aim.SetParent(go.transform, false);
            aim.position = b.center;
            u.AimPoint = aim;
            var col = go.AddComponent<BoxCollider>();
            col.center = go.transform.InverseTransformPoint(b.center);
            col.size = new Vector3(b.size.x * 0.8f, b.size.y * 0.9f, b.size.z * 0.8f);
            var hb = go.AddComponent<Hitbox>();
            hb.Owner = u;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var va = go.AddComponent<VehicleAI>();
            va.Walker = walker;
            va.Speed = v.speed;
            va.MoveClip = walker ? "Walk" : "Move";
            va.WalkClipSpeed = v.period > 0 ? v.stride * 2f / v.period : v.speed;
            va.Range = walker ? 70f : 45f;
            va.FireInterval = walker ? (v.name == "ATST" ? 0.9f : 0.6f) : 0.35f;
            va.BoltDamage = walker ? (v.name == "ATST" ? 80f : 45f) : 30f;
            va.Splash = v.name == "ATST" ? 3.5f : 0f;
            va.BoltSize = walker ? 2.2f : 1.4f;
            va.BoltColor = v.side == "Empire" ? new Color(1f, 0.15f, 0.08f) : new Color(0.25f, 0.5f, 1f);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{v.name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ------------------------------------------------------------------ сцена
        static Material M(string name, Color c, float metal, float smooth, Texture tex = null, float tile = 1f)
        {
            string path = $"{MatDir}/Arena_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.color = c;
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Glossiness", smooth);
            if (tex) { m.mainTexture = tex; m.mainTextureScale = Vector2.one * tile; }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D GroundTexture()
        {
            string path = $"{MatDir}/Arena_Ground.png";
            if (!File.Exists(path))
            {
                const int N = 512;
                var t = new Texture2D(N, N, TextureFormat.RGB24, true);
                var rnd = new System.Random(11);
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.02f, y * 0.02f) * 0.6f + Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.3f + (float)rnd.NextDouble() * 0.1f;
                    var c = Color.Lerp(new Color(0.42f, 0.36f, 0.27f), new Color(0.58f, 0.5f, 0.38f), n);
                    t.SetPixel(x, y, c);
                }
                t.Apply();
                File.WriteAllBytes(path, t.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Скала: деформированная сфера.</summary>
        static GameObject Rock(Vector3 pos, Vector3 size, Material m, Transform parent, int seed)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            g.name = "Rock";
            var src = g.GetComponent<MeshFilter>().sharedMesh;
            var mesh = Object.Instantiate(src);
            var v = mesh.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = v[i];
                float n = Mathf.PerlinNoise(p.x * 3f + seed, p.z * 3f + seed * 0.37f) * 0.35f + Mathf.PerlinNoise(p.y * 6f + seed, p.x * 6f) * 0.12f;
                v[i] = p * (0.8f + n);
                if (v[i].y < -0.1f) v[i].y = -0.1f;
            }
            mesh.vertices = v;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            g.GetComponent<MeshFilter>().sharedMesh = mesh;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            var mc = g.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            g.transform.localScale = size;
            g.transform.rotation = Quaternion.Euler(0, seed * 47 % 360, 0);
            g.GetComponent<Renderer>().sharedMaterial = m;
            g.AddComponent<CoverObject>();
            g.isStatic = true;
            return g;
        }

        static GameObject Box(string name, Vector3 pos, Vector3 size, Material m, Transform parent, float yaw = 0, bool cover = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            g.transform.localScale = size;
            g.transform.rotation = Quaternion.Euler(0, yaw, 0);
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (cover) g.AddComponent<CoverObject>();
            g.isStatic = true;
            return g;
        }

        static void BuildScene(List<UnitEntry> catalog)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.72f, 0.66f, 0.58f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 80f;
            RenderSettings.fogEndDistance = 320f;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(42, -35, 0);

            var env = new GameObject("Arena").transform;
            var ground = M("Ground", Color.white, 0f, 0.15f, GroundTexture(), 30f);
            var rock = M("Rock", new Color(0.46f, 0.4f, 0.33f), 0f, 0.12f);
            var metal = M("ImperialMetal", new Color(0.34f, 0.35f, 0.37f), 0.5f, 0.35f);
            var white = M("RepublicWhite", new Color(0.78f, 0.77f, 0.74f), 0.1f, 0.4f);
            var crate = M("Crate", new Color(0.3f, 0.32f, 0.28f), 0.3f, 0.3f);
            var red = M("RedLight", Color.black, 0, 0.5f);
            red.EnableKeyword("_EMISSION"); red.SetColor("_EmissionColor", new Color(3f, 0.3f, 0.2f));
            var blue = M("BlueLight", Color.black, 0, 0.5f);
            blue.EnableKeyword("_EMISSION"); blue.SetColor("_EmissionColor", new Color(0.3f, 0.7f, 3f));
            Box("Ground", new Vector3(0, -0.5f, 0), new Vector3(260, 1, 190), ground, env, 0, false);
            // окраинные скалы (граница поля)
            var rnd = new System.Random(5);
            for (int i = 0; i < 46; i++)
            {
                float a = i / 46f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * 125f, 0, Mathf.Sin(a) * 90f);
                Rock(p, new Vector3(14 + (float)rnd.NextDouble() * 10, 10 + (float)rnd.NextDouble() * 14, 14 + (float)rnd.NextDouble() * 10), rock, env, i);
            }
            // укрытия: валуны, стенки, ящики, руины
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector3(-95 + (float)rnd.NextDouble() * 190, 0, -55 + (float)rnd.NextDouble() * 110);
                if (Mathf.Abs(p.z) > 60) continue;
                float s = 1.2f + (float)rnd.NextDouble() * 2.5f;
                Rock(p, new Vector3(s * 1.4f, s * (0.6f + (float)rnd.NextDouble() * 0.6f), s), rock, env, i + 100);
            }
            for (int i = 0; i < 26; i++)
            {
                var p = new Vector3(-90 + (float)rnd.NextDouble() * 180, 0, -45 + (float)rnd.NextDouble() * 90);
                float yaw = (float)rnd.NextDouble() * 180f;
                if (i % 3 == 0)
                    Box("Wall", p + Vector3.up * 1.3f, new Vector3(6f, 2.6f, 0.6f), metal, env, yaw);
                else if (i % 3 == 1)
                    Box("Barrier", p + Vector3.up * 0.55f, new Vector3(3.5f, 1.1f, 0.7f), metal, env, yaw);
                else
                {
                    Box("Crate", p + Vector3.up * 0.6f, new Vector3(1.4f, 1.2f, 1.2f), crate, env, yaw);
                    Box("Crate", p + new Vector3(1.6f, 0.6f, 0.3f), new Vector3(1.2f, 1.2f, 1.2f), crate, env, yaw + 12);
                }
            }
            // руины в центре
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * 12f, 0, Mathf.Sin(a) * 12f);
                Box("RuinPillar", p + Vector3.up * 3f, new Vector3(2f, 6f - i % 3 * 1.5f, 2f), rock, env, i * 20);
            }
            // базы сторон
            Transform Base(string name, float z, Material mat, Material light, bool emp)
            {
                var b = new GameObject(name).transform;
                b.SetParent(env, false);
                Box("Bunker", new Vector3(0, 3f, z + Mathf.Sign(z) * 8f), new Vector3(26f, 6f, 8f), mat, b);
                Box("Pad", new Vector3(0, 0.1f, z), new Vector3(20f, 0.2f, 12f), mat, b, 0, false);
                for (int i = -2; i <= 2; i++)
                    Box("Light", new Vector3(i * 4.5f, 0.25f, z - Mathf.Sign(z) * 6.2f), new Vector3(2.5f, 0.1f, 0.3f), light, b, 0, false);
                foreach (int sx in new[] { -1, 1 })
                    Box("Tower", new Vector3(sx * 16f, 5f, z + Mathf.Sign(z) * 4f), new Vector3(3f, 10f, 3f), mat, b);
                var spawn = new GameObject(name + "_Spawn").transform;
                spawn.SetParent(b, false);
                spawn.position = new Vector3(0, 0, z);
                var pl = new GameObject("BaseLight").AddComponent<Light>();
                pl.transform.SetParent(b, false);
                pl.transform.position = new Vector3(0, 6f, z);
                pl.type = LightType.Point; pl.range = 30f; pl.intensity = 1.5f;
                pl.color = emp ? new Color(1f, 0.6f, 0.5f) : new Color(0.6f, 0.75f, 1f);
                return spawn;
            }
            var es = Base("EmpireBase", -68f, metal, red, true);
            var rs = Base("RepublicBase", 68f, white, blue, false);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60;
            cam.farClipPlane = 600;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(-60, 35, -20);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0, 0) - camGo.transform.position);
            var fly = camGo.AddComponent<FlyCamera>();
            fly.Speed = 15f;
            var mgr = new GameObject("BattleManager").AddComponent<BattleManager>();
            mgr.Catalog = catalog.ToArray();
            mgr.EmpireSpawn = es;
            mgr.RepublicSpawn = rs;
            mgr.Cam = fly;
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
