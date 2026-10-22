using System.Collections.Generic;
using SW.Battle;
using UnityEngine;

namespace SW.Commander
{
    public enum PostOwner : byte { Neutral = 0, Republic = 1, Empire = 2 }

    /// <summary>
    /// Командный пункт (аванпост, база): захват присутствием бойцов (как в Battlefront), точка высадки подкреплений,
    /// доход снабжения. Сервер считает прогресс; клиенты получают его в Objectives.
    /// </summary>
    public class CommandPost : MonoBehaviour
    {
        public static readonly List<CommandPost> All = new List<CommandPost>();

        public byte Id;
        public string Title = "Аванпост";
        public string Letter = "A";
        public PostOwner Owner = PostOwner.Empire;
        public float Radius = 14f;
        public bool IsBase;                     // база стороны (захват базы Республики — поражение)
        public bool Locked;                     // нельзя захватить (база Империи, пока цел командный центр)
        public int MinSize;                     // с какой протяжённости фронта пункт участвует (0 — всегда)
        public Transform[] Spawns = new Transform[0];
        public Transform[] Patrol = new Transform[0];
        public Transform[] Guards = new Transform[0];       // места часовых
        public Renderer[] Lights = new Renderer[0];          // подсветка цвета владельца
        public Transform Flag;                                // голограмма / флаг

        /// <summary>-1 (Империя) .. +1 (Республика).</summary>
        [System.NonSerialized] public float Progress;
        [System.NonSerialized] public bool Contested;
        [System.NonSerialized] public int RepublicCount, EmpireCount;
        public event System.Action<CommandPost, PostOwner> Captured;
        PostOwner shownOwner = (PostOwner)255;
        static Material mRep, mEmp, mNeu;

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            Progress = Owner == PostOwner.Republic ? 1f : Owner == PostOwner.Empire ? -1f : 0f;
        }

        void OnDisable() => All.Remove(this);

        public static CommandPost Get(int id) => All.Find(p => p.Id == id);

        public Team? OwnerTeam => Owner == PostOwner.Republic ? Team.Republic : Owner == PostOwner.Empire ? Team.Empire : (Team?)null;

        public bool OwnedBy(Team t) => OwnerTeam.HasValue && OwnerTeam.Value == t;

        public Vector3 SpawnPoint(int i)
        {
            if (Spawns != null && Spawns.Length > 0)
            {
                var s = Spawns[Mathf.Abs(i) % Spawns.Length];
                if (s) return s.position + new Vector3(Random.Range(-1.5f, 1.5f), 0, Random.Range(-1.5f, 1.5f));
            }
            float a = i * 2.4f;
            return transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Radius * 0.6f;
        }

        /// <summary>Сервер: шаг захвата.</summary>
        public void Tick(float dt)
        {
            RepublicCount = EmpireCount = 0;
            float r2 = Radius * Radius;
            Vector3 c = transform.position;
            foreach (var u in Unit.All)
            {
                if (!u || u.Dead || u is Turret) continue;
                var d = u.transform.position - c;
                if (Mathf.Abs(d.y) > 8f) continue;
                d.y = 0;
                if (d.sqrMagnitude > r2) continue;
                if (u.Team == Team.Republic) RepublicCount++; else EmpireCount++;
            }
            Contested = RepublicCount > 0 && EmpireCount > 0;
            if (Contested || (RepublicCount == 0 && EmpireCount == 0)) return;
            bool rep = RepublicCount > 0;
            if (Locked && !rep && Owner == PostOwner.Empire) return;
            if (Locked && rep) return;
            int n = rep ? RepublicCount : EmpireCount;
            float rate = Mathf.Min(0.09f, 0.03f + 0.012f * n);
            float before = Progress;
            Progress = Mathf.Clamp(Progress + (rep ? rate : -rate) * dt, -1f, 1f);
            // нейтрализация при пересечении нуля, захват на краях
            PostOwner now = Owner;
            if (Progress >= 1f) now = PostOwner.Republic;
            else if (Progress <= -1f) now = PostOwner.Empire;
            else if ((before > 0f && Progress <= 0f) || (before < 0f && Progress >= 0f)) now = PostOwner.Neutral;
            if (now != Owner)
            {
                Owner = now;
                Captured?.Invoke(this, now);
            }
        }

        void Update()
        {
            if (shownOwner == Owner) return;
            shownOwner = Owner;
            mRep ??= Glow(TeamColors.Republic);
            mEmp ??= Glow(TeamColors.Empire);
            mNeu ??= Glow(new Color(0.8f, 0.8f, 0.8f));
            var m = Owner == PostOwner.Republic ? mRep : Owner == PostOwner.Empire ? mEmp : mNeu;
            foreach (var r in Lights) if (r) r.sharedMaterial = m;
            foreach (var l in GetComponentsInChildren<Light>())
                l.color = Owner == PostOwner.Republic ? TeamColors.Republic : Owner == PostOwner.Empire ? TeamColors.Empire : Color.white;
        }

        static Material Glow(Color c)
        {
            var m = new Material(Shader.Find("Standard"));
            m.color = c * 0.3f;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 3f);
            return m;
        }

        public static Color ColorOf(PostOwner o) => o == PostOwner.Republic ? TeamColors.Republic : o == PostOwner.Empire ? TeamColors.Empire : new Color(0.85f, 0.85f, 0.85f);
    }
}
