using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Multiplay.Client.Graphics;

/// <summary>
/// A single animation strip: one Texture2D with N frames laid out horizontally.
/// Each frame is <see cref="FrameWidth"/> × <see cref="FrameHeight"/> pixels.
/// </summary>
public sealed class AnimatedSprite
{
    private readonly Texture2D _texture;
    private readonly int _frameCount;
    private readonly float _frameDuration; // seconds per frame

    public int FrameWidth  { get; }
    public int FrameHeight { get; }

    private float _elapsed;
    private int   _currentFrame;

    /// <param name="texture">The sprite strip texture.</param>
    /// <param name="frameWidth">Width of a single frame in pixels.</param>
    /// <param name="frameHeight">Height of a single frame in pixels.</param>
    /// <param name="fps">Playback speed in frames per second.</param>
    public AnimatedSprite(Texture2D texture, int frameWidth, int frameHeight, float fps = 8f)
    {
        _texture      = texture;
        FrameWidth    = frameWidth;
        FrameHeight   = frameHeight;
        _frameCount   = texture.Width / frameWidth;
        _frameDuration = 1f / fps;
    }

    /// <summary>Reset the animation back to frame 0.</summary>
    public void Reset()
    {
        _elapsed      = 0;
        _currentFrame = 0;
    }

    public void Update(float deltaSeconds)
    {
        _elapsed += deltaSeconds;
        if (_elapsed >= _frameDuration)
        {
            _elapsed -= _frameDuration;
            _currentFrame = (_currentFrame + 1) % _frameCount;
        }
    }

    /// <summary>
    /// Draw the current frame centered on <paramref name="position"/>.
    /// </summary>
    public void Draw(SpriteBatch sb, Vector2 position, Color color, float scale = 1f)
    {
        var src = new Rectangle(_currentFrame * FrameWidth, 0, FrameWidth, FrameHeight);
        var dest = new Vector2(
            position.X - FrameWidth  * scale / 2f,
            position.Y - FrameHeight * scale / 2f);
        sb.Draw(_texture, dest, src, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }
}
