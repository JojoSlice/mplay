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
public sealed class NetworkManager : INetworkManager, INetEventListener
{
    private const byte DefaultChannel = 0;

    private readonly NetManager _net;
    private NetPeer? _server;

    public int  LocalId     { get; private set; } = -1;
    public bool IsConnected => _server?.ConnectionState == ConnectionState.Connected;

    public event Action<int, PlayerInfo[]>? WorldSnapshotReceived;
    public event Action<PlayerInfo>?        PlayerJoined;
    public event Action<int, float, float>? PlayerMoved;
    public event Action<int>?               PlayerLeft;

    public NetworkManager()
    {
        _net = new NetManager(this) { AutoRecycle = true };
        _net.Start();
    }

    public void Connect(string host, int port, string token) =>
        _server = _net.Connect(host, port, token);

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

    // ── INetEventListener ──────────────────────────────────────────────────────

    public void OnConnectionRequest(ConnectionRequest request) => request.Reject();

    public void OnPeerConnected(NetPeer peer) => _server = peer;

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) => _server = null;

    public void OnNetworkReceive(
        NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
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
    public void OnNetworkReceiveUnconnected(
        IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
    public void OnMessageDelivered(NetPeer peer, object userData) { }
    public void OnNtpResponse(LiteNetLib.Utils.NtpPacket packet) { }
    public void OnPeerAddressChanged(NetPeer peer, IPEndPoint previousAddress) { }

    public void Dispose() => _net.Stop();
}
