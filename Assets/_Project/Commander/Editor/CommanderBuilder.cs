using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SW.Battle;
using SW.Commander.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SW.Commander.EditorTools
{
    /// <summary>
    /// «Star Wars → Командир клонов → Собрать игру»: каталог юнитов (модели из аддонов или v2), префабы построек,
    /// сцена боя (карта MapGenerator) и сцена меню, список сцен сборки. При первом открытии проекта
    /// всё собирается само; Play всегда стартует с меню.
    /// </summary>
    [InitializeOnLoad]
    public static class CommanderBuilder
    {
        const string Root = "Assets/_Project/Commander";
        const string Gen = WorkshopImport.GenDir;
        public const string MenuScene = Root + "/Scenes/Menu.unity";
        public const string BattleScene = Root + "/Scenes/Battle.unity";
        const string CatalogPath = Root + "/Resources/Commander/Catalog.asset";
        const string PlayPref = "SW_Commander_PlayFromMenu";
        const string PlayMenu = "Star Wars/Командир клонов/Play всегда из меню";

        static CommanderBuilder()
        {
            EditorApplication.delayCall += AutoStart;
        }

        static void AutoStart()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool on = EditorPrefs.GetBool(PlayPref, true);
            Menu.SetChecked(PlayMenu, on);
            if (!File.Exists(MenuScene) || !File.Exists(BattleScene))
            {
                if (!SessionState.GetBool("SW_CommanderAutoBuilt", false))
                {
                    SessionState.SetBool("SW_CommanderAutoBuilt", true);
                    BuildAll();
                }
                return;
            }
            if (string.IsNullOrEmpty(EditorSceneManager.GetActiveScene().path)) EditorSceneManager.OpenScene(MenuScene);
            EditorSceneManager.playModeStartScene = on ? AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScene) : null;
        }

        [MenuItem(PlayMenu, priority = 40)]
        static void TogglePlay()
        {
            EditorPrefs.SetBool(PlayPref, !EditorPrefs.GetBool(PlayPref, true));
            AutoStart();
        }

        [MenuItem("Star Wars/Командир клонов/Собрать игру (всё)", priority = 1)]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var errors = new List<string>();
            try
            {
                Directory.CreateDirectory(Gen);
                Directory.CreateDirectory(Root + "/Scenes");
                AssetDatabase.Refresh();
                var cat = BuildCatalog(errors);
                var mats = EnvMaterials();
                var props = PropPrefabs(mats, errors);
                BuildBattleScene(cat, props, mats, errors);
                BuildMenuScene(cat, props, mats);
                SetBuildScenes();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                errors.Add(e.GetType().Name + ": " + e.Message);
            }
            finally { EditorUtility.ClearProgressBar(); }
            EditorSceneManager.OpenScene(MenuScene);
            EditorSceneManager.playModeStartScene = EditorPrefs.GetBool(PlayPref, true) ? AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScene) : null;
            var c = AssetDatabase.LoadAssetAtPath<Catalog>(CatalogPath);
            string msg = $"Готово. Юнитов в каталоге: {c?.Units.Count ?? 0}" + (Directory.Exists(WorkshopImport.WorkshopDir) ? " (модели из аддонов Workshop)" : " (модели v2 — аддоны не импортированы, см. Tools/SourceImport)") +
                         "\nСцены: Menu и Battle. Нажмите Play.";
            if (errors.Count > 0) msg += "\n\nОшибки (подробности в Console):\n- " + string.Join("\n- ", errors.Take(12));
            Debug.Log("[Командир клонов] " + msg);
            EditorUtility.DisplayDialog("Командир клонов", msg, "OK");
        }

        [MenuItem("Star Wars/Командир клонов/Только каталог юнитов", priority = 2)]
        static void OnlyCatalog() { var e = new List<string>(); BuildCatalog(e); EditorUtility.ClearProgressBar(); }

        // ================================================================== каталог
        static Catalog BuildCatalog(List<string> errors)
        {
            var units = WorkshopImport.BuildWorkshopUnits(errors);
            if (units.Count == 0) units = WorkshopImport.BuildV2Units(errors);
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
            var cat = AssetDatabase.LoadAssetAtPath<Catalog>(CatalogPath);
            if (!cat) { cat = ScriptableObject.CreateInstance<Catalog>(); AssetDatabase.CreateAsset(cat, CatalogPath); }
            // порядок: Республика, затем Империя; внутри — по роли
            cat.Units = units.OrderBy(u => u.Team == Team.Republic ? 0 : 1).ThenBy(u => (int)u.Role).ToList();
            cat.Sounds = WorkshopImport.Sounds();
            KeepShaders();
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            return cat;
        }

        /// <summary>Шейдеры, которые ищутся кодом (Shader.Find), — в сборку через материалы в Resources.</summary>
        static void KeepShaders()
        {
            string dir = Root + "/Resources/Commander/Shaders";
            Directory.CreateDirectory(dir);
            foreach (var n in new[] { "Legacy Shaders/Particles/Additive", "Unlit/Color", "Sprites/Default", "Standard", "Skybox/Procedural" })
            {
                var sh = Shader.Find(n);
                if (!sh) continue;
                string p = $"{dir}/{n.Replace('/', '_').Replace(' ', '_')}.mat";
                if (!File.Exists(p)) AssetDatabase.CreateAsset(new Material(sh), p);
            }
        }

        // ================================================================== материалы окружения
        [Serializable] class EnvItem { public string name; public string[] materials; public float[] size; }
        [Serializable] class EnvManifest { public EnvItem[] items; }

        static Dictionary<string, Material> EnvMaterials()
        {
            var res = new Dictionary<string, Material>();
            string dir = Gen + "/EnvMaterials";
            Directory.CreateDirectory(dir);
            string tex = WorkshopImport.EnvDir + "Textures/";
            Material M(string name, string albedo, string normal, Color c, float smooth, float metal, Color? emission = null, float tile = 1f)
            {
                string p = $"{dir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (!m) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, p); }
                m.color = c;
                if (albedo != null) m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + albedo);
                if (normal != null)
                {
                    m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex + normal));
                    m.EnableKeyword("_NORMALMAP");
                }
                m.mainTextureScale = Vector2.one * tile;
                m.SetFloat("_Glossiness", smooth);
                m.SetFloat("_Metallic", metal);
                if (emission.HasValue)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", emission.Value);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                res[name] = m;
                return m;
            }
            M("imperial_panel", "imperial_panel.png", "imperial_panel_n.png", Color.white, 0.45f, 0.4f);
            M("imperial_light", "imperial_light.png", "imperial_panel_n.png", Color.white, 0.5f, 0.35f);
            M("duracrete", "duracrete.png", "duracrete_n.png", Color.white, 0.15f, 0f);
            M("republic_panel", "republic_panel.png", "republic_panel_n.png", Color.white, 0.55f, 0.1f);
            M("bark", "bark.png", "bark_n.png", Color.white, 0.05f, 0f);
            M("leaves", "leaves.png", null, new Color(0.85f, 0.95f, 0.8f), 0.1f, 0f);
            M("rock", "ground_rock.png", "ground_rock_n.png", new Color(0.92f, 0.9f, 0.88f), 0.12f, 0f);
            M("glow_red", null, null, Color.black, 0.5f, 0f, new Color(4f, 0.35f, 0.25f));
            M("glow_blue", null, null, Color.black, 0.5f, 0f, new Color(0.4f, 1.2f, 4f));
            M("glow_white", null, null, Color.black, 0.5f, 0f, new Color(2.5f, 2.5f, 2.6f));
            var shield = M("shield", null, null, new Color(0.3f, 0.6f, 1f, 0.12f), 0.9f, 0f, new Color(0.15f, 0.35f, 0.9f));
            WorkshopImport.SetMode(shield, "blend");
            AssetDatabase.SaveAssets();
            return res;
        }

        static Dictionary<string, GameObject> PropPrefabs(Dictionary<string, Material> mats, List<string> errors)
        {
            var res = new Dictionary<string, GameObject>();
            var path = WorkshopImport.EnvDir + "manifest.json";
            if (File.Exists(path))
            {
                var man = JsonUtility.FromJson<EnvManifest>(File.ReadAllText(path));
                foreach (var it in man.items)
                {
                    try
                    {
                        string kind = it.name.StartsWith("Pine") || it.name.StartsWith("Tree") ? "tree"
                                    : it.name.StartsWith("Rock") || it.name == "ImpBarrier" || it.name == "RepBarricade" || it.name == "Ruin" || it.name == "Container" ? "cover"
                                    : it.name == "Crate" ? "small" : "mesh";
                        var p = WorkshopImport.PropPrefab(it.name, WorkshopImport.EnvDir + it.name + ".fbx", n => mats.TryGetValue(n, out var m) ? m : null, kind);
                        if (p) res[it.name] = p;
                    }
                    catch (Exception e) { errors.Add($"{it.name}: {e.Message}"); }
                }
            }
            // техника и турели из аддонов
            string pd = WorkshopImport.WorkshopDir + "Props/";
            if (Directory.Exists(pd))
            {
                foreach (var dir in Directory.GetDirectories(pd))
                {
                    string name = Path.GetFileName(dir);
                    string fbx = $"{pd}{name}/{name}.fbx";
                    string json = $"{pd}{name}/{name}.json";
                    if (!File.Exists(fbx) || !File.Exists(json)) continue;
                    try
                    {
                        var pj = JsonUtility.FromJson<WorkshopImport.PropJson>(File.ReadAllText(json));
                        var pm = new Dictionary<string, Material>();
                        foreach (var m in pj.materials ?? new WorkshopImport.MatJson[0]) pm[m.name] = WorkshopImport.MakeMaterial($"{pd}{name}", m, $"{pd}{name}/Materials");
                        var p = WorkshopImport.PropPrefab(name, fbx, n => pm.TryGetValue(n, out var mm) ? mm : null, name.Contains("Sentry") || name == "ThermalDetonator" ? "none" : "mesh");
                        if (p) res[name] = p;
                    }
                    catch (Exception e) { errors.Add($"{name}: {e.Message}"); }
                }
            }
            var cat = AssetDatabase.LoadAssetAtPath<Catalog>(CatalogPath);
            if (cat)
            {
                res.TryGetValue("ThermalDetonator", out var td);
                cat.ThermalDetonator = td;
                EditorUtility.SetDirty(cat);
            }
            AssetDatabase.SaveAssets();
            return res;
        }

        // ================================================================== сцена боя
        static Material SkyMaterial()
        {
            string p = Gen + "/Sky.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(m, p); }
            return m;
        }

        static void BuildBattleScene(Catalog cat, Dictionary<string, GameObject> props, Dictionary<string, Material> mats, List<string> errors)
        {
            EditorUtility.DisplayProgressBar("Командир клонов: карта", "Сцена боя", 0.48f);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = new GameObject("World").transform;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            var sky = SkyMaterial();
            RenderSettings.skybox = sky;
            var atm = new GameObject("Atmosphere").AddComponent<Atmosphere>();
            atm.Sun = sun;
            atm.Sky = sky;
            atm.Apply(1);

            var gen = new MapGenerator(props, mats, cat);
            gen.BuildTerrain(world);
            gen.Vegetation();
            gen.Rocks();
            gen.Structures();
            var map = gen.Map;

            // камера и системы боя
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 62f;
            cam.farClipPlane = 2500f;
            cam.allowHDR = true;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<CommanderCamera>();
            camGo.AddComponent<PostFx>();
            var sys = new GameObject("Battle");
            sys.AddComponent<BattleNet>();
            sys.AddComponent<CommandInput>();
            sys.AddComponent<TacticalView>();
            new GameObject("HUD").AddComponent<BattleHud>();
            try
            {
                var nav = gen.BakeNavMesh();
                var loader = sys.AddComponent<NavMeshLoader>();
                loader.Data = nav;
            }
            catch (Exception e) { errors.Add("NavMesh: " + e.Message); Debug.LogException(e); }
            try { gen.RenderMapImage(); }
            catch (Exception e) { errors.Add("Карта сверху: " + e.Message); }
            atm.Apply(1);
            EditorSceneManager.SaveScene(scene, BattleScene);
        }

        // ================================================================== сцена меню
        static void BuildMenuScene(Catalog cat, Dictionary<string, GameObject> props, Dictionary<string, Material> mats)
        {
            EditorUtility.DisplayProgressBar("Командир клонов", "Сцена меню", 0.97f);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            var sky = SkyMaterial();
            var atm = new GameObject("Atmosphere").AddComponent<Atmosphere>();
            atm.Sun = sun;
            atm.Sky = sky;
            atm.Override = 1;
            atm.Apply(1);
            RenderSettings.fogDensity = 0.006f;
            // земля
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(40, 1, 40);
            var gm = new Material(Shader.Find("Standard"));
            gm.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(WorkshopImport.EnvDir + "Textures/ground_dirt.png");
            gm.mainTextureScale = new Vector2(60, 60);
            var gn = AssetDatabase.LoadAssetAtPath<Texture2D>(WorkshopImport.EnvDir + "Textures/ground_dirt_n.png");
            if (gn) { gm.SetTexture("_BumpMap", gn); gm.EnableKeyword("_NORMALMAP"); }
            gm.SetFloat("_Glossiness", 0.05f);
            AssetDatabase.DeleteAsset(Gen + "/MenuGround.mat");
            AssetDatabase.CreateAsset(gm, Gen + "/MenuGround.mat");
            ground.GetComponent<Renderer>().sharedMaterial = gm;
            GameObject P(string n, Vector3 pos, float yaw, float scale = 1f)
            {
                if (!props.TryGetValue(n, out var pf) || !pf) return null;
                var g = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                g.transform.position = pos;
                g.transform.rotation = Quaternion.Euler(0, yaw, 0);
                g.transform.localScale = Vector3.one * scale;
                return g;
            }
            P("LandingPad", new Vector3(-4, 0.02f, 18), 0);
            P("ATTE", new Vector3(-26, 0, 30), 60f);
            P("RepBunker", new Vector3(22, 0, 26), -20f);
            P("RepBunker", new Vector3(34, 0, 10), -60f);
            P("RepBarricade", new Vector3(8, 0, -6), 10f);
            for (int i = 0; i < 8; i++) P("Crate", new Vector3(14 + (i % 4) * 1.5f, 0, 12 + (i / 4) * 1.5f), i * 9f);
            var rnd = new System.Random(4);
            for (int i = 0; i < 40; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, d = 60f + (float)rnd.NextDouble() * 120f;
                P(rnd.NextDouble() < 0.6 ? "Pine" + rnd.Next(3) : "Rock" + rnd.Next(4), new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d), (float)rnd.NextDouble() * 360f, 0.8f + (float)rnd.NextDouble());
            }
            // строй клонов
            var formation = new GameObject("Formation").transform;
            var rep = cat.Units.Where(u => u.Team == Team.Republic && u.Prefab).ToList();
            var riflemen = rep.Where(u => u.Role == Role.Rifleman).ToList();
            if (riflemen.Count == 0) riflemen = rep;
            if (rep.Count > 0)
            {
                for (int row = 0; row < 3; row++)
                for (int col = 0; col < 7; col++)
                {
                    var u = riflemen[(row * 7 + col) % riflemen.Count];
                    var g = (GameObject)PrefabUtility.InstantiatePrefab(u.Prefab, formation);
                    g.transform.position = new Vector3(col * 1.7f, 0, row * -1.9f + 6f);
                    g.transform.rotation = Quaternion.Euler(0, 180f, 0);
                    var p = g.AddComponent<MenuPoser>();
                    p.Clip = row == 0 ? "Attention" : row == 1 ? "AtEase" : "PortArms";
                    StripRuntime(g);
                }
                var cmd = rep.FirstOrDefault(u => u.Role == Role.Commander) ?? rep[0];
                var c = (GameObject)PrefabUtility.InstantiatePrefab(cmd.Prefab, formation);
                c.transform.position = new Vector3(5.1f, 0, 10.5f);
                c.transform.rotation = Quaternion.Euler(0, 0, 0);
                c.AddComponent<MenuPoser>().Clip = "AtEase";
                StripRuntime(c);
            }
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.farClipPlane = 1500f;
            cam.allowHDR = true;
            camGo.AddComponent<AudioListener>();
            var fx = camGo.AddComponent<PostFx>();
            fx.Vignette = 1.4f;
            var mc = camGo.AddComponent<MenuCamera>();
            var target = new GameObject("CameraTarget").transform;
            target.position = new Vector3(5f, 0, 4f);
            mc.Target = target;
            mc.Radius = 15f;
            mc.Height = 1.8f;
            mc.StartAngle = 200f;
            camGo.transform.position = target.position + new Vector3(-6, 1.8f, -14);
            camGo.transform.LookAt(target.position + Vector3.up * 1.4f);
            new GameObject("Menu").AddComponent<MainMenu>();
            EditorSceneManager.SaveScene(scene, MenuScene);
        }

        /// <summary>В меню бойцы — только «манекены»: без ИИ, навигации и рэгдолла.</summary>
        static void StripRuntime(GameObject g)
        {
            foreach (var c in g.GetComponents<Component>())
                if (c is Soldier || c is Ragdoll || c is UnityEngine.AI.NavMeshAgent) Object.DestroyImmediate(c);
        }

        static void SetBuildScenes()
        {
            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MenuScene, true), new EditorBuildSettingsScene(BattleScene, true) };
            list.AddRange(EditorBuildSettings.scenes.Where(s => s.path != MenuScene && s.path != BattleScene));
            EditorBuildSettings.scenes = list.ToArray();
            PlayerSettings.productName = "Star Wars: Командир клонов";
            PlayerSettings.companyName = "GameUltra2000";
            PlayerSettings.runInBackground = true;
            if (string.IsNullOrEmpty(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "0.1") PlayerSettings.bundleVersion = "1.0.0";
        }
    }
}
