using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using Multiplay.Server.Data;
using Multiplay.Server.Models;
using Multiplay.Shared;

namespace Multiplay.Server;

/// <summary>
/// UDP game server running as an ASP.NET Core hosted service.
/// Position updates are kept in-memory; join/disconnect events are persisted to the DB.
/// </summary>
public sealed class GameServer : IHostedService, INetEventListener
{
    private const byte DefaultChannel = 0;

    private readonly NetManager _net;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<GameServer> _logger;

    // In-memory game state — only touched from the poll loop thread
    private readonly Dictionary<int, PlayerInfo> _players = [];
    // Reusable buffer for GetConnectedPeers to avoid per-broadcast allocations
    private readonly List<NetPeer> _peerBuffer = [];

    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    public GameServer(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<GameServer> logger)
    {
        _scopeFactory = scopeFactory;
        _config       = config;
        _logger       = logger;
        _net          = new NetManager(this) { AutoRecycle = true };
    }

    // ── IHostedService ──────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken ct)
    {
        var port = _config.GetValue<int>("Game:UdpPort", 9050);
        _net.Start(port);
        _logger.LogInformation("Game server listening on UDP :{Port}", port);

        _cts      = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pollTask = PollLoop(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_cts is not null) await _cts.CancelAsync();
        if (_pollTask is not null)
            await _pollTask.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        _net.Stop();
    }

    private async Task PollLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                _net.PollEvents();
                await Task.Delay(15, ct); // ~66 Hz tick rate
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── INetEventListener ──────────────────────────────────────────────────────

    public void OnConnectionRequest(ConnectionRequest request)
    {
        var token = request.Data.GetString();

        using var scope = _scopeFactory.CreateScope();
        var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.FirstOrDefault(u => u.SessionToken == token);

        if (user is null)
        {
            _logger.LogWarning("Rejected connection: invalid token");
            request.Reject();
            return;
        }

        // Store user info on the peer so OnPeerConnected can read it
        var peer = request.Accept();
        peer.Tag = (user.DisplayName ?? user.Username, user.CharacterType ?? Shared.CharacterType.Zink);
    }

    public void OnPeerConnected(NetPeer peer)
    {
        var (displayName, characterType) = peer.Tag is (string n, string c)
            ? (n, c)
            : ($"Player_{peer.Id}", Shared.CharacterType.Zink);
        var player = new PlayerInfo(peer.Id, displayName, 400f, 300f, characterType);
        _players[peer.Id] = player;
        _logger.LogInformation("Player {Id} connected ({Total} online)", peer.Id, _players.Count);

        _ = PersistConnectAsync(player);

        // Send world snapshot (includes local player ID as first field)
        var snap = new NetDataWriter();
        snap.Put((byte)PacketType.WorldSnapshot);
        snap.Put(peer.Id);
        snap.Put(_players.Count);
        foreach (var p in _players.Values)
            snap.WritePlayerInfo(p);
        peer.Send(snap, DefaultChannel, DeliveryMethod.ReliableOrdered);

        // Notify all other peers
        var join = new NetDataWriter();
        join.Put((byte)PacketType.PlayerJoined);
        join.WritePlayerInfo(player);
        Broadcast(join, DeliveryMethod.ReliableOrdered, except: peer.Id);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (!_players.Remove(peer.Id, out var finalState)) return;
        _logger.LogInformation("Player {Id} disconnected ({Total} online)", peer.Id, _players.Count);

        _ = PersistDisconnectAsync(peer.Id, finalState);

        var left = new NetDataWriter();
        left.Put((byte)PacketType.PlayerLeft);
        left.Put(peer.Id);
        Broadcast(left, DeliveryMethod.ReliableOrdered);
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
    {
        var type = (PacketType)reader.GetByte();
        switch (type)
        {
            case PacketType.Move:
                HandleMove(peer, reader);
                break;

            case PacketType.SetName:
                HandleSetName(peer, reader);
                break;
        }
    }

    private void HandleMove(NetPeer peer, NetPacketReader reader)
    {
        if (!_players.TryGetValue(peer.Id, out var cur)) return;

        float x = reader.GetFloat();
        float y = reader.GetFloat();
        _players[peer.Id] = cur with { X = x, Y = y };

        var w = new NetDataWriter();
        w.Put((byte)PacketType.PlayerMoved);
        w.Put(peer.Id);
        w.Put(x);
        w.Put(y);
        // Unreliable: dropping an occasional position update is fine
        Broadcast(w, DeliveryMethod.Unreliable, except: peer.Id);
    }

    private void HandleSetName(NetPeer peer, NetPacketReader reader)
    {
        if (!_players.TryGetValue(peer.Id, out var cur)) return;

        var name = reader.GetString(64)?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var updated = cur with { Name = name };
        _players[peer.Id] = updated;

        var w = new NetDataWriter();
        w.Put((byte)PacketType.PlayerJoined); // reuse to broadcast updated info
        w.WritePlayerInfo(updated);
        Broadcast(w, DeliveryMethod.ReliableOrdered);
    }

    private void Broadcast(NetDataWriter writer, DeliveryMethod delivery, int except = -1)
    {
        _peerBuffer.Clear();
        _net.GetConnectedPeers(_peerBuffer);
        foreach (var peer in _peerBuffer)
            if (peer.Id != except)
                peer.Send(writer, DefaultChannel, delivery);
    }

    // ── Persistence (fire-and-forget, non-critical path) ─────────────────────

    private async Task PersistConnectAsync(PlayerInfo player)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Players.Add(new Player
            {
                Id       = player.Id,
                Name     = player.Name,
                X        = player.X,
                Y        = player.Y,
                LastSeen = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist player {Id} on connect", player.Id);
        }
    }

    private async Task PersistDisconnectAsync(int playerId, PlayerInfo finalState)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Players.FindAsync(playerId);
            if (entity is null) return;
            entity.X        = finalState.X;
            entity.Y        = finalState.Y;
            entity.LastSeen = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist player {Id} on disconnect", playerId);
        }
    }

    // ── Unused INetEventListener members ──────────────────────────────────────

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) =>
        _logger.LogError("Network error from {EndPoint}: {Error}", endPoint, socketError);

    public void OnNetworkReceiveUnconnected(
        IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
    public void OnMessageDelivered(NetPeer peer, object userData) { }
    public void OnNtpResponse(LiteNetLib.Utils.NtpPacket packet) { }
    public void OnPeerAddressChanged(NetPeer peer, IPEndPoint previousAddress) { }
}
