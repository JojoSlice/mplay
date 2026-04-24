using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Multiplay.Client.Graphics;

/// <summary>
/// Animator for ShieldKnight and SwordKnight.
/// These enemies only have a 4-directional walk animation (32×32 px per frame).
/// </summary>
public sealed class KnightAnimator : CharacterAnimator
{
    private const int FrameW = 32;
    private const int FrameH = 32;
    private const float Fps  = 6f;

    private readonly string _spriteName; // "ShieldKnight" or "SwordKnight"
    private readonly Dictionary<Direction, AnimatedSprite> _sprites = [];
    private AnimatedSprite? _active;
    private bool _isIdle;

    public Direction CurrentDirection { get; private set; } = Direction.S;

    public KnightAnimator(string spriteName) => _spriteName = spriteName;

    public override void LoadContent(ContentManager content)
    {
        foreach (var (dir, suffix) in new[] {
            (Direction.S, "S"), (Direction.N, "N"),
            (Direction.E, "E"), (Direction.W, "W") })
        {
            var tex = content.Load<Texture2D>($"Sprites/Enemies/spr{_spriteName}{suffix}");
            _sprites[dir] = new AnimatedSprite(tex, FrameW, FrameH, Fps);
        }

        _active = _sprites[CurrentDirection];
    }

    public override void SetDirection(Direction dir)
    {
        if (dir == CurrentDirection) return;
        CurrentDirection = dir;
        if (_sprites.TryGetValue(dir, out var next))
        {
            _active = next;
            _active.Reset();
        }
    }

    public override void SetAction(PlayerAction action)
    {
        _isIdle = action == PlayerAction.Idle;
        if (_isIdle) _active?.Reset();
    }

    public override void Update(float deltaSeconds)
    {
        if (!_isIdle) _active?.Update(deltaSeconds);
    }

    public override void Draw(SpriteBatch sb, Vector2 position, Color color, float scale = 1f)
        => _active?.Draw(sb, position, color, scale);
}
