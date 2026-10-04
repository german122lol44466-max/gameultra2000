using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SW.EditorTools
{
    /// <summary>
    /// Меню «Star Wars → Собрать тестовую карту»: материалы по таблице из Blender, контроллеры анимаций,
    /// префабы персонажей и сцена TestMap (ангар, персонажи на постаментах, патрули, стойка с оружием).
    /// </summary>
    public static class TestMapBuilder
    {
        const string Root = "Assets/_Project";
        const string ManifestPath = Root + "/Art/Characters/characters_unity.json";
        const string MatDir = Root + "/Materials";
        const string AnimDir = Root + "/Animators";
        const string PrefabDir = Root + "/Prefabs";
        const string TexDir = Root + "/Textures";
        const string ScenePath = Root + "/Scenes/TestMap.unity";

        [Serializable] class Manifest { public CharacterInfo[] characters; }
        [Serializable] class CharacterInfo
        {
            public string name, title, weapon, style;
            public float height;
            public ClipInfo[] clips;
            public MatInfo[] materials;
        }
        [Serializable] class ClipInfo { public string name; public int frames; public bool loop; }
        [Serializable] class MatInfo
        {
            public string name;
            public float[] color;
            public float smoothness, metallic, alpha;
            public float[] emission;
        }

        [MenuItem("Star Wars/Собрать тестовую карту", priority = 0)]
        public static void BuildAll()
        {
            var manifest = LoadManifest();
            if (manifest == null) return;
            foreach (var d in new[] { MatDir, AnimDir, PrefabDir, TexDir, Root + "/Scenes" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();

            var mats = BuildMaterials(manifest);
            var prefabs = new Dictionary<string, GameObject>();
            foreach (var c in manifest.characters)
            {
                string fbx = CharacterImportSettings.CharactersDir + c.name + ".fbx";
                if (AssetImporter.GetAtPath(fbx) == null) { Debug.LogWarning("Нет модели " + fbx); continue; }
                Remap(fbx, mats);
                var ctrl = BuildController(c, fbx);
                prefabs[c.name] = BuildPrefab(c, fbx, ctrl);
            }
            foreach (var w in Directory.GetFiles(CharacterImportSettings.WeaponsDir, "*.fbx"))
                Remap(w.Replace('\\', '/'), mats);
            BuildScene(manifest, prefabs);
            Debug.Log("Тестовая карта собрана: " + ScenePath);
        }

        [MenuItem("Star Wars/Открыть тестовую карту", priority = 1)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) BuildAll();
            EditorSceneManager.OpenScene(ScenePath);
        }

        static Manifest LoadManifest()
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (ta == null)
            {
                EditorUtility.DisplayDialog("Star Wars", "Не найден " + ManifestPath + ".\nСначала соберите модели (Tools/Blender).", "OK");
                return null;
            }
            return JsonUtility.FromJson<Manifest>(ta.text);
        }

        // ------------------------------------------------------------------ материалы

        static Color Lin2Gamma(float[] c, float a = 1f) =>
            new Color(Mathf.LinearToGammaSpace(c[0]), Mathf.LinearToGammaSpace(c[1]), Mathf.LinearToGammaSpace(c[2]), a);

        static Dictionary<string, Material> BuildMaterials(Manifest m)
        {
            var res = new Dictionary<string, Material>();
            var shader = Shader.Find("Standard");
            foreach (var mi in m.characters.SelectMany(c => c.materials ?? new MatInfo[0]))
            {
                if (res.ContainsKey(mi.name)) continue;
                string path = $"{MatDir}/{mi.name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                mat.SetColor("_Color", Lin2Gamma(mi.color));
                mat.SetFloat("_Metallic", mi.metallic);
                mat.SetFloat("_Glossiness", mi.smoothness);
                if (mi.emission != null && mi.emission.Sum() > 0.01f)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", new Color(mi.emission[0], mi.emission[1], mi.emission[2]));
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    mat.DisableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", Color.black);
                }
                EditorUtility.SetDirty(mat);
                res[mi.name] = mat;
            }
            AssetDatabase.SaveAssets();
            return res;
        }

        static void Remap(string fbx, Dictionary<string, Material> mats)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            // имена материалов модели: ещё не переназначенные (вложенные) + уже переназначенные
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

        // ------------------------------------------------------------------ анимации и префабы

        static AnimatorController BuildController(CharacterInfo c, string fbx)
        {
            string path = $"{AnimDir}/{c.name}.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
                .Where(a => !a.name.StartsWith("__preview__")).ToDictionary(a => a.name, a => a);
            int i = 0;
            foreach (var ci in c.clips)
            {
                if (!clips.TryGetValue(ci.name, out var clip)) { Debug.LogWarning($"{c.name}: нет клипа {ci.name}"); continue; }
                var st = sm.AddState(ci.name, new Vector3(300, 60 * i++, 0));
                st.motion = clip;
                if (ci.name == "Idle") sm.defaultState = st;
            }
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static GameObject BuildPrefab(CharacterInfo c, string fbx, AnimatorController ctrl)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = c.name;
            var an = go.GetComponent<Animator>();
            if (an == null) an = go.AddComponent<Animator>();
            an.runtimeAnimatorController = ctrl;
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var sc = go.AddComponent<ShowcaseCharacter>();
            sc.Title = c.title;
            sc.Style = c.style;
            sc.Clips = c.clips.Select(x => x.name).ToArray();
            sc.Loops = c.clips.Select(x => x.loop).ToArray();
            sc.Lengths = c.clips.Select(x => x.frames / 30f).ToArray();
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.updateWhenOffscreen = true;
                r.quality = SkinQuality.Bone4;
            }
            string path = $"{PrefabDir}/{c.name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ------------------------------------------------------------------ сцена

        static Material Mat(string name, Color color, float metallic, float smooth, Color? emission = null, Texture tex = null, Vector2? tiling = null)
        {
            string path = $"{MatDir}/Map_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_Color", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Glossiness", smooth);
            if (tex != null)
            {
                m.mainTexture = tex;
                m.mainTextureScale = tiling ?? Vector2.one;
            }
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D FloorTexture()
        {
            string path = $"{TexDir}/HangarFloor.png";
            if (!File.Exists(path))
            {
                const int N = 512;
                var t = new Texture2D(N, N, TextureFormat.RGB24, true);
                var rnd = new System.Random(7);
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float v = 0.22f + (float)rnd.NextDouble() * 0.03f;
                    bool seam = x % 256 < 3 || y % 256 < 3;
                    bool inner = (x % 256 > 40 && x % 256 < 44) || (y % 256 > 40 && y % 256 < 44);
                    if (seam) v = 0.06f;
                    else if (inner) v = 0.17f;
                    t.SetPixel(x, y, new Color(v, v * 1.02f, v * 1.08f));
                }
                t.Apply();
                File.WriteAllBytes(path, t.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static GameObject Box(string name, Vector3 pos, Vector3 size, Material m, Transform parent, bool collider = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        static void PointLight(Vector3 pos, Color c, float range, float intensity, Transform parent)
        {
            var g = new GameObject("Light");
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            var l = g.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
        }

        static void Label3D(string text, Vector3 pos, Transform parent, float size = 0.06f)
        {
            var g = new GameObject("Label_" + text);
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            var tm = g.AddComponent<TextMesh>();
            tm.text = text;
            tm.characterSize = size;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(1f, 0.85f, 0.25f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                tm.font = font;
                g.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            // текст читается со стороны камеры (камера смотрит в -Z)
            g.transform.rotation = Quaternion.Euler(0, 180, 0);
        }

        static void BuildScene(Manifest manifest, Dictionary<string, GameObject> prefabs)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- свет и атмосфера
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.32f, 0.36f, 0.45f);
            RenderSettings.ambientEquatorColor = new Color(0.18f, 0.19f, 0.22f);
            RenderSettings.ambientGroundColor = new Color(0.06f, 0.06f, 0.07f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.04f, 0.045f, 0.06f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 25f;
            RenderSettings.fogEndDistance = 90f;
            var sun = new GameObject("Key Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.9f;
            sun.color = new Color(0.9f, 0.93f, 1f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52, 150, 0);

            // --- ангар
            var env = new GameObject("Hangar").transform;
            var floorMat = Mat("Floor", Color.white, 0.6f, 0.55f, null, FloorTexture(), new Vector2(20, 15));
            var wallMat = Mat("Wall", new Color(0.16f, 0.17f, 0.19f), 0.5f, 0.45f);
            var trimMat = Mat("Trim", new Color(0.08f, 0.085f, 0.095f), 0.7f, 0.6f);
            var stripMat = Mat("LightStrip", Color.white, 0f, 0.5f, new Color(1.6f, 1.8f, 2.2f));
            var redMat = Mat("RedStrip", Color.black, 0f, 0.5f, new Color(2.2f, 0.15f, 0.1f));
            var crateMat = Mat("Crate", new Color(0.3f, 0.31f, 0.33f), 0.4f, 0.35f);
            var padMat = Mat("Pedestal", new Color(0.1f, 0.1f, 0.11f), 0.8f, 0.75f);
            Box("Floor", new Vector3(0, -0.05f, 0), new Vector3(80, 0.1f, 60), floorMat, env);
            Box("Wall_Back", new Vector3(0, 6, -22), new Vector3(80, 12, 0.5f), wallMat, env);
            Box("Wall_Left", new Vector3(-30, 6, 0), new Vector3(0.5f, 12, 60), wallMat, env);
            Box("Wall_Right", new Vector3(30, 6, 0), new Vector3(0.5f, 12, 60), wallMat, env);
            Box("Ceiling", new Vector3(0, 12, 0), new Vector3(80, 0.3f, 60), trimMat, env);
            for (int i = -6; i <= 6; i++)
            {
                Box("Pillar", new Vector3(i * 4.5f, 6, -21.5f), new Vector3(0.8f, 12, 0.6f), trimMat, env);
                Box("Strip", new Vector3(i * 4.5f + 2.25f, 7.5f, -21.7f), new Vector3(3f, 0.12f, 0.05f), stripMat, env, false);
                Box("RedStrip", new Vector3(i * 4.5f + 2.25f, 0.4f, -21.7f), new Vector3(3f, 0.05f, 0.05f), redMat, env, false);
            }
            for (int i = -2; i <= 2; i++)
                PointLight(new Vector3(i * 10, 7, -19), new Color(0.75f, 0.85f, 1f), 18, 1.2f, env);
            // ящики и укрытия
            var rnd = new System.Random(3);
            for (int i = 0; i < 14; i++)
            {
                float x = -26 + (float)rnd.NextDouble() * 52;
                float z = -18 + (float)rnd.NextDouble() * 6;
                float s = 0.8f + (float)rnd.NextDouble() * 0.8f;
                Box("Crate", new Vector3(x, s / 2, z), new Vector3(s * 1.4f, s, s), crateMat, env)
                    .transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 40 - 20, 0);
            }

            // --- персонажи на постаментах: ситхи впереди, штурмовики сзади на платформе
            var cast = new GameObject("Characters").transform;
            var sith = manifest.characters.Where(c => c.style != "rifle").ToArray();
            var troopers = manifest.characters.Where(c => c.style == "rifle").ToArray();
            void Row(CharacterInfo[] row, float z, float y, float spacing)
            {
                for (int i = 0; i < row.Length; i++)
                {
                    if (!prefabs.TryGetValue(row[i].name, out var pf)) continue;
                    float x = (i - (row.Length - 1) / 2f) * spacing;
                    var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    pad.name = "Pedestal_" + row[i].name;
                    pad.transform.SetParent(cast, false);
                    pad.transform.position = new Vector3(x, y + 0.08f, z);
                    pad.transform.localScale = new Vector3(2.6f, 0.08f, 2.6f);
                    pad.GetComponent<Renderer>().sharedMaterial = padMat;
                    var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    ring.name = "Ring";
                    ring.transform.SetParent(pad.transform, false);
                    ring.transform.localScale = new Vector3(1.03f, 0.3f, 1.03f);
                    ring.GetComponent<Renderer>().sharedMaterial = row[i].style == "rifle" ? stripMat : redMat;
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                    inst.transform.SetParent(cast, false);
                    inst.transform.position = new Vector3(x, y + 0.16f, z);
                    inst.transform.rotation = Quaternion.identity;
                }
            }
            Box("TrooperPlatform", new Vector3(0, 0.3f, -6), new Vector3(16, 0.6f, 4), trimMat, env);
            Row(sith, 0f, 0f, 4.2f);
            Row(troopers, -6f, 0.6f, 4.2f);

            // --- патрули штурмовиков вокруг
            var patrols = new GameObject("Patrols").transform;
            void AddPatrol(string name, Vector3[] pts, float speed, string clip, float phase)
            {
                if (!prefabs.TryGetValue(name, out var pf)) return;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                inst.transform.SetParent(patrols, false);
                inst.name = name + "_Patrol";
                int k = Mathf.FloorToInt(phase * pts.Length) % pts.Length;
                inst.transform.position = pts[k];
                var show = inst.GetComponent<ShowcaseCharacter>();
                if (show != null) Object.DestroyImmediate(show);
                var p = inst.AddComponent<Patrol>();
                p.Points = pts;
                p.Speed = speed;
                p.Clip = clip;
            }
            var loop = new[] { new Vector3(-14, 0, 6), new Vector3(14, 0, 6), new Vector3(14, 0, -12), new Vector3(-14, 0, -12) };
            AddPatrol("Stormtrooper", loop, 1.6f, "Walk", 0f);
            AddPatrol("Stormtrooper", loop, 1.6f, "Walk", 0.5f);
            AddPatrol("TrooperCommander", loop, 1.6f, "Walk", 0.25f);
            AddPatrol("HeavyTrooper", new[] { new Vector3(-22, 0, 10), new Vector3(-22, 0, -14), new Vector3(22, 0, -14), new Vector3(22, 0, 10) },
                4.5f, "Run", 0f);

            // --- стойка с оружием
            var rack = new GameObject("WeaponRack").transform;
            Box("RackTable", new Vector3(18, 0.45f, -2), new Vector3(2.2f, 0.9f, 7), trimMat, rack);
            var weapons = Directory.GetFiles(CharacterImportSettings.WeaponsDir, "*.fbx").OrderBy(s => s).ToArray();
            for (int i = 0; i < weapons.Length; i++)
            {
                var wpf = AssetDatabase.LoadAssetAtPath<GameObject>(weapons[i].Replace('\\', '/'));
                if (wpf == null) continue;
                var w = (GameObject)PrefabUtility.InstantiatePrefab(wpf);
                w.transform.SetParent(rack, false);
                var b = Bounds(w);
                // самую длинную ось — вдоль стола (Z), оружие лежит на столе
                if (b.size.x >= b.size.y && b.size.x >= b.size.z) w.transform.rotation = Quaternion.Euler(0, 90, 0);
                else if (b.size.y >= b.size.x && b.size.y >= b.size.z) w.transform.rotation = Quaternion.Euler(90, 0, 0);
                b = Bounds(w);
                float z = -2 + (i - (weapons.Length - 1) / 2f) * 1.1f;
                w.transform.position += new Vector3(18, 0.92f, z) - new Vector3(b.center.x, b.min.y, b.center.z);
                Label3D(Path.GetFileNameWithoutExtension(weapons[i]).Replace("_", " "), new Vector3(16.75f, 1.05f, z), rack, 0.035f);
            }
            PointLight(new Vector3(17, 3, -2), new Color(1f, 0.95f, 0.85f), 7, 1.4f, rack);

            // --- камера и интерфейс
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.022f, 0.03f);
            cam.fieldOfView = 55;
            cam.farClipPlane = 200;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0, 3.4f, 12.5f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0, 1.2f, -2f) - camGo.transform.position);
            var fly = camGo.AddComponent<FlyCamera>();
            var ui = new GameObject("TestMapUI").AddComponent<TestMapUI>();
            ui.Cam = fly;

            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static Bounds Bounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.one * 0.1f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }

    /// <summary>При первом открытии проекта сам собирает карту, если её ещё нет.</summary>
    [InitializeOnLoad]
    static class TestMapAutoBuild
    {
        static TestMapAutoBuild()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool("SW_AutoBuildDone", false)) return;
                SessionState.SetBool("SW_AutoBuildDone", true);
                if (!File.Exists("Assets/_Project/Scenes/TestMap.unity") &&
                    File.Exists("Assets/_Project/Art/Characters/characters_unity.json"))
                {
                    TestMapBuilder.BuildAll();
                    EditorSceneManager.OpenScene("Assets/_Project/Scenes/TestMap.unity");
                }
            };
        }
    }
}
