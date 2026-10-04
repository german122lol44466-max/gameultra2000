using System.Collections.Generic;
using UnityEngine;

namespace SW
{
    /// <summary>
    /// Персонаж на тестовой карте: проигрывает клипы по очереди (или выбранный вручную),
    /// подсвечивает клинки светового меча, стреляет болтами из бластера, пускает молнии Силы.
    /// </summary>
    public class ShowcaseCharacter : MonoBehaviour
    {
        public string Title;
        public string Style;          // rifle / vader / maul / sidious / dooku
        public string[] Clips = new string[0];
        public bool[] Loops = new bool[0];
        public float[] Lengths = new float[0];
        public bool AutoCycle = true;
        public float LoopHold = 4f;   // сколько секунд показывать зацикленный клип
        public Color BladeColor = new Color(1f, 0.08f, 0.05f);

        Animator anim;
        int current;
        float timer;
        readonly List<(Transform bone, Light light)> blades = new List<(Transform, Light)>();
        Transform weapon, handL;
        LineRenderer lightning;
        bool fired;

        public string CurrentClip => Clips.Length > 0 ? Clips[current] : "";
        public int CurrentIndex => current;
        public float ClipTime => timer;

        void Awake()
        {
            anim = GetComponentInChildren<Animator>();
            if (anim != null) anim.applyRootMotion = false;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Blade.R" || t.name == "Blade.R2")
                {
                    var go = new GameObject("BladeLight");
                    go.transform.SetParent(t, false);
                    go.transform.localPosition = new Vector3(0, 0.5f, 0);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = BladeColor;
                    l.range = 3.5f;
                    l.intensity = 2.2f;
                    l.shadows = LightShadows.None;
                    blades.Add((t, l));
                }
                else if (t.name == "Weapon.R") weapon = t;
                else if (t.name == "Hand.L") handL = t;
            }
        }

        void Start()
        {
            if (Clips.Length > 0) Play(System.Array.IndexOf(Clips, "Idle") >= 0 ? System.Array.IndexOf(Clips, "Idle") : 0);
        }

        public void Play(int i)
        {
            if (Clips.Length == 0 || anim == null) return;
            current = (i % Clips.Length + Clips.Length) % Clips.Length;
            anim.CrossFadeInFixedTime(Clips[current], 0.12f, 0, 0f);
            timer = 0f;
            fired = false;
        }

        public void Play(string clip)
        {
            int i = System.Array.IndexOf(Clips, clip);
            if (i >= 0) Play(i);
        }

        public void Next() => Play(current + 1);
        public void Prev() => Play(current - 1);

        void Update()
        {
            if (Clips.Length == 0) return;
            timer += Time.deltaTime;
            float len = current < Lengths.Length ? Lengths[current] : 1f;
            bool loop = current < Loops.Length && Loops[current];
            if (AutoCycle && timer > (loop ? Mathf.Max(LoopHold, len) : len + 0.7f)) Next();

            // свет клинка следует за его длиной (клинок гаснет масштабом кости)
            foreach (var (bone, light) in blades)
            {
                float s = bone.localScale.y;
                light.enabled = s > 0.05f;
                light.intensity = 2.2f * Mathf.Clamp01(s) * (0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 9f, 0.3f));
            }

            string clip = Clips[current];
            if (clip == "Fire" && !fired && weapon != null)
            {
                fired = true;
                SpawnBolt();
            }
            UpdateLightning(clip == "ForceLightning" && timer > 0.4f && timer < 2.1f);
        }

        void SpawnBolt()
        {
            // ствол оружия направлен вдоль локальной оси Y кости Weapon.R
            Vector3 dir = weapon.up;
            Vector3 pos = weapon.position + dir * 0.45f * transform.lossyScale.y;
            var bolt = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(bolt.GetComponent<Collider>());
            bolt.name = "BlasterBolt";
            bolt.transform.position = pos;
            bolt.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            bolt.transform.localScale = new Vector3(0.035f, 0.22f, 0.035f);
            var mat = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.15f, 0.1f) };
            bolt.GetComponent<Renderer>().sharedMaterial = mat;
            var l = bolt.AddComponent<Light>();
            l.color = new Color(1f, 0.2f, 0.1f);
            l.range = 2.5f;
            l.intensity = 3f;
            bolt.AddComponent<Bolt>().Velocity = dir * 45f;
        }

        void UpdateLightning(bool on)
        {
            if (!on)
            {
                if (lightning != null) lightning.enabled = false;
                return;
            }
            if (lightning == null)
            {
                var go = new GameObject("ForceLightning");
                go.transform.SetParent(transform, false);
                lightning = go.AddComponent<LineRenderer>();
                lightning.material = new Material(Shader.Find("Sprites/Default"));
                lightning.startColor = new Color(0.75f, 0.85f, 1f);
                lightning.endColor = new Color(0.4f, 0.55f, 1f, 0.6f);
                lightning.widthMultiplier = 0.03f;
                lightning.positionCount = 24;
                var l = go.AddComponent<Light>();
                l.color = new Color(0.55f, 0.7f, 1f);
                l.range = 6f;
                l.intensity = 4f;
            }
            lightning.enabled = true;
            Vector3 a = handL != null ? handL.position : transform.position + Vector3.up * 1.3f;
            Vector3 b = a + transform.forward * 4.5f;
            int n = lightning.positionCount;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                Vector3 p = Vector3.Lerp(a, b, t);
                if (i > 0 && i < n - 1) p += Random.insideUnitSphere * 0.22f * Mathf.Sin(t * Mathf.PI);
                lightning.SetPosition(i, p);
            }
            lightning.transform.GetComponent<Light>().transform.position = Vector3.Lerp(a, b, 0.4f);
        }
    }

    /// <summary>Летящий болт бластера.</summary>
    public class Bolt : MonoBehaviour
    {
        public Vector3 Velocity;
        float life = 1.5f;

        void Update()
        {
            transform.position += Velocity * Time.deltaTime;
            life -= Time.deltaTime;
            if (life <= 0) Destroy(gameObject);
        }
    }
}
