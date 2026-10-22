using System;
using System.Collections.Generic;
using System.Linq;
using Steamworks;
using SW.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SW.Commander.Net
{
    public enum SessionMode { None, Offline, Steam, Lan }
    public enum SessionPhase { Idle, Connecting, Lobby, Loading, InGame }

    public class PlayerInfo
    {
        public byte Id;
        public string Name = "";
        public ulong SteamId;
        public Team Team = Team.Republic;
        public bool Ready, Host, Loaded, InMatch;
        public byte Color;
        public int Ping;
        [NonSerialized] public int Conn = -1;          // соединение на сервере (-1 — локальный игрок хоста)
        [NonSerialized] public float HelloDeadline;
        public bool IsLocal => Id == NetSession.Instance?.LocalId;
    }

    /// <summary>Настройки боя (задаёт хост в лобби).</summary>
    [Serializable]
    public class MatchSettings
    {
        public string LobbyName = "Командир клонов";
        public LobbyPrivacy Privacy = LobbyPrivacy.Friends;
        public int MaxPlayers = 4;
        public int Difficulty = 1;            // 0 новобранец, 1 солдат, 2 ветеран, 3 легенда
        public int MapSize = 1;               // 0 малая, 1 средняя, 2 большая
        public int MapSeed = 1977;
        public int EmpireStrength = 1;        // 0 гарнизоны, 1 армия, 2 легион
        public int StartRequisition = 800;
        public int UnitCap = 60;              // бойцов на игрока
        public int TimeOfDay = 1;             // 0 день, 1 закат, 2 пасмурно
        public bool AllowEmpirePlayers = true;

        public static readonly string[] DifficultyNames = { "Новобранец", "Солдат", "Ветеран", "Легенда" };
        public static readonly string[] MapSizeNames = { "Малая (1 км)", "Средняя (1.5 км)", "Большая (2 км)" };
        public static readonly string[] StrengthNames = { "Гарнизоны", "Армия", "Легион" };
        public static readonly string[] TimeNames = { "День", "Закат", "Пасмурно" };

        public void Write(NetWriter w)
        {
            w.String(LobbyName); w.Byte((byte)Privacy); w.Byte((byte)MaxPlayers); w.Byte((byte)Difficulty); w.Byte((byte)MapSize);
            w.Int(MapSeed); w.Byte((byte)EmpireStrength); w.Int(StartRequisition); w.UShort((ushort)UnitCap); w.Byte((byte)TimeOfDay);
            w.Bool(AllowEmpirePlayers);
        }

        public void Read(NetReader r)
        {
            LobbyName = r.String(); Privacy = (LobbyPrivacy)r.Byte(); MaxPlayers = r.Byte(); Difficulty = r.Byte(); MapSize = r.Byte();
            MapSeed = r.Int(); EmpireStrength = r.Byte(); StartRequisition = r.Int(); UnitCap = r.UShort(); TimeOfDay = r.Byte();
            AllowEmpirePlayers = r.Bool();
        }

        public MatchSettings Clone() => (MatchSettings)MemberwiseClone();
    }

    /// <summary>
    /// Сетевая сессия: хост (сервер + локальный игрок) или клиент. Лобби (игроки, готовность, сторона, чат, настройки),
    /// загрузка боя у всех, начало боя, выход и потеря соединения. Игровые системы регистрируют обработчики сообщений.
    /// Режимы: одиночная игра (без сети), Steam (лобби + P2P через релеи Valve), по IP (LiteNetLib, порт 7777).
    /// </summary>
    public class NetSession : MonoBehaviour, ITransportHandler
    {
        public const int ProtocolVersion = 3;
        public const int DefaultPort = 7777;
        public const string MenuScene = "Menu";
        public const string BattleScene = "Battle";

        public static NetSession Instance { get; private set; }

        public SessionMode Mode { get; private set; }
        public SessionPhase Phase { get; private set; }
        public bool IsServer { get; private set; }
        public bool IsClient => Mode != SessionMode.None;
        public bool Online => Mode == SessionMode.Steam || Mode == SessionMode.Lan;
        public byte LocalId { get; private set; }
        public MatchSettings Settings { get; private set; } = new MatchSettings();
        public readonly List<PlayerInfo> Players = new List<PlayerInfo>();
        public SteamLobby Lobby { get; private set; }
        public string LastStatus { get; private set; } = "";
        public string LobbyCode => Mode == SessionMode.Steam && Lobby.InLobby ? Lobby.LobbyId.m_SteamID.ToString() : "";

        public PlayerInfo Local => Players.FirstOrDefault(p => p.Id == LocalId);
        public PlayerInfo Get(byte id) => Players.FirstOrDefault(p => p.Id == id);

        public event Action LobbyChanged;
        public event Action<string> StatusChanged;
        public event Action<string, string> ChatReceived;       // имя, текст
        public event Action MatchLoading;
        public event Action MatchBegan;                         // у всех
        public event Action<PlayerInfo> PlayerJoinedMatch;      // сервер: игрок загрузился (в т. ч. поздний вход)
        public event Action<PlayerInfo> PlayerLeft;             // сервер
        public event Action<string> Disconnected;               // клиент: выкинуло в меню

        public delegate void ServerHandler(PlayerInfo from, NetReader r);
        public delegate void ClientHandler(NetReader r);
        readonly Dictionary<Msg, ServerHandler> serverHandlers = new Dictionary<Msg, ServerHandler>();
        readonly Dictionary<Msg, ClientHandler> clientHandlers = new Dictionary<Msg, ClientHandler>();

        ITransport transport;
        readonly NetWriter w = new NetWriter(1024);
        readonly NetReader reader = new NetReader();
        float lobbyBroadcastAt, loadDeadline;
        bool leaving;

        public static NetSession Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("[NetSession]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<NetSession>();
            Instance.Lobby = go.AddComponent<SteamLobby>();
            return Instance;
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            Lobby ??= GetComponent<SteamLobby>();
            Lobby.Created += OnSteamLobbyCreated;
            Lobby.JoinedAsClient += OnSteamLobbyJoined;
            Lobby.Failed += Fail;
        }

        // ================================================================== регистрация обработчиков
        public void OnServer(Msg m, ServerHandler h) => serverHandlers[m] = h;
        public void OnClient(Msg m, ClientHandler h) => clientHandlers[m] = h;
        public void ClearGameHandlers()
        {
            foreach (var k in serverHandlers.Keys.Where(k => (byte)k >= 32).ToList()) serverHandlers.Remove(k);
            foreach (var k in clientHandlers.Keys.Where(k => (byte)k >= 32).ToList()) clientHandlers.Remove(k);
        }

        // ================================================================== запуск
        public void HostOffline(MatchSettings s)
        {
            if (!BeginHost(SessionMode.Offline, s)) return;
            Status("");
        }

        public void HostSteam(MatchSettings s)
        {
            if (Phase != SessionPhase.Idle) return;
            if (!SteamBootstrap.Initialized) { Fail("Steam не запущен. Запустите Steam или играйте по IP."); return; }
            Settings = s.Clone();
            Phase = SessionPhase.Connecting;
            Status("Создаём лобби Steam...");
            Lobby.Create(s.Privacy, s.MaxPlayers, LobbyData());
        }

        void OnSteamLobbyCreated(CSteamID id)
        {
            transport = new SteamTransport(0);
            Phase = SessionPhase.Idle;
            if (!BeginHost(SessionMode.Steam, Settings)) { Lobby.Leave(); return; }
            Status("");
        }

        /// <summary>Сцена боя открыта напрямую (редактор): одиночная игра без меню.</summary>
        public void StartInPlace(MatchSettings s)
        {
            if (!BeginHost(SessionMode.Offline, s)) return;
            Phase = SessionPhase.InGame;
            var me = Local;
            me.Loaded = true;
            me.InMatch = true;
            MatchBegan?.Invoke();
            PlayerJoinedMatch?.Invoke(me);
        }

        public void HostLan(MatchSettings s, int port = DefaultPort)
        {
            if (Phase != SessionPhase.Idle) return;
            transport = new LanTransport("0.0.0.0", port);
            BeginHost(SessionMode.Lan, s);
        }

        bool BeginHost(SessionMode mode, MatchSettings s)
        {
            Settings = s.Clone();
            if (mode != SessionMode.Offline && (transport == null || !transport.StartServer()))
            {
                transport = null;
                Fail(mode == SessionMode.Lan ? "Не удалось открыть порт (занят?)" : "Не удалось запустить сервер Steam");
                return false;
            }
            Mode = mode;
            IsServer = true;
            Phase = SessionPhase.Lobby;
            leaving = false;
            Players.Clear();
            LocalId = 0;
            Players.Add(new PlayerInfo { Id = 0, Name = SteamBootstrap.LocalName, SteamId = SteamBootstrap.LocalId, Host = true, Ready = true, Conn = -1, Color = 0 });
            LobbyChanged?.Invoke();
            return true;
        }

        public void JoinSteamLobby(string code)
        {
            if (Phase != SessionPhase.Idle) return;
            if (!SteamBootstrap.Initialized) { Fail("Steam не запущен"); return; }
            if (Lobby.JoinByCode(code)) { Phase = SessionPhase.Connecting; Status("Входим в лобби..."); }
        }

        public void JoinSteamLobby(ulong lobby)
        {
            if (!SteamBootstrap.Initialized) { Fail("Steam не запущен"); return; }
            if (Phase != SessionPhase.Idle) Leave(false);
            Phase = SessionPhase.Connecting;
            Status("Входим в лобби...");
            Lobby.Join(new CSteamID(lobby));
        }

        void OnSteamLobbyJoined(CSteamID host)
        {
            // приглашение могло прийти, когда мы сами были хостом
            if (Phase != SessionPhase.Connecting && Phase != SessionPhase.Idle) ShutdownTransport();
            transport = new SteamTransport(host.m_SteamID);
            Mode = SessionMode.Steam;
            IsServer = false;
            Phase = SessionPhase.Connecting;
            leaving = false;
            Status($"Подключение к {SteamFriends.GetFriendPersonaName(host)}...");
            if (!transport.StartClient()) { Lobby.Leave(); ShutdownTransport(); Fail("Не удалось подключиться к хосту"); }
        }

        public void JoinLan(string address, int port = DefaultPort)
        {
            if (Phase != SessionPhase.Idle) return;
            transport = new LanTransport(address.Trim(), port);
            Mode = SessionMode.Lan;
            IsServer = false;
            Phase = SessionPhase.Connecting;
            leaving = false;
            Status($"Подключение к {address}:{port}...");
            if (!transport.StartClient()) { ShutdownTransport(); Fail("Не удалось подключиться"); }
        }

        Dictionary<string, string> LobbyData() => new Dictionary<string, string>
        {
            { "name", Settings.LobbyName }, { "hostname", SteamBootstrap.LocalName }, { "state", "lobby" },
            { "map", MatchSettings.MapSizeNames[Mathf.Clamp(Settings.MapSize, 0, 2)] },
            { "mode", Settings.AllowEmpirePlayers ? "Кооп / против" : "Кооператив" }, { "difficulty", Settings.Difficulty.ToString() },
        };

        // ================================================================== лобби: действия игрока
        public void SetReady(bool ready)
        {
            var me = Local;
            if (me == null) return;
            if (IsServer) { me.Ready = ready; BroadcastLobby(); return; }
            w.Reset(Msg.SetReady).Bool(ready);
            SendToServer(w, true);
        }

        public void SetTeam(Team t)
        {
            if (IsServer) { var me = Local; if (me != null) { me.Team = t; BroadcastLobby(); } return; }
            w.Reset(Msg.SetTeam).Byte((byte)t);
            SendToServer(w, true);
        }

        public void SendChat(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 200) text = text.Substring(0, 200);
            if (IsServer) { RelayChat(Local, text); return; }
            w.Reset(Msg.Chat).String(text);
            SendToServer(w, true);
        }

        public void UpdateSettings(MatchSettings s)
        {
            if (!IsServer) return;
            int seed = Settings.MapSeed;
            Settings = s.Clone();
            Settings.MapSeed = seed;
            if (Mode == SessionMode.Steam) foreach (var kv in LobbyData()) Lobby.SetData(kv.Key, kv.Value);
            BroadcastLobby();
        }

        public void Kick(byte id)
        {
            if (!IsServer) return;
            var p = Get(id);
            if (p == null || p.Conn < 0) return;
            w.Reset(Msg.Kick).String("Хост исключил вас из лобби");
            transport.Send(p.Conn, w.Segment, true);
            transport.Disconnect(p.Conn);
            RemovePlayer(p, "исключён");
        }

        public bool AllReady => Players.All(p => p.Ready || p.Host);

        public void OpenInvites()
        {
            if (Mode == SessionMode.Steam) Lobby.OpenInviteOverlay();
        }

        // ================================================================== бой
        public void StartMatch()
        {
            if (!IsServer || Phase != SessionPhase.Lobby) return;
            Settings.MapSeed = UnityEngine.Random.Range(1, int.MaxValue);
            foreach (var p in Players) { p.Loaded = false; p.InMatch = false; }
            if (Mode == SessionMode.Steam) { Lobby.SetData("state", "ingame"); Lobby.SetPresence("В бою"); }
            w.Reset(Msg.LoadMatch);
            Settings.Write(w);
            SendToAllRemote(w, true);
            loadDeadline = Time.unscaledTime + 90f;
            BeginLoading();
        }

        void BeginLoading()
        {
            Phase = SessionPhase.Loading;
            MatchLoading?.Invoke();
            SceneManager.LoadScene(BattleScene);
        }

        /// <summary>Сцена боя загружена и готова (вызывает BattleBootstrap).</summary>
        public void NotifyLoaded()
        {
            if (IsServer)
            {
                var me = Local;
                if (me != null) me.Loaded = true;
                TryBegin();
            }
            else
            {
                w.Reset(Msg.Loaded);
                SendToServer(w, true);
            }
        }

        void TryBegin()
        {
            if (!IsServer || Phase != SessionPhase.Loading) return;
            bool all = Players.All(p => p.Loaded);
            if (!all && Time.unscaledTime < loadDeadline) return;
            Phase = SessionPhase.InGame;
            w.Reset(Msg.BeginMatch);
            SendToAllRemote(w, true);
            MatchBegan?.Invoke();
            foreach (var p in Players.Where(p => p.Loaded)) { p.InMatch = true; PlayerJoinedMatch?.Invoke(p); }
        }

        public void ReturnToLobby()
        {
            if (!IsServer) return;
            w.Reset(Msg.ReturnToLobby);
            SendToAllRemote(w, true);
            GoToLobby();
        }

        void GoToLobby()
        {
            ClearGameHandlers();
            Phase = SessionPhase.Lobby;
            foreach (var p in Players) { p.Loaded = false; p.InMatch = false; p.Ready = p.Host; }
            if (IsServer && Mode == SessionMode.Steam) { Lobby.SetData("state", "lobby"); Lobby.SetPresence("В лобби"); }
            if (Mode == SessionMode.Offline) { Leave(); return; }
            SceneManager.LoadScene(MenuScene);
            LobbyChanged?.Invoke();
        }

        // ================================================================== выход
        public void Leave(bool toMenu = true)
        {
            if (leaving) return;
            leaving = true;
            try
            {
                ShutdownTransport();
                Lobby?.Leave();
                Mode = SessionMode.None;
                Phase = SessionPhase.Idle;
                IsServer = false;
                Players.Clear();
                ClearGameHandlers();
                LobbyChanged?.Invoke();
                if (toMenu && SceneManager.GetActiveScene().name != MenuScene) SceneManager.LoadScene(MenuScene);
            }
            finally { leaving = false; }
        }

        void ShutdownTransport()
        {
            transport?.Shutdown();
            transport = null;
        }

        void OnApplicationQuit() => ShutdownTransport();

        // ================================================================== отправка
        public void SendToServer(NetWriter msg, bool reliable)
        {
            if (IsServer)
            {
                Dispatch(Local, msg.Segment, true);
                return;
            }
            transport?.Send(0, msg.Segment, reliable);
        }

        /// <summary>Всем клиентам в бою (кроме хоста). includeHost — выполнить и у себя (хост тоже клиент).</summary>
        public void SendToAll(NetWriter msg, bool reliable, bool includeHost = false, byte? except = null)
        {
            if (!IsServer) return;
            foreach (var p in Players)
            {
                if (p.Conn < 0 || (except.HasValue && p.Id == except.Value)) continue;
                if (Phase == SessionPhase.InGame && !p.InMatch) continue;
                transport?.Send(p.Conn, msg.Segment, reliable);
            }
            if (includeHost && (!except.HasValue || except.Value != LocalId)) DispatchClient(msg.Segment);
        }

        public void SendTo(PlayerInfo p, NetWriter msg, bool reliable)
        {
            if (!IsServer || p == null) return;
            if (p.Conn < 0) DispatchClient(msg.Segment);
            else transport?.Send(p.Conn, msg.Segment, reliable);
        }

        void SendToAllRemote(NetWriter msg, bool reliable)
        {
            foreach (var p in Players) if (p.Conn >= 0) transport?.Send(p.Conn, msg.Segment, reliable);
        }

        public int MaxUnreliable => transport?.MaxUnreliable ?? 1100;

        // ================================================================== цикл
        void Update()
        {
            transport?.Poll(this);
            if (!IsServer) return;
            float now = Time.unscaledTime;
            // клиенты, не приславшие Hello вовремя
            foreach (var p in Players.ToList())
                if (p.Conn >= 0 && p.Name == "" && now > p.HelloDeadline) { transport?.Disconnect(p.Conn); RemovePlayer(p, "таймаут"); }
            if (Phase == SessionPhase.Lobby && now > lobbyBroadcastAt)
            {
                lobbyBroadcastAt = now + 2f;
                foreach (var p in Players) if (p.Conn >= 0) p.Ping = transport?.Ping(p.Conn) ?? 0;
                BroadcastLobby();
            }
            if (Phase == SessionPhase.Loading) TryBegin();
        }

        // ================================================================== транспорт
        public void OnConnected(int conn, ulong steamId)
        {
            if (IsServer)
            {
                var p = new PlayerInfo { Id = NextId(), Conn = conn, SteamId = steamId, Name = "", HelloDeadline = Time.unscaledTime + 15f };
                Players.Add(p);
                return;
            }
            // клиент: соединились с сервером — представляемся
            w.Reset(Msg.Hello);
            w.Int(ProtocolVersion);
            w.String(Application.version);
            w.String(SteamBootstrap.LocalName);
            w.ULong(SteamBootstrap.LocalId);
            transport.Send(0, w.Segment, true);
            Status("Вход в лобби...");
        }

        public void OnDisconnected(int conn, string reason)
        {
            if (IsServer)
            {
                var p = Players.FirstOrDefault(x => x.Conn == conn);
                if (p != null) RemovePlayer(p, reason);
                return;
            }
            if (leaving || Mode == SessionMode.None) return;
            string msg = Phase == SessionPhase.Connecting ? "Не удалось подключиться к хосту" : "Соединение с хостом потеряно";
            Leave();
            Fail(msg);
            Disconnected?.Invoke(msg);
        }

        public void OnData(int conn, ArraySegment<byte> data)
        {
            if (data.Count == 0) return;
            if (IsServer)
            {
                var p = Players.FirstOrDefault(x => x.Conn == conn);
                if (p != null) Dispatch(p, data, false);
            }
            else DispatchClient(data);
        }

        byte NextId()
        {
            for (byte i = 1; i < 255; i++) if (Players.All(p => p.Id != i)) return i;
            return 255;
        }

        void RemovePlayer(PlayerInfo p, string reason)
        {
            Players.Remove(p);
            if (!string.IsNullOrEmpty(p.Name))
            {
                RelaySystem($"{p.Name} покинул игру");
                PlayerLeft?.Invoke(p);
            }
            BroadcastLobby();
        }

        // ================================================================== сервер: обработка
        void Dispatch(PlayerInfo from, ArraySegment<byte> data, bool local)
        {
            reader.Set(data);
            var type = (Msg)reader.Byte();
            try
            {
                switch (type)
                {
                    case Msg.Hello: if (!local) OnHello(from, reader); return;
                    case Msg.SetReady: from.Ready = reader.Bool(); BroadcastLobby(); return;
                    case Msg.SetTeam:
                        var t = (Team)reader.Byte();
                        if (t == Team.Empire && !Settings.AllowEmpirePlayers) t = Team.Republic;
                        if (Phase == SessionPhase.Lobby) from.Team = t;
                        BroadcastLobby();
                        return;
                    case Msg.Chat: RelayChat(from, reader.String()); return;
                    case Msg.Loaded:
                        from.Loaded = true;
                        if (Phase == SessionPhase.Loading) TryBegin();
                        else if (Phase == SessionPhase.InGame)
                        {
                            // поздний вход: сразу в бой
                            w.Reset(Msg.BeginMatch);
                            transport?.Send(from.Conn, w.Segment, true);
                            from.InMatch = true;
                            PlayerJoinedMatch?.Invoke(from);
                        }
                        return;
                }
                if (string.IsNullOrEmpty(from.Name)) return;            // до Hello — ничего
                if (serverHandlers.TryGetValue(type, out var h)) h(from, reader);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Net] ошибка в сообщении {type} от {from.Name}: {e.Message}");
            }
        }

        void OnHello(PlayerInfo p, NetReader r)
        {
            int proto = r.Int();
            string ver = r.String();
            string name = r.String();
            ulong steam = r.ULong();
            string reject = null;
            if (proto != ProtocolVersion || ver != Application.version) reject = $"Версии игры не совпадают (хост {Application.version})";
            else if (Players.Count(x => !string.IsNullOrEmpty(x.Name)) >= Settings.MaxPlayers) reject = "Лобби заполнено";
            if (reject != null)
            {
                w.Reset(Msg.Reject).String(reject);
                transport.Send(p.Conn, w.Segment, true);
                transport.Disconnect(p.Conn);
                Players.Remove(p);
                return;
            }
            p.Name = string.IsNullOrWhiteSpace(name) ? "Клон " + p.Id : name.Trim();
            if (steam != 0) p.SteamId = steam;
            p.Color = (byte)(p.Id % 8);
            p.Team = Team.Republic;
            w.Reset(Msg.Welcome).Byte(p.Id);
            transport.Send(p.Conn, w.Segment, true);
            RelaySystem($"{p.Name} присоединился");
            BroadcastLobby();
            if (Phase == SessionPhase.Loading || Phase == SessionPhase.InGame)
            {
                w.Reset(Msg.LoadMatch);
                Settings.Write(w);
                transport.Send(p.Conn, w.Segment, true);
            }
        }

        void RelayChat(PlayerInfo from, string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 200) text = text.Substring(0, 200);
            w.Reset(Msg.Chat).String(from?.Name ?? "").String(text);
            SendToAllRemote(w, true);
            ChatReceived?.Invoke(from?.Name ?? "", text);
        }

        void RelaySystem(string text)
        {
            w.Reset(Msg.Chat).String("").String(text);
            SendToAllRemote(w, true);
            ChatReceived?.Invoke("", text);
        }

        void BroadcastLobby()
        {
            if (!IsServer) return;
            w.Reset(Msg.LobbyState);
            w.Byte((byte)Phase);
            Settings.Write(w);
            var list = Players.Where(p => !string.IsNullOrEmpty(p.Name)).ToList();
            w.Byte((byte)list.Count);
            foreach (var p in list)
            {
                w.Byte(p.Id); w.String(p.Name); w.ULong(p.SteamId); w.Byte((byte)p.Team); w.Bool(p.Ready); w.Bool(p.Host);
                w.Byte(p.Color); w.UShort((ushort)Mathf.Clamp(p.Ping, 0, 65535));
            }
            SendToAllRemote(w, true);
            if (Mode == SessionMode.Steam) Lobby.SetData("players", list.Count.ToString());
            LobbyChanged?.Invoke();
        }

        // ================================================================== клиент: обработка
        void DispatchClient(ArraySegment<byte> data)
        {
            var r = new NetReader(data);
            var type = (Msg)r.Byte();
            try
            {
                switch (type)
                {
                    case Msg.Welcome:
                        LocalId = r.Byte();
                        Phase = SessionPhase.Lobby;
                        Status("");
                        LobbyChanged?.Invoke();
                        return;
                    case Msg.Reject:
                    case Msg.Kick:
                        var why = r.String();
                        Leave();
                        Fail(why);
                        Disconnected?.Invoke(why);
                        return;
                    case Msg.LobbyState:
                        if (IsServer) return;
                        r.Byte();
                        var s = new MatchSettings();
                        s.Read(r);
                        Settings = s;
                        int n = r.Byte();
                        Players.Clear();
                        for (int i = 0; i < n; i++)
                        {
                            var p = new PlayerInfo
                            {
                                Id = r.Byte(), Name = r.String(), SteamId = r.ULong(), Team = (Team)r.Byte(), Ready = r.Bool(), Host = r.Bool(),
                                Color = r.Byte(), Ping = r.UShort(),
                            };
                            Players.Add(p);
                        }
                        LobbyChanged?.Invoke();
                        return;
                    case Msg.Chat:
                        if (IsServer) return;
                        ChatReceived?.Invoke(r.String(), r.String());
                        return;
                    case Msg.LoadMatch:
                        if (IsServer) return;
                        var ms = new MatchSettings();
                        ms.Read(r);
                        Settings = ms;
                        BeginLoading();
                        return;
                    case Msg.BeginMatch:
                        if (IsServer) return;
                        Phase = SessionPhase.InGame;
                        MatchBegan?.Invoke();
                        return;
                    case Msg.ReturnToLobby:
                        if (IsServer) return;
                        GoToLobby();
                        return;
                }
                if (clientHandlers.TryGetValue(type, out var h)) h(r);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Net] ошибка в сообщении {type}: {e.Message}\n{e.StackTrace}");
            }
        }

        // ================================================================== статус
        void Fail(string message)
        {
            if (Phase == SessionPhase.Connecting) Phase = SessionPhase.Idle;
            Debug.LogWarning("[Session] " + message);
            Status(message);
        }

        void Status(string message)
        {
            LastStatus = message;
            StatusChanged?.Invoke(message);
        }
    }
}
