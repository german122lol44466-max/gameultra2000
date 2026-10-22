using System.Collections.Generic;
using SW.Battle;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>
    /// Звук боя: выстрелы оружия (звуки из аддонов, если импортированы, иначе процедурные из SW_Audio),
    /// дальние выстрелы, реплики бойцов (клоны/штурмовики), шаги. Ограничение одинаковых звуков за кадр — в Sfx.
    /// Имена звуков каталога: w_* (выстрел), r_* (перезарядка), vc_* / vs_* (голоса клонов / штурмовиков), step_*.
    /// </summary>
    public static class Audio
    {
        static readonly Dictionary<string, List<AudioClip>> groups = new Dictionary<string, List<AudioClip>>();
        static bool indexed;
        public static float VoiceVolume = 1f;
        static float lastVoice;

        static void Index()
        {
            if (indexed) return;
            indexed = true;
            foreach (var c in Catalog.I.Sounds)
            {
                if (!c) continue;
                string n = c.name;
                // «vc_alert_3» -> группа «vc_alert»; «w_dc15a» -> «w_dc15a»
                int us = n.LastIndexOf('_');
                string key = us > 0 && int.TryParse(n.Substring(us + 1), out _) ? n.Substring(0, us) : n;
                if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<AudioClip>();
                l.Add(c);
            }
        }

        public static AudioClip Pick(string group)
        {
            Index();
            return groups.TryGetValue(group, out var l) && l.Count > 0 ? l[Random.Range(0, l.Count)] : null;
        }

        static Vector3 Listener()
        {
            var cam = Camera.main;
            return cam ? cam.transform.position : Vector3.zero;
        }

        /// <summary>Выстрел: звук оружия + дальний «хлопок» для далёких выстрелов.</summary>
        public static void Shot(string sound, Vector3 pos, BoltKind kind)
        {
            float dist = Vector3.Distance(pos, Listener());
            var clip = Pick("w_" + sound);
            if (dist > 70f)
            {
                var far = Pick("w_distant");
                if (far) { Play(far, pos, 0.5f, 30f, 600f); return; }
            }
            if (clip) Play(clip, pos, 0.75f, kind == BoltKind.Rocket || kind == BoltKind.WalkerRed ? 8f : 4f, kind == BoltKind.Rocket ? 260f : 160f);
            else
            {
                string fallback = kind == BoltKind.WalkerRed || kind == BoltKind.WalkerBlue ? "cannon"
                                : kind == BoltKind.BlueHeavy || kind == BoltKind.RedHeavy || kind == BoltKind.Rocket ? "blaster_heavy" : "blaster";
                Sfx.Play(fallback, pos, 0.8f, 1f, 4f, 150f);
            }
        }

        public static void Reload(string sound, Vector3 pos)
        {
            var clip = Pick("r_" + sound);
            if (clip) Play(clip, pos, 0.7f, 1.5f, 25f);
            else Sfx.Play("reload", pos, 0.7f, 1f, 1.5f, 25f);
        }

        /// <summary>Звук по имени: группа каталога или процедурный из SW_Audio.</summary>
        public static void At(string name, Vector3 pos, float volume = 1f, float maxDist = 80f)
        {
            var clip = Pick(name);
            if (clip) Play(clip, pos, volume, 2f, maxDist);
            else Sfx.Play(name, pos, volume, 1f, 2f, maxDist);
        }

        /// <summary>Реплика бойца: alert, combat, killedenemy, mandown, pain, death, suppressing, grenadespotted...</summary>
        public static bool Voice(Team team, string category, Vector3 pos, float minGap = 1.2f)
        {
            if (Time.time - lastVoice < minGap) return false;
            var clip = Pick((team == Team.Republic ? "vc_" : "vs_") + category);
            if (!clip) return false;
            lastVoice = Time.time;
            Play(clip, pos, 0.9f * VoiceVolume, 3f, 45f);
            return true;
        }

        static readonly List<AudioSource> pool = new List<AudioSource>();
        static Transform poolRoot;
        static int next, frame, perFrame;

        public static void Play(AudioClip clip, Vector3 pos, float volume, float minDist, float maxDist)
        {
            if (!clip) return;
            if (Time.frameCount != frame) { frame = Time.frameCount; perFrame = 0; }
            if (++perFrame > 8) return;                         // не больше 8 новых звуков за кадр
            if (!poolRoot)
            {
                pool.Clear();
                poolRoot = new GameObject("[AudioPool]").transform;
                for (int i = 0; i < 48; i++)
                {
                    var g = new GameObject("snd");
                    g.transform.SetParent(poolRoot, false);
                    var a = g.AddComponent<AudioSource>();
                    a.playOnAwake = false;
                    a.spatialBlend = 1f;
                    a.rolloffMode = AudioRolloffMode.Logarithmic;
                    a.dopplerLevel = 0.2f;
                    pool.Add(a);
                }
            }
            AudioSource s = null;
            for (int i = 0; i < pool.Count; i++)
            {
                var c = pool[(next + i) % pool.Count];
                if (!c.isPlaying) { s = c; next = (next + i + 1) % pool.Count; break; }
            }
            if (s == null) { next = (next + 1) % pool.Count; s = pool[next]; }
            s.transform.position = pos;
            s.clip = clip;
            s.minDistance = minDist;
            s.maxDistance = maxDist;
            s.volume = volume * Sfx.Volume;
            s.pitch = Random.Range(0.95f, 1.05f) * Mathf.Max(0.3f, Time.timeScale);
            s.Play();
        }

        /// <summary>2D-звук интерфейса.</summary>
        public static void Ui(string name, float volume = 0.6f)
        {
            var clip = Pick(name) ?? Sfx.Get(name);
            if (!clip) return;
            var cam = Camera.main;
            var go = new GameObject("ui_snd");
            if (cam) go.transform.position = cam.transform.position;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.spatialBlend = 0f;
            s.volume = volume * Sfx.Volume;
            s.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }
    }
}
