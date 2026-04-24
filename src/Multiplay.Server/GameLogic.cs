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
    private static readonly (float X, float Y, float DirX)[] SpawnPoints =
        [(400f, 340f, 1f)];

    private const float EnemyMaxSpeed    = 70f;
    private const float EnemyMinSpeed    = 10f;
    private const float MapMinX          = 14f;
    private const float MapMaxX          = 786f;
    private const float HopCycle         = 7 * 0.18f; // matches 7-frame animation at 0.18 s/frame

    private readonly IGameState       _state;
    private readonly IGameBroadcaster _broadcaster;
    private readonly EnemyInfo[]      _enemies;
    private readonly float[]          _enemyDirX;
    private readonly float[]          _enemyHopTime; // elapsed seconds within current hop cycle

    public GameLogic(IGameState state, IGameBroadcaster broadcaster)
    {
        _state       = state;
        _broadcaster = broadcaster;

        _enemies      = new EnemyInfo[SpawnPoints.Length];
        _enemyDirX    = new float[SpawnPoints.Length];
        _enemyHopTime = new float[SpawnPoints.Length];
        for (int i = 0; i < SpawnPoints.Length; i++)
        {
            _enemies[i]   = new EnemyInfo(i + 1, EnemyType.Slime, SpawnPoints[i].X, SpawnPoints[i].Y);
            _enemyDirX[i] = SpawnPoints[i].DirX;
        }
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
        if (!_state.TryGet(peerId, out var self)) return;
        float pr = ColliderRadius.ForCharacter(self.CharacterType);

        // Resolve against enemies
        foreach (var e in _enemies)
        {
            float er = ColliderRadius.ForEnemy(e.Type);
            if (!Collision.Overlaps(x, y, pr, e.X, e.Y, er)) continue;
            var (sepX, sepY) = Collision.Resolve(x, y, pr, e.X, e.Y, er);
            x += sepX;
            y += sepY;
        }

        // Resolve against other players
        foreach (var other in _state.All)
        {
            if (other.Id == peerId) continue;
            float or = ColliderRadius.ForCharacter(other.CharacterType);
            if (!Collision.Overlaps(x, y, pr, other.X, other.Y, or)) continue;
            var (sepX, sepY) = Collision.Resolve(x, y, pr, other.X, other.Y, or);
            x += sepX;
            y += sepY;
        }

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
            _enemyHopTime[i] = (_enemyHopTime[i] + dt) % HopCycle;
            float speed = _enemyHopTime[i] < HopCycle / 2f ? EnemyMinSpeed : EnemyMaxSpeed;

            float newX = _enemies[i].X + speed * _enemyDirX[i] * dt;
            if (newX <= MapMinX || newX >= MapMaxX)
            {
                _enemyDirX[i] = -_enemyDirX[i];
                newX = Math.Clamp(newX, MapMinX, MapMaxX);
            }

            _enemies[i] = _enemies[i] with { X = newX };

            // Push any overlapping players out of the enemy's new position
            var e  = _enemies[i];
            float er = ColliderRadius.ForEnemy(e.Type);
            foreach (var player in _state.All)
            {
                float pr = ColliderRadius.ForCharacter(player.CharacterType);
                if (!Collision.Overlaps(player.X, player.Y, pr, e.X, e.Y, er)) continue;
                var (sepX, sepY) = Collision.Resolve(player.X, player.Y, pr, e.X, e.Y, er);
                float nx = player.X + sepX;
                float ny = player.Y + sepY;
                if (!_state.TryMove(player.Id, nx, ny)) continue;

                var pw = new NetDataWriter();
                pw.Put((byte)PacketType.PlayerMoved);
                pw.Put(player.Id);
                pw.Put(nx);
                pw.Put(ny);
                _broadcaster.Broadcast(pw, DeliveryMethod.Unreliable);
            }

            // Broadcast enemy position
            var ew = new NetDataWriter();
            ew.Put((byte)PacketType.EnemyMoved);
            ew.WriteEnemyInfo(e);
            _broadcaster.Broadcast(ew, DeliveryMethod.Unreliable);
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
