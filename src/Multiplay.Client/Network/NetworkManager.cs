using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using Multiplay.Shared;

namespace Multiplay.Client.Network;

/// <summary>
/// UDP client wrapping LiteNetLib.
/// Call <see cref="Connect"/> once at startup and <see cref="PollEvents"/> every Update() tick.
/// All event callbacks fire synchronously on the thread that calls PollEvents (i.e. the game thread).
/// </summary>
public sealed class NetworkManager : INetEventListener, IDisposable
{
    private const byte DefaultChannel = 0;

    private readonly NetManager _net;
    private NetPeer? _server;

    public int LocalId { get; private set; } = -1;
    public bool IsConnected => _server?.ConnectionState == ConnectionState.Connected;

    // Called on the game thread (inside PollEvents → Game1.Update) — no marshaling needed
    public event Action<int, PlayerInfo[]>? WorldSnapshotReceived; // (localId, players)
    public event Action<PlayerInfo>? PlayerJoined;
    public event Action<int, float, float>? PlayerMoved;           // (id, x, y)
    public event Action<int>? PlayerLeft;

    public NetworkManager()
    {
        _net = new NetManager(this) { AutoRecycle = true };
        _net.Start();
    }

    /// <param name="token">Session token returned by the auth server. Used as connection key.</param>
    public void Connect(string host, int port, string token)
    {
        _server = _net.Connect(host, port, token);
    }

    /// <summary>Call once per game tick (Game1.Update). Processes all pending UDP events.</summary>
    public void PollEvents() => _net.PollEvents();

    public void SendMove(float x, float y)
    {
        if (!IsConnected) return;
        var w = new NetDataWriter();
        w.Put((byte)PacketType.Move);
        w.Put(x);
        w.Put(y);
        _server!.Send(w, DefaultChannel, DeliveryMethod.Unreliable);
    }

    public void SendSetName(string name)
    {
        if (!IsConnected) return;
        var w = new NetDataWriter();
        w.Put((byte)PacketType.SetName);
        w.Put(name);
        _server!.Send(w, DefaultChannel, DeliveryMethod.ReliableOrdered);
    }

    // ── INetEventListener ──────────────────────────────────────────────────────

    public void OnConnectionRequest(ConnectionRequest request) => request.Reject();

    public void OnPeerConnected(NetPeer peer) => _server = peer;

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) => _server = null;

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
    {
        var type = (PacketType)reader.GetByte();
        switch (type)
        {
            case PacketType.WorldSnapshot:
            {
                int localId = reader.GetInt();
                int count   = reader.GetInt();
                var players = new PlayerInfo[count];
                for (int i = 0; i < count; i++)
                    players[i] = reader.ReadPlayerInfo();
                LocalId = localId;
                WorldSnapshotReceived?.Invoke(localId, players);
                break;
            }
            case PacketType.PlayerJoined:
                PlayerJoined?.Invoke(reader.ReadPlayerInfo());
                break;

            case PacketType.PlayerLeft:
                PlayerLeft?.Invoke(reader.GetInt());
                break;

            case PacketType.PlayerMoved:
                PlayerMoved?.Invoke(reader.GetInt(), reader.GetFloat(), reader.GetFloat());
                break;
        }
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
    public void OnMessageDelivered(NetPeer peer, object userData) { }
    public void OnNtpResponse(LiteNetLib.Utils.NtpPacket packet) { }
    public void OnPeerAddressChanged(NetPeer peer, IPEndPoint previousAddress) { }

    public void Dispose() => _net.Stop();
}
