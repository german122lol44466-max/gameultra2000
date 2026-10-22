using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using UnityEngine;

namespace SW.Commander.Net
{
    /// <summary>
    /// Транспорт: соединения и доставка байтов. Id соединения на сервере — от 1 и выше, на клиенте сервер — 0.
    /// </summary>
    public interface ITransport
    {
        bool IsServer { get; }
        bool StartServer();
        bool StartClient();
        void Send(int conn, ArraySegment<byte> data, bool reliable);
        void Poll(ITransportHandler handler);
        void Disconnect(int conn);
        void Shutdown();
        int Ping(int conn);
        /// <summary>Максимальный размер ненадёжного пакета (больше — делим).</summary>
        int MaxUnreliable { get; }
    }

    public interface ITransportHandler
    {
        void OnConnected(int conn, ulong steamId);
        void OnDisconnected(int conn, string reason);
        void OnData(int conn, ArraySegment<byte> data);
    }

    // =====================================================================================
    /// <summary>Игра по IP (LAN, VPN, проброс порта) через LiteNetLib (UDP, MIT).</summary>
    public class LanTransport : ITransport, INetEventListener
    {
        public const string Key = "sw-commander";
        readonly NetManager net;
        readonly string address;
        readonly int port;
        readonly Dictionary<int, NetPeer> peers = new Dictionary<int, NetPeer>();
        readonly Dictionary<NetPeer, int> ids = new Dictionary<NetPeer, int>();
        int nextId = 1;
        ITransportHandler handler;
        public bool IsServer { get; private set; }
        public int MaxUnreliable => 1100;

        public LanTransport(string address, int port)
        {
            this.address = address;
            this.port = port;
            net = new NetManager(this) { AutoRecycle = true, DisconnectTimeout = 15000, UnconnectedMessagesEnabled = false, IPv6Enabled = false };
        }

        public bool StartServer()
        {
            IsServer = true;
            return net.Start(port);
        }

        public bool StartClient()
        {
            IsServer = false;
            if (!net.Start()) return false;
            var p = net.Connect(address, port, Key);
            return p != null;
        }

        public void Send(int conn, ArraySegment<byte> data, bool reliable)
        {
            if (!peers.TryGetValue(conn, out var p)) return;
            p.Send(data.Array, data.Offset, data.Count, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
        }

        public void Poll(ITransportHandler h)
        {
            handler = h;
            net.PollEvents();
        }

        public void Disconnect(int conn)
        {
            if (peers.TryGetValue(conn, out var p)) p.Disconnect();
        }

        public void Shutdown()
        {
            net.Stop();
            peers.Clear();
            ids.Clear();
        }

        public int Ping(int conn) => peers.TryGetValue(conn, out var p) ? p.Ping : 0;

        // --- LiteNetLib
        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            int id = IsServer ? nextId++ : 0;
            peers[id] = peer;
            ids[peer] = id;
            handler?.OnConnected(id, 0);
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            if (!ids.TryGetValue(peer, out var id)) return;
            ids.Remove(peer);
            peers.Remove(id);
            handler?.OnDisconnected(id, info.Reason.ToString());
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Debug.LogWarning("[LAN] ошибка сети: " + socketError);
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            if (!ids.TryGetValue(peer, out var id)) return;
            var data = new byte[reader.AvailableBytes];
            reader.GetBytes(data, data.Length);
            handler?.OnData(id, new ArraySegment<byte>(data));
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            if (IsServer && peers.Count < 16) request.AcceptIfKey(Key);
            else request.Reject();
        }
    }
}
