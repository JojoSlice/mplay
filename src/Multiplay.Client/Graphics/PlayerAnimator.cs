using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Multiplay.Client.Graphics;

/// <summary>
/// Full animator for the Zink character.
/// Supports Walk, SwordAttack, ClassicSwordAttack, BowAttack, WandAttack, Jump, Death.
/// </summary>
public sealed class ZinkAnimator : CharacterAnimator
{
    private const int FrameW = 48;
    private const int FrameH = 48;

    private readonly Dictionary<(PlayerAction, Direction), AnimatedSprite> _sprites = [];
    private (PlayerAction action, Direction dir) _current = (PlayerAction.Walk, Direction.S);
    private AnimatedSprite? _active;
    private bool _isIdle;
    private bool _isAttacking;

    private Direction    _currentDirection  = Direction.S;
    private PlayerAction _currentAction     = PlayerAction.Walk;
    private float        _attackHoldSeconds = 0f;

    public override Direction CurrentDirection => _currentDirection;
    public PlayerAction       CurrentAction    => _currentAction;

    public override bool IsAttacking => _isAttacking;

    public override void HoldAttackFrame(float seconds) =>
        _attackHoldSeconds = MathF.Max(_attackHoldSeconds, seconds);

    public override void LoadContent(ContentManager content)
    {
        Load(content, PlayerAction.Walk,               "Walk",               fps: 8);
        Load(content, PlayerAction.SwordAttack,        "WeaponsClassicSwordAttack", fps: 12);
        Load(content, PlayerAction.ClassicSwordAttack, "ClassicSwordAttack", fps: 12);
        Load(content, PlayerAction.BowAttack,          "BowAttack",          fps: 10);
        Load(content, PlayerAction.WandAttack,         "WandAttack",         fps: 10);
        Load(content, PlayerAction.Jump,               "Jump",               fps: 8);

        var deathTex = content.Load<Texture2D>("Sprites/Zink/sprZinkDeath");
        _sprites[(PlayerAction.Death, Direction.S)] = new AnimatedSprite(deathTex, FrameW, FrameH, fps: 8);
        foreach (var d in new[] { Direction.N, Direction.E, Direction.W })
            _sprites[(PlayerAction.Death, d)] = _sprites[(PlayerAction.Death, Direction.S)];

        _active = _sprites[_current];
    }

    private void Load(ContentManager content, PlayerAction action, string namePart, float fps)
    {
        foreach (var (dir, suffix) in new[] {
            (Direction.S, "S"), (Direction.N, "N"),
            (Direction.E, "E"), (Direction.W, "W") })
        {
            var tex = content.Load<Texture2D>($"Sprites/Zink/sprZink{namePart}{suffix}");
            _sprites[(action, dir)] = new AnimatedSprite(tex, FrameW, FrameH, fps);
        }
    }

    public override void SetDirection(Direction dir)
    {
        if (dir == _currentDirection) return;
        _currentDirection = dir;
        SwitchActive();
    }

    private static bool IsAttackAction(PlayerAction a) =>
        a is PlayerAction.SwordAttack;

    public override void SetAction(PlayerAction action)
    {
        // Attack plays to completion — nothing can interrupt it
        if (_isAttacking) return;

        if (action == PlayerAction.Idle)
        {
            if (_isIdle) return;
            _isIdle        = true;
            _currentAction = PlayerAction.Walk;
            SwitchActive();
            _active?.Reset();
            return;
        }

        _isIdle = false;

        if (IsAttackAction(action))
        {
            _isAttacking   = true;
            _currentAction = action;
            SwitchActive();
            if (_active is not null) { _active.Loop = false; _active.Reset(); }
            return;
        }

        if (action == _currentAction) return;
        _currentAction = action;
        SwitchActive();
    }

    private void SwitchActive()
    {
        var key = (_currentAction, _currentDirection);
        if (!_sprites.TryGetValue(key, out var next)) return;
        _active  = next;
        _current = key;
        _active.Reset();
    }

    public override void Update(float deltaSeconds)
    {
        if (_isIdle) return;

        if (_attackHoldSeconds > 0f)
            _attackHoldSeconds = MathF.Max(0f, _attackHoldSeconds - deltaSeconds);

        _active?.Update(deltaSeconds);

        // Return to walk once the attack animation finishes AND any miss-hold expires
        if (_isAttacking && (_active?.IsFinished ?? false) && _attackHoldSeconds <= 0f)
        {
            _isAttacking   = false;
            _currentAction = PlayerAction.Walk;
            if (_active is not null) _active.Loop = true;
            SwitchActive();
        }
    }

    public override void Draw(SpriteBatch sb, Vector2 position, Color color, float scale = 1f)
        => _active?.Draw(sb, position, color, scale);
}
