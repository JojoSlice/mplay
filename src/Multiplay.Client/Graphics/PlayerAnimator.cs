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
    private const int FrameW = 16;
    private const int FrameH = 48;

    private readonly Dictionary<(PlayerAction, Direction), AnimatedSprite> _sprites = [];
    private (PlayerAction action, Direction dir) _current = (PlayerAction.Walk, Direction.S);
    private AnimatedSprite? _active;

    public Direction    CurrentDirection { get; private set; } = Direction.S;
    public PlayerAction CurrentAction    { get; private set; } = PlayerAction.Walk;

    public override void LoadContent(ContentManager content)
    {
        Load(content, PlayerAction.Walk,               "Walk",               fps: 8);
        Load(content, PlayerAction.SwordAttack,        "SwordAttack",        fps: 12);
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
        if (dir == CurrentDirection) return;
        CurrentDirection = dir;
        SwitchActive();
    }

    public override void SetAction(PlayerAction action)
    {
        if (action == CurrentAction) return;
        CurrentAction = action;
        SwitchActive();
    }

    private void SwitchActive()
    {
        var key = (CurrentAction, CurrentDirection);
        if (!_sprites.TryGetValue(key, out var next)) return;
        _active  = next;
        _current = key;
        _active.Reset();
    }

    public override void Update(float deltaSeconds) => _active?.Update(deltaSeconds);

    public override void Draw(SpriteBatch sb, Vector2 position, Color color, float scale = 1f)
        => _active?.Draw(sb, position, color, scale);
}
