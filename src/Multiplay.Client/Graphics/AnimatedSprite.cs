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
    private readonly int       _frameCount;
    private readonly float[]   _frameDurations; // seconds per frame

    public int FrameWidth  { get; }
    public int FrameHeight { get; }

    private float _elapsed;
    private int   _currentFrame;

    /// <param name="texture">The sprite strip texture.</param>
    /// <param name="frameWidth">Width of a single frame in pixels.</param>
    /// <param name="frameHeight">Height of a single frame in pixels.</param>
    /// <param name="fps">Uniform playback speed in frames per second.</param>
    public AnimatedSprite(Texture2D texture, int frameWidth, int frameHeight, float fps = 8f)
    {
        _texture    = texture;
        FrameWidth  = frameWidth;
        FrameHeight = frameHeight;
        _frameCount = texture.Width / frameWidth;

        float d = 1f / fps;
        _frameDurations = new float[_frameCount];
        Array.Fill(_frameDurations, d);
    }

    /// <param name="texture">The sprite strip texture.</param>
    /// <param name="frameWidth">Width of a single frame in pixels.</param>
    /// <param name="frameHeight">Height of a single frame in pixels.</param>
    /// <param name="frameDurations">Duration in seconds for each individual frame.</param>
    public AnimatedSprite(Texture2D texture, int frameWidth, int frameHeight, float[] frameDurations)
    {
        _texture        = texture;
        FrameWidth      = frameWidth;
        FrameHeight     = frameHeight;
        _frameCount     = texture.Width / frameWidth;
        _frameDurations = frameDurations;
    }

    /// <summary>When false the animation stops on the last frame instead of looping.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>True after a non-looping animation plays its last frame.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Reset the animation back to frame 0.</summary>
    public void Reset()
    {
        _elapsed      = 0;
        _currentFrame = 0;
        IsFinished    = false;
    }

    public void Update(float deltaSeconds)
    {
        if (IsFinished) return;
        _elapsed += deltaSeconds;
        if (_elapsed >= _frameDurations[_currentFrame])
        {
            _elapsed -= _frameDurations[_currentFrame];
            int next = _currentFrame + 1;
            if (next >= _frameCount)
            {
                if (Loop) _currentFrame = 0;
                else { _currentFrame = _frameCount - 1; IsFinished = true; }
            }
            else _currentFrame = next;
        }
    }

    /// <summary>
    /// Draw the current frame centered on <paramref name="position"/>.
    /// </summary>
    public void Draw(SpriteBatch sb, Vector2 position, Color color, float scale = 1f,
                     SpriteEffects effects = SpriteEffects.None)
    {
        var src = new Rectangle(_currentFrame * FrameWidth, 0, FrameWidth, FrameHeight);
        var dest = new Vector2(
            position.X - FrameWidth  * scale / 2f,
            position.Y - FrameHeight * scale / 2f);
        sb.Draw(_texture, dest, src, color, 0f, Vector2.Zero, scale, effects, 0f);
    }
}
