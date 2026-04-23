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

    private readonly AuthService _auth;

    private SpriteBatch _spriteBatch = null!;

    private readonly NetworkManager _network = new();
    private readonly Dictionary<int, PlayerInfo>        _remotePlayers   = [];
    private readonly Dictionary<int, CharacterAnimator> _remoteAnimators = [];
    private Vector2 _localPos = new(400, 300);

    private CharacterAnimator _localAnimator = null!;
    private KeyboardState _prevKb;

    private TileMapRenderer? _map;
    private static readonly string MapPath = "";

    public GameScreen(AuthService auth) => _auth = auth;

    public override void LoadContent(ContentManager content, GraphicsDevice gd)
    {
        _spriteBatch   = new SpriteBatch(gd);
        _localAnimator = CharacterAnimator.Create(_auth.CharacterType ?? Shared.CharacterType.Zink);
        _localAnimator.LoadContent(content);

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
                _remoteAnimators[p.Id] = CreateRemoteAnimator(content, p.CharacterType);
            }
        };

        _network.PlayerJoined += p =>
        {
            if (p.Id == _network.LocalId) return;
            _remotePlayers[p.Id]   = p;
            _remoteAnimators[p.Id] = CreateRemoteAnimator(content, p.CharacterType);
        };

        _network.PlayerMoved += (id, x, y) =>
        {
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
    }

    public override void Update(GameTime gameTime)
    {
        _network.PollEvents();

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        HandleMovement(dt);

        _localAnimator.Update(dt);
        foreach (var a in _remoteAnimators.Values) a.Update(dt);

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
            _localPos += vel * (Speed * dt);
            _localPos.X = Math.Clamp(_localPos.X, 0, 800);
            _localPos.Y = Math.Clamp(_localPos.Y, 0, 600);
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

        _spriteBatch.End();
    }

    private static CharacterAnimator CreateRemoteAnimator(ContentManager content, string characterType)
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
