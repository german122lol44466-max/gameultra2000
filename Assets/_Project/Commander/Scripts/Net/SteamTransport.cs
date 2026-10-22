// Транспорт поверх Steam Networking Sockets (P2P через релеи Valve — порты открывать не нужно).
// По мотивам SteamNetworkingSocketsTransport из Unity multiplayer-community-contributions (MIT,
// Copyright (c) 2021 Unity Technologies) и YourFinalOrder.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace SW.Commander.Net
{
    public class SteamTransport : ITransport
    {
        class Conn
        {
            public int Id;
            public CSteamID Steam;
            public HSteamNetConnection Handle;
            public bool Connected;
        }

        const int MaxMessagesPerPoll = 256;
        readonly ulong hostSteamId;
        Callback<SteamNetConnectionStatusChangedCallback_t> statusCallback;
        HSteamListenSocket listen = HSteamListenSocket.Invalid;
        readonly Dictionary<int, Conn> conns = new Dictionary<int, Conn>();
        readonly Dictionary<uint, Conn> byHandle = new Dictionary<uint, Conn>();
        readonly Queue<SteamNetConnectionStatusChangedCallback_t> status = new Queue<SteamNetConnectionStatusChangedCallback_t>();
        readonly IntPtr[] messages = new IntPtr[MaxMessagesPerPoll];
        int nextId = 1;

        public bool IsServer { get; private set; }
        public int MaxUnreliable => 1100;

        /// <param name="host">SteamID хоста для клиента (0 — для сервера).</param>
        public SteamTransport(ulong host) { hostSteamId = host; }

        void EnsureCallback()
        {
            statusCallback ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(s => status.Enqueue(s));
        }

        public bool StartServer()
        {
            if (!SteamBootstrap.Initialized) return false;
            IsServer = true;
            EnsureCallback();
            SteamNetworkingUtils.InitRelayNetworkAccess();
            listen = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
            return listen != HSteamListenSocket.Invalid;
        }

        public bool StartClient()
        {
            if (!SteamBootstrap.Initialized || hostSteamId == 0) return false;
            IsServer = false;
            EnsureCallback();
            SteamNetworkingUtils.InitRelayNetworkAccess();
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(new CSteamID(hostSteamId));
            var h = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
            if (h == HSteamNetConnection.Invalid) return false;
            var c = new Conn { Id = 0, Steam = new CSteamID(hostSteamId), Handle = h };
            conns[0] = c;
            byHandle[h.m_HSteamNetConnection] = c;
            return true;
        }

        public void Poll(ITransportHandler handler)
        {
            while (status.Count > 0)
            {
                var s = status.Dequeue();
                uint hh = s.m_hConn.m_HSteamNetConnection;
                switch (s.m_info.m_eState)
                {
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                        if (IsServer && !byHandle.ContainsKey(hh))
                        {
                            if (SteamNetworkingSockets.AcceptConnection(s.m_hConn) == EResult.k_EResultOK)
                            {
                                var c = new Conn { Id = nextId++, Steam = s.m_info.m_identityRemote.GetSteamID(), Handle = s.m_hConn };
                                conns[c.Id] = c;
                                byHandle[hh] = c;
                            }
                            else SteamNetworkingSockets.CloseConnection(s.m_hConn, 0, "Rejected", false);
                        }
                        break;
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                        if (byHandle.TryGetValue(hh, out var cc) && !cc.Connected)
                        {
                            cc.Connected = true;
                            handler.OnConnected(cc.Id, cc.Steam.m_SteamID);
                        }
                        break;
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                        SteamNetworkingSockets.CloseConnection(s.m_hConn, 0, "Closed", false);
                        if (byHandle.TryGetValue(hh, out var dc))
                        {
                            byHandle.Remove(hh);
                            conns.Remove(dc.Id);
                            handler.OnDisconnected(dc.Id, s.m_info.m_szEndDebug);
                        }
                        break;
                }
            }
            foreach (var c in conns.Values)
            {
                if (!c.Connected) continue;
                int n = SteamNetworkingSockets.ReceiveMessagesOnConnection(c.Handle, messages, MaxMessagesPerPoll);
                for (int i = 0; i < n; i++)
                {
                    var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(messages[i]);
                    var data = new byte[msg.m_cbSize];
                    Marshal.Copy(msg.m_pData, data, 0, msg.m_cbSize);
                    SteamNetworkingMessage_t.Release(messages[i]);
                    pending.Add((c.Id, data));
                }
            }
            // доставка после обхода (обработчик может закрыть соединение)
            for (int i = 0; i < pending.Count; i++) handler.OnData(pending[i].id, new ArraySegment<byte>(pending[i].data));
            pending.Clear();
        }

        readonly List<(int id, byte[] data)> pending = new List<(int, byte[])>();

        public void Send(int conn, ArraySegment<byte> data, bool reliable)
        {
            if (!conns.TryGetValue(conn, out var c) || !c.Connected) return;
            int flags = reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_UnreliableNoNagle;
            var h = GCHandle.Alloc(data.Array, GCHandleType.Pinned);
            try
            {
                var ptr = h.AddrOfPinnedObject() + data.Offset;
                var r = SteamNetworkingSockets.SendMessageToConnection(c.Handle, ptr, (uint)data.Count, flags, out _);
                if (r != EResult.k_EResultOK && r != EResult.k_EResultLimitExceeded && r != EResult.k_EResultIgnored)
                    Debug.LogWarning("[Steam] ошибка отправки: " + r);
            }
            finally { h.Free(); }
        }

        public void Disconnect(int conn)
        {
            if (!conns.TryGetValue(conn, out var c)) return;
            SteamNetworkingSockets.CloseConnection(c.Handle, 0, "Kicked", true);
            conns.Remove(conn);
            byHandle.Remove(c.Handle.m_HSteamNetConnection);
        }

        public int Ping(int conn)
        {
            if (!conns.TryGetValue(conn, out var c)) return 0;
            var st = new SteamNetConnectionRealTimeStatus_t();
            var lane = new SteamNetConnectionRealTimeLaneStatus_t();
            return SteamNetworkingSockets.GetConnectionRealTimeStatus(c.Handle, ref st, 0, ref lane) == EResult.k_EResultOK ? Mathf.Max(0, st.m_nPing) : 0;
        }

        public void Shutdown()
        {
            if (SteamBootstrap.Initialized)
            {
                foreach (var c in conns.Values) SteamNetworkingSockets.CloseConnection(c.Handle, 0, "Shutdown", true);
                if (listen != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(listen);
            }
            listen = HSteamListenSocket.Invalid;
            conns.Clear();
            byHandle.Clear();
            status.Clear();
            statusCallback?.Dispose();
            statusCallback = null;
        }
    }
}
