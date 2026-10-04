using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SW.Battle
{
    [System.Serializable]
    public class UnitEntry
    {
        public string Name;
        public string Title;
        public Team Team;
        public GameObject Prefab;
        public bool Vehicle;
        public GameObject Crew;           // пилот (префаб персонажа), сажается в Seat
        public string CrewClip = "Ride";
    }

    /// <summary>
    /// Полигон: строит NavMesh (люди и техника — разные агенты), расставляет укрытия, спавнит юнитов по кнопкам,
    /// ведёт счёт. Камера: свободная (FlyCamera) или следование за выбранным юнитом.
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        public UnitEntry[] Catalog = new UnitEntry[0];
        public Transform EmpireSpawn, RepublicSpawn;
        public Vector3 ArenaSize = new Vector3(240, 30, 170);
        public FlyCamera Cam;
        [Header("Старт боя сразу при запуске")]
        public bool StartBattle = true;
        public bool AutoReinforce = true;
        public string[] EmpireStart = { "Stormtrooper:5", "HeavyTrooper:1", "TrooperCommander:1", "DarthVader:1", "DarthMaul:1", "ATST:1", "SpeederBike74Z:1" };
        public string[] RepublicStart = { "CloneTrooper:5", "CloneHeavy:1", "CloneCommander:1", "ObiWan:1", "MaceWindu:1", "ATRT:1", "BARCSpeeder:1" };

        public static int HumanAgent, VehicleAgent, BigVehicleAgent;
        readonly Dictionary<Team, int> kills = new Dictionary<Team, int> { { Team.Empire, 0 }, { Team.Republic, 0 } };
        int spawnCount = 5;
        bool autoBattle, slowMo, paused, showUI = true;
        float nextAuto;
        Unit follow;
        Vector2 scroll;
        GUIStyle head, small;

        void Awake()
        {
            BuildNavMesh();
            CoverPoint.Generate(new GameObject("CoverPoints").transform);
            Unit.OnDied += (u, d) => { if (d.Attacker) kills[d.Attacker.Team]++; };
            if (!Cam) Cam = FindFirstObjectByType<FlyCamera>();
        }

        void OnDestroy() { Unit.All.Clear(); }

        void Start()
        {
            autoBattle = AutoReinforce;
            nextAuto = Time.time + 20f;
            if (StartBattle)
            {
                SpawnList(EmpireStart);
                SpawnList(RepublicStart);
            }
        }

        void SpawnList(string[] list)
        {
            foreach (var item in list)
            {
                var parts = item.Split(':');
                int n = parts.Length > 1 && int.TryParse(parts[1], out var k) ? k : 1;
                foreach (var e in Catalog)
                    if (e.Name == parts[0] && e.Prefab) { SpawnMany(e, n); break; }
            }
        }

        // ------------------------------------------------------------------ NavMesh во время игры (без пакетов)
        void BuildNavMesh()
        {
            var bounds = new Bounds(Vector3.zero, ArenaSize);
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            var human = NavMesh.GetSettingsByID(0);
            human.agentRadius = 0.35f;
            human.agentHeight = 1.9f;
            human.agentClimb = 0.4f;
            human.agentSlope = 40f;
            HumanAgent = human.agentTypeID;
            NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(human, sources, bounds, Vector3.zero, Quaternion.identity));
            var veh = NavMesh.CreateSettings();
            veh.agentRadius = 1.2f;
            veh.agentHeight = 3f;
            veh.agentClimb = 0.5f;
            veh.agentSlope = 30f;
            VehicleAgent = veh.agentTypeID;
            NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(veh, sources, bounds, Vector3.zero, Quaternion.identity));
            var big = NavMesh.CreateSettings();
            big.agentRadius = 2.4f;
            big.agentHeight = 8f;
            big.agentClimb = 0.8f;
            big.agentSlope = 25f;
            BigVehicleAgent = big.agentTypeID;
            NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(big, sources, bounds, Vector3.zero, Quaternion.identity));
        }

        // ------------------------------------------------------------------ спавн
        public GameObject Spawn(UnitEntry e, Vector3 near)
        {
            Vector3 p = near + new Vector3(Random.Range(-8f, 8f), 0, Random.Range(-6f, 6f));
            if (NavMesh.SamplePosition(p, out var hit, 10f, NavMesh.AllAreas)) p = hit.position;
            var rot = Quaternion.LookRotation(e.Team == Team.Empire ? Vector3.forward : Vector3.back);
            var go = Instantiate(e.Prefab, p, rot);
            go.name = e.Name;
            var u = go.GetComponent<Unit>();
            if (u) u.Team = e.Team;
            var ag = go.GetComponent<NavMeshAgent>();
            if (ag)
            {
                var va = go.GetComponent<VehicleAI>();
                ag.agentTypeID = va ? (va.Walker && go.GetComponent<Unit>().MaxHealth > 1000 ? BigVehicleAgent : VehicleAgent) : HumanAgent;
                ag.Warp(p);
            }
            var rd = go.GetComponent<Ragdoll>();
            if (rd) rd.Build(u);
            if (e.Vehicle && e.Crew)
            {
                Transform seat = null;
                foreach (var t in go.GetComponentsInChildren<Transform>()) if (t.name == "Seat") seat = t;
                if (seat)
                {
                    var crew = Instantiate(e.Crew, seat.position, seat.rotation, seat);
                    foreach (var c in crew.GetComponents<MonoBehaviour>())
                        if (c is SoldierAI || c is DuelistAI || c is CharacterBody) c.enabled = false;
                    var cag = crew.GetComponent<NavMeshAgent>();
                    if (cag) cag.enabled = false;
                    var cu = crew.GetComponent<Unit>();
                    if (cu) { cu.Team = e.Team; cu.enabled = false; }
                    var anim = crew.GetComponentInChildren<Animator>();
                    if (anim) anim.Play(e.CrewClip, 0, Random.value);
                    crew.transform.localPosition = Vector3.zero;
                    crew.transform.localRotation = Quaternion.Euler(0, 0, 0);
                    var va = go.GetComponent<VehicleAI>();
                    if (va) va.Crew = crew;
                    var crd = crew.GetComponent<Ragdoll>();
                    if (crd && cu) crd.Build(cu);
                    crew.transform.SetParent(seat, true);
                }
            }
            return go;
        }

        void SpawnMany(UnitEntry e, int n)
        {
            var at = e.Team == Team.Empire ? EmpireSpawn : RepublicSpawn;
            for (int i = 0; i < n; i++) Spawn(e, at ? at.position : Vector3.zero);
        }

        void Update()
        {
            if (InputCompat.Down(KeyCode.F1)) showUI = !showUI;
            if (InputCompat.Down(KeyCode.P)) paused = !paused;
            if (InputCompat.Down(KeyCode.T)) slowMo = !slowMo;
            Time.timeScale = paused ? 0f : slowMo ? 0.3f : 1f;
            if (autoBattle && Time.time > nextAuto)
            {
                nextAuto = Time.time + 6f;
                foreach (Team t in new[] { Team.Empire, Team.Republic })
                {
                    if (Unit.CountAlive(t) >= 20) continue;
                    var list = Catalog.FindAll(x => x.Team == t && !x.Vehicle);
                    if (list.Count > 0) SpawnMany(list[Random.Range(0, list.Count)], 3);
                }
            }
            if (follow && Cam)
            {
                if (follow.Dead) follow = null;
                else Cam.Focus(follow.transform.position, follow.IsVehicle ? 14f : 5f, follow.IsVehicle ? 6f : 2f);
            }
            if (InputCompat.Down(KeyCode.Tab))
            {
                var all = Unit.All;
                if (all.Count > 0) follow = all[Random.Range(0, all.Count)];
            }
            if (InputCompat.Down(KeyCode.Escape)) follow = null;
        }

        void Styles()
        {
            if (head != null) return;
            head = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        }

        void Side(Team t, Rect r, Color c)
        {
            GUILayout.BeginArea(r, GUI.skin.box);
            var old = GUI.color;
            GUI.color = c;
            GUILayout.Label(t == Team.Empire ? "ИМПЕРИЯ / СИТХИ" : "РЕСПУБЛИКА / ДЖЕДАИ", head);
            GUI.color = old;
            GUILayout.Label($"живых: {Unit.CountAlive(t)}   убито врагов: {kills[t]}", small);
            foreach (var e in Catalog)
            {
                if (e.Team != t) continue;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(e.Title, GUILayout.Width(r.width - 70))) SpawnMany(e, e.Vehicle ? 1 : spawnCount);
                if (GUILayout.Button("x1", GUILayout.Width(40))) SpawnMany(e, 1);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        void OnGUI()
        {
            Styles();
            if (!showUI) return;
            Side(Team.Empire, new Rect(10, 10, 250, 34 + 26 * (Catalog.Length / 2 + 3)), new Color(1f, 0.45f, 0.4f));
            Side(Team.Republic, new Rect(Screen.width - 260, 10, 250, 34 + 26 * (Catalog.Length / 2 + 3)), new Color(0.55f, 0.75f, 1f));
            GUILayout.BeginArea(new Rect(Screen.width / 2 - 260, 10, 520, 64), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("В отряде:", GUILayout.Width(70));
            foreach (int n in new[] { 1, 3, 5, 10 })
                if (GUILayout.Toggle(spawnCount == n, n.ToString(), GUI.skin.button, GUILayout.Width(40))) spawnCount = n;
            autoBattle = GUILayout.Toggle(autoBattle, " Авто-подкрепления");
            if (GUILayout.Button("Очистить")) ClearAll();
            GUILayout.EndHorizontal();
            GUILayout.Label("F1 — панель, P — пауза, T — замедление, Tab — следить за случайным, Esc — свободная камера", small);
            GUILayout.EndArea();
        }

        void ClearAll()
        {
            foreach (var u in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Destroy(u.gameObject);
            Unit.All.Clear();
            follow = null;
        }
    }

    static class ArrayExt
    {
        public static List<T> FindAll<T>(this T[] a, System.Predicate<T> p)
        {
            var r = new List<T>();
            foreach (var x in a) if (p(x)) r.Add(x);
            return r;
        }
    }
}
