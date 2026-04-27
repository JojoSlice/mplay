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
    private const float MapMinY          = 14f;
    private const float MapMaxY          = 586f;
    private const float HopCycle         = 7 * 0.18f; // matches 7-frame animation at 0.18 s/frame

    private const float AttackPushback    = 80f;
    private const float HitCooldown       = 1.0f;  // seconds before the same enemy can hit the same player again
    private const float AttackOffset      = 28f;   // how far in front of the player the hitbox center sits
    private const float AttackRadius      = 20f;   // hitbox radius
    private const float EnemyKnockback    = 80f;   // push distance on successful player attack
    private const float EnemyRespawnTime  = 10f;   // seconds until a dead enemy respawns

    private readonly IGameState       _state;
    private readonly IGameBroadcaster _broadcaster;
    private readonly EnemyInfo[]      _enemies;
    private readonly float[]          _enemyDirX;
    private readonly float[]          _enemyHopTime; // elapsed seconds within current hop cycle
    private readonly EnemyStats[]     _enemyStats;
    private readonly float[]          _enemyRespawnTimers; // 0 = alive, >0 = seconds until respawn
    private readonly Dictionary<(int enemyId, int playerId), float> _playerHitCooldowns = [];
    private readonly Dictionary<int, PlayerStats> _playerStats = [];

    public GameLogic(IGameState state, IGameBroadcaster broadcaster)
    {
        _state       = state;
        _broadcaster = broadcaster;

        _enemies            = new EnemyInfo[SpawnPoints.Length];
        _enemyDirX          = new float[SpawnPoints.Length];
        _enemyHopTime       = new float[SpawnPoints.Length];
        _enemyStats         = new EnemyStats[SpawnPoints.Length];
        _enemyRespawnTimers = new float[SpawnPoints.Length];
        for (int i = 0; i < SpawnPoints.Length; i++)
        {
            _enemies[i]      = new EnemyInfo(i + 1, EnemyType.Slime, SpawnPoints[i].X, SpawnPoints[i].Y);
            _enemyDirX[i]    = SpawnPoints[i].DirX;
            _enemyStats[i]   = DefaultStats.ForEnemy(EnemyType.Slime);
        }
    }

    public void OnPlayerConnected(int peerId, string displayName, string characterType)
    {
        var player = new PlayerInfo(peerId, displayName, 400f, 300f, characterType);
        _state.Add(player);

        var stats = DefaultStats.ForCharacter(characterType);
        _playerStats[peerId] = stats;

        // Send the full world snapshot to the joining player
        var snap = new NetDataWriter();
        snap.Put((byte)PacketType.WorldSnapshot);
        snap.Put(peerId);
        var all = _state.All;
        snap.Put(all.Count);
        foreach (var p in all)
            snap.WritePlayerInfo(p);
        _broadcaster.SendTo(peerId, snap, DeliveryMethod.ReliableOrdered);

        // Send current enemy positions (with health) to the joining player
        SendEnemySnapshotTo(peerId);

        // Send the player's own stats
        var sw = new NetDataWriter();
        sw.Put((byte)PacketType.PlayerStats);
        sw.WritePlayerStats(stats);
        _broadcaster.SendTo(peerId, sw, DeliveryMethod.ReliableOrdered);

        // Announce the new player to everyone else
        var join = new NetDataWriter();
        join.Put((byte)PacketType.PlayerJoined);
        join.WritePlayerInfo(player);
        _broadcaster.Broadcast(join, DeliveryMethod.ReliableOrdered, except: peerId);
    }

    /// <returns>The player's final state, or null if the player was unknown.</returns>
    public PlayerInfo? OnPlayerDisconnected(int peerId)
    {
        _playerStats.Remove(peerId);
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

    public void OnAttack(int peerId, float dirX, float dirY)
    {
        if (!_state.TryGet(peerId, out var attacker)) return;
        if (!_playerStats.TryGetValue(peerId, out var atkStats)) return;

        // Normalize direction (guard against zero vector)
        float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (len < 0.0001f) return;
        dirX /= len; dirY /= len;

        float cx = attacker.X + dirX * AttackOffset;
        float cy = attacker.Y + dirY * AttackOffset;

        for (int i = 0; i < _enemies.Length; i++)
        {
            if (_enemyRespawnTimers[i] > 0f) continue; // dead enemy

            var e = _enemies[i];
            if (!Collision.Overlaps(cx, cy, AttackRadius, e.X, e.Y, ColliderRadius.ForEnemy(e.Type))) continue;

            // Push enemy away from the attack center
            float dx = e.X - cx, dy = e.Y - cy;
            float dl = MathF.Sqrt(dx * dx + dy * dy);
            if (dl < 0.0001f) { dx = 1f; dy = 0f; dl = 1f; }
            float nx = Math.Clamp(e.X + dx / dl * EnemyKnockback, MapMinX, MapMaxX);
            float ny = Math.Clamp(e.Y + dy / dl * EnemyKnockback, MapMinY, MapMaxY);
            _enemies[i] = e with { X = nx, Y = ny };

            // Apply damage
            int dmg       = Math.Max(1, atkStats.Attack - _enemyStats[i].Defence);
            int newHealth = Math.Max(0, _enemyStats[i].Health - dmg);
            _enemyStats[i] = _enemyStats[i] with { Health = newHealth };
            if (newHealth <= 0)
                _enemyRespawnTimers[i] = EnemyRespawnTime;

            var ew = new NetDataWriter();
            ew.Put((byte)PacketType.EnemyDamaged);
            ew.WriteEnemyInfo(_enemies[i]);
            ew.Put(newHealth);
            _broadcaster.Broadcast(ew, DeliveryMethod.ReliableOrdered);
        }
    }

    public void TickEnemies(float dt)
    {
        // Tick down hit cooldowns
        foreach (var key in _playerHitCooldowns.Keys.ToArray())
        {
            _playerHitCooldowns[key] -= dt;
            if (_playerHitCooldowns[key] <= 0f)
                _playerHitCooldowns.Remove(key);
        }

        for (int i = 0; i < _enemies.Length; i++)
        {
            // Handle dead enemy: tick respawn timer
            if (_enemyRespawnTimers[i] > 0f)
            {
                _enemyRespawnTimers[i] -= dt;
                if (_enemyRespawnTimers[i] <= 0f)
                {
                    _enemyRespawnTimers[i] = 0f;
                    _enemies[i]   = new EnemyInfo(i + 1, EnemyType.Slime, SpawnPoints[i].X, SpawnPoints[i].Y);
                    _enemyDirX[i] = SpawnPoints[i].DirX;
                    _enemyHopTime[i] = 0f;
                    _enemyStats[i]   = DefaultStats.ForEnemy(EnemyType.Slime);

                    // Notify clients of respawn (full health at spawn position)
                    var respawnPkt = new NetDataWriter();
                    respawnPkt.Put((byte)PacketType.EnemyDamaged);
                    respawnPkt.WriteEnemyInfo(_enemies[i]);
                    respawnPkt.Put(_enemyStats[i].Health);
                    _broadcaster.Broadcast(respawnPkt, DeliveryMethod.ReliableOrdered);
                }
                continue;
            }

            _enemyHopTime[i] = (_enemyHopTime[i] + dt) % HopCycle;
            float speed = _enemyHopTime[i] < HopCycle / 2f ? EnemyMinSpeed : EnemyMaxSpeed;
            bool  isAttacking = _enemyHopTime[i] < HopCycle / 2f;

            float newX = _enemies[i].X + speed * _enemyDirX[i] * dt;
            if (newX <= MapMinX || newX >= MapMaxX)
            {
                _enemyDirX[i] = -_enemyDirX[i];
                newX = Math.Clamp(newX, MapMinX, MapMaxX);
            }

            _enemies[i] = _enemies[i] with { X = newX };

            var e    = _enemies[i];
            float er = ColliderRadius.ForEnemy(e.Type);

            foreach (var player in _state.All)
            {
                float pr = ColliderRadius.ForCharacter(player.CharacterType);
                if (!Collision.Overlaps(player.X, player.Y, pr, e.X, e.Y, er)) continue;

                var cooldownKey = (e.Id, player.Id);
                if (isAttacking && !_playerHitCooldowns.ContainsKey(cooldownKey))
                {
                    // Attack hit: large pushback away from enemy centre
                    float dx = player.X - e.X;
                    float dy = player.Y - e.Y;
                    float plen = MathF.Sqrt(dx * dx + dy * dy);
                    if (plen < 0.0001f) { dx = 1f; dy = 0f; plen = 1f; }
                    float nx = player.X + dx / plen * AttackPushback;
                    float ny = player.Y + dy / plen * AttackPushback;
                    nx = Math.Clamp(nx, 0, 800);
                    ny = Math.Clamp(ny, 0, 600);

                    // Apply damage
                    int newHealth = 0;
                    if (_playerStats.TryGetValue(player.Id, out var defStats))
                    {
                        int dmg = Math.Max(1, _enemyStats[i].Attack - defStats.Defence);
                        newHealth = Math.Max(0, defStats.Health - dmg);
                        _playerStats[player.Id] = defStats with { Health = newHealth };
                    }

                    if (_state.TryMove(player.Id, nx, ny))
                    {
                        _playerHitCooldowns[cooldownKey] = HitCooldown;

                        var pw = new NetDataWriter();
                        pw.Put((byte)PacketType.PlayerDamaged);
                        pw.Put(player.Id);
                        pw.Put(nx);
                        pw.Put(ny);
                        pw.Put(newHealth);
                        _broadcaster.Broadcast(pw, DeliveryMethod.ReliableOrdered);
                    }
                }
                else if (!isAttacking)
                {
                    // Passive contact: normal separation push
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
        for (int i = 0; i < _enemies.Length; i++)
        {
            w.WriteEnemyInfo(_enemies[i]);
            w.Put(_enemyStats[i].Health);
        }
        _broadcaster.SendTo(peerId, w, DeliveryMethod.ReliableOrdered);
    }
}
