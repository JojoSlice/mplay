using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Multiplay.Client.Graphics;
using Multiplay.Client.Network;
using Multiplay.Client.Services;
using Multiplay.Client.World;
using Multiplay.Shared;

namespace Multiplay.Client.Screens;

public sealed class GameScreen : Screen
{
    private const string ServerHost = "127.0.0.1";
    private const int    ServerPort = 9050;
    private const float  Speed      = 100f;

    private readonly IAuthService    _auth;
    private readonly INetworkManager _network;

    private ContentManager _content    = null!;
    private SpriteBatch    _spriteBatch = null!;
    private SpriteFont     _font        = null!;

    private readonly Dictionary<int, PlayerInfo>        _remotePlayers    = [];
    private readonly Dictionary<int, CharacterAnimator> _remoteAnimators  = [];
    private readonly Dictionary<int, float>             _remoteIdleTimers = [];
    private const float IdleThreshold = 0.15f;
    private const float AttackOffset  = 28f; // must match server
    private Vector2 _localPos = new(400, 300);

    private readonly Dictionary<int, EnemyInfo> _enemies   = [];
    private readonly Dictionary<int, float>     _enemyDirX = []; // +1 right, -1 left

    private bool    _isDashing    = false;
    private float   _dashTimer    = 0f;
    private float   _dashCooldown = 0f;
    private Vector2 _dashDir      = new(0f, 1f);
    private Vector2 _lastMoveDir  = new(0f, 1f);

    private const float DashSpeed          = 300f;
    private const float DashDuration       = 0.18f;
    private const float DashCooldown       = 0.7f;
    private const int   DashStaminaCost    = 30;
    private const float StaminaRegenDelay  = 3f;   // seconds after last use before regen starts
    private const float StaminaRegenRate   = 10f;  // stamina per second

    private float _staminaF;           // float-precision stamina (synced to _localStats.Stamina for display)
    private float _staminaRegenCooldown = 0f;

    private float _localFlashTimer;
    private readonly Dictionary<int, float> _remoteFlashTimers = [];
    private readonly Dictionary<int, float> _enemyFlashTimers  = [];
    private const float FlashDuration = 0.4f;

    private PlayerStats _localStats;
    private readonly Dictionary<int, (int health, int maxHealth)> _remotePlayerHealth = [];
    private readonly Dictionary<int, (int health, int maxHealth)> _enemyHealth        = [];
    private Texture2D _pixel = null!;

    private AnimatedSprite _slimeIdleSprite    = null!;
    private AnimatedSprite _slimeJumpSprite    = null!;
    private AnimatedSprite _bunnyNpcSprite     = null!;

    // Bottom-right corner of the inn startPoint (hub.tmx: x=513,y=640,w=79,h=33)
    private static readonly Vector2 BunnyNpcPos = new(582f, 658f);

    private CharacterAnimator _localAnimator = null!;
    private KeyboardState     _prevKb;

    private TileMapRenderer? _map;
    private Rectangle        _playArea          = new(0, 0, 800, 800);
    private List<Rectangle>  _mapColliders      = [];
    private List<Vector2[]>  _mapPolyColliders  = [];
    private List<(string Name, Rectangle Bounds)> _interactableZones = [];

    private string? _activePrompt;
    private string? _activeTargetZone;
    private string  _currentZone  = Zone.Hub;
    private bool    EnemiesActive => _currentZone == Zone.Map1;

    private static readonly Dictionary<string, string> ZoneToMapFile = new()
    {
        { Zone.Hub,  "hub.tmx"  },
        { Zone.Map1, "map1.tmx" },
    };

    private static readonly Dictionary<string, (string Prompt, string TargetZone)> MapLinks = new()
    {
        { "gate",    ("Leave camp [E]",     Zone.Map1) },
        { "hubArea", ("Return to camp [E]", Zone.Hub)  },
    };

    private Vector2 _camera;
    private int     _viewportWidth;
    private int     _viewportHeight;
    private int     _mapPixelWidth;
    private int     _mapPixelHeight;

    public GameScreen(IAuthService auth) : this(auth, new NetworkManager()) { }

    // Secondary constructor for testing — inject a mock network manager
    public GameScreen(IAuthService auth, INetworkManager network)
    {
        _auth    = auth;
        _network = network;
    }

    public override void LoadContent(ContentManager content, GraphicsDevice gd)
    {
        _content        = content;
        _spriteBatch    = new SpriteBatch(gd);
        _viewportWidth  = gd.Viewport.Width;
        _viewportHeight = gd.Viewport.Height;
        _mapPixelWidth  = _viewportWidth;
        _mapPixelHeight = _viewportHeight;

        _localAnimator = CharacterAnimator.Create(
            _auth.CharacterType ?? Shared.CharacterType.Zink);
        _localAnimator.LoadContent(content);
        _localStats = DefaultStats.ForCharacter(_auth.CharacterType);
        _staminaF   = _localStats.Stamina;

        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _font = content.Load<SpriteFont>("Fonts/Default");

        if (DevFlags.DebugColliders)
            DebugDraw.Initialize(gd);

        // Slime sprites — 16 × 16 px per frame
        _slimeIdleSprite = new AnimatedSprite(
            content.Load<Texture2D>("Sprites/Enemies/sprSlimeIdle"), 16, 16, 8f);
        _slimeJumpSprite = new AnimatedSprite(
            content.Load<Texture2D>("Sprites/Enemies/sprSlimeJump"), 16, 16,
            new float[] { 0.18f, 0.18f, 0.18f, 0.18f, 0.18f, 0.18f, 0.18f });

        _bunnyNpcSprite = new AnimatedSprite(
            content.Load<Texture2D>("Sprites/NPCs/sprBunnyGirlSearch"), 16, 24, fps: 4f);

        var mapPath = Path.Combine(AppContext.BaseDirectory, "Content", "Maps", "hub.tmx");
        _map = new TileMapRenderer(mapPath);
        _map.LoadContent(content);
        ApplyMapData();

        WireNetworkEvents(content);
        _network.Connect(ServerHost, ServerPort, _auth.Token!);
    }

    // ── Map helpers ─────────────────────────────────────────────────────────────

    private void ApplyMapData()
    {
        if (_map is null) return;
        _playArea          = _map.PlayArea;
        _mapColliders      = [.. _map.Colliders];
        _mapPolyColliders  = [.. _map.PolygonColliders];
        _mapPixelWidth     = _map.MapWidthPixels;
        _mapPixelHeight    = _map.MapHeightPixels;
        _interactableZones = [.. _map.Interactables];
        if (_map.StartPoint.HasValue)
            _localPos = _map.StartPoint.Value;
    }

    private void TransitionToMap(string zone)
    {
        if (!ZoneToMapFile.TryGetValue(zone, out var mapFile)) return;
        var path = Path.Combine(AppContext.BaseDirectory, "Content", "Maps", mapFile);
        _currentZone = zone;
        _network.SendZoneChanged(zone);
        _map = new TileMapRenderer(path);
        _map.LoadContent(_content);
        _enemies.Clear();
        _enemyDirX.Clear();
        _enemyHealth.Clear();
        _enemyFlashTimers.Clear();
        ApplyMapData();
        UpdateCamera();
    }

    // ── Network wiring ───────────────────────────────────────────────────────────

    private void WireNetworkEvents(ContentManager content)
    {
        _network.WorldSnapshotReceived += (localId, players) =>
        {
            _remotePlayers.Clear();
            _remoteAnimators.Clear();
            _remoteIdleTimers.Clear();
            _remotePlayerHealth.Clear();
            foreach (var p in players)
            {
                if (p.Id == localId) { _localPos = new Vector2(p.X, p.Y); continue; }
                _remotePlayers[p.Id]    = p;
                _remoteAnimators[p.Id]  = CreateAnimator(content, p.CharacterType);
                _remoteIdleTimers[p.Id] = 0f;
                var def = DefaultStats.ForCharacter(p.CharacterType);
                _remotePlayerHealth[p.Id] = (def.Health, def.MaxHealth);
            }
        };

        _network.PlayerJoined += p =>
        {
            if (p.Id == _network.LocalId) return;
            _remotePlayers[p.Id]    = p;
            _remoteAnimators[p.Id]  = CreateAnimator(content, p.CharacterType);
            _remoteIdleTimers[p.Id] = 0f;
            var def = DefaultStats.ForCharacter(p.CharacterType);
            _remotePlayerHealth[p.Id] = (def.Health, def.MaxHealth);
        };

        _network.PlayerMoved += (id, x, y) =>
        {
            if (id == _network.LocalId)
            {
                _localPos = new Vector2(x, y);
                return;
            }

            if (!_remotePlayers.TryGetValue(id, out var cur)) return;
            _remotePlayers[id] = cur with { X = x, Y = y };
            _remoteIdleTimers[id] = 0f;
            if (_remoteAnimators.TryGetValue(id, out var anim))
            {
                anim.SetDirection(InferDirection(x - cur.X, y - cur.Y));
                anim.SetAction(PlayerAction.Walk);
            }
        };

        _network.PlayerLeft += id =>
        {
            _remotePlayers.Remove(id);
            _remoteAnimators.Remove(id);
            _remoteIdleTimers.Remove(id);
            _remotePlayerHealth.Remove(id);
        };

        _network.EnemySnapshotReceived += (enemies, healths) =>
        {
            _enemies.Clear();
            _enemyDirX.Clear();
            _enemyHealth.Clear();
            if (!EnemiesActive) return;
            for (int i = 0; i < enemies.Length; i++)
            {
                var e = enemies[i];
                _enemies[e.Id]   = e;
                _enemyDirX[e.Id] = 1f;
                var def = DefaultStats.ForEnemy(e.Type);
                _enemyHealth[e.Id] = (healths[i], def.MaxHealth);
            }
        };

        _network.EnemyMoved += e =>
        {
            if (!EnemiesActive) return;
            if (_enemies.TryGetValue(e.Id, out var prev))
            {
                float dx = e.X - prev.X;
                if (MathF.Abs(dx) > 0.001f)
                    _enemyDirX[e.Id] = MathF.Sign(dx);
            }
            _enemies[e.Id] = e;
        };

        _network.EnemyDamaged += (e, health) =>
        {
            if (!EnemiesActive) return;
            _enemies.TryGetValue(e.Id, out var prev);
            float dx = e.X - prev.X;
            if (MathF.Abs(dx) > 0.001f)
                _enemyDirX[e.Id] = MathF.Sign(dx);
            _enemies[e.Id] = e;

            _enemyHealth.TryGetValue(e.Id, out var cur);
            int maxHealth  = cur.maxHealth > 0 ? cur.maxHealth : DefaultStats.ForEnemy(e.Type).MaxHealth;
            int prevHealth = cur.health;
            _enemyHealth[e.Id] = (health, maxHealth);

            if (health < prevHealth)
                _enemyFlashTimers[e.Id] = FlashDuration;
        };

        _network.PlayerDamaged += (id, x, y, health) =>
        {
            if (id == _network.LocalId)
            {
                _localPos        = new Vector2(x, y);
                _localFlashTimer = FlashDuration;
                _localStats      = _localStats with { Health = health };
            }
            else
            {
                if (_remotePlayers.TryGetValue(id, out var cur))
                    _remotePlayers[id] = cur with { X = x, Y = y };
                _remoteFlashTimers[id] = FlashDuration;
                if (_remotePlayerHealth.TryGetValue(id, out var hp))
                    _remotePlayerHealth[id] = (health, hp.maxHealth);
            }
        };

        _network.PlayerStatsReceived += stats => { _localStats = stats; _staminaF = stats.Stamina; };

        _network.AttackMissed     += () => _localAnimator.HoldAttackFrame(0.8f);
        _network.PlayerRespawned  += () => TransitionToMap(Zone.Hub);
    }

    // ── Update ───────────────────────────────────────────────────────────────────

    public override void Update(GameTime gameTime)
    {
        _network.PollEvents();

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        HandleAttack();
        HandleMovement(dt);
        HandleInteraction();

        _localAnimator.Update(dt);
        _slimeJumpSprite.Update(dt);
        _slimeIdleSprite.Update(dt);
        _bunnyNpcSprite.Update(dt);

        if (_localFlashTimer > 0f) _localFlashTimer = MathF.Max(0f, _localFlashTimer - dt);

        // Stamina regen: delayed start, 10/s after the delay
        if (_staminaRegenCooldown > 0f)
            _staminaRegenCooldown = MathF.Max(0f, _staminaRegenCooldown - dt);
        else if (_staminaF < _localStats.MaxStamina)
        {
            _staminaF = MathF.Min(_localStats.MaxStamina, _staminaF + StaminaRegenRate * dt);
            _localStats = _localStats with { Stamina = (int)_staminaF };
        }
        foreach (var id in _remoteFlashTimers.Keys.ToArray())
            _remoteFlashTimers[id] = MathF.Max(0f, _remoteFlashTimers[id] - dt);
        foreach (var id in _enemyFlashTimers.Keys.ToArray())
            _enemyFlashTimers[id] = MathF.Max(0f, _enemyFlashTimers[id] - dt);

        foreach (var id in _remoteIdleTimers.Keys.ToArray())
        {
            _remoteIdleTimers[id] += dt;
            if (_remoteIdleTimers[id] >= IdleThreshold && _remoteAnimators.TryGetValue(id, out var a))
                a.SetAction(PlayerAction.Idle);
        }
        foreach (var a in _remoteAnimators.Values) a.Update(dt);

        UpdateCamera();
        _prevKb = Keyboard.GetState();
    }

    private const float CameraZoom = 2f;

    private void UpdateCamera()
    {
        float visW = _viewportWidth  / CameraZoom;
        float visH = _viewportHeight / CameraZoom;
        float x = _localPos.X - visW / 2f;
        float y = _localPos.Y - visH / 2f;
        x = Math.Clamp(x, 0, Math.Max(0, _mapPixelWidth  - visW));
        y = Math.Clamp(y, 0, Math.Max(0, _mapPixelHeight - visH));
        _camera = new Vector2(x, y);
    }

    private void HandleAttack()
    {
        var kb = Keyboard.GetState();
        if (!_localAnimator.IsAttacking && !_isDashing && kb.IsKeyDown(Keys.H))
        {
            _localAnimator.SetAction(PlayerAction.SwordAttack);
            var (dx, dy) = DirectionToVec(_localAnimator.CurrentDirection);
            _network.SendAttack(dx, dy);
        }
    }

    private void HandleMovement(float dt)
    {
        if (_localAnimator.IsAttacking) return;

        var kb = Keyboard.GetState();
        if (_dashCooldown > 0f)
            _dashCooldown = MathF.Max(0f, _dashCooldown - dt);

        if (!_isDashing && _dashCooldown <= 0f &&
            _staminaF >= DashStaminaCost &&
            kb.IsKeyDown(Keys.LeftShift) && !_prevKb.IsKeyDown(Keys.LeftShift))
        {
            _staminaF            -= DashStaminaCost;
            _staminaRegenCooldown = StaminaRegenDelay;
            _localStats           = _localStats with { Stamina = (int)_staminaF };

            var input   = GetMoveInput(kb);
            var dashDir = input != Vector2.Zero ? Vector2.Normalize(input) : _lastMoveDir;
            _isDashing  = true;
            _dashTimer  = DashDuration;
            _dashDir    = dashDir;
            _localAnimator.SetDirection(InferDirection(dashDir.X, dashDir.Y));
            _localAnimator.SetAction(PlayerAction.Jump);
        }

        if (_isDashing)
        {
            _dashTimer -= dt;
            if (_dashTimer <= 0f)
            {
                _isDashing    = false;
                _dashCooldown = DashCooldown;
                _localAnimator.SetAction(PlayerAction.Idle);
            }
            else
            {
                var newPos = _localPos + _dashDir * (DashSpeed * dt);
                newPos.X   = Math.Clamp(newPos.X, _playArea.Left, _playArea.Right);
                newPos.Y   = Math.Clamp(newPos.Y, _playArea.Top,  _playArea.Bottom);
                ApplyCollision(ref newPos);
                _localPos = newPos;
                _network.SendMove(_localPos.X, _localPos.Y);
            }
            return;
        }

        var vel = GetMoveInput(kb);
        if (vel != Vector2.Zero)
        {
            _lastMoveDir = Vector2.Normalize(vel);
            var newPos   = _localPos + Vector2.Normalize(vel) * (Speed * dt);
            newPos.X     = Math.Clamp(newPos.X, _playArea.Left, _playArea.Right);
            newPos.Y     = Math.Clamp(newPos.Y, _playArea.Top,  _playArea.Bottom);
            ApplyCollision(ref newPos);
            _localPos = newPos;
            _localAnimator.SetDirection(InferDirection(vel.X, vel.Y));
            _localAnimator.SetAction(PlayerAction.Walk);
            _network.SendMove(_localPos.X, _localPos.Y);
        }
        else
        {
            _localAnimator.SetAction(PlayerAction.Idle);
        }
    }

    private void HandleInteraction()
    {
        _activePrompt      = null;
        _activeTargetZone  = null;

        foreach (var (name, bounds) in _interactableZones)
        {
            if (!bounds.Contains((int)_localPos.X, (int)_localPos.Y)) continue;
            if (!MapLinks.TryGetValue(name, out var link)) continue;
            _activePrompt     = link.Prompt;
            _activeTargetZone = link.TargetZone;
            break;
        }

        if (_activeTargetZone is not null)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.E) && !_prevKb.IsKeyDown(Keys.E))
                TransitionToMap(_activeTargetZone);
        }
    }

    private static Vector2 GetMoveInput(KeyboardState kb)
    {
        var v = Vector2.Zero;
        if (kb.IsKeyDown(Keys.W) || kb.IsKeyDown(Keys.Up))    v.Y -= 1;
        if (kb.IsKeyDown(Keys.S) || kb.IsKeyDown(Keys.Down))  v.Y += 1;
        if (kb.IsKeyDown(Keys.A) || kb.IsKeyDown(Keys.Left))  v.X -= 1;
        if (kb.IsKeyDown(Keys.D) || kb.IsKeyDown(Keys.Right)) v.X += 1;
        return v;
    }

    // ── Collision ────────────────────────────────────────────────────────────────

    private const float BunnyNpcRadius = 7f;

    private void ApplyCollision(ref Vector2 pos)
    {
        float pr = ColliderRadius.ForCharacter(_auth.CharacterType);

        if (_currentZone == Zone.Hub &&
            Collision.Overlaps(pos.X, pos.Y, pr, BunnyNpcPos.X, BunnyNpcPos.Y, BunnyNpcRadius))
        {
            var (sx, sy) = Collision.Resolve(pos.X, pos.Y, pr, BunnyNpcPos.X, BunnyNpcPos.Y, BunnyNpcRadius);
            pos.X += sx; pos.Y += sy;
        }

        foreach (var e in _enemies.Values)
        {
            if (_enemyHealth.TryGetValue(e.Id, out var eh) && eh.health <= 0) continue;
            float er = ColliderRadius.ForEnemy(e.Type);
            if (!Collision.Overlaps(pos.X, pos.Y, pr, e.X, e.Y, er)) continue;
            var (sx, sy) = Collision.Resolve(pos.X, pos.Y, pr, e.X, e.Y, er);
            pos.X += sx; pos.Y += sy;
        }
        foreach (var p in _remotePlayers.Values)
        {
            float or = ColliderRadius.ForCharacter(p.CharacterType);
            if (!Collision.Overlaps(pos.X, pos.Y, pr, p.X, p.Y, or)) continue;
            var (sx, sy) = Collision.Resolve(pos.X, pos.Y, pr, p.X, p.Y, or);
            pos.X += sx; pos.Y += sy;
        }
        foreach (var rect in _mapColliders)
            ResolveRectCollision(ref pos, pr, rect);
        foreach (var poly in _mapPolyColliders)
            ResolvePolygonCollision(ref pos, pr, poly);
    }

    private static void ResolveRectCollision(ref Vector2 pos, float radius, Rectangle rect)
    {
        float cx = Math.Clamp(pos.X, rect.Left, rect.Right);
        float cy = Math.Clamp(pos.Y, rect.Top,  rect.Bottom);
        float dx = pos.X - cx;
        float dy = pos.Y - cy;
        float distSq = dx * dx + dy * dy;

        if (distSq >= radius * radius) return;

        if (distSq < 0.0001f)
        {
            float overlapL = pos.X - rect.Left   + radius;
            float overlapR = rect.Right  - pos.X + radius;
            float overlapT = pos.Y - rect.Top    + radius;
            float overlapB = rect.Bottom - pos.Y + radius;
            float min = Math.Min(Math.Min(overlapL, overlapR), Math.Min(overlapT, overlapB));
            if      (min == overlapL) pos.X = rect.Left   - radius;
            else if (min == overlapR) pos.X = rect.Right  + radius;
            else if (min == overlapT) pos.Y = rect.Top    - radius;
            else                      pos.Y = rect.Bottom + radius;
            return;
        }

        float dist    = MathF.Sqrt(distSq);
        float overlap = radius - dist;
        pos.X += (dx / dist) * overlap;
        pos.Y += (dy / dist) * overlap;
    }

    private static void ResolvePolygonCollision(ref Vector2 pos, float radius, Vector2[] polygon)
    {
        for (int i = 0; i < polygon.Length; i++)
        {
            var a       = polygon[i];
            var b       = polygon[(i + 1) % polygon.Length];
            var closest = ClosestPointOnSegment(a, b, pos);
            var diff    = pos - closest;
            float dist  = diff.Length();
            if (dist < radius && dist > 0.0001f)
                pos += diff / dist * (radius - dist);
        }
    }

    private static Vector2 ClosestPointOnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        var ab = b - a;
        float d = Vector2.Dot(ab, ab);
        if (d < 0.0001f) return a;
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / d, 0f, 1f);
        return a + t * ab;
    }

    // ── Draw ─────────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, GraphicsDevice gd)
    {
        gd.Clear(new Color(30, 30, 46));

        // ── World pass (CameraZoom scale) ─────────────────────────────────────────
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp,
            transformMatrix: Matrix.CreateScale(CameraZoom, CameraZoom, 1f));

        _map?.Draw(_spriteBatch, _camera);

        // Enemies
        foreach (var (id, e) in _enemies)
        {
            if (_enemyHealth.TryGetValue(id, out var eh) && eh.health <= 0) continue;
            var screenPos = new Vector2(e.X, e.Y) - _camera;
            var tint      = FlashColor(_enemyFlashTimers.GetValueOrDefault(id));
            var effects   = _enemyDirX.GetValueOrDefault(id, 1f) < 0f
                            ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            _slimeJumpSprite.Draw(_spriteBatch, screenPos, tint, scale: 1f, effects: effects);
            if (eh.maxHealth > 0)
                DrawBar(_spriteBatch, screenPos + new Vector2(-15, -20), 30, 4,
                    (float)eh.health / eh.maxHealth, Color.MediumSeaGreen, new Color(0, 40, 0));
        }

        // NPCs (hub only)
        if (_currentZone == Zone.Hub)
            _bunnyNpcSprite.Draw(_spriteBatch, BunnyNpcPos - _camera, Color.White, scale: 1f);

        // Remote players
        foreach (var (id, p) in _remotePlayers)
            if (_remoteAnimators.TryGetValue(id, out var a))
            {
                var screenPos = new Vector2(p.X, p.Y) - _camera;
                var tint = FlashColor(_remoteFlashTimers.GetValueOrDefault(id));
                a.Draw(_spriteBatch, screenPos, tint, scale: 1f);
                if (_remotePlayerHealth.TryGetValue(id, out var hp) && hp.maxHealth > 0)
                    DrawBar(_spriteBatch, screenPos + new Vector2(-15, -30), 30, 4,
                        (float)hp.health / hp.maxHealth, Color.MediumSeaGreen, new Color(0, 40, 0));
            }

        // Local player
        _localAnimator.Draw(_spriteBatch, _localPos - _camera, FlashColor(_localFlashTimer), scale: 1f);

        // Debug collider overlays
        if (DevFlags.DebugColliders)
        {
            float lr = ColliderRadius.ForCharacter(_auth.CharacterType);
            DebugDraw.Circle(_spriteBatch, _localPos - _camera, lr, Color.LimeGreen);

            if (_localAnimator.IsAttacking)
            {
                var (adx, ady) = DirectionToVec(_localAnimator.CurrentDirection);
                var attackCenter = _localPos - _camera + new Vector2(adx, ady) * AttackOffset;
                DebugDraw.Circle(_spriteBatch, attackCenter, 20f, Color.Orange);
            }

            foreach (var p in _remotePlayers.Values)
                DebugDraw.Circle(_spriteBatch, new Vector2(p.X, p.Y) - _camera,
                    ColliderRadius.ForCharacter(p.CharacterType), Color.Yellow);

            foreach (var (id, e) in _enemies)
            {
                if (_enemyHealth.TryGetValue(id, out var eh) && eh.health <= 0) continue;
                DebugDraw.Circle(_spriteBatch, new Vector2(e.X, e.Y) - _camera,
                    ColliderRadius.ForEnemy(e.Type), Color.Magenta);
            }

            DebugDraw.Rect(_spriteBatch, OffsetRect(_playArea), Color.Cyan);
            foreach (var rect in _mapColliders)
                DebugDraw.Rect(_spriteBatch, OffsetRect(rect), Color.OrangeRed);
            foreach (var poly in _mapPolyColliders)
            {
                var offsetPoly = poly.Select(v => v - _camera).ToArray();
                DebugDraw.Polygon(_spriteBatch, offsetPoly, Color.OrangeRed);
            }
        }

        _spriteBatch.End();

        // ── HUD pass (no zoom) ────────────────────────────────────────────────────
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        // Local player bars (bottom-left)
        if (_localStats.MaxHealth > 0)
            DrawBar(_spriteBatch, new Vector2(10, 550), 150, 12,
                (float)_localStats.Health / _localStats.MaxHealth, Color.Crimson, new Color(60, 0, 0));
        if (_localStats.MaxStamina > 0)
            DrawBar(_spriteBatch, new Vector2(10, 565), 100, 8,
                (float)_localStats.Stamina / _localStats.MaxStamina, Color.Gold, new Color(60, 50, 0));
        if (_localStats.MaxMagicPower > 0)
            DrawBar(_spriteBatch, new Vector2(10, 575), 80, 8,
                (float)_localStats.MagicPower / _localStats.MaxMagicPower, Color.DodgerBlue, new Color(0, 0, 60));

        // XP bar — full width at very bottom
        {
            int xpBarH   = 8;
            int xpBarY   = _viewportHeight - xpBarH;
            int xpNeeded = XpSystem.XpForNextLevel(_localStats.Level);
            float xpFill = xpNeeded > 0 ? (float)_localStats.Xp / xpNeeded : 0f;
            DrawBar(_spriteBatch, new Vector2(0, xpBarY), _viewportWidth, xpBarH,
                xpFill, new Color(80, 120, 220), new Color(15, 20, 50));
            string xpLabel = $"Lv {_localStats.Level}";
            _spriteBatch.DrawString(_font, xpLabel,
                new Vector2(4, xpBarY - _font.LineSpacing), Color.CornflowerBlue);
        }

        // Interaction prompt (centered)
        if (_activePrompt is not null)
        {
            var measure   = _font.MeasureString(_activePrompt);
            var promptPos = new Vector2((_viewportWidth - measure.X) / 2f, _viewportHeight * 0.72f);
            _spriteBatch.DrawString(_font, _activePrompt, promptPos + new Vector2(1, 1), Color.Black * 0.8f);
            _spriteBatch.DrawString(_font, _activePrompt, promptPos, Color.Yellow);
        }

        // Debug text
        if (DevFlags.DebugColliders)
        {
            _spriteBatch.DrawString(_font, $"server: {(_network.IsConnected ? "connected" : "NOT connected")}",
                new Vector2(8, 8), _network.IsConnected ? Color.LimeGreen : Color.OrangeRed);
            _spriteBatch.DrawString(_font, $"enemies: {_enemies.Count}", new Vector2(8, 24), Color.White);
        }

        _spriteBatch.End();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private void DrawBar(SpriteBatch sb, Vector2 pos, int width, int height,
        float fill, Color filledColor, Color emptyColor)
    {
        sb.Draw(_pixel, new Rectangle((int)pos.X, (int)pos.Y, width, height), emptyColor);
        int filled = (int)(width * Math.Clamp(fill, 0f, 1f));
        if (filled > 0)
            sb.Draw(_pixel, new Rectangle((int)pos.X, (int)pos.Y, filled, height), filledColor);
    }

    private static CharacterAnimator CreateAnimator(ContentManager content, string characterType)
    {
        var a = CharacterAnimator.Create(characterType);
        a.LoadContent(content);
        return a;
    }

    private static Direction InferDirection(float dx, float dy) =>
        Math.Abs(dx) >= Math.Abs(dy)
            ? (dx >= 0 ? Direction.E : Direction.W)
            : (dy >= 0 ? Direction.S : Direction.N);

    private static (float dx, float dy) DirectionToVec(Direction dir) => dir switch
    {
        Direction.N => ( 0f, -1f),
        Direction.S => ( 0f,  1f),
        Direction.E => ( 1f,  0f),
        _           => (-1f,  0f),
    };

    private Rectangle OffsetRect(Rectangle r) =>
        new(r.X - (int)_camera.X, r.Y - (int)_camera.Y, r.Width, r.Height);

    private static Color FlashColor(float timer) =>
        timer > 0f && (int)(timer / 0.1f) % 2 == 1 ? Color.Red : Color.White;
}
