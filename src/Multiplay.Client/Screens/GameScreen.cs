using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Multiplay.Client.Graphics;
using Multiplay.Client.Network;
using Multiplay.Client.Services;
using Multiplay.Client.UI;
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
    private const float AttackOffsetDefault = 6f;  // must match server GameLogic
    private const float AttackOffsetSword   = 10f; // must match server GameLogic
    private const float AttackRadius        = 12f; // must match server GameLogic

    private float AttackOffset => _weaponType is WeaponType.Sword ? AttackOffsetSword : AttackOffsetDefault;
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
    private string      _xpLabel      = "Lv 0";
    private int         _xpLabelLevel = 0;
    private readonly Dictionary<int, (int health, int maxHealth)> _remotePlayerHealth = [];
    private readonly Dictionary<int, (int health, int maxHealth)> _enemyHealth        = [];
    private Texture2D _pixel = null!;

    private AnimatedSprite _slimeJumpSprite    = null!;
    private AnimatedSprite _bunnyNpcSprite     = null!;
    private Texture2D      _bunnyPortrait      = null!;
    private AnimatedSprite _oldManSprite       = null!;
    private Texture2D      _oldManPortrait     = null!;

    // Bottom-right corner of the inn startPoint (hub.tmx: x=513,y=640,w=79,h=33)
    private static readonly Vector2 BunnyNpcPos  = new(582f, 658f);
    // In front of the shop (hub.tmx: shop interactable x=199,y=465,w=68,h=30)
    private static readonly Vector2 OldManPos    = new(233f, 480f);

    private CharacterAnimator _localAnimator = null!;
    private KeyboardState     _prevKb;

    private TileMapRenderer? _map;
    private Rectangle        _playArea          = new(0, 0, 800, 800);
    private List<Rectangle>  _mapColliders      = [];
    private List<Vector2[]>  _mapPolyColliders  = [];
    private List<(string Name, Rectangle Bounds)> _interactableZones = [];

    private string? _activePrompt;
    private string? _activeTargetZone;
    private string? _activeNpc;
    private string  _currentZone  = Zone.Hub;
    private bool    EnemiesActive => _currentZone == Zone.Map1;

    // ── Dialog ───────────────────────────────────────────────────────────────────
    private DialogBox?    _dialog;
    private Action<int>?  _dialogChoiceCallback;  // invoked with selected index on confirm; null for message-only
    private bool          _dialogJustClosed;       // prevents HandleInteraction consuming the same key press
    private const float BunnyTalkRadius  = 50f;
    private const float OldManTalkRadius = 50f;
    private const float OldManRadius     = 7f;

    // ── Quest ────────────────────────────────────────────────────────────────────
    private enum QuestState { None, Active, ReadyToTurn }
    private QuestState _questState    = QuestState.None;
    private int        _questKillCount = 0;
    private const int  QuestKillTarget  = 3;

    // ── Weapon ───────────────────────────────────────────────────────────────────
    private enum WeaponType { None, Sword, Bow, Wand }
    private WeaponType _weaponType         = WeaponType.Sword;
    private bool       _oldManHasWeapon    = false;
    private bool       _weaponNeedsReclaim = false;  // set after death when weapon was equipped
    private bool       _slimeQuestDone     = false;  // mirrors _auth.SlimeQuestDone; set locally on reward

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
        _localStats = DefaultStats.ForClass(_auth.WeaponType);
        _staminaF   = _localStats.Stamina;

        _weaponType     = ParseWeapon(_auth.WeaponType);
        _slimeQuestDone = _auth.SlimeQuestDone;

        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _font = content.Load<SpriteFont>("Fonts/Default");

        if (DevFlags.DebugColliders)
            DebugDraw.Initialize(gd);

        // Slime sprites — 16 × 16 px per frame
        _slimeJumpSprite = new AnimatedSprite(
            content.Load<Texture2D>("Sprites/Enemies/sprSlimeJump"), 16, 16,
            new float[] { 0.18f, 0.18f, 0.18f, 0.18f, 0.18f, 0.18f, 0.18f });

        _bunnyNpcSprite = new AnimatedSprite(
            content.Load<Texture2D>("Sprites/NPCs/sprBunnyGirlSearch"), 16, 24, fps: 4f);
        _bunnyPortrait = content.Load<Texture2D>("Dialog/bunnyGirl");

        _oldManSprite   = new AnimatedSprite(
            content.Load<Texture2D>("Sprites/NPCs/sprOldManS"), 16, 16, fps: 4f);
        _oldManPortrait = content.Load<Texture2D>("Dialog/oldMan");

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

    // Centre of the hub gate zone (hub.tmx: gate x=369,y=144,w=79,h=33), just south of the arch
    private static readonly Vector2 HubGateSpawn = new(408f, 185f);

    private void TransitionToMap(string zone, Vector2? spawnPos = null)
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
        if (spawnPos.HasValue)
            _localPos = spawnPos.Value;
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
                var def = DefaultStats.ForClass(null); // weapon type unknown; updated on damage
                _remotePlayerHealth[p.Id] = (def.Health, def.MaxHealth);
            }
        };

        _network.PlayerJoined += p =>
        {
            if (p.Id == _network.LocalId) return;
            _remotePlayers[p.Id]    = p;
            _remoteAnimators[p.Id]  = CreateAnimator(content, p.CharacterType);
            _remoteIdleTimers[p.Id] = 0f;
            var def = DefaultStats.ForClass(null); // weapon type unknown; updated on damage
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

        _network.PlayerStatsReceived += stats =>
        {
            bool wasKill = stats.Xp > _localStats.Xp || stats.Level > _localStats.Level;
            if (_weaponNeedsReclaim && stats.Level >= 1 && _localStats.Level < 1)
            {
                _weaponNeedsReclaim = false;
                _oldManHasWeapon    = true;
            }
            _localStats  = stats;
            _staminaF    = stats.Stamina;
            if (wasKill && _questState == QuestState.Active && _currentZone == Zone.Map1)
            {
                _questKillCount++;
                if (_questKillCount >= QuestKillTarget)
                    _questState = QuestState.ReadyToTurn;
            }
        };

        _network.AttackMissed     += () => _localAnimator.HoldAttackFrame(0.8f);
        _network.PlayerRespawned += () =>
        {
            if (_auth.WeaponType is not null)
            {
                _weaponType         = WeaponType.None; // disarmed until reclaim
                _weaponNeedsReclaim = true;
            }
            TransitionToMap(Zone.Hub);
            ShowBunnyMessage("..You're back.\nDead already? How embarrassing.");
        };
    }

    // ── Update ───────────────────────────────────────────────────────────────────

    public override void Update(GameTime gameTime)
    {
        _network.PollEvents();

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var kb = Keyboard.GetState();
        _dialogJustClosed = false;
        HandleDialog(kb);
        HandleAttack();
        HandleMovement(dt);
        HandleInteraction();

        _localAnimator.Update(dt);
        _slimeJumpSprite.Update(dt);
        _bunnyNpcSprite.Update(dt);
        _oldManSprite.Update(dt);

        if (_localFlashTimer > 0f) _localFlashTimer = MathF.Max(0f, _localFlashTimer - dt);

        // Stamina regen: delayed start, 10/s after the delay
        if (_staminaRegenCooldown > 0f)
            _staminaRegenCooldown = MathF.Max(0f, _staminaRegenCooldown - dt);
        else if (_staminaF < _localStats.MaxStamina)
        {
            _staminaF = MathF.Min(_localStats.MaxStamina, _staminaF + StaminaRegenRate * dt);
            int s = (int)_staminaF;
            if (s != _localStats.Stamina)
                _localStats = _localStats with { Stamina = s };
        }
        foreach (var id in _remoteFlashTimers.Keys)
            _remoteFlashTimers[id] = MathF.Max(0f, _remoteFlashTimers[id] - dt);
        foreach (var id in _enemyFlashTimers.Keys)
            _enemyFlashTimers[id] = MathF.Max(0f, _enemyFlashTimers[id] - dt);

        foreach (var id in _remoteIdleTimers.Keys)
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
        if (_dialog != null) return;
        var kb = Keyboard.GetState();
        if (!_localAnimator.IsAttacking && !_isDashing && kb.IsKeyDown(Keys.H))
        {
            var action = _weaponType switch
            {
                WeaponType.Sword => PlayerAction.ClassicSwordAttack,
                WeaponType.Bow   => PlayerAction.BowAttack,
                WeaponType.Wand  => PlayerAction.WandAttack,
                _                => PlayerAction.SwordAttack,
            };
            _localAnimator.SetAction(action);
            var (dx, dy) = DirectionToVec(_localAnimator.CurrentDirection);
            _network.SendAttack(dx, dy);
        }
    }

    private void HandleMovement(float dt)
    {
        if (_dialog != null) return;
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
        if (_dialog != null || _dialogJustClosed) return;

        _activePrompt     = null;
        _activeTargetZone = null;
        _activeNpc        = null;

        foreach (var (name, bounds) in _interactableZones)
        {
            if (!bounds.Contains((int)_localPos.X, (int)_localPos.Y)) continue;
            if (!MapLinks.TryGetValue(name, out var link)) continue;
            _activePrompt     = link.Prompt;
            _activeTargetZone = link.TargetZone;
            break;
        }

        // NPC proximity checks (hub only)
        if (_currentZone == Zone.Hub)
        {
            if (Vector2.Distance(_localPos, BunnyNpcPos) <= BunnyTalkRadius)
            {
                _activePrompt = "Talk [E]";
                _activeNpc    = "bunnygirl";
            }
            else if (Vector2.Distance(_localPos, OldManPos) <= OldManTalkRadius)
            {
                _activePrompt = "Talk [E]";
                _activeNpc    = "oldman";
            }
        }

        var kb = Keyboard.GetState();
        bool ePressed = kb.IsKeyDown(Keys.E) && !_prevKb.IsKeyDown(Keys.E);
        if (ePressed)
        {
            if (_activeTargetZone is not null)
                TransitionToMap(_activeTargetZone,
                    _activeTargetZone == Zone.Hub ? HubGateSpawn : null);
            else if (_activeNpc == "bunnygirl") OpenBunnyDialog();
            else if (_activeNpc == "oldman")    OpenOldManDialog();
        }
    }

    private void HandleDialog(KeyboardState kb)
    {
        if (_dialog == null) return;
        if (kb.IsKeyDown(Keys.W) && !_prevKb.IsKeyDown(Keys.W)) _dialog.SelectPrev();
        if (kb.IsKeyDown(Keys.S) && !_prevKb.IsKeyDown(Keys.S)) _dialog.SelectNext();

        bool confirm = (kb.IsKeyDown(Keys.E)     && !_prevKb.IsKeyDown(Keys.E))
                    || (kb.IsKeyDown(Keys.Space)  && !_prevKb.IsKeyDown(Keys.Space))
                    || (kb.IsKeyDown(Keys.Enter)  && !_prevKb.IsKeyDown(Keys.Enter));
        if (confirm)
        {
            int choice        = _dialog.Options.Length > 0 ? _dialog.SelectedIndex : -1;
            var callback      = _dialogChoiceCallback;
            _dialog           = null;
            _dialogChoiceCallback = null;
            _dialogJustClosed = true;
            if (choice >= 0) callback?.Invoke(choice);
        }
    }

    private void OpenBunnyDialog()
    {
        if (_slimeQuestDone && _questState == QuestState.None)
        {
            ShowBunnyMessage("You've already proven yourself.\nDon't push your luck.");
            return;
        }

        // Auto-complete on turn-in: no reward here — direct to Old Man
        if (_questState == QuestState.ReadyToTurn)
        {
            _questState     = QuestState.None;
            _slimeQuestDone = true;
            _ = _auth.SavePlayerDataAsync(slimeQuestDone: true);
            ShowBunnyMessage("Hm. You actually did it.\nI'm mildly surprised.\nGo see the Old Man at the shop.\nHe'll have something for you.");
            return;
        }

        string body = _questState switch
        {
            QuestState.None   => "Oh. Another wanderer.\nWhat do you want?",
            QuestState.Active => "Still here? You haven't proven yourself yet.\nGo kill the slimes.",
            _                 => "",
        };
        string[] options = _questState switch
        {
            QuestState.None => ["Talk", "Leave"],
            _               => ["Leave"],
        };
        _dialogChoiceCallback = OnBunnyChoice;
        _dialog = new DialogBox
        {
            SpeakerName = "BunnyGirl",
            Portrait    = _bunnyPortrait,
            BodyText    = body,
            Options     = options,
        };
        _dialog.Reset();
    }

    private void OnBunnyChoice(int choice)
    {
        switch (_questState)
        {
            case QuestState.None when choice == 0:  // Talk
                _questState     = QuestState.Active;
                _questKillCount = 0;
                ShowBunnyMessage("Ugh. Fine. There are slimes everywhere.\nKill 3 of them before you even\nthink about talking to me again.");
                break;
            case QuestState.None:                   // Leave
                ShowBunnyMessage("Good. I didn't want to talk\nto you anyway. Bye.");
                break;
            case QuestState.Active:                 // Leave
                ShowBunnyMessage("Not done yet. Bye.");
                break;
        }
    }

    private void ShowBunnyMessage(string text)
    {
        _dialogChoiceCallback = null;
        _dialog = new DialogBox
        {
            SpeakerName = "BunnyGirl",
            Portrait    = _bunnyPortrait,
            BodyText    = text,
            Options     = [],
        };
    }

    // ── Old Man NPC ──────────────────────────────────────────────────────────────

    private void OpenOldManDialog()
    {
        // Reclaim after death — weapon already chosen and stored in auth
        if (_oldManHasWeapon && _auth.WeaponType is not null)
        {
            _dialogChoiceCallback = OnOldManReclaimWeapon;
            string name = WeaponDisplayName(ParseWeapon(_auth.WeaponType));
            _dialog = new DialogBox
            {
                SpeakerName = "Old Man",
                Portrait    = _oldManPortrait,
                BodyText    = $"Welcome back! I've kept your {name}\nsafe for you, young one.\nReady to take it back?",
                Options     = [$"Take {name}"],
            };
            _dialog.Reset();
            return;
        }

        // First-time weapon selection — unlocked after completing slime quest
        if (_slimeQuestDone && _auth.WeaponType is null)
        {
            _dialogChoiceCallback = OnOldManWeaponChoice;
            _dialog = new DialogBox
            {
                SpeakerName = "Old Man",
                Portrait    = _oldManPortrait,
                BodyText    = "Ah! So she finally sent you.\nEvery warrior needs a weapon.\nChoose wisely. This defines your path.",
                Options     = ["Sword", "Bow", "Wand"],
            };
            _dialog.Reset();
            return;
        }

        _dialogChoiceCallback = OnOldManChoice;
        _dialog = new DialogBox
        {
            SpeakerName = "Old Man",
            Portrait    = _oldManPortrait,
            BodyText    = "Ah, welcome, young adventurer!\nGood to see a new face around here.\nWhat can I do for you?",
            Options     = ["Talk", "Shop", "Leave"],
        };
        _dialog.Reset();
    }

    private void OnOldManWeaponChoice(int choice)
    {
        _oldManHasWeapon = false;
        _weaponType = choice switch
        {
            0 => WeaponType.Sword,
            1 => WeaponType.Bow,
            _ => WeaponType.Wand,
        };
        string name = WeaponDisplayName(_weaponType);
        _ = _auth.SavePlayerDataAsync(weaponType: _weaponType.ToString());
        _network.SendWeaponChanged(_weaponType.ToString());
        ShowOldManMessage($"A fine choice! The {name} has served\nmany great warriors before you.\nMay it serve you well, young one.");
    }

    private void OnOldManReclaimWeapon(int _)
    {
        _oldManHasWeapon = false;
        _weaponType      = ParseWeapon(_auth.WeaponType);
        string name      = WeaponDisplayName(_weaponType);
        _network.SendWeaponChanged(_weaponType.ToString());
        ShowOldManMessage($"There you go! Your trusty {name}.\nMay it serve you well once more,\nyoung one.");
    }

    private static WeaponType ParseWeapon(string? s) => s switch
    {
        "Sword" => WeaponType.Sword,
        "Bow"   => WeaponType.Bow,
        "Wand"  => WeaponType.Wand,
        _       => WeaponType.None,
    };

    private static string WeaponDisplayName(WeaponType w) => w switch
    {
        WeaponType.Bow  => "bow",
        WeaponType.Wand => "wand",
        _               => "sword",
    };

    private void OnOldManChoice(int choice)
    {
        switch (choice)
        {
            case 0: // Talk
                ShowOldManMessage("Ha! You want to hear about the old days?\nI once held the eastern mountain pass\nalone for three days straight.\nThose were the days, young one.");
                break;
            case 1: // Shop
                ShowOldManMessage("Ah, the shop. Ha! I'm afraid the old\ngoods aren't quite ready yet.\nCome back and see me soon.");
                break;
            default: // Leave
                ShowOldManMessage("Safe travels, young one!\nKeep your guard up out there.\nThe world is not as kind as it looks.");
                break;
        }
    }

    private void ShowOldManMessage(string text)
    {
        _dialogChoiceCallback = null;
        _dialog = new DialogBox
        {
            SpeakerName = "Old Man",
            Portrait    = _oldManPortrait,
            BodyText    = text,
            Options     = [],
        };
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

        if (_currentZone == Zone.Hub)
        {
            if (Collision.Overlaps(pos.X, pos.Y, pr, BunnyNpcPos.X, BunnyNpcPos.Y, BunnyNpcRadius))
            {
                var (sx, sy) = Collision.Resolve(pos.X, pos.Y, pr, BunnyNpcPos.X, BunnyNpcPos.Y, BunnyNpcRadius);
                pos.X += sx; pos.Y += sy;
            }
            if (Collision.Overlaps(pos.X, pos.Y, pr, OldManPos.X, OldManPos.Y, OldManRadius))
            {
                var (sx, sy) = Collision.Resolve(pos.X, pos.Y, pr, OldManPos.X, OldManPos.Y, OldManRadius);
                pos.X += sx; pos.Y += sy;
            }
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
            (pos.X, pos.Y) = Collision.ResolveRect(pos.X, pos.Y, pr, rect.Left, rect.Top, rect.Right, rect.Bottom);
        foreach (var poly in _mapPolyColliders)
        {
            var pts = new (float x, float y)[poly.Length];
            for (int i = 0; i < poly.Length; i++) pts[i] = (poly[i].X, poly[i].Y);
            (pos.X, pos.Y) = Collision.ResolvePoly(pos.X, pos.Y, pr, pts);
        }
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
        {
            _bunnyNpcSprite.Draw(_spriteBatch, BunnyNpcPos - _camera, Color.White, scale: 1f);
            _oldManSprite.Draw(_spriteBatch,   OldManPos   - _camera, Color.White, scale: 1f);
        }

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
                DebugDraw.Circle(_spriteBatch, attackCenter, AttackRadius, Color.Orange);
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
            if (_localStats.Level != _xpLabelLevel)
            {
                _xpLabelLevel = _localStats.Level;
                _xpLabel      = $"Lv {_localStats.Level}";
            }
            _spriteBatch.DrawString(_font, _xpLabel,
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

        // Quest HUD (right side)
        bool showQuestHud = _questState != QuestState.None
                         || (_slimeQuestDone && _auth.WeaponType is null);
        if (showQuestHud)
        {
            const int qw = 200, qh = 52, qx_pad = 10, qy = 10;
            int qx = _viewportWidth - qw - qx_pad;
            _spriteBatch.Draw(_pixel, new Rectangle(qx, qy, qw, qh), new Color(20, 20, 40));
            DrawBorderRect(_spriteBatch, new Rectangle(qx, qy, qw, qh), new Color(120, 120, 180), 2);
            _spriteBatch.DrawString(_font, "Quest: Slay Slimes",
                new Vector2(qx + 8, qy + 6), new Color(220, 200, 100));
            string line2 = (_slimeQuestDone && _auth.WeaponType is null)
                ? "Visit the Old Man!"
                : _questState == QuestState.ReadyToTurn
                    ? "Return to BunnyGirl!"
                    : $"Slimes: {_questKillCount} / {QuestKillTarget}";
            _spriteBatch.DrawString(_font, line2,
                new Vector2(qx + 8, qy + 6 + _font.LineSpacing), Color.White);
        }

        // Debug text
        if (DevFlags.DebugColliders)
        {
            _spriteBatch.DrawString(_font, $"server: {(_network.IsConnected ? "connected" : "NOT connected")}",
                new Vector2(8, 8), _network.IsConnected ? Color.LimeGreen : Color.OrangeRed);
            _spriteBatch.DrawString(_font, $"enemies: {_enemies.Count}", new Vector2(8, 24), Color.White);
        }

        // Dialog box (on top of everything)
        _dialog?.Draw(_spriteBatch, _font, _pixel, _viewportWidth, _viewportHeight);

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

    private void DrawBorderRect(SpriteBatch sb, Rectangle r, Color c, int t)
    {
        sb.Draw(_pixel, new Rectangle(r.X,          r.Y,          r.Width, t), c);
        sb.Draw(_pixel, new Rectangle(r.X,          r.Bottom - t, r.Width, t), c);
        sb.Draw(_pixel, new Rectangle(r.X,          r.Y,          t, r.Height), c);
        sb.Draw(_pixel, new Rectangle(r.Right - t,  r.Y,          t, r.Height), c);
    }
}
