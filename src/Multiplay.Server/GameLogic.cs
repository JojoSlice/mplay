using LiteNetLib;
using LiteNetLib.Utils;
using Multiplay.Server.Domain;
using Multiplay.Server.Infrastructure.GameState;
using Multiplay.Server.Infrastructure.Network;
using Multiplay.Server.Services;
using Multiplay.Shared;

namespace Multiplay.Server;

/// <summary>
/// Orchestrates game state: delegates AI to <see cref="IEnemyAI"/>, combat math
/// to <see cref="ICombatService"/>, and player/enemy persistence to
/// <see cref="IGameState"/> and <see cref="IGameBroadcaster"/>.
/// No sockets, no DB, no ASP.NET — fully testable in isolation.
/// </summary>
public sealed class GameLogic
{
    // 6 enemy spawn locations from map1.tmx, 5 slimes each = 30 enemies.
    private static readonly (float X, float Y, float DirX)[] SpawnPoints = BuildSpawnPoints();

    private static (float X, float Y, float DirX)[] BuildSpawnPoints()
    {
        (float cx, float cy)[] centers =
        [
            (761.333f, 1448f),
            (760f,     857.333f),
            (760.667f, 217.333f),
            (36.667f,  186f),
            (34.667f,  443.333f),
            (37.333f,  1164.67f),
        ];
        (float dx, float dy)[] offsets = [(0, 0), (20, 0), (-20, 0), (0, 20), (0, -20)];

        var result = new List<(float, float, float)>();
        foreach (var (cx, cy) in centers)
            foreach (var (odx, ody) in offsets)
                result.Add((cx + odx, cy + ody, result.Count % 2 == 0 ? 1f : -1f));
        return [.. result];
    }

    internal const float PlayerSpawnX = 552.5f;
    internal const float PlayerSpawnY = 656.5f;

    private const float EnemyMaxSpeed     = 35f;
    private const float EnemyMinSpeed     = 5f;
    private const float MapMinX           = 14f;
    private const float MapMaxX           = 786f;
    private const float MapMinY           = 14f;
    private const float MapMaxY           = 2400f;
    private const float HopCycle          = 7 * 0.18f;
    private const float AttackPushback    = 80f;
    private const float HitCooldown       = 1.0f;
    private const float AttackOffset      = 28f;
    private const float AttackRadius      = 20f;
    private const float EnemyKnockback    = 80f;
    private const float EnemyRespawnTime  = 30f;
    private const int   SlimeXp           = 10;
    private const float DetectionRadius   = 200f;
    private const float ChaseRadius       = 300f;
    private const float WanderDirInterval = 2f;

    private readonly IGameState       _state;
    private readonly IGameBroadcaster _broadcaster;
    private readonly ICombatService   _combat;
    private readonly IEnemyAI         _ai;

    private readonly Enemy[] _enemies;

    private readonly Dictionary<(int enemyId, int playerId), float> _playerHitCooldowns = [];
    private readonly Dictionary<int, PlayerStats>                   _playerStats = [];
    private readonly HashSet<int>                                   _map1Players = [];

    public GameLogic(IGameState state, IGameBroadcaster broadcaster,
                     ICombatService combat, IEnemyAI ai)
    {
        _state       = state;
        _broadcaster = broadcaster;
        _combat      = combat;
        _ai          = ai;

        _enemies = new Enemy[SpawnPoints.Length];
        for (int i = 0; i < SpawnPoints.Length; i++)
            _enemies[i] = SpawnEnemy(i);
    }

    // ── Player lifecycle ───────────────────────────────────────────────────────

    public void OnPlayerConnected(int peerId, string displayName, string characterType)
    {
        var player = new PlayerInfo(peerId, displayName, PlayerSpawnX, PlayerSpawnY, characterType);
        _state.Add(player);

        var stats = DefaultStats.ForCharacter(characterType);
        _playerStats[peerId] = stats;

        var snap = new NetDataWriter();
        snap.Put((byte)PacketType.WorldSnapshot);
        snap.Put(peerId);
        var all = _state.All;
        snap.Put(all.Count);
        foreach (var p in all)
            snap.WritePlayerInfo(p);
        _broadcaster.SendTo(peerId, snap, DeliveryMethod.ReliableOrdered);

        SendEnemySnapshotTo(peerId);

        var sw = new NetDataWriter();
        sw.Put((byte)PacketType.PlayerStats);
        sw.WritePlayerStats(stats);
        _broadcaster.SendTo(peerId, sw, DeliveryMethod.ReliableOrdered);

        var join = new NetDataWriter();
        join.Put((byte)PacketType.PlayerJoined);
        join.WritePlayerInfo(player);
        _broadcaster.Broadcast(join, DeliveryMethod.ReliableOrdered, except: peerId);
    }

    public void OnZoneChanged(int peerId, string zone)
    {
        if (zone == Zone.Map1) _map1Players.Add(peerId);
        else                   _map1Players.Remove(peerId);
    }

    /// <returns>The player's final state, or null if the player was unknown.</returns>
    public PlayerInfo? OnPlayerDisconnected(int peerId)
    {
        _playerStats.Remove(peerId);
        _map1Players.Remove(peerId);
        if (!_state.Remove(peerId, out var final)) return null;

        var left = new NetDataWriter();
        left.Put((byte)PacketType.PlayerLeft);
        left.Put(peerId);
        _broadcaster.Broadcast(left, DeliveryMethod.ReliableOrdered);

        return final;
    }

    // ── Player input ───────────────────────────────────────────────────────────

    public void OnMove(int peerId, float x, float y)
    {
        if (!_state.TryGet(peerId, out var self)) return;
        float pr     = ColliderRadius.ForCharacter(self.CharacterType);
        bool  isMap1 = _map1Players.Contains(peerId);

        if (isMap1)
        {
            foreach (var e in _enemies)
            {
                if (e.IsDead) continue;
                float er = ColliderRadius.ForEnemy(e.Info.Type);
                if (!Collision.Overlaps(x, y, pr, e.Info.X, e.Info.Y, er)) continue;
                var (sepX, sepY) = Collision.Resolve(x, y, pr, e.Info.X, e.Info.Y, er);
                x += sepX;
                y += sepY;
            }
        }

        foreach (var other in _state.All)
        {
            if (other.Id == peerId) continue;
            float or = ColliderRadius.ForCharacter(other.CharacterType);
            if (!Collision.Overlaps(x, y, pr, other.X, other.Y, or)) continue;
            var (sepX, sepY) = Collision.Resolve(x, y, pr, other.X, other.Y, or);
            x += sepX;
            y += sepY;
        }

        if (isMap1)
            (x, y) = Map1Colliders.Resolve(x, y, pr);

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
        if (!_map1Players.Contains(peerId)) return;
        if (!_state.TryGet(peerId, out var attacker)) return;
        if (!_playerStats.TryGetValue(peerId, out var atkStats)) return;

        float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (len < 0.0001f) return;
        dirX /= len; dirY /= len;

        float cx = attacker.X + dirX * AttackOffset;
        float cy = attacker.Y + dirY * AttackOffset;

        bool hitAny = false;
        for (int i = 0; i < _enemies.Length; i++)
        {
            var e = _enemies[i];
            if (e.IsDead) continue;
            if (!Collision.Overlaps(cx, cy, AttackRadius, e.Info.X, e.Info.Y,
                    ColliderRadius.ForEnemy(e.Info.Type))) continue;

            hitAny = true;

            var (nx, ny) = _combat.Knockback(cx, cy, e.Info.X, e.Info.Y,
                EnemyKnockback, MapMinX, MapMaxX, MapMinY, MapMaxY);
            (nx, ny) = Map1Colliders.Resolve(nx, ny, ColliderRadius.ForEnemy(e.Info.Type));
            _enemies[i].Info = e.Info with { X = nx, Y = ny };

            int dmg       = _combat.CalculateDamage(atkStats.Attack, e.Stats.Defence);
            int newHealth  = Math.Max(0, e.Stats.Health - dmg);
            _enemies[i].Stats = e.Stats with { Health = newHealth };
            if (newHealth <= 0)
            {
                _enemies[i].RespawnTimer = EnemyRespawnTime;
                var updated = XpSystem.AwardXp(atkStats, SlimeXp);
                _playerStats[peerId] = updated;
                atkStats = updated;
                var sw = new NetDataWriter();
                sw.Put((byte)PacketType.PlayerStats);
                sw.WritePlayerStats(updated);
                _broadcaster.SendTo(peerId, sw, DeliveryMethod.ReliableOrdered);
            }

            var ew = new NetDataWriter();
            ew.Put((byte)PacketType.EnemyDamaged);
            ew.WriteEnemyInfo(_enemies[i].Info);
            ew.Put(newHealth);
            _broadcaster.Broadcast(ew, DeliveryMethod.ReliableOrdered);
        }

        if (!hitAny)
        {
            var mw = new NetDataWriter();
            mw.Put((byte)PacketType.AttackMissed);
            _broadcaster.SendTo(peerId, mw, DeliveryMethod.ReliableOrdered);
        }
    }

    // ── Game loop ──────────────────────────────────────────────────────────────

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
            var enemy = _enemies[i];

            // ── Respawn timer ────────────────────────────────────────────────
            if (enemy.IsDead)
            {
                enemy.RespawnTimer -= dt;
                if (enemy.RespawnTimer <= 0f)
                {
                    _enemies[i] = SpawnEnemy(i);
                    var respawnPkt = new NetDataWriter();
                    respawnPkt.Put((byte)PacketType.EnemyDamaged);
                    respawnPkt.WriteEnemyInfo(_enemies[i].Info);
                    respawnPkt.Put(_enemies[i].Stats.Health);
                    _broadcaster.Broadcast(respawnPkt, DeliveryMethod.ReliableOrdered);
                }
                continue;
            }

            // ── Find nearest map1 player ─────────────────────────────────────
            PlayerInfo? nearestPlayer = null;
            float       nearestDist   = float.MaxValue;
            foreach (var p in _state.All)
            {
                if (!_map1Players.Contains(p.Id)) continue;
                float d = MathF.Sqrt(
                    (p.X - enemy.Info.X) * (p.X - enemy.Info.X) +
                    (p.Y - enemy.Info.Y) * (p.Y - enemy.Info.Y));
                if (d < nearestDist) { nearestDist = d; nearestPlayer = p; }
            }

            enemy.AIState = _ai.NextState(enemy.AIState, nearestDist, DetectionRadius, ChaseRadius);

            // ── Movement (AI) ────────────────────────────────────────────────
            bool isAttacking = enemy.AIState == EnemyAIState.Wander
                ? _ai.ApplyWander(enemy, dt, MapMinX, MapMaxX, EnemyMinSpeed, EnemyMaxSpeed, HopCycle, WanderDirInterval)
                : _ai.ApplyChase(enemy, nearestPlayer, dt, EnemyMaxSpeed, MapMinX, MapMaxX, MapMinY, MapMaxY);

            // ── Map colliders ────────────────────────────────────────────────
            (float mx, float my) = Map1Colliders.Resolve(enemy.Info.X, enemy.Info.Y,
                ColliderRadius.ForEnemy(enemy.Info.Type));
            if (mx != enemy.Info.X || my != enemy.Info.Y)
                enemy.Info = enemy.Info with { X = mx, Y = my };

            float er = ColliderRadius.ForEnemy(enemy.Info.Type);

            // ── Player collisions (map1 only) ────────────────────────────────
            foreach (var player in _state.All)
            {
                if (!_map1Players.Contains(player.Id)) continue;
                float pr = ColliderRadius.ForCharacter(player.CharacterType);
                if (!Collision.Overlaps(player.X, player.Y, pr, enemy.Info.X, enemy.Info.Y, er)) continue;

                var cooldownKey = (enemy.Info.Id, player.Id);
                if (isAttacking && !_playerHitCooldowns.ContainsKey(cooldownKey))
                {
                    var (nx, ny) = _combat.Knockback(
                        enemy.Info.X, enemy.Info.Y, player.X, player.Y,
                        AttackPushback, MapMinX, MapMaxX, MapMinY, MapMaxY);
                    (nx, ny) = Map1Colliders.Resolve(nx, ny, pr);

                    int newHealth = 0;
                    if (_playerStats.TryGetValue(player.Id, out var defStats))
                    {
                        int dmg = _combat.CalculateDamage(enemy.Stats.Attack, defStats.Defence);
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

                    if (newHealth <= 0)
                    {
                        _map1Players.Remove(player.Id);
                        var resetStats = DefaultStats.ForCharacter(player.CharacterType);
                        _playerStats[player.Id] = resetStats;
                        _state.TryMove(player.Id, PlayerSpawnX, PlayerSpawnY);

                        var rw = new NetDataWriter();
                        rw.Put((byte)PacketType.PlayerRespawned);
                        _broadcaster.SendTo(player.Id, rw, DeliveryMethod.ReliableOrdered);

                        var sw = new NetDataWriter();
                        sw.Put((byte)PacketType.PlayerStats);
                        sw.WritePlayerStats(resetStats);
                        _broadcaster.SendTo(player.Id, sw, DeliveryMethod.ReliableOrdered);

                        var mw = new NetDataWriter();
                        mw.Put((byte)PacketType.PlayerMoved);
                        mw.Put(player.Id);
                        mw.Put(PlayerSpawnX);
                        mw.Put(PlayerSpawnY);
                        _broadcaster.Broadcast(mw, DeliveryMethod.ReliableOrdered, except: player.Id);
                    }
                }
                else if (!isAttacking)
                {
                    var (sepX, sepY) = Collision.Resolve(player.X, player.Y, pr, enemy.Info.X, enemy.Info.Y, er);
                    float nx = player.X + sepX;
                    float ny = player.Y + sepY;
                    (nx, ny) = Map1Colliders.Resolve(nx, ny, pr);
                    if (!_state.TryMove(player.Id, nx, ny)) continue;
                    var pw = new NetDataWriter();
                    pw.Put((byte)PacketType.PlayerMoved);
                    pw.Put(player.Id);
                    pw.Put(nx);
                    pw.Put(ny);
                    _broadcaster.Broadcast(pw, DeliveryMethod.Unreliable);
                }
            }

            var ew = new NetDataWriter();
            ew.Put((byte)PacketType.EnemyMoved);
            ew.WriteEnemyInfo(enemy.Info);
            _broadcaster.Broadcast(ew, DeliveryMethod.Unreliable);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Enemy SpawnEnemy(int i) => new()
    {
        Index        = i,
        Info         = new EnemyInfo(i + 1, EnemyType.Slime, SpawnPoints[i].X, SpawnPoints[i].Y),
        Stats        = DefaultStats.ForEnemy(EnemyType.Slime),
        DirX         = SpawnPoints[i].DirX,
        WanderTimer  = WanderDirInterval * ((i % 5) + 1f) / 5f,
        AIState      = EnemyAIState.Wander,
    };

    private void SendEnemySnapshotTo(int peerId)
    {
        var w = new NetDataWriter();
        w.Put((byte)PacketType.EnemySnapshot);
        w.Put(_enemies.Length);
        foreach (var e in _enemies)
        {
            w.WriteEnemyInfo(e.Info);
            w.Put(e.Stats.Health);
        }
        _broadcaster.SendTo(peerId, w, DeliveryMethod.ReliableOrdered);
    }
}
