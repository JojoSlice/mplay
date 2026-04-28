using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Multiplay.Client.Graphics;

/// <summary>
/// Immediate-mode debug primitives. Call <see cref="Initialize"/> once before use,
/// then call <see cref="Circle"/> inside an active SpriteBatch.Begin/End block.
/// </summary>
internal static class DebugDraw
{
    private static Texture2D? _pixel;

    internal static void Initialize(GraphicsDevice gd)
    {
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    internal static void Circle(SpriteBatch sb, Vector2 center, float radius, Color color, int segments = 32)
    {
        if (_pixel is null) return;
        float step = MathF.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float a    = step * i;
            float b    = step * (i + 1);
            var   from = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            var   to   = center + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius;
            Line(sb, from, to, color);
        }
    }

    internal static void Polygon(SpriteBatch sb, Vector2[] vertices, Color color)
    {
        for (int i = 0; i < vertices.Length; i++)
            Line(sb, vertices[i], vertices[(i + 1) % vertices.Length], color);
    }

    internal static void Rect(SpriteBatch sb, Rectangle rect, Color color)
    {
        var tl = new Vector2(rect.Left,  rect.Top);
        var tr = new Vector2(rect.Right, rect.Top);
        var bl = new Vector2(rect.Left,  rect.Bottom);
        var br = new Vector2(rect.Right, rect.Bottom);
        Line(sb, tl, tr, color);
        Line(sb, tr, br, color);
        Line(sb, br, bl, color);
        Line(sb, bl, tl, color);
    }

    private static void Line(SpriteBatch sb, Vector2 from, Vector2 to, Color color)
    {
        if (_pixel is null) return;
        var   edge   = to - from;
        float angle  = MathF.Atan2(edge.Y, edge.X);
        float length = edge.Length();
        sb.Draw(_pixel, from, null, color, angle, Vector2.Zero,
                new Vector2(length, 1f), SpriteEffects.None, 0f);
    }
}
