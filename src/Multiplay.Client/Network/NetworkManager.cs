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

    public event Action<int, PlayerInfo[]>?      WorldSnapshotReceived;
    public event Action<PlayerInfo>?             PlayerJoined;
    public event Action<int, float, float>?      PlayerMoved;
    public event Action<int>?                    PlayerLeft;

    public event Action<EnemyInfo[], int[]>?     EnemySnapshotReceived;
    public event Action<EnemyInfo>?              EnemyMoved;
    public event Action<EnemyInfo, int>?         EnemyDamaged;

    public event Action<int, float, float, int>? PlayerDamaged;
    public event Action<PlayerStats>?            PlayerStatsReceived;

    public NetworkManager()
    {
        _net = new NetManager(this) { AutoRecycle = true };
        _net.Start();
    }

    public void Connect(string host, int port, string token) =>
        _server = _net.Connect(host, port, token);

    public void PollEvents() => _net.PollEvents();

    public void SendMove(float x, float y) =>
        SendPacket(PacketType.Move, w => { w.Put(x); w.Put(y); }, DeliveryMethod.Unreliable);

    public void SendAttack(float dirX, float dirY) =>
        SendPacket(PacketType.Attack, w => { w.Put(dirX); w.Put(dirY); }, DeliveryMethod.ReliableOrdered);

    public void SendZoneChanged(string zone) =>
        SendPacket(PacketType.ZoneChanged, w => w.Put(zone), DeliveryMethod.ReliableOrdered);

    private void SendPacket(PacketType type, Action<NetDataWriter> write, DeliveryMethod delivery)
    {
        if (!IsConnected) return;
        var w = new NetDataWriter();
        w.Put((byte)type);
        write(w);
        _server!.Send(w, DefaultChannel, delivery);
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

            case PacketType.EnemySnapshot:
            {
                int count   = reader.GetInt();
                var enemies = new EnemyInfo[count];
                var healths = new int[count];
                for (int i = 0; i < count; i++)
                {
                    enemies[i] = reader.ReadEnemyInfo();
                    healths[i] = reader.GetInt();
                }
                EnemySnapshotReceived?.Invoke(enemies, healths);
                break;
            }
            case PacketType.EnemyMoved:
                EnemyMoved?.Invoke(reader.ReadEnemyInfo());
                break;

            case PacketType.EnemyDamaged:
            {
                var e      = reader.ReadEnemyInfo();
                int health = reader.GetInt();
                EnemyDamaged?.Invoke(e, health);
                break;
            }
            case PacketType.PlayerDamaged:
                PlayerDamaged?.Invoke(
                    reader.GetInt(), reader.GetFloat(), reader.GetFloat(), reader.GetInt());
                break;

            case PacketType.PlayerStats:
                PlayerStatsReceived?.Invoke(reader.ReadPlayerStats());
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
