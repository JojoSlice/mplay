using LiteNetLib;
using LiteNetLib.Utils;
using Multiplay.Server.Infrastructure.GameState;
using Multiplay.Server.Infrastructure.Network;
using Multiplay.Shared;

namespace Multiplay.Server;

/// <summary>
/// Pure game logic: manages player and enemy state, emits network packets.
/// No sockets, no DB, no ASP.NET — fully testable in isolation.
/// </summary>
public sealed class GameLogic
{
    private static readonly (float X, float Y)[] SpawnPoints =
        [(150f, -16f), (400f, -16f), (650f, -16f)];

    private const float EnemySpeed = 60f;
    private const float ResetY     = -16f;
    private const float MapHeight  = 600f;

    private readonly IGameState      _state;
    private readonly IGameBroadcaster _broadcaster;
    private readonly EnemyInfo[]     _enemies;

    public GameLogic(IGameState state, IGameBroadcaster broadcaster)
    {
        _state       = state;
        _broadcaster = broadcaster;

        _enemies = new EnemyInfo[SpawnPoints.Length];
        for (int i = 0; i < SpawnPoints.Length; i++)
            _enemies[i] = new EnemyInfo(i + 1, EnemyType.Slime, SpawnPoints[i].X, SpawnPoints[i].Y);
    }

    public void OnPlayerConnected(int peerId, string displayName, string characterType)
    {
        var player = new PlayerInfo(peerId, displayName, 400f, 300f, characterType);
        _state.Add(player);

        // Send the full world snapshot to the joining player
        var snap = new NetDataWriter();
        snap.Put((byte)PacketType.WorldSnapshot);
        snap.Put(peerId);
        var all = _state.All;
        snap.Put(all.Count);
        foreach (var p in all)
            snap.WritePlayerInfo(p);
        _broadcaster.SendTo(peerId, snap, DeliveryMethod.ReliableOrdered);

        // Send current enemy positions to the joining player
        SendEnemySnapshotTo(peerId);

        // Announce the new player to everyone else
        var join = new NetDataWriter();
        join.Put((byte)PacketType.PlayerJoined);
        join.WritePlayerInfo(player);
        _broadcaster.Broadcast(join, DeliveryMethod.ReliableOrdered, except: peerId);
    }

    /// <returns>The player's final state, or null if the player was unknown.</returns>
    public PlayerInfo? OnPlayerDisconnected(int peerId)
    {
        if (!_state.Remove(peerId, out var final)) return null;

        var left = new NetDataWriter();
        left.Put((byte)PacketType.PlayerLeft);
        left.Put(peerId);
        _broadcaster.Broadcast(left, DeliveryMethod.ReliableOrdered);

        return final;
    }

    public void OnMove(int peerId, float x, float y)
    {
        if (!_state.TryMove(peerId, x, y)) return;

        var w = new NetDataWriter();
        w.Put((byte)PacketType.PlayerMoved);
        w.Put(peerId);
        w.Put(x);
        w.Put(y);
        _broadcaster.Broadcast(w, DeliveryMethod.Unreliable, except: peerId);
    }

    public void TickEnemies(float dt)
    {
        for (int i = 0; i < _enemies.Length; i++)
        {
            float newY = _enemies[i].Y + EnemySpeed * dt;
            if (newY > MapHeight + 16f)
                newY = ResetY;

            _enemies[i] = _enemies[i] with { Y = newY };

            var w = new NetDataWriter();
            w.Put((byte)PacketType.EnemyMoved);
            w.WriteEnemyInfo(_enemies[i]);
            _broadcaster.Broadcast(w, DeliveryMethod.Unreliable);
        }
    }

    private void SendEnemySnapshotTo(int peerId)
    {
        var w = new NetDataWriter();
        w.Put((byte)PacketType.EnemySnapshot);
        w.Put(_enemies.Length);
        foreach (var e in _enemies)
            w.WriteEnemyInfo(e);
        _broadcaster.SendTo(peerId, w, DeliveryMethod.ReliableOrdered);
    }
}
