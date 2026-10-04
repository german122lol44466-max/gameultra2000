using System.Collections.Generic;
using UnityEngine;

namespace SW.Battle
{
    /// <summary>
    /// Звуки боя: клипы из Resources/SW_Audio (сгенерированы Tools/Audio/gen_sfx.py), пул 3D-источников,
    /// случайный питч и ограничение одинаковых звуков за кадр (чтобы 30 бластеров не превращались в кашу).
    /// </summary>
    public static class Sfx
    {
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        static readonly Dictionary<string, float> lastPlay = new Dictionary<string, float>();
        static readonly Dictionary<string, int> burst = new Dictionary<string, int>();
        static readonly List<AudioSource> pool = new List<AudioSource>();
        static Transform root;
        static int next;
        const int PoolSize = 40;
        public static float Volume = 1f;

        public static AudioClip Get(string name)
        {
            if (!cache.TryGetValue(name, out var c))
            {
                c = Resources.Load<AudioClip>("SW_Audio/" + name);
                if (c == null) Debug.LogWarning("[Sfx] нет звука " + name);
                cache[name] = c;
            }
            return c;
        }

        static AudioSource Source()
        {
            if (root == null)
            {
                pool.Clear();
                root = new GameObject("SfxPool").transform;
                for (int i = 0; i < PoolSize; i++)
                {
                    var g = new GameObject("Sfx");
                    g.transform.SetParent(root, false);
                    var s = g.AddComponent<AudioSource>();
                    Setup3D(s, 3f, 120f);
                    s.playOnAwake = false;
                    pool.Add(s);
                }
            }
            for (int i = 0; i < pool.Count; i++)
            {
                var s = pool[(next + i) % pool.Count];
                if (!s.isPlaying) { next = (next + i + 1) % pool.Count; return s; }
            }
            next = (next + 1) % pool.Count;                 // все заняты — перебиваем самый старый
            return pool[next];
        }

        static void Setup3D(AudioSource s, float minDist, float maxDist)
        {
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = minDist;
            s.maxDistance = maxDist;
            s.dopplerLevel = 0.3f;
        }

        /// <summary>Проиграть звук в точке. minDist — радиус «полной громкости» (взрывы больше, щелчки меньше).</summary>
        public static void Play(string name, Vector3 pos, float volume = 1f, float pitch = 1f, float minDist = 3f, float maxDist = 120f,
                                int maxPerFrame = 3)
        {
            var clip = Get(name);
            if (clip == null) return;
            float now = Time.unscaledTime;
            if (lastPlay.TryGetValue(name, out var lt) && now - lt < 0.03f)
            {
                if (burst[name] >= maxPerFrame) return;
                burst[name]++;
            }
            else burst[name] = 1;
            lastPlay[name] = now;
            var s = Source();
            s.transform.position = pos;
            s.clip = clip;
            s.loop = false;
            s.volume = volume * Volume;
            s.pitch = pitch * Random.Range(0.94f, 1.06f) * Mathf.Max(0.3f, Time.timeScale);
            s.minDistance = minDist;
            s.maxDistance = maxDist;
            s.Play();
        }

        /// <summary>Зацикленный звук, привязанный к объекту (гул меча, двигатель). Остановить — Destroy/Stop источника.</summary>
        public static AudioSource Loop(string name, Transform parent, float volume, float minDist = 2f, float maxDist = 40f)
        {
            var clip = Get(name);
            if (clip == null || parent == null) return null;
            var g = new GameObject("Loop_" + name);
            g.transform.SetParent(parent, false);
            var s = g.AddComponent<AudioSource>();
            Setup3D(s, minDist, maxDist);
            s.clip = clip;
            s.loop = true;
            s.volume = volume * Volume;
            s.pitch = Random.Range(0.96f, 1.04f);
            s.time = Random.value * clip.length;
            s.Play();
            return s;
        }
    }
}
