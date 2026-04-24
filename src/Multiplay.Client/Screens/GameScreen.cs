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
    private const float  Speed      = 200f;

    private readonly IAuthService    _auth;
    private readonly INetworkManager _network;

    private SpriteBatch _spriteBatch = null!;

    private readonly Dictionary<int, PlayerInfo>        _remotePlayers   = [];
    private readonly Dictionary<int, CharacterAnimator> _remoteAnimators = [];
    private Vector2 _localPos = new(400, 300);

    private readonly Dictionary<int, EnemyInfo> _enemies    = [];
    private readonly Dictionary<int, float>    _enemyDirX  = []; // +1 right, -1 left
    private AnimatedSprite? _slimeSprite;

    // Static reference dummies for checking collider sizes
    private (CharacterAnimator Anim, Vector2 Pos, string CharType)[] _dummyChars = [];
    private Vector2[] _dummySlimes = [];

    private CharacterAnimator _localAnimator = null!;
    private KeyboardState _prevKb;

    private SpriteFont? _debugFont;

    private TileMapRenderer? _map;
    private static readonly string MapPath = "";

    public GameScreen(IAuthService auth) : this(auth, new NetworkManager()) { }

    // Secondary constructor for testing — inject a mock network manager
    public GameScreen(IAuthService auth, INetworkManager network)
    {
        _auth    = auth;
        _network = network;
    }

    public override void LoadContent(ContentManager content, GraphicsDevice gd)
    {
        _spriteBatch   = new SpriteBatch(gd);
        _localAnimator = CharacterAnimator.Create(
            _auth.CharacterType ?? Shared.CharacterType.Zink);
        _localAnimator.LoadContent(content);

        var slimeTex = content.Load<Texture2D>("Sprites/Enemies/sprSlimeJump");
        _slimeSprite = new AnimatedSprite(slimeTex, frameWidth: 16, frameHeight: 16, fps: 1f / 0.18f);

        if (DevFlags.DebugColliders)
        {
            DebugDraw.Initialize(gd);
            _debugFont = content.Load<SpriteFont>("Fonts/Default");
        }

        // One of each character type in a row, then slimes below
        _dummyChars =
        [
            (CreateAnimator(content, CharacterType.Zink),         new Vector2(150, 120), CharacterType.Zink),
            (CreateAnimator(content, CharacterType.ShieldKnight), new Vector2(300, 120), CharacterType.ShieldKnight),
            (CreateAnimator(content, CharacterType.SwordKnight),  new Vector2(450, 120), CharacterType.SwordKnight),
        ];
        _dummySlimes = [new Vector2(400, 200)];

        if (MapPath != string.Empty)
        {
            _map = new TileMapRenderer(MapPath);
            _map.LoadContent(content);
        }

        WireNetworkEvents(content);
        _network.Connect(ServerHost, ServerPort, _auth.Token!);
    }

    private void WireNetworkEvents(ContentManager content)
    {
        _network.WorldSnapshotReceived += (localId, players) =>
        {
            _remotePlayers.Clear();
            _remoteAnimators.Clear();
            foreach (var p in players)
            {
                if (p.Id == localId) { _localPos = new Vector2(p.X, p.Y); continue; }
                _remotePlayers[p.Id]   = p;
                _remoteAnimators[p.Id] = CreateAnimator(content, p.CharacterType);
            }
        };

        _network.PlayerJoined += p =>
        {
            if (p.Id == _network.LocalId) return;
            _remotePlayers[p.Id]   = p;
            _remoteAnimators[p.Id] = CreateAnimator(content, p.CharacterType);
        };

        _network.PlayerMoved += (id, x, y) =>
        {
            // Server-authoritative correction for the local player (e.g. pushed by an enemy)
            if (id == _network.LocalId)
            {
                _localPos = new Vector2(x, y);
                return;
            }

            if (!_remotePlayers.TryGetValue(id, out var cur)) return;
            _remotePlayers[id] = cur with { X = x, Y = y };
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
        };

        _network.EnemySnapshotReceived += enemies =>
        {
            _enemies.Clear();
            _enemyDirX.Clear();
            foreach (var e in enemies)
            {
                _enemies[e.Id]   = e;
                _enemyDirX[e.Id] = 1f;
            }
        };

        _network.EnemyMoved += e =>
        {
            if (_enemies.TryGetValue(e.Id, out var prev))
            {
                float dx = e.X - prev.X;
                if (MathF.Abs(dx) > 0.001f)
                    _enemyDirX[e.Id] = MathF.Sign(dx);
            }
            _enemies[e.Id] = e;
        };
    }

    public override void Update(GameTime gameTime)
    {
        _network.PollEvents();

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        HandleMovement(dt);

        _localAnimator.Update(dt);
        foreach (var a in _remoteAnimators.Values) a.Update(dt);
        foreach (var (anim, _, _) in _dummyChars) anim.Update(dt);
        _slimeSprite?.Update(dt);

        _prevKb = Keyboard.GetState();
    }

    private void HandleMovement(float dt)
    {
        var kb  = Keyboard.GetState();
        var vel = Vector2.Zero;

        if (kb.IsKeyDown(Keys.W) || kb.IsKeyDown(Keys.Up))    vel.Y -= 1;
        if (kb.IsKeyDown(Keys.S) || kb.IsKeyDown(Keys.Down))  vel.Y += 1;
        if (kb.IsKeyDown(Keys.A) || kb.IsKeyDown(Keys.Left))  vel.X -= 1;
        if (kb.IsKeyDown(Keys.D) || kb.IsKeyDown(Keys.Right)) vel.X += 1;

        if (vel != Vector2.Zero)
        {
            var newPos = _localPos + Vector2.Normalize(vel) * (Speed * dt);
            newPos.X = Math.Clamp(newPos.X, 0, 800);
            newPos.Y = Math.Clamp(newPos.Y, 0, 600);

            float pr = ColliderRadius.ForCharacter(_auth.CharacterType);

            foreach (var e in _enemies.Values)
            {
                float er = ColliderRadius.ForEnemy(e.Type);
                if (!Collision.Overlaps(newPos.X, newPos.Y, pr, e.X, e.Y, er)) continue;
                var (sepX, sepY) = Collision.Resolve(newPos.X, newPos.Y, pr, e.X, e.Y, er);
                newPos.X += sepX;
                newPos.Y += sepY;
            }

            foreach (var p in _remotePlayers.Values)
            {
                float or = ColliderRadius.ForCharacter(p.CharacterType);
                if (!Collision.Overlaps(newPos.X, newPos.Y, pr, p.X, p.Y, or)) continue;
                var (sepX, sepY) = Collision.Resolve(newPos.X, newPos.Y, pr, p.X, p.Y, or);
                newPos.X += sepX;
                newPos.Y += sepY;
            }

            _localPos = newPos;
            _localAnimator.SetDirection(InferDirection(vel.X, vel.Y));
            _localAnimator.SetAction(PlayerAction.Walk);
            _network.SendMove(_localPos.X, _localPos.Y);
        }
        else
        {
            _localAnimator.SetAction(PlayerAction.Walk);
        }
    }

    public override void Draw(SpriteBatch sb, GraphicsDevice gd)
    {
        gd.Clear(new Color(30, 30, 46));
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        _map?.Draw(_spriteBatch);

        foreach (var (id, p) in _remotePlayers)
            if (_remoteAnimators.TryGetValue(id, out var a))
                a.Draw(_spriteBatch, new Vector2(p.X, p.Y), Color.White, scale: 2f);

        _localAnimator.Draw(_spriteBatch, _localPos, Color.White, scale: 2f);

        if (_slimeSprite is not null)
            foreach (var e in _enemies.Values)
            {
                var fx = _enemyDirX.GetValueOrDefault(e.Id, 1f) < 0
                    ? SpriteEffects.FlipHorizontally
                    : SpriteEffects.None;
                _slimeSprite.Draw(_spriteBatch, new Vector2(e.X, e.Y), Color.White, scale: 2f, fx);
            }

        // Reference dummies
        foreach (var (anim, pos, _) in _dummyChars)
            anim.Draw(_spriteBatch, pos, Color.White, scale: 2f);
        if (_slimeSprite is not null)
            foreach (var pos in _dummySlimes)
                _slimeSprite.Draw(_spriteBatch, pos, Color.White, scale: 2f);

        if (DevFlags.DebugColliders)
        {
            float lr = ColliderRadius.ForCharacter(_auth.CharacterType);
            DebugDraw.Circle(_spriteBatch, _localPos, lr, Color.LimeGreen);

            foreach (var p in _remotePlayers.Values)
                DebugDraw.Circle(_spriteBatch, new Vector2(p.X, p.Y),
                    ColliderRadius.ForCharacter(p.CharacterType), Color.Yellow);

            foreach (var e in _enemies.Values)
                DebugDraw.Circle(_spriteBatch, new Vector2(e.X, e.Y),
                    ColliderRadius.ForEnemy(e.Type), Color.Red);

            // Dummy colliders
            foreach (var (_, pos, charType) in _dummyChars)
                DebugDraw.Circle(_spriteBatch, pos, ColliderRadius.ForCharacter(charType), Color.LimeGreen);
            foreach (var pos in _dummySlimes)
                DebugDraw.Circle(_spriteBatch, pos, ColliderRadius.ForEnemy(EnemyType.Slime), Color.Red);

            // HUD
            if (_debugFont is not null)
            {
                var conn    = _network.IsConnected ? "connected" : "NOT connected";
                var connClr = _network.IsConnected ? Color.LimeGreen : Color.OrangeRed;
                _spriteBatch.DrawString(_debugFont, $"server: {conn}", new Vector2(8, 8),  connClr);
                _spriteBatch.DrawString(_debugFont, $"enemies: {_enemies.Count}", new Vector2(8, 24), Color.White);
            }
        }

        _spriteBatch.End();
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
}
