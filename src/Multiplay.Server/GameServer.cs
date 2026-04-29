using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using Multiplay.Server.Data;
using Multiplay.Server.Infrastructure.Auth;
using Multiplay.Server.Infrastructure.GameState;
using Multiplay.Server.Infrastructure.Network;
using Multiplay.Server.Models;
using Multiplay.Server.Services;
using Multiplay.Shared;

namespace Multiplay.Server;

/// <summary>
/// Thin UDP host: owns the LiteNetLib socket, delegates all game logic to
/// <see cref="GameLogic"/> and all persistence to fire-and-forget helpers.
/// </summary>
public sealed class GameServer : IHostedService, INetEventListener
{
    private readonly NetManager         _net;
    private readonly GameLogic          _logic;
    private readonly ISessionStore      _sessions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration     _config;
    private readonly ILogger<GameServer> _logger;

    private CancellationTokenSource? _cts;
    private Task?                     _pollTask;

    public GameServer(
        IGameState state,
        ISessionStore sessions,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<GameServer> logger,
        ICombatService combat,
        IEnemyAI ai)
    {
        _sessions     = sessions;
        _scopeFactory = scopeFactory;
        _config       = config;
        _logger       = logger;
        _net          = new NetManager(this) { AutoRecycle = true };
        _logic        = new GameLogic(state, new LiteNetBroadcaster(_net), combat, ai);
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
        var lastTick = DateTime.UtcNow;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                float dt = (float)(now - lastTick).TotalSeconds;
                lastTick = now;

                _net.PollEvents();
                _logic.TickEnemies(dt);

                await Task.Delay(15, ct); // ~66 Hz tick rate
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── INetEventListener ──────────────────────────────────────────────────────

    public void OnConnectionRequest(ConnectionRequest request)
    {
        var token = request.Data.GetString();

        if (!_sessions.TryGet(token, out var info) || info is null)
        {
            _logger.LogWarning("Rejected connection: unknown or expired token");
            request.Reject();
            return;
        }

        var peer = request.Accept();
        peer.Tag = new PeerIdentity(
            info.DisplayName ?? info.Username,
            info.CharacterType ?? CharacterType.Zink);
    }

    public void OnPeerConnected(NetPeer peer)
    {
        var id = peer.Tag as PeerIdentity
            ?? new PeerIdentity($"Player_{peer.Id}", CharacterType.Zink);

        _logic.OnPlayerConnected(peer.Id, id.DisplayName, id.CharacterType);
        _logger.LogInformation("Player {Id} ({Name}) connected", peer.Id, id.DisplayName);

        _ = PersistConnectAsync(peer.Id, id.DisplayName);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        var final = _logic.OnPlayerDisconnected(peer.Id);
        if (final is null) return;
        _logger.LogInformation("Player {Id} disconnected", peer.Id);
        _ = PersistDisconnectAsync(peer.Id, final.Value);
    }

    public void OnNetworkReceive(
        NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
    {
        var type = (PacketType)reader.GetByte();
        switch (type)
        {
            case PacketType.Move:
                _logic.OnMove(peer.Id, reader.GetFloat(), reader.GetFloat());
                break;
            case PacketType.Attack:
                _logic.OnAttack(peer.Id, reader.GetFloat(), reader.GetFloat());
                break;
            case PacketType.ZoneChanged:
                _logic.OnZoneChanged(peer.Id, reader.GetString());
                break;
        }
    }

    // ── Persistence (fire-and-forget, non-critical) ─────────────────────────────

    private async Task PersistConnectAsync(int peerId, string displayName)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Players.Add(new Player
            {
                Id       = peerId,
                Name     = displayName,
                X        = GameLogic.PlayerSpawnX,
                Y        = GameLogic.PlayerSpawnY,
                LastSeen = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist player {Id} on connect", peerId);
        }
    }

    private async Task PersistDisconnectAsync(int peerId, PlayerInfo final)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Players.FindAsync(peerId);
            if (entity is null) return;
            entity.X        = final.X;
            entity.Y        = final.Y;
            entity.LastSeen = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist player {Id} on disconnect", peerId);
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

    private sealed record PeerIdentity(string DisplayName, string CharacterType);
}
