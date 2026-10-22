using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SW.Battle;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace SW.Commander.EditorTools
{
    /// <summary>
    /// Импорт моделей из аддонов (Art/Workshop, собраны Tools/SourceImport) и окружения (Commander/Art/Environment):
    /// настройки FBX и текстур, материалы Standard по JSON, контроллеры анимаций, префабы бойцов, каталог юнитов.
    /// Если моделей из аддонов нет — каталог собирается из моделей v2 (Art/CharactersV2).
    /// </summary>
    public class WorkshopImport : AssetPostprocessor
    {
        public const string WorkshopDir = "Assets/_Project/Art/Workshop/";
        public const string EnvDir = "Assets/_Project/Commander/Art/Environment/";
        public const string GenDir = "Assets/_Project/Commander/Generated";

        static bool IsUnit(string p) => p.StartsWith(WorkshopDir) && !p.StartsWith(WorkshopDir + "Props/") && p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
        static bool IsStatic(string p) => (p.StartsWith(WorkshopDir + "Props/") || p.StartsWith(EnvDir)) && p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            var mi = (ModelImporter)assetImporter;
            if (IsUnit(assetPath))
            {
                mi.animationType = ModelImporterAnimationType.Generic;
                mi.importAnimation = true;
                mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                mi.importBlendShapes = false;
                mi.importCameras = false;
                mi.importLights = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            }
            else if (IsStatic(assetPath))
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.importCameras = false;
                mi.importLights = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                mi.isReadable = assetPath.StartsWith(EnvDir);       // нужно для NavMesh/коллайдеров
            }
        }

        void OnPreprocessAnimation()
        {
            if (!IsUnit(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var loops = LoopsFor(assetPath);
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                string n = c.name;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                c.name = n;
                c.loopTime = loops.Contains(n);
                c.loopPose = false;
            }
            mi.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(WorkshopDir) && !assetPath.StartsWith(EnvDir)) return;
            var ti = (TextureImporter)assetImporter;
            string f = Path.GetFileNameWithoutExtension(assetPath);
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.mipmapEnabled = !f.EndsWith("_portrait");
            if (f.EndsWith("_normal") || f.EndsWith("_n")) ti.textureType = TextureImporterType.NormalMap;
            else if (f.EndsWith("_mask")) ti.sRGBTexture = false;
            if (f.EndsWith("_portrait")) { ti.textureType = TextureImporterType.Default; ti.alphaIsTransparency = true; ti.npotScale = TextureImporterNPOTScale.None; }
            if (f == "grass_blades") ti.alphaIsTransparency = true;
            if (assetPath.StartsWith(EnvDir)) ti.wrapMode = TextureWrapMode.Repeat;
        }

        static HashSet<string> LoopsFor(string fbx)
        {
            var json = Path.ChangeExtension(fbx, ".json");
            var set = new HashSet<string> { "Idle", "Walk", "Run", "Aim", "WalkAim", "WalkBack", "StrafeL", "StrafeR", "Crouch", "CrouchAim", "FireAuto",
                                            "Ride", "Drive", "Attention", "AtEase", "PortArms", "March", "Patrol", "CrouchWalk", "CrouchIdle", "Prone", "ProneAim" };
            if (!File.Exists(json)) return set;
            var u = JsonUtility.FromJson<UnitJson>(File.ReadAllText(json));
            if (u?.clips == null) return set;
            set.Clear();
            foreach (var c in u.clips) if (c.loop) set.Add(c.name);
            return set;
        }

        // ================================================================== JSON
        [Serializable] public class ClipJson { public string name; public int frames; public bool loop; }
        [Serializable] public class MatJson { public string name, albedo, normal, mask, emission, alpha; public float smooth, metallic; public float[] color; }
        [Serializable] public class WeaponJson { public string id, title, bolt, sound; public float damage, rpm, range, spread; public int mag; public float[] muzzle; }
        [Serializable] public class StatsJson { public float health, speed; public int cost; }
        [Serializable] public class UnitJson { public string name, title, team, role; public float height; public WeaponJson weapon; public StatsJson stats; public ClipJson[] clips; public MatJson[] materials; }
        [Serializable] public class UnitsJson { public UnitJson[] units; }
        [Serializable] public class PropJson { public string name; public float[] size; public MatJson[] materials; }

        // ================================================================== материалы
        static readonly Regex Suffix = new Regex(@"\.\d+$");

        public static Material MakeMaterial(string dir, MatJson m, string matDir)
        {
            Directory.CreateDirectory(matDir);
            string path = $"{matDir}/{m.name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = Shader.Find("Standard");
            Texture2D T(string f) => string.IsNullOrEmpty(f) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{f}");
            var alb = T(m.albedo);
            mat.mainTexture = alb;
            var c = m.color != null && m.color.Length >= 3 ? new Color(m.color[0], m.color[1], m.color[2]) : Color.white;
            mat.color = alb ? c : new Color(0.6f, 0.6f, 0.62f) * c;
            var nrm = T(m.normal);
            if (nrm) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); mat.SetFloat("_BumpScale", 1f); }
            var mask = T(m.mask);
            if (mask)
            {
                mat.SetTexture("_MetallicGlossMap", mask);
                mat.EnableKeyword("_METALLICGLOSSMAP");
                mat.SetFloat("_GlossMapScale", 0.85f);
            }
            else
            {
                mat.SetFloat("_Glossiness", Mathf.Clamp(m.smooth, 0.1f, 0.8f));
                mat.SetFloat("_Metallic", m.metallic);
            }
            var em = T(m.emission);
            if (em)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetTexture("_EmissionMap", em);
                mat.SetColor("_EmissionColor", Color.white * 2f);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            SetMode(mat, m.alpha);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static void SetMode(Material mat, string mode)
        {
            if (mode == "cutout")
            {
                mat.SetFloat("_Mode", 1);
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                mat.SetFloat("_Cutoff", 0.5f);
            }
            else if (mode == "blend")
            {
                mat.SetFloat("_Mode", 2);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
        }

        /// <summary>Подменить встроенные материалы FBX на созданные (по имени без суффикса .001).</summary>
        public static void Remap(string fbx, Func<string, Material> resolve)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (mi == null) return;
            var names = new HashSet<string>(AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().Select(m => m.name));
            foreach (var id in mi.GetExternalObjectMap().Keys) if (id.type == typeof(Material)) names.Add(id.name);
            bool changed = false;
            foreach (var n in names)
            {
                var m = resolve(Suffix.Replace(n, ""));
                if (!m) continue;
                var key = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                if (mi.GetExternalObjectMap().TryGetValue(key, out var cur) && cur == m) continue;
                mi.AddRemap(key, m);
                changed = true;
            }
            if (changed) mi.SaveAndReimport();
        }

        // ================================================================== юниты
        public static AnimatorController Controller(string name, string fbx, IEnumerable<string> clipNames)
        {
            Directory.CreateDirectory(GenDir + "/Animators");
            string path = $"{GenDir}/Animators/{name}.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            var all = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(a => !a.name.StartsWith("__preview__"))
                .GroupBy(a => a.name.Substring(a.name.LastIndexOf('|') + 1)).ToDictionary(g => g.Key, g => g.First());
            int i = 0;
            foreach (var c in clipNames)
            {
                if (!all.TryGetValue(c, out var clip)) continue;
                var st = sm.AddState(c, new Vector3(300 + (i / 20) * 260, 50 * (i % 20), 0));
                i++;
                st.motion = clip;
                if (c == "Idle") sm.defaultState = st;
            }
            return ctrl;
        }

        public static GameObject UnitPrefab(string name, string fbx, AnimatorController ctrl, float height)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (!model) throw new Exception("FBX не загрузился: " + fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                go.name = name;
                var an = go.GetComponent<Animator>();
                if (!an) an = go.AddComponent<Animator>();
                an.runtimeAnimatorController = ctrl;
                an.applyRootMotion = false;
                an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    r.updateWhenOffscreen = false;
                    r.skinnedMotionVectors = false;
                    r.quality = SkinQuality.Bone2;
                }
                var ag = go.AddComponent<NavMeshAgent>();
                ag.radius = 0.35f;
                ag.height = Mathf.Max(1.6f, height);
                ag.speed = 3f;
                ag.stoppingDistance = 0.2f;
                ag.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
                go.AddComponent<Soldier>();
                go.AddComponent<Ragdoll>();
                Directory.CreateDirectory(GenDir + "/Units");
                return PrefabUtility.SaveAsPrefabAsset(go, $"{GenDir}/Units/{name}.prefab");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static BoltKind Bolt(string s, string team) => s switch
        {
            "blue" => BoltKind.Blue,
            "red" => BoltKind.Red,
            "blue_heavy" => BoltKind.BlueHeavy,
            "red_heavy" => BoltKind.RedHeavy,
            "rocket" => BoltKind.Rocket,
            _ => team == "Empire" ? BoltKind.Red : BoltKind.Blue,
        };

        static Role RoleOf(string s) => s switch
        {
            "heavy" => Role.Heavy,
            "sniper" => Role.Sniper,
            "rocket" => Role.Rocket,
            "engineer" => Role.Engineer,
            "officer" => Role.Officer,
            "elite" => Role.Elite,
            "commander" => Role.Commander,
            _ => Role.Rifleman,
        };

        /// <summary>Все юниты из Art/Workshop/units.json -> префабы и записи каталога.</summary>
        public static List<UnitType> BuildWorkshopUnits(List<string> errors)
        {
            var list = new List<UnitType>();
            string manifest = WorkshopDir + "units.json";
            if (!File.Exists(manifest)) return list;
            var data = JsonUtility.FromJson<UnitsJson>(File.ReadAllText(manifest));
            int i = 0;
            foreach (var u in data.units)
            {
                EditorUtility.DisplayProgressBar("Командир клонов", "Бойцы: " + u.title, 0.05f + 0.4f * i++ / data.units.Length);
                try
                {
                    string dir = WorkshopDir + u.name;
                    string fbx = $"{dir}/{u.name}.fbx";
                    if (AssetImporter.GetAtPath(fbx) == null) { errors.Add("нет " + fbx); continue; }
                    EnsureAnimated(fbx);
                    var mats = new Dictionary<string, Material>();
                    foreach (var m in u.materials) mats[m.name] = MakeMaterial(dir, m, dir + "/Materials");
                    Remap(fbx, n => mats.TryGetValue(n, out var mm) ? mm : null);
                    var ctrl = Controller(u.name, fbx, u.clips.Select(c => c.name));
                    var prefab = UnitPrefab(u.name, fbx, ctrl, u.height);
                    var w = u.weapon;
                    var t = new UnitType
                    {
                        Id = u.name, Title = u.title, Team = u.team == "Empire" ? Team.Empire : Team.Republic, Role = RoleOf(u.role),
                        Prefab = prefab, Portrait = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{u.name}_portrait.png"),
                        Cost = u.stats?.cost ?? 60, Health = u.stats?.health ?? 100, SpeedMul = u.stats?.speed ?? 1f,
                        Accuracy = u.role == "sniper" ? 1.8f : u.role == "elite" ? 1.3f : u.role == "officer" ? 1.2f : 1f,
                        Recruitable = u.role != "commander",
                        ClipNames = u.clips.Select(c => c.name).ToArray(), ClipLengths = u.clips.Select(c => c.frames / 30f).ToArray(),
                        Weapon = new WeaponDef
                        {
                            Id = w.id, Title = w.title, Damage = w.damage, Rpm = w.rpm, Magazine = Mathf.Max(1, w.mag), Range = w.range, Spread = w.spread,
                            Bolt = Bolt(w.bolt, u.team), Sound = w.sound, Splash = w.bolt == "rocket" ? 5f : 0f,
                            ReloadTime = w.bolt == "rocket" ? 3.2f : u.role == "heavy" ? 3.4f : 2.1f,
                        },
                        Muzzle = w.muzzle != null && w.muzzle.Length == 3 ? new Vector3(w.muzzle[0], w.muzzle[1], w.muzzle[2]) : new Vector3(0, 0.7f, 0.065f),
                    };
                    list.Add(t);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Командир] {u.name}: {e}");
                    errors.Add($"{u.name}: {e.Message}");
                }
            }
            return list;
        }

        public static void EnsureAnimated(string fbx)
        {
            var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (mi == null) return;
            bool bad = mi.animationType != ModelImporterAnimationType.Generic || !mi.importAnimation;
            var clips = mi.clipAnimations;
            if (clips == null || clips.Length == 0 || clips.Any(c => c.name.Contains("|"))) bad = true;
            if (!bad) return;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            var loops = LoopsFor(fbx);
            clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                string n = c.name;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                c.name = n;
                c.loopTime = loops.Contains(n);
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
        }

        // ================================================================== запасной вариант: модели v2
        [Serializable] class V2Mat { public string name; public float[] color; public float smoothness, metallic; public float[] emission; }
        [Serializable] class V2Char { public string name, title, side, role, weapon; public float height; public ClipJson[] clips; public V2Mat[] materials; }
        [Serializable] class V2Manifest { public V2Char[] characters; }

        public static List<UnitType> BuildV2Units(List<string> errors)
        {
            var list = new List<UnitType>();
            const string dir = "Assets/_Project/Art/CharactersV2/";
            if (!File.Exists(dir + "manifest_v2.json")) return list;
            var data = JsonUtility.FromJson<V2Manifest>(File.ReadAllText(dir + "manifest_v2.json"));
            var map = new Dictionary<string, (Role role, string weapon)>
            {
                { "CloneTrooper", (Role.Rifleman, "DC15A") }, { "CloneHeavy", (Role.Heavy, "DLT19") }, { "CloneCommander", (Role.Commander, "DC15A") },
                { "Stormtrooper", (Role.Rifleman, "E11") }, { "HeavyTrooper", (Role.Heavy, "DLT19") }, { "TrooperCommander", (Role.Commander, "E11") },
            };
            foreach (var c in data.characters)
            {
                if (!map.TryGetValue(c.name, out var info)) continue;
                try
                {
                    string fbx = dir + c.name + ".fbx";
                    if (AssetImporter.GetAtPath(fbx) == null) continue;
                    var mats = new Dictionary<string, Material>();
                    string md = GenDir + "/MaterialsV2";
                    Directory.CreateDirectory(md);
                    foreach (var m in c.materials)
                    {
                        string p = $"{md}/{m.name}.mat";
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
                        if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, p); }
                        mat.color = new Color(Mathf.LinearToGammaSpace(m.color[0]), Mathf.LinearToGammaSpace(m.color[1]), Mathf.LinearToGammaSpace(m.color[2]));
                        mat.SetFloat("_Glossiness", m.smoothness);
                        mat.SetFloat("_Metallic", m.metallic);
                        if (m.emission != null && m.emission.Sum() > 0.01f) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", new Color(m.emission[0], m.emission[1], m.emission[2])); }
                        mats[m.name] = mat;
                    }
                    Remap(fbx, n => mats.TryGetValue(n, out var mm) ? mm : null);
                    var ctrl = Controller(c.name, fbx, c.clips.Select(x => x.name));
                    var prefab = UnitPrefab(c.name, fbx, ctrl, c.height);
                    bool emp = c.side == "Empire";
                    list.Add(new UnitType
                    {
                        Id = c.name, Title = c.title, Team = emp ? Team.Empire : Team.Republic, Role = info.role, Prefab = prefab,
                        Cost = info.role == Role.Heavy ? 110 : 60, Health = info.role == Role.Heavy ? 140 : info.role == Role.Commander ? 250 : 100,
                        Recruitable = info.role != Role.Commander,
                        ClipNames = c.clips.Select(x => x.name).ToArray(), ClipLengths = c.clips.Select(x => x.frames / 30f).ToArray(),
                        Weapon = new WeaponDef
                        {
                            Id = info.weapon, Title = info.weapon == "DC15A" ? "DC-15A" : info.weapon == "E11" ? "E-11" : "DLT-19",
                            Damage = info.weapon == "DLT19" ? 17 : 22, Rpm = info.weapon == "DLT19" ? 700 : 360, Magazine = info.weapon == "DLT19" ? 120 : 45,
                            Range = 75, Spread = info.weapon == "DLT19" ? 1.8f : 1.1f, Bolt = emp ? BoltKind.Red : BoltKind.Blue,
                            Sound = info.weapon == "DC15A" ? "dc15a" : info.weapon == "E11" ? "e11" : "dlt19",
                        },
                        Muzzle = new Vector3(0, info.weapon == "E11" ? 0.39f : info.weapon == "DLT19" ? 0.78f : 0.7f, 0.06f),
                    });
                }
                catch (Exception e) { errors.Add($"{c.name}: {e.Message}"); }
            }
            return list;
        }

        // ================================================================== статичные модели
        public static GameObject PropPrefab(string name, string fbx, Func<string, Material> resolve, string kind)
        {
            Remap(fbx, resolve);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (!model) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                go.name = name;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (kind == "tree")
                    {
                        var cc = r.gameObject.AddComponent<CapsuleCollider>();
                        cc.radius = 0.4f;
                        cc.height = 6f;
                        cc.center = new Vector3(0, 3f, 0);
                    }
                    else if (kind == "small")
                    {
                        r.gameObject.AddComponent<BoxCollider>();
                    }
                    else if (mf && mf.sharedMesh)
                    {
                        var mc = r.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
                }
                if (kind == "cover" || kind == "small") go.AddComponent<CoverObject>();
                Directory.CreateDirectory(GenDir + "/Props");
                return PrefabUtility.SaveAsPrefabAsset(go, $"{GenDir}/Props/{name}.prefab");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Звуки из Art/Workshop/Sounds + процедурные Resources/SW_Audio.</summary>
        public static List<AudioClip> Sounds()
        {
            var res = new List<AudioClip>();
            foreach (var dir in new[] { WorkshopDir + "Sounds" })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { dir }))
                {
                    var c = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                    if (c) res.Add(c);
                }
            }
            return res;
        }
    }
}
