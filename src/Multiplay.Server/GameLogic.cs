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
    private enum EnemyAIState { Wander, Chase }

    // 6 enemy spawn locations from map1.tmx, 5 slimes each = 30 enemies.
    private static readonly (float X, float Y, float DirX)[] SpawnPoints = BuildSpawnPoints();

    private static (float X, float Y, float DirX)[] BuildSpawnPoints()
    {
        // Center positions of the enemySpawn objects in map1.tmx
        (float cx, float cy)[] centers =
        [
            (761.333f, 1448f),
            (760f,     857.333f),
            (760.667f, 217.333f),
            (36.667f,  186f),
            (34.667f,  443.333f),
            (37.333f,  1164.67f),
        ];
        // 5 slimes spread around each center in a plus pattern
        (float dx, float dy)[] offsets = [(0, 0), (20, 0), (-20, 0), (0, 20), (0, -20)];

        var result = new List<(float, float, float)>();
        foreach (var (cx, cy) in centers)
            foreach (var (odx, ody) in offsets)
                result.Add((cx + odx, cy + ody, result.Count % 2 == 0 ? 1f : -1f));
        return [.. result];
    }

    // Center of the inn startPoint object in hub.tmx (x=513 y=640 w=79 h=33)
    internal const float PlayerSpawnX = 552.5f;
    internal const float PlayerSpawnY = 656.5f;

    private const float EnemyMaxSpeed     = 70f;
    private const float EnemyMinSpeed     = 10f;
    private const float MapMinX           = 14f;
    private const float MapMaxX           = 786f;
    private const float MapMinY           = 14f;
    private const float MapMaxY           = 2400f;  // covers full map1 height
    private const float HopCycle          = 7 * 0.18f; // matches 7-frame animation at 0.18 s/frame

    private const float AttackPushback    = 80f;
    private const float HitCooldown       = 1.0f;  // seconds before the same enemy can hit the same player again
    private const float AttackOffset      = 28f;   // how far in front of the player the hitbox center sits
    private const float AttackRadius      = 20f;   // hitbox radius
    private const float EnemyKnockback    = 80f;   // push distance on successful player attack
    private const float EnemyRespawnTime  = 30f;   // seconds until a dead enemy respawns
    private const float DetectionRadius   = 200f;  // chase starts when player is within this distance
    private const float ChaseRadius       = 300f;  // chase ends when player moves beyond this distance
    private const float WanderDirInterval = 2f;    // seconds between random direction changes while wandering

    private readonly IGameState       _state;
    private readonly IGameBroadcaster _broadcaster;
    private readonly EnemyInfo[]      _enemies;
    private readonly float[]          _enemyDirX;
    private readonly float[]          _enemyHopTime;
    private readonly EnemyStats[]     _enemyStats;
    private readonly float[]          _enemyRespawnTimers;
    private readonly EnemyAIState[]   _enemyAIStates;
    private readonly float[]          _enemyWanderTimers;
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
        _enemyAIStates      = new EnemyAIState[SpawnPoints.Length];
        _enemyWanderTimers  = new float[SpawnPoints.Length];

        for (int i = 0; i < SpawnPoints.Length; i++)
        {
            _enemies[i]           = new EnemyInfo(i + 1, EnemyType.Slime, SpawnPoints[i].X, SpawnPoints[i].Y);
            _enemyDirX[i]         = SpawnPoints[i].DirX;
            _enemyStats[i]        = DefaultStats.ForEnemy(EnemyType.Slime);
            // Stagger wander timers (0.4 s … 2.0 s) so enemies don't all flip direction at once
            _enemyWanderTimers[i] = WanderDirInterval * ((i % 5) + 1f) / 5f;
        }
    }

    public void OnPlayerConnected(int peerId, string displayName, string characterType)
    {
        var player = new PlayerInfo(peerId, displayName, PlayerSpawnX, PlayerSpawnY, characterType);
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
            // ── Respawn timer ───────────────────────────────────────────────────
            if (_enemyRespawnTimers[i] > 0f)
            {
                _enemyRespawnTimers[i] -= dt;
                if (_enemyRespawnTimers[i] <= 0f)
                {
                    _enemyRespawnTimers[i] = 0f;
                    _enemies[i]            = new EnemyInfo(i + 1, EnemyType.Slime, SpawnPoints[i].X, SpawnPoints[i].Y);
                    _enemyDirX[i]          = SpawnPoints[i].DirX;
                    _enemyHopTime[i]       = 0f;
                    _enemyStats[i]         = DefaultStats.ForEnemy(EnemyType.Slime);
                    _enemyAIStates[i]      = EnemyAIState.Wander;
                    _enemyWanderTimers[i]  = WanderDirInterval * ((i % 5) + 1f) / 5f;

                    var respawnPkt = new NetDataWriter();
                    respawnPkt.Put((byte)PacketType.EnemyDamaged);
                    respawnPkt.WriteEnemyInfo(_enemies[i]);
                    respawnPkt.Put(_enemyStats[i].Health);
                    _broadcaster.Broadcast(respawnPkt, DeliveryMethod.ReliableOrdered);
                }
                continue;
            }

            // ── AI: find nearest player ─────────────────────────────────────────
            PlayerInfo? nearestPlayer = null;
            float nearestDist = float.MaxValue;
            foreach (var p in _state.All)
            {
                float pdx   = p.X - _enemies[i].X;
                float pdy   = p.Y - _enemies[i].Y;
                float pdist = MathF.Sqrt(pdx * pdx + pdy * pdy);
                if (pdist < nearestDist) { nearestDist = pdist; nearestPlayer = p; }
            }

            // State transitions
            if (_enemyAIStates[i] == EnemyAIState.Wander && nearestDist < DetectionRadius)
                _enemyAIStates[i] = EnemyAIState.Chase;
            else if (_enemyAIStates[i] == EnemyAIState.Chase && nearestDist > ChaseRadius)
                _enemyAIStates[i] = EnemyAIState.Wander;

            // ── Movement ────────────────────────────────────────────────────────
            _enemyHopTime[i] = (_enemyHopTime[i] + dt) % HopCycle;
            bool isAttacking;

            if (_enemyAIStates[i] == EnemyAIState.Wander)
            {
                isAttacking = _enemyHopTime[i] < HopCycle / 2f;
                float speed = isAttacking ? EnemyMinSpeed : EnemyMaxSpeed;

                // Random direction flip on timer
                _enemyWanderTimers[i] -= dt;
                if (_enemyWanderTimers[i] <= 0f)
                {
                    _enemyDirX[i]         = -_enemyDirX[i];
                    _enemyWanderTimers[i] = WanderDirInterval;
                }

                float newX = _enemies[i].X + speed * _enemyDirX[i] * dt;
                if (newX <= MapMinX || newX >= MapMaxX)
                {
                    _enemyDirX[i] = -_enemyDirX[i];
                    newX = Math.Clamp(newX, MapMinX, MapMaxX);
                }
                _enemies[i] = _enemies[i] with { X = newX };
            }
            else // Chase: move toward the nearest player in 2D at full speed
            {
                isAttacking = true; // always ready to deal damage when chasing

                if (nearestPlayer.HasValue)
                {
                    float cdx = nearestPlayer.Value.X - _enemies[i].X;
                    float cdy = nearestPlayer.Value.Y - _enemies[i].Y;
                    float cdl = MathF.Sqrt(cdx * cdx + cdy * cdy);
                    if (cdl > 0.001f)
                    {
                        cdx /= cdl;
                        cdy /= cdl;
                        if (MathF.Abs(cdx) > 0.001f)
                            _enemyDirX[i] = MathF.Sign(cdx);
                        float newX = Math.Clamp(_enemies[i].X + cdx * EnemyMaxSpeed * dt, MapMinX, MapMaxX);
                        float newY = Math.Clamp(_enemies[i].Y + cdy * EnemyMaxSpeed * dt, MapMinY, MapMaxY);
                        _enemies[i] = _enemies[i] with { X = newX, Y = newY };
                    }
                }
            }

            var e    = _enemies[i];
            float er = ColliderRadius.ForEnemy(e.Type);

            // ── Player collisions ───────────────────────────────────────────────
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
                    float nx = Math.Clamp(player.X + dx / plen * AttackPushback, MapMinX, MapMaxX);
                    float ny = Math.Clamp(player.Y + dy / plen * AttackPushback, MapMinY, MapMaxY);

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
