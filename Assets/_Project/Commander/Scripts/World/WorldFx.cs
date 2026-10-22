using SW.Commander.Net;
using SW.Commander.UI;
using UnityEngine;
using UnityEngine.AI;

namespace SW.Commander
{
    /// <summary>Постобработка камеры (встроенный рендер): bloom, тонмаппинг ACES, цветокоррекция, виньетка.</summary>
    [RequireComponent(typeof(Camera))]
    public class PostFx : MonoBehaviour
    {
        public float Threshold = 1.0f, Intensity = 0.9f, Exposure = 1.1f, Contrast = 1.08f, Saturation = 1.05f, Vignette = 0.9f;
        public Color Tint = new Color(1.02f, 1f, 0.97f), Lift = new Color(0.005f, 0.006f, 0.012f);
        Material mat;
        const int Levels = 6;
        readonly RenderTexture[] chain = new RenderTexture[Levels];

        void OnEnable()
        {
            var cam = GetComponent<Camera>();
            cam.allowHDR = true;
            var sh = Shader.Find("Hidden/SW/PostFx");
            if (sh && sh.isSupported) mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (!mat) { Graphics.Blit(src, dst); return; }
            bool bloom = Prefs.Bloom;
            mat.SetFloat("_Threshold", Threshold);
            mat.SetFloat("_Knee", Threshold * 0.5f);
            mat.SetFloat("_Intensity", bloom ? Intensity : 0f);
            mat.SetFloat("_Exposure", Exposure);
            mat.SetFloat("_Contrast", Contrast);
            mat.SetFloat("_Saturation", Saturation);
            mat.SetFloat("_Vignette", Vignette);
            mat.SetColor("_Tint", Tint);
            mat.SetColor("_Lift", Lift);
            int w = src.width / 2, h = src.height / 2;
            var fmt = RenderTextureFormat.DefaultHDR;
            RenderTexture last = null;
            int n = 0;
            if (bloom)
            {
                for (int i = 0; i < Levels && w > 8 && h > 8; i++, n++)
                {
                    chain[i] = RenderTexture.GetTemporary(w, h, 0, fmt);
                    Graphics.Blit(i == 0 ? src : chain[i - 1], chain[i], mat, i == 0 ? 0 : 1);
                    w /= 2; h /= 2;
                }
                for (int i = n - 2; i >= 0; i--) Graphics.Blit(chain[i + 1], chain[i], mat, 2);
                last = n > 0 ? chain[0] : null;
            }
            mat.SetTexture("_BloomTex", last ? (Texture)last : Texture2D.blackTexture);
            Graphics.Blit(src, dst, mat, 3);
            for (int i = 0; i < n; i++) RenderTexture.ReleaseTemporary(chain[i]);
        }
    }

    /// <summary>Купол щита над командным центром: виден, пока работают генераторы.</summary>
    public class ShieldDome : MonoBehaviour
    {
        public GameObject Dome;
        Destructible target;

        void Start() => target = GetComponent<Destructible>();

        void Update()
        {
            if (!Dome || !target) return;
            bool on = target.Invulnerable && !target.Dead;
            if (Dome.activeSelf != on)
            {
                Dome.SetActive(on);
                if (!on) Battle.Fx.Flash(Dome.transform.position, new Color(0.4f, 0.7f, 1f), 12f, 80f, 0.4f);
            }
            if (on) Dome.transform.Rotate(0, Time.deltaTime * 3f, 0);
        }
    }

    /// <summary>Добавляет запечённый в редакторе NavMesh (нужен хосту для ИИ).</summary>
    public class NavMeshLoader : MonoBehaviour
    {
        public NavMeshData Data;
        NavMeshDataInstance inst;

        void OnEnable()
        {
            if (Data) inst = NavMesh.AddNavMeshData(Data);
        }

        void OnDisable() => inst.Remove();
    }

    /// <summary>Время суток боя: солнце, туман, небо и тон постобработки — по настройке хоста.</summary>
    public class Atmosphere : MonoBehaviour
    {
        public Light Sun;
        public Material Sky;
        public int Override = -1;

        void Start()
        {
            int t = Override >= 0 ? Override : NetSession.Instance ? NetSession.Instance.Settings.TimeOfDay : 1;
            Apply(t);
        }

        public void Apply(int t)
        {
            var fx = Camera.main ? Camera.main.GetComponent<PostFx>() : null;
            switch (t)
            {
                case 0: // день
                    Set(new Vector3(52, -38, 0), new Color(1f, 0.96f, 0.88f), 1.35f, new Color(0.62f, 0.7f, 0.78f), 0.0018f, 1.0f, new Color(0.5f, 0.62f, 0.75f), 1.15f);
                    if (fx) { fx.Tint = new Color(1f, 1f, 0.98f); fx.Exposure = 1.05f; }
                    break;
                case 2: // пасмурно
                    Set(new Vector3(60, 20, 0), new Color(0.82f, 0.86f, 0.92f), 0.8f, new Color(0.5f, 0.54f, 0.58f), 0.0035f, 2.2f, new Color(0.45f, 0.47f, 0.5f), 0.9f);
                    if (fx) { fx.Tint = new Color(0.96f, 0.99f, 1.03f); fx.Saturation = 0.85f; }
                    break;
                default: // закат
                    Set(new Vector3(12, -62, 0), new Color(1f, 0.68f, 0.42f), 1.25f, new Color(0.78f, 0.58f, 0.45f), 0.0024f, 1.6f, new Color(0.62f, 0.45f, 0.4f), 1.0f);
                    if (fx) { fx.Tint = new Color(1.05f, 0.98f, 0.92f); fx.Exposure = 1.15f; }
                    break;
            }
        }

        void Set(Vector3 sunEuler, Color sunColor, float sunIntensity, Color fog, float fogDensity, float atmos, Color skyTint, float exposure)
        {
            if (Sun)
            {
                Sun.transform.rotation = Quaternion.Euler(sunEuler);
                Sun.color = sunColor;
                Sun.intensity = sunIntensity;
                Sun.shadows = LightShadows.Soft;
                RenderSettings.sun = Sun;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(skyTint, Color.white, 0.2f) * 0.9f;
            RenderSettings.ambientEquatorColor = fog * 0.75f;
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.2f, 0.17f);
            if (Sky)
            {
                RenderSettings.skybox = Sky;
                if (Sky.HasProperty("_AtmosphereThickness")) Sky.SetFloat("_AtmosphereThickness", atmos);
                if (Sky.HasProperty("_SkyTint")) Sky.SetColor("_SkyTint", skyTint);
                if (Sky.HasProperty("_Exposure")) Sky.SetFloat("_Exposure", exposure);
                if (Sky.HasProperty("_SunSize")) Sky.SetFloat("_SunSize", 0.05f);
            }
            DynamicGI.UpdateEnvironment();
        }
    }

    /// <summary>Поза бойца в меню: проигрывает выбранный клип (смирно, вольно, на грудь) со случайным сдвигом.</summary>
    public class MenuPoser : MonoBehaviour
    {
        public string Clip = "Attention";
        public float Offset = -1f;

        void Start()
        {
            var a = GetComponentInChildren<Animator>();
            if (!a) return;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            a.Play(Clip, 0, Offset >= 0 ? Offset : Random.value);
            a.speed = Random.Range(0.9f, 1.1f);
        }
    }

    /// <summary>Камера меню: медленный проезд вдоль строя.</summary>
    public class MenuCamera : MonoBehaviour
    {
        public Transform Target;
        public float Radius = 14f, Height = 2.2f, Speed = 2.2f, StartAngle = 200f, Sweep = 40f;

        void LateUpdate()
        {
            if (!Target) return;
            float a = (StartAngle + Mathf.Sin(Time.time * Speed * 0.02f) * Sweep) * Mathf.Deg2Rad;
            var p = Target.position + new Vector3(Mathf.Sin(a) * Radius, Height, Mathf.Cos(a) * Radius);
            transform.position = Vector3.Lerp(transform.position, p, 0.05f);
            var look = Target.position + Vector3.up * 1.4f + Vector3.right * 3.5f;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look - transform.position), 0.05f);
        }
    }
}
