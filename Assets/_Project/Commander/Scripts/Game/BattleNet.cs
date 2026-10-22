using System;
using System.Collections.Generic;
using System.Linq;
using SW.Battle;
using SW.Commander.Net;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>Состояние игрока в бою (сервер — полное, клиент — своё).</summary>
    public class PlayerState
    {
        public byte Id;
        public Team Team;
        public float Requisition;
        public ushort CommanderId;
        public float RespawnAt;
        public int Kills, Losses, Captures;
        public int UnitCount;
    }

    /// <summary>
    /// Бой по сети: общая часть (реестр бойцов и отрядов, сообщения, интерфейс к UI).
    /// Сервер — в BattleNet.Server.cs, клиент — в BattleNet.Client.cs.
    /// </summary>
    public partial class BattleNet : MonoBehaviour
    {
        public static BattleNet I { get; private set; }

        public NetSession Session { get; private set; }
        public bool IsServer => Session && Session.IsServer;
        public MatchSettings Settings => Session ? Session.Settings : new MatchSettings();
        public Team LocalTeam => Session?.Local?.Team ?? Team.Republic;
        public byte LocalId => Session ? Session.LocalId : (byte)0;

        public readonly Dictionary<ushort, Squad> Squads = new Dictionary<ushort, Squad>();
        public readonly Dictionary<byte, PlayerState> Players = new Dictionary<byte, PlayerState>();
        public PlayerState Me => Players.TryGetValue(LocalId, out var p) ? p : null;
        public Soldier LocalCommander { get; private set; }
        public int EmpireTickets;
        public Team? Winner { get; private set; }
        public float MatchStart { get; private set; }

        public event Action<string, Color> NoticeShown;
        public event Action<Team> MatchEnded;
        public event Action SquadsChanged;
        public event Action<Soldier> LocalCommanderSpawned;
        public event Action LocalCommanderDied;

        readonly NetWriter w = new NetWriter(2048);

        void Awake()
        {
            I = this;
        }

        void Start()
        {
            Session = NetSession.Instance;
            if (!Session || Session.Phase == SessionPhase.Idle)
            {
                // сцена открыта напрямую — одиночная игра
                Session = NetSession.Ensure();
                RegisterHandlers();
                ApplyDifficulty();
                Session.MatchBegan += OnMatchBegan;
                Session.PlayerJoinedMatch += OnPlayerJoined;
                Session.PlayerLeft += OnPlayerLeft;
                Session.StartInPlace(new MatchSettings());
                return;
            }
            RegisterHandlers();
            ApplyDifficulty();
            Session.MatchBegan += OnMatchBegan;
            Session.PlayerJoinedMatch += OnPlayerJoined;
            Session.PlayerLeft += OnPlayerLeft;
            Session.Disconnected += OnDisconnected;
            if (Session.IsServer && !NavMeshReady()) StartCoroutine(BuildNavMeshThenLoad());
            else Session.NotifyLoaded();
        }

        public static bool NavMeshReady()
        {
            Vector3 p = MapInfo.I && MapInfo.I.RepublicBase ? MapInfo.I.RepublicBase.transform.position : Vector3.zero;
            return UnityEngine.AI.NavMesh.SamplePosition(p, out _, 30f, UnityEngine.AI.NavMesh.AllAreas);
        }

        public static string NavStatus = "";

        /// <summary>Запасной вариант: NavMesh не запёкся в редакторе — строим его при загрузке боя (только хост).</summary>
        System.Collections.IEnumerator BuildNavMeshThenLoad()
        {
            NavStatus = "Строим навигацию карты (один раз, ~20–60 с)...";
            Debug.LogWarning("[Командир] запечённого NavMesh нет — строим во время загрузки");
            var size = MapInfo.I ? MapInfo.I.Size : new Vector2(1500, 1500);
            var bounds = new Bounds(new Vector3(0, 80, 0), new Vector3(size.x, 200, size.y));
            var sources = new List<UnityEngine.AI.NavMeshBuildSource>();
            UnityEngine.AI.NavMeshBuilder.CollectSources(bounds, ~0, UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<UnityEngine.AI.NavMeshBuildMarkup>(), sources);
            var st = UnityEngine.AI.NavMesh.GetSettingsByID(0);
            st.agentRadius = 0.4f;
            st.agentClimb = 0.6f;
            st.agentSlope = 40f;
            st.overrideVoxelSize = true;
            st.voxelSize = 0.3f;
            var data = new UnityEngine.AI.NavMeshData(0);
            UnityEngine.AI.NavMesh.AddNavMeshData(data);
            var op = UnityEngine.AI.NavMeshBuilder.UpdateNavMeshDataAsync(data, st, sources, bounds);
            while (!op.isDone)
            {
                NavStatus = $"Строим навигацию карты: {Mathf.RoundToInt(op.progress * 100)}%";
                yield return null;
            }
            NavStatus = NavMeshReady() ? "" : "NavMesh не построился — бойцы будут ходить напрямую";
            Session.NotifyLoaded();
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            // статические события — иначе в следующем бою сработают обработчики уничтоженного объекта
            Soldier.Killed -= OnSoldierKilled;
            Bolts.Fired -= OnBoltFired;
            Bolts.Exploded -= OnExploded;
            Destructible.Destroyed -= OnStructureDestroyed;
            foreach (var cp in CommandPost.All) cp.Captured -= OnPostCaptured;
            if (!Session) return;
            Session.MatchBegan -= OnMatchBegan;
            Session.PlayerJoinedMatch -= OnPlayerJoined;
            Session.PlayerLeft -= OnPlayerLeft;
            Session.Disconnected -= OnDisconnected;
            Session.ClearGameHandlers();
            Soldier.ById.Clear();
            Soldier.Living.Clear();
        }

        void ApplyDifficulty()
        {
            int d = Settings.Difficulty;
            SoldierBrain.EmpireAccuracy = new[] { 0.6f, 0.85f, 1.05f, 1.3f }[Mathf.Clamp(d, 0, 3)];
            SoldierBrain.RepublicAccuracy = new[] { 1.3f, 1.15f, 1.05f, 1f }[Mathf.Clamp(d, 0, 3)];
        }

        void OnDisconnected(string reason) { }

        void RegisterHandlers()
        {
            // сервер
            Session.OnServer(Msg.AvatarInput, OnAvatarInput);
            Session.OnServer(Msg.Order, OnOrder);
            Session.OnServer(Msg.SquadConfig, OnSquadConfig);
            Session.OnServer(Msg.Recruit, OnRecruit);
            Session.OnServer(Msg.SquadEdit, OnSquadEdit);
            Session.OnServer(Msg.CommanderFire, OnCommanderFire);
            Session.OnServer(Msg.CommanderGrenade, OnCommanderGrenade);
            Session.OnServer(Msg.CommanderRespawn, OnCommanderRespawn);
            // клиент
            Session.OnClient(Msg.Snapshot, ClSnapshot);
            Session.OnClient(Msg.UnitSpawn, ClUnitSpawn);
            Session.OnClient(Msg.UnitDespawn, ClUnitDespawn);
            Session.OnClient(Msg.UnitDeath, ClUnitDeath);
            Session.OnClient(Msg.Shots, ClShots);
            Session.OnClient(Msg.Explosion, ClExplosion);
            Session.OnClient(Msg.SquadState, ClSquadState);
            Session.OnClient(Msg.Objectives, ClObjectives);
            Session.OnClient(Msg.StructureState, ClStructures);
            Session.OnClient(Msg.AvatarStates, ClAvatarStates);
            Session.OnClient(Msg.PlayerEconomy, ClEconomy);
            Session.OnClient(Msg.Voice, ClVoice);
            Session.OnClient(Msg.MatchEnd, ClMatchEnd);
            Session.OnClient(Msg.Notice, ClNotice);
            Session.OnClient(Msg.CommanderDamage, ClCommanderDamage);
            Session.OnClient(Msg.CommanderGrenade, ClGrenade);
        }

        // ================================================================== общий спавн
        Soldier Instantiate(int typeIndex, Team team, Vector3 pos, float yaw)
        {
            var type = Catalog.I.Get(typeIndex);
            if (type == null || !type.Prefab) { Debug.LogWarning("Нет типа юнита " + typeIndex); return null; }
            var go = Instantiate(type.Prefab, pos, Quaternion.Euler(0, yaw, 0));
            go.name = type.Id;
            var s = go.GetComponent<Soldier>();
            if (!s) s = go.AddComponent<Soldier>();
            return s;
        }

        // ================================================================== команды игрока (UI -> сервер)
        public void SendOrder(IList<ushort> squads, OrderType order, Vector3 pos, float yaw = float.NaN, int post = -1)
        {
            w.Reset(Msg.Order);
            w.Byte((byte)squads.Count);
            foreach (var id in squads) w.UShort(id);
            w.Byte((byte)order);
            w.Pos(pos);
            w.Bool(!float.IsNaN(yaw));
            w.Angle16(float.IsNaN(yaw) ? 0f : yaw);
            w.Short((short)post);
            Session.SendToServer(w, true);
            BattleVoiceLocal(order);
        }

        public void SendSquadConfig(Squad sq)
        {
            w.Reset(Msg.SquadConfig);
            w.UShort(sq.Id); w.Byte((byte)sq.Formation); w.Float(sq.Spacing); w.Byte((byte)sq.Stance); w.Byte((byte)sq.Gait); w.Byte((byte)sq.Roe);
            w.String(sq.Name); w.Byte(sq.Color);
            Session.SendToServer(w, true);
        }

        /// <summary>Набор бойцов: squad = 0 — новый отряд.</summary>
        public void SendRecruit(int typeIndex, int count, ushort squad, int post)
        {
            w.Reset(Msg.Recruit);
            w.Byte((byte)typeIndex); w.Byte((byte)count); w.UShort(squad); w.Short((short)post);
            Session.SendToServer(w, true);
        }

        public enum EditOp : byte { CreateSquad, MoveSoldiers, Dismiss, DeleteSquad }

        public void SendSquadEdit(EditOp op, ushort squad, IList<ushort> soldiers = null, string name = null)
        {
            w.Reset(Msg.SquadEdit);
            w.Byte((byte)op); w.UShort(squad); w.String(name ?? "");
            int n = soldiers?.Count ?? 0;
            w.Byte((byte)n);
            for (int i = 0; i < n; i++) w.UShort(soldiers[i]);
            Session.SendToServer(w, true);
        }

        public void SendRespawn(int post)
        {
            w.Reset(Msg.CommanderRespawn).Short((short)post);
            Session.SendToServer(w, true);
        }

        /// <summary>Свои отряды (для UI).</summary>
        public IEnumerable<Squad> MySquads => Squads.Values.Where(s => s.Owner == LocalId).OrderBy(s => s.Id);

        public static int CountOwned(byte owner) => Soldier.Living.Count(s => s.Owner == owner && !s.IsCommander);

        void BattleVoiceLocal(OrderType o)
        {
            if (!LocalCommander) return;
            Audio.Voice(LocalTeam, o == OrderType.Attack ? "combat" : "alert", LocalCommander.transform.position, 0.6f);
        }

        // ================================================================== уведомления
        public void ShowNotice(string text, Color c) => NoticeShown?.Invoke(text, c);

        /// <summary>Реплика бойца: проигрывается у всех (сервер рассылает).</summary>
        public static void Voice(Soldier s, string category)
        {
            if (!I || !s) return;
            byte cat = (byte)Array.IndexOf(VoiceCategories, category);
            if (cat == 255) return;
            if (I.IsServer)
            {
                Audio.Voice(s.Team, category, s.Eye);
                I.w.Reset(Msg.Voice).UShort(s.NetId).Byte(cat);
                I.Session.SendToAll(I.w, false);
            }
        }

        public static readonly string[] VoiceCategories = { "alert", "combat", "danger", "death", "friendlyfire", "grenadespotted", "killedenemy", "mandown", "pain", "suppressing" };
    }
}
