using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace SW.Commander.Net
{
    public enum LobbyPrivacy { Public = 0, Friends = 1, Private = 2 }

    /// <summary>Строка в списке лобби (браузер серверов).</summary>
    public class LobbyListing
    {
        public ulong Id;
        public string Name, Host, Map, Mode, State, Version;
        public int Players, MaxPlayers, Difficulty;
        public int Ping;
    }

    /// <summary>
    /// Лобби Steam: создание (публичное / для друзей / закрытое), список публичных лобби с фильтрами,
    /// вход по приглашению, коду или из списка, данные для браузера, Rich Presence «Присоединиться».
    /// Сама игра идёт через SteamTransport (P2P к хосту, SteamID хоста хранится в данных лобби).
    /// </summary>
    public class SteamLobby : MonoBehaviour
    {
        public const string GameKey = "game";
        public const string GameId = "sw-clone-commander";

        public CSteamID LobbyId { get; private set; } = CSteamID.Nil;
        public bool InLobby => LobbyId != CSteamID.Nil;
        public bool IsOwner => InLobby && SteamMatchmaking.GetLobbyOwner(LobbyId) == SteamUser.GetSteamID();

        public event Action<CSteamID> Created;
        /// <summary>Вошли в чужое лобби: SteamID хоста.</summary>
        public event Action<CSteamID> JoinedAsClient;
        public event Action<string> Failed;
        public event Action<List<LobbyListing>> ListReceived;

        CallResult<LobbyCreated_t> createdResult;
        CallResult<LobbyEnter_t> enterResult;
        CallResult<LobbyMatchList_t> listResult;
        Callback<GameLobbyJoinRequested_t> joinRequested;
        Callback<GameRichPresenceJoinRequested_t> richJoin;
        bool ready, creating;
        LobbyPrivacy pendingPrivacy;
        Dictionary<string, string> pendingData;

        void Start()
        {
            if (SteamBootstrap.Initialized) Setup();
            else SteamBootstrap.Ready += Setup;
        }

        void OnDestroy() => SteamBootstrap.Ready -= Setup;

        void Setup()
        {
            if (ready) return;
            ready = true;
            createdResult = CallResult<LobbyCreated_t>.Create(OnCreated);
            enterResult = CallResult<LobbyEnter_t>.Create(OnEntered);
            listResult = CallResult<LobbyMatchList_t>.Create(OnList);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => Join(r.m_steamIDLobby));
            richJoin = Callback<GameRichPresenceJoinRequested_t>.Create(r =>
            {
                if (TryParseConnect(r.m_rgchConnect, out var id)) Join(id);
            });
            // запуск игры через приглашение: "+connect_lobby <id>"
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out var lobby))
                    Join(new CSteamID(lobby));
        }

        // ------------------------------------------------------------------ создание
        public void Create(LobbyPrivacy privacy, int maxMembers, Dictionary<string, string> data)
        {
            if (!SteamBootstrap.Initialized) { Failed?.Invoke("Steam не запущен"); return; }
            if (creating) return;
            Leave();
            creating = true;
            pendingPrivacy = privacy;
            pendingData = data;
            var type = privacy == LobbyPrivacy.Public ? ELobbyType.k_ELobbyTypePublic
                     : privacy == LobbyPrivacy.Friends ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePrivate;
            createdResult.Set(SteamMatchmaking.CreateLobby(type, Mathf.Clamp(maxMembers, 1, 16)));
        }

        void OnCreated(LobbyCreated_t r, bool ioFailure)
        {
            creating = false;
            if (ioFailure || r.m_eResult != EResult.k_EResultOK) { Failed?.Invoke($"Не удалось создать лобби ({r.m_eResult})"); return; }
            LobbyId = new CSteamID(r.m_ulSteamIDLobby);
            SetData("host", SteamUser.GetSteamID().m_SteamID.ToString());
            SetData(GameKey, GameId);
            SetData("version", Application.version);
            if (pendingData != null) foreach (var kv in pendingData) SetData(kv.Key, kv.Value);
            SetPresence("В лобби");
            Created?.Invoke(LobbyId);
        }

        public void SetData(string key, string value)
        {
            if (InLobby && IsOwner) SteamMatchmaking.SetLobbyData(LobbyId, key, value ?? "");
        }

        public void SetJoinable(bool joinable)
        {
            if (InLobby && IsOwner) SteamMatchmaking.SetLobbyJoinable(LobbyId, joinable);
        }

        // ------------------------------------------------------------------ вход
        public void Join(CSteamID lobby)
        {
            if (!SteamBootstrap.Initialized) { Failed?.Invoke("Steam не запущен"); return; }
            if (lobby == LobbyId) return;
            Leave();
            enterResult.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        public bool JoinByCode(string code)
        {
            code = (code ?? "").Trim();
            if (!ulong.TryParse(code, out var id)) { Failed?.Invoke("Неверный код лобби"); return false; }
            Join(new CSteamID(id));
            return true;
        }

        void OnEntered(LobbyEnter_t r, bool ioFailure)
        {
            if (ioFailure || r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Failed?.Invoke("Не удалось войти в лобби (закрыто, заполнено или идёт бой)");
                return;
            }
            var lobby = new CSteamID(r.m_ulSteamIDLobby);
            if (SteamMatchmaking.GetLobbyData(lobby, GameKey) != GameId)
            {
                SteamMatchmaking.LeaveLobby(lobby);
                Failed?.Invoke("Это лобби другой игры");
                return;
            }
            string ver = SteamMatchmaking.GetLobbyData(lobby, "version");
            if (!string.IsNullOrEmpty(ver) && ver != Application.version)
            {
                SteamMatchmaking.LeaveLobby(lobby);
                Failed?.Invoke($"Другая версия игры у хоста ({ver}, у вас {Application.version})");
                return;
            }
            LobbyId = lobby;
            SetPresence("В лобби");
            if (ulong.TryParse(SteamMatchmaking.GetLobbyData(lobby, "host"), out var host) && host != SteamUser.GetSteamID().m_SteamID)
                JoinedAsClient?.Invoke(new CSteamID(host));
        }

        public void Leave()
        {
            if (!InLobby) return;
            if (SteamBootstrap.Initialized)
            {
                SteamMatchmaking.LeaveLobby(LobbyId);
                SteamFriends.ClearRichPresence();
            }
            LobbyId = CSteamID.Nil;
        }

        // ------------------------------------------------------------------ браузер
        /// <summary>Запросить публичные лобби этой игры. Результат — событие ListReceived.</summary>
        public void RequestList(bool onlyOpen = true)
        {
            if (!SteamBootstrap.Initialized) { ListReceived?.Invoke(new List<LobbyListing>()); return; }
            SteamMatchmaking.AddRequestLobbyListStringFilter(GameKey, GameId, ELobbyComparison.k_ELobbyComparisonEqual);
            if (onlyOpen) SteamMatchmaking.AddRequestLobbyListStringFilter("state", "lobby", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(60);
            listResult.Set(SteamMatchmaking.RequestLobbyList());
        }

        void OnList(LobbyMatchList_t r, bool ioFailure)
        {
            var list = new List<LobbyListing>();
            if (!ioFailure)
            {
                for (int i = 0; i < r.m_nLobbiesMatching; i++)
                {
                    var id = SteamMatchmaking.GetLobbyByIndex(i);
                    int.TryParse(SteamMatchmaking.GetLobbyData(id, "difficulty"), out int diff);
                    list.Add(new LobbyListing
                    {
                        Id = id.m_SteamID,
                        Name = SteamMatchmaking.GetLobbyData(id, "name"),
                        Host = SteamMatchmaking.GetLobbyData(id, "hostname"),
                        Map = SteamMatchmaking.GetLobbyData(id, "map"),
                        Mode = SteamMatchmaking.GetLobbyData(id, "mode"),
                        State = SteamMatchmaking.GetLobbyData(id, "state"),
                        Version = SteamMatchmaking.GetLobbyData(id, "version"),
                        Players = SteamMatchmaking.GetNumLobbyMembers(id),
                        MaxPlayers = SteamMatchmaking.GetLobbyMemberLimit(id),
                        Difficulty = diff,
                    });
                }
            }
            ListReceived?.Invoke(list);
        }

        // ------------------------------------------------------------------ друзья
        public void OpenInviteOverlay()
        {
            if (InLobby) SteamFriends.ActivateGameOverlayInviteDialog(LobbyId);
        }

        public static bool OverlayEnabled => SteamBootstrap.Initialized && SteamUtils.IsOverlayEnabled();

        public void SetPresence(string status)
        {
            if (!SteamBootstrap.Initialized) return;
            if (InLobby) SteamFriends.SetRichPresence("connect", $"+connect_lobby {LobbyId.m_SteamID}");
            SteamFriends.SetRichPresence("status", status);
        }

        static bool TryParseConnect(string connect, out CSteamID lobby)
        {
            lobby = CSteamID.Nil;
            if (string.IsNullOrEmpty(connect)) return false;
            var parts = connect.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
                if (parts[i] == "+connect_lobby" && ulong.TryParse(parts[i + 1], out var id)) { lobby = new CSteamID(id); return true; }
            return false;
        }
    }
}
